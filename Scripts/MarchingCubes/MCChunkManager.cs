using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;


// Clipmap-style chunked LOD world:
//   Level k chunks all have the same cell count but cover 2^k x the base world
//   size (so their cells are 2^k x coarser). Each level maintains a box of
//   chunks around the player; the region covered by the finer level is cut out.
//   Boxes are built bottom-up with even alignment and >= 1-chunk margins, which
//   guarantees every LOD interface is exactly 2:1 and lies on whole chunk faces
//   of both levels -- the configuration the Transvoxel transition cells stitch.
//
// Chunk meshes are BUILT ON WORKER THREADS (ChunkMesher); the main thread only
// dispatches jobs, uploads finished mesh data and swaps LOD rings atomically.
public class MCChunkManager : MonoBehaviour
{
    public Transform target;              // usually the player/camera
    public MarchingChunk chunkPrefab;
    public WorldConfig worldConfig;

    [Header("GPU Acceleration")]
    [Tooltip("Optional. Shaders/Compute/TerrainDensity.compute -- GPU-accelerates base density sampling (see the 'GPU Compute-Shader Acceleration for Terrain Density Fields' plan). Falls back to the original CPU path entirely if unset, unsupported (SystemInfo.supportsComputeShaders), or the active world doesn't resolve every biome to a known GPU leaf type -- never a per-chunk GPU/CPU mix.")]
    public ComputeShader densityCompute;

    [Header("Pooling")]
    public bool usePooling = true;
    public int prewarm = 16;             // how many chunks to create up-front
    public int maxPoolSize = 2048;       // safety cap

    [Header("Streaming")]
    [Tooltip("Max milliseconds per frame spent uploading finished chunk meshes (colliders included). At least one is applied per frame while results are pending.")]
    public float generationBudgetMs = 6f;
    [Tooltip("Background mesh-building threads. 0 = auto (cores - 2, clamped 1..6).")]
    [Range(0, 8)] public int workerThreads = 0;

    [Header("Terrain modification")]
    [Tooltip("Maximum accumulated lowering per voxel, in meters.")]
    public float modMaxDepth = 30.35f;
    [Tooltip("Seconds an edited voxel stays in the short-term cache after its last touch before moving to the persistent (run-lifetime) cache.")]
    public float modShortTermSeconds = 20f;
    public float modFlushInterval = 5f;
    [Tooltip("Min seconds between rebuilds of a chunk dirtied by VISUAL-only edits (footprints). Coalesces the stamp stream while sprinting; physical edits rebuild immediately.")]
    public float visualEditRebuildInterval = 0.25f;

    public struct ChunkKey : IEquatable<ChunkKey>
    {
        public int level;
        public Vector3Int coord; // in level-sized chunk units

        public bool Equals(ChunkKey o) => level == o.level && coord == o.coord;
        public override bool Equals(object o) => o is ChunkKey k && Equals(k);
        public override int GetHashCode() => (coord.GetHashCode() * 397) ^ level;
    }

    readonly Dictionary<ChunkKey, MarchingChunk> _chunks = new();
    readonly Stack<MarchingChunk> _pool = new();

    // needsMask each chunk was last generated with (level is fixed by the key)
    readonly Dictionary<ChunkKey, uint> _generatedNeeds = new();
    readonly HashSet<ChunkKey> _dirty = new();        // must regenerate even if needs match (terrain edits)
    readonly HashSet<ChunkKey> _dirtyPhysics = new(); // subset whose collider must also re-cook

    readonly HashSet<ChunkKey> _needed = new();
    readonly List<ChunkKey> _jobs = new();
    readonly List<ChunkKey> _dirtyJobs = new();
    readonly List<ChunkKey> _dirtyDeferred = new(); // see DispatchBuilds' IsPendingAnywhere collision handling
    readonly List<ChunkKey> _removeScratch = new();
    int _jobCursor, _dirtyCursor;

    // Async build pipeline. Stages, in order:
    //   _gpuPending         -> admitted (PrepareBuild done), not yet GPU-dispatched
    //   _gpuInFlightBatches -> dispatched, waiting on AsyncGPUReadback (skipped
    //                          entirely for GPU-incapable worlds -- those go
    //                          straight from admission to _readyForMeshing)
    //   _readyForMeshing    -> density filled (or GPU unavailable), waiting for
    //                          a free CPU worker slot -- the dirty/regular
    //                          WorkerCount ratio is enforced HERE, not at
    //                          admission, since this is the only stage that
    //                          competes for the genuinely scarce resource
    //   _inFlight           -> Task.Run(ChunkMesher.Build) in progress (as before)
    class InFlight
    {
        public ChunkKey key;
        public uint needsMask;
        public ChunkMeshJob job;
        public Task task;
        public bool fromDirty;
        public bool structural; // true if the chunk itself changed (new/LOD/needs) -- false means this rebuild is edit-only, base terrain unchanged; see _densityCache
        public bool countedInFlightDirty; // true only once PumpMeshingQueue actually incremented _inFlightDirty for this job -- see ApplyBuildResult's decrement
    }
    readonly List<InFlight> _inFlight = new();
    readonly List<InFlight> _gpuPending = new();
    readonly List<InFlight> _readyForMeshing = new();
    class GpuBatch { public readonly List<InFlight> jobs = new(); public int generation; }
    readonly List<GpuBatch> _gpuInFlightBatches = new();
    // Bumped by ClearAllChunks and by the _pendingRefresh (TerrainTuning/
    // Inspector) path -- both rebuild/rebind the GPU world-param buffers a
    // dispatch was recorded against. A batch's AsyncGPUReadback completion
    // callback fires on whatever later frame the GPU finishes on, entirely
    // outside this class's own queue-clearing (SubmitBatchAsync captures the
    // buffer/callback directly, not via _gpuInFlightBatches), so without this
    // check a stale callback can still land and apply density computed under
    // a world config that's since changed. Compared at completion; a
    // mismatch means "drop it, the data may not reflect the current world."
    int _gpuGeneration;
    const int kMaxConcurrentGpuBatches = 3; // AsyncGPUReadback supports multiple in-flight requests; serializing to 1 would create an artificial bubble
    const int kAdmissionCap = 64;           // total jobs allowed ahead of _inFlight -- bounds queue memory, not latency (GPU dispatch is cheap; CPU worker slots are the real throttle, enforced in PumpMeshingQueue)
    readonly Stack<ChunkMeshJob> _meshJobPool = new();
    int _inFlightDirty;
    TerrainGpuSampler _gpuSampler;

    // Per-chunk raw (pre-edit) base density, cached across rebuilds so an
    // EDIT-ONLY rebuild (footprints/digging -- the chunk itself didn't move
    // in the clipmap, its needsMask didn't change) never has to re-dispatch
    // to the GPU just to re-apply a delta on top of terrain that hasn't
    // actually changed: edits are always re-applied fresh on CPU regardless
    // (see ModifiedDensityField.SampleGridWithRawBase), only the expensive
    // base-noise evaluation is what this avoids repeating. Without this, an
    // edit-driven rebuild still pays the full AsyncGPUReadback round-trip
    // (several frames of latency) for a value that was already computed
    // moments earlier and hasn't changed -- which is what made footprint
    // updates feel slow once base density moved to GPU.
    class DensityCacheEntry
    {
        public uint needsMask; // which shape (regular + which faces) this was computed for; must match exactly to be reusable
        public float[] regular;
        public readonly float[][] faces = new float[6][];
    }
    readonly Dictionary<ChunkKey, DensityCacheEntry> _densityCache = new();

    // Coalescing for edit-driven rebuilds: earliest allowed dispatch per key,
    // and when each key last dispatched a dirty rebuild.
    readonly Dictionary<ChunkKey, float> _dirtyNotBefore = new();
    readonly Dictionary<ChunkKey, float> _lastDirtyDispatch = new();

    // Staged LOD swap: chunks replaced by a different level are NOT released
    // immediately (that would show a hole until the replacement streams in).
    // They keep rendering while their replacements generate hidden; when a
    // retiring chunk's whole covering set is ready, it is released and the
    // replacements are shown in the same frame.
    class RetiringChunk
    {
        public ChunkKey key;
        public uint needsMask;
        public MarchingChunk chunk;
        public readonly List<ChunkKey> required = new();
    }
    readonly List<RetiringChunk> _retiring = new();
    readonly Dictionary<ChunkKey, int> _showBlockers = new(); // key -> #retiring chunks waiting on it

    // Replacement chunks for retiring chunks near the player. These jump the
    // job queue: until they generate, the player is looking at (and leaving
    // footprints "under") a stale retired mesh that never remeshes.
    readonly HashSet<ChunkKey> _priorityKeys = new();

    Vector3Int[] _boxMin;      // per level, min corner in level-k chunk units
    Vector3Int[] _boxScratch;
    bool _boxesValid;
    bool _pendingRefresh;
    float _lastFlush;

    // Terrain modification stack. Render field sees all edits; physics field
    // only sees edits stamped with affectsPhysics, so visual-only edits
    // (footprints) never re-cook colliders.
    TerrainModificationSystem _modSystem;
    ModifiedDensityField _renderField;
    ModifiedDensityField _physicsField;

    // Properties that read from worldConfig
    Vector3Int CellsPerChunk => worldConfig ? worldConfig.cellsPerChunk : new Vector3Int(32, 32, 32);
    float CellSize => worldConfig ? worldConfig.cellSize : 0.5f;
    float IsoLevel => worldConfig ? worldConfig.isoLevel : 0f;
    int Levels => Mathf.Clamp(worldConfig ? worldConfig.clipmapLevels : 6, 1, 12);
    // Extent must be even and >= 6 for the box math (margins + even alignment).
    int Extent => Mathf.Max(6, (worldConfig ? worldConfig.chunksPerLevel : 6) & ~1);

    float ChunkWorldSize => CellsPerChunk.x * CellSize;
    float LevelChunkSize(int k) => ChunkWorldSize * (1 << k);

    int WorkerCount => Mathf.Clamp(workerThreads > 0 ? workerThreads : SystemInfo.processorCount - 2, 1, 8);

    DensityField BaseField
    {
        get
        {
            if (worldConfig && worldConfig.EffectiveDensity) return worldConfig.EffectiveDensity;
            return chunkPrefab ? chunkPrefab.densityField : null;
        }
    }

    // PlanetField wraps a BiomeWorld rather than being one, so anything that
    // needs to reach the actual biome list (material props, terrain material)
    // must unwrap it first -- otherwise a straight "BaseField as
    // BiomeDensityField" check silently finds nothing once a world is
    // wrapped in a planet.
    PlanetField ActivePlanet => BaseField as PlanetField;
    BiomeDensityField ActiveBiomeWorld => BaseField as BiomeDensityField ?? ActivePlanet?.surface as BiomeDensityField;

    // Public entry point for all terrain edits (StampSphere / StampLine, with
    // an affectsPhysics flag per edit).
    public TerrainModificationSystem Modifications { get { EnsureField(); return _modSystem; } }

    // Surface hardness at a world position (0 soft .. 1 hard rock); blends
    // across biome transitions. Used by footprints/tools.
    public float HardnessAt(Vector3 worldPos)
    {
        EnsureField();
        return _renderField != null ? _renderField.SurfaceHardness(worldPos) : 0f;
    }

    // Lets player/gravity code ask "are we on a planet, and if so where's its
    // center/radius" without needing to know about PlanetField or WorldConfig
    // itself -- true exactly when WorldConfig.useGlobe is on.
    public bool TryGetPlanetCenter(out Vector3 center, out float radius)
    {
        if (BaseField is PlanetField p) { center = p.center; radius = p.radius; return true; }
        center = default;
        radius = 0f;
        return false;
    }

    // Where to spawn/drop something onto a planet from: comfortably above
    // the tallest possible terrain (PlanetField.SafeSpawnRadius), not an
    // exact surface find -- see PlayerBootstrap for why.
    public bool TryGetPlanetSpawnPoint(out Vector3 center, out float spawnRadius)
    {
        if (BaseField is PlanetField p) { center = p.center; spawnRadius = p.SafeSpawnRadius(); return true; }
        center = default;
        spawnRadius = 0f;
        return false;
    }

    void OnEnable()
    {
        TerrainTuning.Changed += OnTerrainTuningChanged;
    }

    void OnDisable()
    {
        TerrainTuning.Changed -= OnTerrainTuningChanged;
    }

    // Inspector tweaks to any Biome / density-field asset hot-reload the world.
    void OnTerrainTuningChanged()
    {
        if (Application.isPlaying && isActiveAndEnabled)
            _pendingRefresh = true;
    }

    // Push per-biome material data (palette, style, detail textures) from the
    // Biome assets into the terrain material via a property block — no asset
    // mutation, applied to every chunk renderer.
    //
    // DATA-DRIVEN by design: biomes[i] maps to vertex-color CHANNEL i (0 =
    // implicit remainder, 1 = R, 2 = G, 3 = B) — that mapping is fixed and
    // just reflects how BiomeDensityField.GetVertexColor bakes weights. But
    // WHICH SHADER MODULE renders channel i is read from that biome's own
    // Biome.surfaceStyle, not hardcoded per channel. Swap which Biome asset
    // sits at an index and its style follows it to whatever channel it lands
    // on — SandTerrain.shader's EVALUATE_CHANNEL macro dispatches on this
    // per-channel style tag at runtime.
    MaterialPropertyBlock _biomeProps;
    static readonly string[] ChannelSuffix = { "0", "1", "2", "3" };
    void BuildBiomeMaterialProps()
    {
        var world = ActiveBiomeWorld;
        if (world == null || world.biomes == null) { _biomeProps = null; return; }
        _biomeProps = new MaterialPropertyBlock();

        // Planet mode: tell the shader to use radial "up" instead of world Y
        // for slope tinting / sediment banding (see SandTerrain.shader's
        // Frag). Left at the shader's defaults (flat world) otherwise.
        var planet = ActivePlanet;
        if (planet != null)
        {
            _biomeProps.SetFloat("_UseSphericalUp", 1f);
            _biomeProps.SetVector("_PlanetCenter", new Vector4(planet.center.x, planet.center.y, planet.center.z, 0f));
        }

        int n = Mathf.Min(world.biomes.Length, ChannelSuffix.Length);
        for (int i = 0; i < n; i++)
        {
            var b = world.biomes[i];
            if (b == null) continue;
            string suf = ChannelSuffix[i];

            _biomeProps.SetFloat("_Chan" + suf + "Style", (float)b.surfaceStyle);
            _biomeProps.SetFloat("_Chan" + suf + "Sharpness", Mathf.Max(0.01f, b.blendSharpness));
            _biomeProps.SetColor("_Chan" + suf + "Flat", b.colorFlat);
            _biomeProps.SetColor("_Chan" + suf + "Steep", b.colorSteep);

            // Style-specific extras that stay global material properties
            // (only one active source per style — see Biome.albedo's tooltip).
            switch (b.surfaceStyle)
            {
                case Biome.SurfaceStyle.Sand:
                    if (b.albedo != null) _biomeProps.SetTexture("_MainTex", b.albedo);
                    if (b.normalMap != null) _biomeProps.SetTexture("_NormalTex", b.normalMap);
                    break;
                case Biome.SurfaceStyle.Canyon:
                    _biomeProps.SetColor("_CanyonFloorColor", b.colorFlat);
                    break;
                case Biome.SurfaceStyle.Alien:
                    if (b.albedo != null) _biomeProps.SetTexture("_PebbleTex", b.albedo);
                    if (b.normalMap != null) _biomeProps.SetTexture("_PebbleNormal", b.normalMap);
                    break;
                case Biome.SurfaceStyle.Frost:
                    // fully procedural (BiomeFrost.hlsl) -- _Chan{i}Flat/Steep
                    // above already cover its ice palette.
                    break;
            }
        }
    }

    void ApplyBiomeProps(MarchingChunk chunk)
    {
        if (_biomeProps == null) return;
        var r = chunk.GetComponent<MeshRenderer>();
        if (r != null) r.SetPropertyBlock(_biomeProps);
    }

    // Lets a BiomeWorld asset pick which material (and therefore shader) the
    // whole world renders with, instead of that being fixed on the chunk
    // prefab. Falls back to the prefab's own material when unset.
    void ApplyTerrainMaterial(MarchingChunk chunk)
    {
        var world = ActiveBiomeWorld;
        if (world == null || world.terrainMaterial == null) return;
        var r = chunk.GetComponent<MeshRenderer>();
        if (r != null) r.sharedMaterial = world.terrainMaterial;
    }

    void EnsureField()
    {
        if (_modSystem == null)
        {
            _modSystem = new TerrainModificationSystem(CellSize, modShortTermSeconds) { maxDepth = modMaxDepth };
            _modSystem.RegionModified += OnRegionModified;
        }
        if (_modSystem.baseField == null) _modSystem.baseField = BaseField;

        // Wrappers can survive a domain reload (recompile / play transition)
        // with their non-serialized cache references lost, or point at an old
        // base field. Detect and rebuild instead of crashing mid-mesh.
        bool stale = _renderField != null &&
                     (!_renderField.IsValid || _renderField.source != BaseField);
        if (stale || (_physicsField != null && !_physicsField.IsValid))
        {
            DestroyField(ref _renderField);
            DestroyField(ref _physicsField);
        }

        if (_renderField == null && BaseField != null)
        {
            _renderField = ModifiedDensityField.Create(BaseField, _modSystem.visual, _modSystem.physical);
            _physicsField = ModifiedDensityField.Create(BaseField, _modSystem.physical);
            BuildBiomeMaterialProps();
            RefreshGpuParams();
        }
    }

    // Rebuilds the GPU sampler's persistent per-world parameter buffers.
    // Cheap to call speculatively (no-ops if densityCompute is unset); a
    // stale buffer would mean the GPU path silently drifts from whatever
    // the CPU fields' current Inspector values are, so this has to run
    // whenever those can have changed -- field (re)creation here, and
    // TerrainTuning changes (see the _pendingRefresh handling in Update()).
    void RefreshGpuParams()
    {
        if (_gpuSampler == null)
        {
            if (densityCompute == null) return;
            _gpuSampler = new TerrainGpuSampler(densityCompute);
        }
        _gpuSampler.RefreshWorldParams(BaseField);
    }

    static void DestroyField(ref ModifiedDensityField f)
    {
        if (f == null) { return; }
#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(f);
        else Destroy(f);
#else
        Destroy(f);
#endif
        f = null;
    }

    void Start()
    {
        if (Application.isPlaying)
        {
            CleanupStaleChildren();
            if (usePooling) PrewarmPool();
        }

        RecomputeTargets();

        // Generate the player's immediate surroundings synchronously so there
        // is ground (and a collider) to stand on; the rest streams in.
        if (_boxesValid && target)
        {
            float s0 = LevelChunkSize(0);
            var c = FloorCoord(target.position, s0);
            var keys = new List<ChunkKey>();
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        keys.Add(new ChunkKey { level = 0, coord = c + new Vector3Int(dx, dy, dz) });
            BuildBatchSync(keys);
        }
    }

    void OnDestroy()
    {
        // Let in-flight tasks finish so pooled jobs aren't mutated mid-build.
        foreach (var f in _inFlight)
        {
            try { f.task?.Wait(1000); } catch { }
        }
        _inFlight.Clear();

        DestroyField(ref _renderField);
        DestroyField(ref _physicsField);
        _gpuSampler?.Dispose();
    }

    void CleanupStaleChildren()
    {
        // Chunks serialized into the scene from edit mode aren't tracked at
        // play start; destroy them so they don't overlap fresh terrain.
        var stale = GetComponentsInChildren<MarchingChunk>(true);
        foreach (var ch in stale)
            if (ch) Destroy(ch.gameObject);
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) _pendingRefresh = true;
    }

    // Full-regen path shared by OnValidate/TerrainTuning changes. Everything
    // here is idempotent, so it is safe to call speculatively.
    void ApplyPendingRefresh()
    {
        _pendingRefresh = false;
        _generatedNeeds.Clear(); // settings may have changed: regenerate all (streamed)
        _boxesValid = false;
        RefreshGpuParams(); // keep GPU tunables in sync with whatever triggered this
        _densityCache.Clear(); // base density itself may have changed -- every cached entry is now potentially stale
        _gpuGeneration++; // any already-dispatched batch's readback now reflects the pre-refresh params -- see field comment
    }

    void Update()
    {
        if (!target) return;

        if (_pendingRefresh) ApplyPendingRefresh();

        if (BoxesChanged())
            RecomputeTargets(); // only when the player crosses a clipmap boundary

        ApplyCompletedBuilds();
        DispatchBuilds();
        StageGpuBatch();
        PumpMeshingQueue();
        SweepRetiring();

        if (_modSystem != null && Time.time - _lastFlush > modFlushInterval)
        {
            _lastFlush = Time.time;
            _modSystem.SetShortTermSeconds(modShortTermSeconds);
            _modSystem.maxDepth = modMaxDepth;
            _modSystem.Flush(Time.time);
        }
    }

    // Convenience wrapper; prefer Modifications.StampSphere/StampLine directly.
    // depth > 0 lowers the terrain around center (visual-only by default).
    public void ModifyTerrain(Vector3 center, float radius, float depth, bool affectsPhysics = false)
    {
        Modifications.StampSphere(center, radius, depth, affectsPhysics);
    }

    // Remesh every generated chunk (at every level) whose samples or gradients
    // can see the edit. Coarser levels must be included: they sample the same
    // field, and skipping them would open seams at LOD boundaries crossing the
    // edit. Colliders only re-cook for physics-affecting edits.
    void OnRegionModified(Bounds bounds, bool affectsPhysics)
    {
        for (int k = 0; k < Levels; k++)
        {
            float S = LevelChunkSize(k);
            float cellK = S / CellsPerChunk.x;
            float pad = CellSize /* trilinear support */ + 0.5f * cellK /* gradient eps */;

            Vector3Int qMin = FloorCoord(bounds.min - Vector3.one * pad, S);
            Vector3Int qMax = FloorCoord(bounds.max + Vector3.one * pad, S);
            for (int z = qMin.z; z <= qMax.z; z++)
                for (int y = qMin.y; y <= qMax.y; y++)
                    for (int x = qMin.x; x <= qMax.x; x++)
                    {
                        var key = new ChunkKey { level = k, coord = new Vector3Int(x, y, z) };
                        // Generated chunks need a remesh; so do chunks whose
                        // FIRST build is currently anywhere in the pipeline
                        // (GPU-pending, GPU-in-flight, ready-for-meshing, or
                        // CPU-in-flight) -- it may have already sampled
                        // pre-edit data at whatever stage it's at, and
                        // without this the edit is silently lost for that
                        // chunk: a permanent crack.
                        if (!_generatedNeeds.ContainsKey(key) && !IsPendingAnywhere(key)) continue;
                        if (affectsPhysics) _dirtyPhysics.Add(key);
                        if (_dirty.Add(key))
                        {
                            _dirtyJobs.Add(key);
                            // Visual-only edits coalesce: rebuild at most every
                            // visualEditRebuildInterval per chunk, so a sprint's
                            // stamp stream (30+/s) batches instead of flooding
                            // the workers. Physical edits rebuild immediately.
                            float notBefore = 0f;
                            if (!affectsPhysics && _lastDirtyDispatch.TryGetValue(key, out float last))
                                notBefore = last + visualEditRebuildInterval;
                            _dirtyNotBefore[key] = notBefore;
                        }
                        else if (affectsPhysics)
                        {
                            _dirtyNotBefore[key] = 0f; // upgrade pending visual to immediate
                        }
                    }
        }
    }

    // ========================================================================
    // Clipmap boxes. Built bottom-up: level 0 tracks the player (snapped to
    // 2-chunk alignment); each parent box wraps the child with >= 1-chunk
    // margins on an even-aligned position. Verified: parity, margins and
    // player containment hold for all positions with Extent >= 6.
    // ========================================================================
    void ComputeBoxes(Vector3 p, Vector3Int[] dst)
    {
        int L = Levels, E = Extent;
        float s1 = LevelChunkSize(1);
        Vector3Int c0 = FloorCoord(p + Vector3.one * (0.5f * s1), s1);
        int half = 2 * (E / 4);
        dst[0] = 2 * c0 - new Vector3Int(half, half, half);

        for (int k = 1; k < L; k++)
        {
            Vector3Int c = dst[k - 1];
            dst[k] = new Vector3Int(
                ParentAxis(c.x, E),
                ParentAxis(c.y, E),
                ParentAxis(c.z, E));
        }
    }

    static int ParentAxis(int childMin, int E)
    {
        int c = childMin >> 1;            // hole min in parent-level units (childMin is even)
        int lo = c - E / 2 + 1, hi = c - 1;
        int mn = Mathf.Clamp(c - E / 4, lo, hi);
        if ((mn & 1) != 0) mn = (mn + 1 <= hi) ? mn + 1 : mn - 1;
        return mn;
    }

    bool BoxesChanged()
    {
        if (_boxMin == null || _boxMin.Length != Levels)
        {
            _boxMin = new Vector3Int[Levels];
            _boxScratch = new Vector3Int[Levels];
            _boxesValid = false;
        }
        ComputeBoxes(target.position, _boxScratch);
        bool changed = !_boxesValid;
        for (int k = 0; k < Levels && !changed; k++)
            if (_boxScratch[k] != _boxMin[k]) changed = true;
        if (changed) { _boxScratch.CopyTo(_boxMin, 0); _boxesValid = true; }
        return changed;
    }

    bool InBox(int level, Vector3Int q)
    {
        var d = q - _boxMin[level];
        int E = Extent;
        return d.x >= 0 && d.x < E && d.y >= 0 && d.y < E && d.z >= 0 && d.z < E;
    }

    // Is this level-k cell covered by the finer level's box?
    bool CoveredByFiner(int level, Vector3Int q)
    {
        if (level == 0) return false;
        var holeMin = _boxMin[level - 1] / 2; // exact: box mins are even
        var d = q - holeMin;
        int h = Extent / 2;
        return d.x >= 0 && d.x < h && d.y >= 0 && d.y < h && d.z >= 0 && d.z < h;
    }

    bool IsDesired(ChunkKey key) => InBox(key.level, key.coord) && !CoveredByFiner(key.level, key.coord);

    // ========================================================================
    // Target computation. Runs only when a clipmap box moves (or settings
    // change): releases out-of-range chunks and queues changed ones near-first.
    // ========================================================================
    void RecomputeTargets()
    {
        if (!worldConfig || !chunkPrefab || !target) return;
        EnsureField();
        if (_renderField == null)
        {
            Debug.LogWarning("[MCChunkManager] No density field assigned (worldConfig.defaultDensity or chunk prefab).");
            return;
        }
        if (!_boxesValid) BoxesChanged();

        int L = Levels, E = Extent;

        _needed.Clear();
        for (int k = 0; k < L; k++)
            for (int z = 0; z < E; z++)
                for (int y = 0; y < E; y++)
                    for (int x = 0; x < E; x++)
                    {
                        var q = _boxMin[k] + new Vector3Int(x, y, z);
                        if (!CoveredByFiner(k, q))
                            _needed.Add(new ChunkKey { level = k, coord = q });
                    }

        // Retire chunks that fell out of the target set. They stay visible
        // until their replacements are generated (see SweepRetiring).
        _removeScratch.Clear();
        foreach (var kv in _chunks)
            if (!_needed.Contains(kv.Key)) _removeScratch.Add(kv.Key);
        foreach (var key in _removeScratch)
        {
            var e = new RetiringChunk { key = key, chunk = _chunks[key] };
            _generatedNeeds.TryGetValue(key, out e.needsMask);
            _retiring.Add(e);
            _chunks.Remove(key);
            _generatedNeeds.Remove(key);
            _dirty.Remove(key);
            _dirtyPhysics.Remove(key);
            _dirtyNotBefore.Remove(key);
            _lastDirtyDispatch.Remove(key);
        }

        // Resurrect retiring chunks whose key became desired again (fast
        // back-and-forth movement): they still hold valid geometry.
        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            var e = _retiring[i];
            if (!_needed.Contains(e.key) || _chunks.ContainsKey(e.key)) continue;
            _chunks[e.key] = e.chunk;
            _generatedNeeds[e.key] = e.needsMask;
            e.chunk.SetVisible(true);
            _retiring.RemoveAt(i);
        }

        // Re-resolve each retiring chunk's covering set against the new boxes
        // (a previous recompute's requirements may no longer be desired).
        _showBlockers.Clear();
        foreach (var e in _retiring)
        {
            e.required.Clear();
            ResolveCovering(e.key.level, e.key.coord, e.required);
            foreach (var k in e.required)
                _showBlockers[k] = _showBlockers.TryGetValue(k, out int b) ? b + 1 : 1;
        }

        // Swap groups the player can see up close must finish first: collect
        // the replacement keys of retiring chunks near the player.
        Vector3 p = target.position;
        _priorityKeys.Clear();
        float prioDist = ChunkWorldSize * 1.5f;
        foreach (var e in _retiring)
        {
            float S = LevelChunkSize(e.key.level);
            Vector3 mn = (Vector3)e.key.coord * S;
            Vector3 closest = Vector3.Max(mn, Vector3.Min(p, mn + Vector3.one * S));
            if ((closest - p).sqrMagnitude <= prioDist * prioDist)
                foreach (var k in e.required) _priorityKeys.Add(k);
        }

        // Queue chunks whose desired state differs from what they have:
        // player-adjacent swap completions first, then nearest first.
        _jobs.Clear();
        _jobCursor = 0;
        foreach (var key in _needed)
            if (NeedsWork(key)) _jobs.Add(key);
        _jobs.Sort((a, b) =>
        {
            bool pa = _priorityKeys.Contains(a), pb = _priorityKeys.Contains(b);
            if (pa != pb) return pa ? -1 : 1;
            return ChunkDistSq(a, p).CompareTo(ChunkDistSq(b, p));
        });

        // dirty jobs get folded into the regular queue on recompute
        _dirtyJobs.Clear();
        _dirtyCursor = 0;
    }

    float ChunkDistSq(ChunkKey key, Vector3 p)
    {
        float S = LevelChunkSize(key.level);
        Vector3 center = ((Vector3)key.coord + Vector3.one * 0.5f) * S;
        return (center - p).sqrMagnitude;
    }

    bool NeedsWork(ChunkKey key)
    {
        if (IsPendingAnywhere(key)) return false;
        if (!_chunks.ContainsKey(key)) return true;
        if (_dirty.Contains(key)) return true;
        if (!_generatedNeeds.TryGetValue(key, out uint prev)) return true;
        return prev != ComputeNeeds(key).Mask;
    }

    // Which desired chunks cover the volume of cell (level, q)? Either the cell
    // itself, its finer children (recursively, if that region was refined), or
    // a coarser ancestor (if it left this level's box). An empty result means
    // nothing will cover it (fell off the world's far edge) -> release at once.
    // Recursion is bounded: downward descent only happens inside CoveredByFiner
    // regions, which are capped by each level's hole size.
    void ResolveCovering(int level, Vector3Int q, List<ChunkKey> output)
    {
        if (level >= Levels || level < 0) return;

        if (InBox(level, q))
        {
            if (!CoveredByFiner(level, q))
            {
                output.Add(new ChunkKey { level = level, coord = q });
                return;
            }
            for (int dz = 0; dz <= 1; dz++)
                for (int dy = 0; dy <= 1; dy++)
                    for (int dx = 0; dx <= 1; dx++)
                        ResolveCovering(level - 1, q * 2 + new Vector3Int(dx, dy, dz), output);
        }
        else
        {
            // outside this level's box: a coarser chunk covers this volume
            // (arithmetic shift floors correctly for negative coords)
            ResolveCovering(level + 1, new Vector3Int(q.x >> 1, q.y >> 1, q.z >> 1), output);
        }
    }

    bool IsReady(ChunkKey key) => _chunks.ContainsKey(key) && _generatedNeeds.ContainsKey(key);

    bool IsShowBlocked(ChunkKey key) => _showBlockers.TryGetValue(key, out int b) && b > 0;

    // Release retiring chunks whose replacements are all generated, and reveal
    // those replacements in the same frame — no visible hole, no overlap.
    void SweepRetiring()
    {
        if (_retiring.Count == 0) return;

        for (int i = _retiring.Count - 1; i >= 0; i--)
        {
            var e = _retiring[i];

            bool ready = true;
            for (int r = 0; r < e.required.Count && ready; r++)
                ready = IsReady(e.required[r]);
            if (!ready) continue;

            ReleaseChunk(e.chunk);
            _retiring.RemoveAt(i);
            _densityCache.Remove(e.key); // chunk is truly gone (not just retired-but-resurrectable); stop holding its cached density

            foreach (var k in e.required)
            {
                if (!_showBlockers.TryGetValue(k, out int b)) continue;
                if (b <= 1)
                {
                    _showBlockers.Remove(k);
                    if (_chunks.TryGetValue(k, out var c) && c) c.SetVisible(true);
                }
                else _showBlockers[k] = b - 1;
            }
        }
    }

    public TransitionNeeds ComputeNeeds(ChunkKey key)
    {
        int k = key.level;
        var q = key.coord;
        int neighbors = 0;
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0 && dz == 0) continue;
                    if (CoveredByFiner(k, q + new Vector3Int(dx, dy, dz)))
                        neighbors |= 1 << TransitionNeeds.NeighborBit(dx, dy, dz);
                }
        return new TransitionNeeds
        {
            px = CoveredByFiner(k, q + FaceDirs[0]),
            nx = CoveredByFiner(k, q + FaceDirs[1]),
            py = CoveredByFiner(k, q + FaceDirs[2]),
            ny = CoveredByFiner(k, q + FaceDirs[3]),
            pz = CoveredByFiner(k, q + FaceDirs[4]),
            nz = CoveredByFiner(k, q + FaceDirs[5]),
            neighbors = neighbors,
        };
    }

    // ========================================================================
    // Async build pipeline: worker threads run ChunkMesher.Build; the main
    // thread uploads finished results under a time budget. Ordering only
    // affects latency, never correctness (validated at apply time).
    // ========================================================================
    bool IsInFlight(ChunkKey key)
    {
        for (int i = 0; i < _inFlight.Count; i++)
            if (_inFlight[i].key.Equals(key)) return true;
        return false;
    }

    // Is this key anywhere in the pipeline -- any of the GPU staging queues,
    // or already CPU-meshing? Same idea as IsInFlight, extended to the new
    // intermediate stages the GPU path inserts.
    bool IsPendingAnywhere(ChunkKey key)
    {
        if (IsInFlight(key)) return true;
        for (int i = 0; i < _gpuPending.Count; i++)
            if (_gpuPending[i].key.Equals(key)) return true;
        for (int i = 0; i < _readyForMeshing.Count; i++)
            if (_readyForMeshing[i].key.Equals(key)) return true;
        for (int b = 0; b < _gpuInFlightBatches.Count; b++)
        {
            var jobs = _gpuInFlightBatches[b].jobs;
            for (int i = 0; i < jobs.Count; i++)
                if (jobs[i].key.Equals(key)) return true;
        }
        return false;
    }

    ChunkMeshJob GetMeshJob() => _meshJobPool.Count > 0 ? _meshJobPool.Pop() : new ChunkMeshJob();

    // Admission only: pulls keys off the pre-sorted priority/nearest-first
    // queues (unchanged) and routes each into the pipeline. Deliberately NOT
    // gated by the dirty/regular WorkerCount ratio here -- GPU dispatch
    // isn't CPU-thread-scarce, so admission can freely let dirty jobs
    // through; the ratio is enforced once, at PumpMeshingQueue's promotion
    // into _inFlight, which is the only stage that actually competes for
    // the scarce WorkerCount slots.
    void DispatchBuilds()
    {
        int pipelineDepth = _gpuPending.Count + _readyForMeshing.Count + _inFlight.Count;
        foreach (var batch in _gpuInFlightBatches) pipelineDepth += batch.jobs.Count;

        while (pipelineDepth < kAdmissionCap)
        {
            bool dirtyReady = _dirtyCursor < _dirtyJobs.Count &&
                              (!_dirtyNotBefore.TryGetValue(_dirtyJobs[_dirtyCursor], out float nb) || Time.time >= nb);
            bool regularPending = _jobCursor < _jobs.Count;

            ChunkKey key;
            bool fromDirty;
            if (dirtyReady)
            {
                key = _dirtyJobs[_dirtyCursor++];
                fromDirty = true;
            }
            else if (regularPending)
            {
                key = _jobs[_jobCursor++];
                fromDirty = false;
            }
            else break;

            if (IsPendingAnywhere(key))
            {
                // A dirty (edit) request for a chunk whose EARLIER rebuild is
                // still somewhere in the pipeline (GPU has multi-frame
                // latency, so this is common -- e.g. a rapid footprint
                // stream while walking across one chunk). PrepareBuild
                // already claimed/cleared this chunk's dirty flag when that
                // earlier rebuild was admitted, on the promise that any edit
                // landing afterward would "re-add and re-queue" it -- but
                // just dropping it here (as this used to do) breaks that
                // promise: the cursor consumes it and nothing ever retries,
                // so the edit is silently lost until some unrelated later
                // edit happens to re-dirty the same chunk. Defer it instead
                // (spliced back into _dirtyJobs after this loop, so it's
                // retried next frame, not immediately -- re-trying inside
                // THIS same pass would just collide again and spin).
                // Regular (non-dirty) collisions don't need this: they
                // self-heal on the next RecomputeTargets rescan.
                if (fromDirty) _dirtyDeferred.Add(key);
                continue;
            }
            var inflight = PrepareBuild(key);
            if (inflight == null) continue;

            inflight.fromDirty = fromDirty;
            pipelineDepth++;

            // Hoisted empty-chunk skip (used to run only inside
            // ChunkMesher.Build): finishing here means a provably-empty
            // chunk never costs a GPU batch slot at all.
            if (TryFastEmptySkip(inflight))
            {
                FinishEmptyBuild(inflight);
            }
            else if (!inflight.structural && !inflight.job.refreshCollider &&
                     _densityCache.TryGetValue(key, out var cached) && cached.needsMask == inflight.needsMask)
            {
                // Edit-only rebuild (footprint/dig) with a matching cached
                // base density already in hand -- skip the GPU dispatch
                // entirely. (refreshCollider excluded deliberately: a
                // physics-affecting edit needs a fresh collider grid we
                // haven't cached, so those still go through GPU as normal --
                // they're already exempt from the visual-edit throttle and
                // rarer than the footprint stream this exists for.)
                ApplyDensityCache(inflight.job, cached);
                _readyForMeshing.Add(inflight);
            }
            else if (_gpuSampler != null && _gpuSampler.IsWorldGpuCapable)
            {
                _gpuPending.Add(inflight);
            }
            else
            {
                _readyForMeshing.Add(inflight);
            }
        }

        // Splice deferred dirty collisions back in for next frame's pass --
        // done here, outside the while loop, so they can't be immediately
        // re-examined (and re-collide) within this same call.
        if (_dirtyDeferred.Count > 0)
        {
            _dirtyJobs.AddRange(_dirtyDeferred);
            _dirtyDeferred.Clear();
        }
    }

    static void ApplyDensityCache(ChunkMeshJob job, DensityCacheEntry cached)
    {
        // Clone rather than hand out the cache's own arrays: ChunkMesher
        // one-shot-consumes (nulls) job.gpuRaw* after use, which must not
        // reach into and destroy the cached copy.
        job.gpuRawRegular = (float[])cached.regular.Clone();
        for (int i = 0; i < 6; i++)
            job.gpuRawFaces[i] = cached.faces[i] != null ? (float[])cached.faces[i].Clone() : null;
    }

    // Snapshots a job's freshly GPU-filled raw density into the cache, for
    // future edit-only rebuilds of this same chunk to reuse. Call right
    // after a successful GPU fill, before ChunkMesher.Build consumes (nulls)
    // the job's own gpuRaw* buffers.
    void SaveDensityCache(InFlight inflight)
    {
        var job = inflight.job;
        if (job.gpuRawRegular == null) return; // GPU wasn't used/failed for this job
        var entry = new DensityCacheEntry { needsMask = inflight.needsMask, regular = (float[])job.gpuRawRegular.Clone() };
        for (int i = 0; i < 6; i++)
            if (job.gpuRawFaces[i] != null) entry.faces[i] = (float[])job.gpuRawFaces[i].Clone();
        _densityCache[inflight.key] = entry;
    }

    bool TryFastEmptySkip(InFlight inflight)
    {
        var job = inflight.job;
        if (job.modsOverlapChunk) return false; // a carved cave inside "solid" rock must still be meshed
        ChunkMesher.GetEffectiveGrid(job, out int nx, out int ny, out int nz, out float step);
        Vector3 boxMin = job.origin;
        Vector3 boxMax = job.origin + new Vector3(nx, ny, nz) * step;
        return job.renderField.TryGetEmptySkip(boxMin, boxMax);
    }

    void FinishEmptyBuild(InFlight inflight)
    {
        inflight.job.ResetOutputs();
        inflight.job.colliderSharesRenderMesh = true; // matches ChunkMesher.Build's own skip path exactly
        ApplyBuildResult(inflight);
    }

    static void ClearGpuRaw(ChunkMeshJob job)
    {
        job.gpuRawRegular = null;
        for (int i = 0; i < job.gpuRawFaces.Length; i++) job.gpuRawFaces[i] = null;
        job.gpuRawCollision = null;
    }

    // Shared staleness handling for the two new pre-CPU-meshing gates (GPU
    // admission in StageGpuBatch, GPU-done promotion in StageGpuBatch's
    // readback callback). Deliberately simpler than ApplyBuildResult's own
    // stale-branch (which also calls NeedsWork): NeedsWork's IsPendingAnywhere
    // check would see this job still sitting in the very queue we're in the
    // middle of clearing, which risks a false "already pending" negative --
    // IsDesired alone is a safe, if slightly less precise, substitute here.
    // Nothing is ever truly lost either way: RecomputeTargets/OnRegionModified
    // always repopulate _jobs/_dirtyJobs for anything still desired.
    void RequeueOrDrop(InFlight f)
    {
        var job = f.job;
        job.ResetOutputs();
        ClearGpuRaw(job);
        _meshJobPool.Push(job);

        if (IsDesired(f.key))
            _jobs.Insert(Mathf.Min(_jobCursor, _jobs.Count), f.key);
    }

    // Batches every pending job's grid-fill requests into one dispatch (up
    // to kMaxConcurrentGpuBatches outstanding at once -- AsyncGPUReadback
    // supports multiple in-flight requests, so this isn't serialized to one
    // at a time). On completion, jobs move to _readyForMeshing for
    // PumpMeshingQueue to promote into actual CPU meshing.
    void StageGpuBatch()
    {
        if (_gpuSampler == null || !_gpuSampler.IsWorldGpuCapable) return;
        if (_gpuPending.Count == 0) return;
        if (_gpuInFlightBatches.Count >= kMaxConcurrentGpuBatches) return;

        var batch = new GpuBatch { generation = _gpuGeneration };
        var requests = new List<TerrainGpuSampler.Request>();

        foreach (var inflight in _gpuPending)
        {
            // Re-validate: staleness can only have grown since admission.
            if (!_boxesValid || !IsDesired(inflight.key)) { RequeueOrDrop(inflight); continue; }

            var job = inflight.job;
            var planned = ChunkMesher.PlanGridRequests(job);
            foreach (var req in planned)
            {
                var dest = new float[req.countX * req.countY * req.countZ];
                switch (req.kind)
                {
                    case ChunkMesher.PlannedRequest.Kind.Regular: job.gpuRawRegular = dest; break;
                    case ChunkMesher.PlannedRequest.Kind.Face: job.gpuRawFaces[req.face] = dest; break;
                    case ChunkMesher.PlannedRequest.Kind.Collision: job.gpuRawCollision = dest; break;
                }
                requests.Add(new TerrainGpuSampler.Request
                {
                    origin = req.origin,
                    step = req.step,
                    countX = req.countX,
                    countY = req.countY,
                    countZ = req.countZ,
                    dest = dest,
                });
            }
            batch.jobs.Add(inflight);
        }

        _gpuPending.Clear();
        if (batch.jobs.Count == 0) return;

        _gpuInFlightBatches.Add(batch);
        _gpuSampler.SubmitBatchAsync(requests, ok =>
        {
            _gpuInFlightBatches.Remove(batch);

            // The world was cleared/regenerated or its GPU params were
            // rebuilt after this batch was dispatched -- the buffers this
            // readback reflects may no longer match the current world.
            // Drop it rather than risk applying stale density.
            if (batch.generation != _gpuGeneration)
            {
                foreach (var inflight in batch.jobs)
                {
                    ClearGpuRaw(inflight.job);
                    RequeueOrDrop(inflight);
                }
                return;
            }

            foreach (var inflight in batch.jobs)
            {
                if (!ok) ClearGpuRaw(inflight.job); // GPU failed for the whole batch -- ChunkMesher falls back to its own CPU SampleGrid since the raw buffers are null
                else SaveDensityCache(inflight); // let future edit-only rebuilds of this chunk skip GPU entirely
                if (!_boxesValid || !IsDesired(inflight.key)) { RequeueOrDrop(inflight); continue; }
                _readyForMeshing.Add(inflight);
            }
        });
    }

    // Promotes _readyForMeshing jobs into actual CPU meshing (_inFlight),
    // respecting WorkerCount and the dirty/regular ratio -- this is the one
    // place that still needs that ratio (see DispatchBuilds' comment).
    void PumpMeshingQueue()
    {
        int max = WorkerCount;
        // Edit rebuilds may not crowd out streaming work (new terrain, LOD
        // swaps): while regular jobs are pending, dirty jobs get half the
        // slots at most. Without this, sprinting (a continuous footprint
        // stream) starves streaming completely and the world stops updating.
        int maxDirty = Mathf.Max(1, max / 2);

        int i = 0;
        while (_inFlight.Count < max && i < _readyForMeshing.Count)
        {
            var f = _readyForMeshing[i];
            if (f.fromDirty && _inFlightDirty >= maxDirty)
            {
                i++; // leave this dirty job queued; a regular job further down may still go through
                continue;
            }

            _readyForMeshing.RemoveAt(i); // don't advance i: next entry slides in

            if (!_boxesValid || !IsDesired(f.key)) { RequeueOrDrop(f); continue; }

            if (f.fromDirty)
            {
                f.countedInFlightDirty = true;
                _inFlightDirty++;
                _lastDirtyDispatch[f.key] = Time.time;
            }
            var job = f.job;
            f.task = Task.Run(() => ChunkMesher.Build(job));
            _inFlight.Add(f);
        }
    }

    // Validates the key still needs work and snapshots all build inputs.
    InFlight PrepareBuild(ChunkKey key)
    {
        if (!_boxesValid || !IsDesired(key)) return null;

        TransitionNeeds needs = ComputeNeeds(key);
        bool dirty = _dirty.Contains(key);
        bool structural = !_generatedNeeds.TryGetValue(key, out uint prev) || prev != needs.Mask;
        bool exists = _chunks.ContainsKey(key);
        if (!dirty && !structural && exists) return null; // up to date

        // Claim the dirty flags now; edits landing during the build re-add
        // them (and re-queue), so nothing is lost.
        _dirty.Remove(key);
        bool physicsDirty = _dirtyPhysics.Remove(key);

        float S = LevelChunkSize(key.level);
        var job = GetMeshJob();
        job.origin = (Vector3)key.coord * S;
        job.cells = CellsPerChunk;
        job.cellSize = S / CellsPerChunk.x;
        job.isoLevel = IsoLevel;
        job.densitySampling = 1;
        job.renderField = _renderField;
        job.physicsField = _physicsField;
        job.needs = needs;
        job.buildCollider = key.level == 0;
        // Colliders re-cook only when the chunk itself changed (new/LOD/needs)
        // or a physics-affecting edit touched it — never for visual-only edits.
        job.refreshCollider = structural || !exists || physicsDirty;

        Vector3 padVec = Vector3.one * (CellSize + 0.5f * job.cellSize);
        Vector3 bMin = job.origin - padVec;
        Vector3 bMax = job.origin + Vector3.one * S + padVec;
        job.physicsDiffersNearby = job.buildCollider && _modSystem.visual.OverlapsBounds(bMin, bMax);
        // Edits near this chunk disable the all-air/all-solid skip (a carved
        // cave inside "solid" rock must still be meshed).
        job.modsOverlapChunk = _modSystem.visual.OverlapsBounds(bMin, bMax) ||
                               _modSystem.physical.OverlapsBounds(bMin, bMax);

        return new InFlight { key = key, needsMask = needs.Mask, job = job, structural = structural };
    }

    void ApplyCompletedBuilds()
    {
        if (_inFlight.Count == 0) return;

        // OLDEST-FIRST (FIFO). New dispatches append at the end, so iterating
        // from the back would apply the newest completions first — with a tight
        // budget and a steady stream of fast dirty-rebuilds (footprints while
        // walking), the oldest entry starves and its chunk stays stale for a
        // long time. Forward iteration guarantees fairness.
        float t0 = Time.realtimeSinceStartup;
        int done = 0;
        int i = 0;
        while (i < _inFlight.Count)
        {
            var f = _inFlight[i];
            if (f.task == null || !f.task.IsCompleted) { i++; continue; }
            if (done > 0 && (Time.realtimeSinceStartup - t0) * 1000f >= generationBudgetMs) break;

            _inFlight.RemoveAt(i); // don't advance: next entry slides into i
            ApplyBuildResult(f);
            done++;
        }
    }

    void ApplyBuildResult(InFlight f)
    {
        // NOT f.fromDirty: jobs that finished via the hoisted empty-chunk
        // skip (FinishEmptyBuild) never went through PumpMeshingQueue's
        // promotion, so _inFlightDirty was never incremented for them --
        // decrementing based on fromDirty alone would corrupt the count.
        if (f.countedInFlightDirty) _inFlightDirty = Mathf.Max(0, _inFlightDirty - 1);

        var job = f.job;
        bool ok = job.error == null;
        if (!ok) Debug.LogException(job.error);

        // Stale? (boxes moved or needs changed while building) -> drop; the
        // queue rebuild / NeedsWork picks it up again with fresh inputs.
        bool stale = !_boxesValid || !IsDesired(f.key) || ComputeNeeds(f.key).Mask != f.needsMask;
        if (ok && !stale)
        {
            if (!_chunks.TryGetValue(f.key, out var chunk) || chunk == null)
            {
                chunk = CreateChunk(f.key);
                _chunks[f.key] = chunk;
            }
            ApplyLevelSettings(chunk, f.key);
            chunk.ApplyBuild(job);
            _generatedNeeds[f.key] = f.needsMask;
            chunk.SetVisible(!IsShowBlocked(f.key));
        }
        else if (!ok && IsDesired(f.key))
        {
            // Build threw: force a retry (the dirty claim was consumed at
            // dispatch, so without this the chunk would stay stale forever).
            if (job.refreshCollider) _dirtyPhysics.Add(f.key);
            if (_dirty.Add(f.key)) _dirtyJobs.Add(f.key);
        }
        else if (ok && stale && IsDesired(f.key) && NeedsWork(f.key))
        {
            // Rebuild with fresh inputs — at the FRONT of the remaining queue.
            // These are usually swap-critical chunks near LOD boundaries;
            // appending at the end would park them behind far-away work.
            _jobs.Insert(Mathf.Min(_jobCursor, _jobs.Count), f.key);
        }

        ClearGpuRaw(job); // insurance: normally already null (one-shot-consumed inside ChunkMesher.Build), but Build() can exit early on an exception
        job.ResetOutputs();
        _meshJobPool.Push(job);
    }

    // Synchronous build+apply for a batch of chunks -- the initial ground
    // under the player at Start(), and bulk regeneration (ProcessAllJobsNow).
    // Batches ALL of them through one (or a couple of) blocking GPU dispatch
    // instead of one per chunk: GetData()/pipeline-flush overhead is
    // nontrivial per call regardless of payload size, so doing this
    // per-chunk in a tight loop -- as a naive single-key BuildSync would --
    // turns bulk regeneration into a stall-per-chunk fest.
    // Chunk-count cap per GPU sub-batch: bounds how large a single dispatch's
    // buffers get for a big bulk regen (hundreds of chunks), independent of
    // TerrainGpuSampler's own 65535-groups-per-axis handling -- that removes
    // the hard failure, this keeps memory/copy-out cost sane regardless.
    const int kMaxChunksPerSyncBatch = 32;

    void BuildBatchSync(List<ChunkKey> keys)
    {
        // Same admission checks DispatchBuilds applies to the async path --
        // without these, every chunk in a sync batch (initial spawn burst,
        // "Generate/Refresh All Chunks") paid full GPU dispatch cost even
        // when provably empty or already covered by a cached base density,
        // which the async streaming path has skipped since the GPU port.
        var toGpu = new List<InFlight>();
        foreach (var key in keys)
        {
            var f = PrepareBuild(key);
            if (f == null) continue;

            if (TryFastEmptySkip(f))
            {
                FinishEmptyBuild(f);
                continue;
            }
            if (!f.structural && !f.job.refreshCollider &&
                _densityCache.TryGetValue(key, out var cached) && cached.needsMask == f.needsMask)
            {
                ApplyDensityCache(f.job, cached);
                ChunkMesher.Build(f.job);
                ApplyBuildResult(f);
                continue;
            }
            toGpu.Add(f);
        }

        for (int start = 0; start < toGpu.Count; start += kMaxChunksPerSyncBatch)
        {
            int count = Mathf.Min(kMaxChunksPerSyncBatch, toGpu.Count - start);
            var sub = toGpu.GetRange(start, count);
            RunGpuSyncIfAvailable(sub);
            foreach (var f in sub)
            {
                ChunkMesher.Build(f.job);
                ApplyBuildResult(f);
            }
        }
    }

    // Blocking GPU fill for a batch of already-prepared jobs (BuildBatchSync
    // only) -- ComputeBuffer.GetData() stalls the calling thread, which is
    // fine here: these paths (initial spawn, editor "Generate/Refresh All
    // Chunks") already tolerate it, and GetData() works identically in and
    // out of Play mode with no player-loop dependency (unlike pumping
    // AsyncGPUReadback.Update() manually would need).
    void RunGpuSyncIfAvailable(List<InFlight> inflights)
    {
        if (_gpuSampler == null || !_gpuSampler.IsWorldGpuCapable || inflights.Count == 0) return;

        var requests = new List<TerrainGpuSampler.Request>();
        foreach (var f in inflights)
        {
            var job = f.job;
            var planned = ChunkMesher.PlanGridRequests(job);
            foreach (var req in planned)
            {
                var dest = new float[req.countX * req.countY * req.countZ];
                switch (req.kind)
                {
                    case ChunkMesher.PlannedRequest.Kind.Regular: job.gpuRawRegular = dest; break;
                    case ChunkMesher.PlannedRequest.Kind.Face: job.gpuRawFaces[req.face] = dest; break;
                    case ChunkMesher.PlannedRequest.Kind.Collision: job.gpuRawCollision = dest; break;
                }
                requests.Add(new TerrainGpuSampler.Request
                {
                    origin = req.origin,
                    step = req.step,
                    countX = req.countX,
                    countY = req.countY,
                    countZ = req.countZ,
                    dest = dest,
                });
            }
        }
        if (requests.Count == 0) return;

        bool ok = _gpuSampler.SubmitBatchSync(requests);
        foreach (var f in inflights)
        {
            if (!ok) ClearGpuRaw(f.job);
            else SaveDensityCache(f); // let future edit-only rebuilds of this chunk skip GPU entirely
        }
    }

    void ProcessAllJobsNow()
    {
        var keys = new List<ChunkKey>();
        while (_dirtyCursor < _dirtyJobs.Count) keys.Add(_dirtyJobs[_dirtyCursor++]);
        while (_jobCursor < _jobs.Count) keys.Add(_jobs[_jobCursor++]);
        BuildBatchSync(keys);
        _dirtyJobs.Clear();
        _dirtyCursor = 0;
        SweepRetiring();
    }

    void ApplyLevelSettings(MarchingChunk chunk, ChunkKey key)
    {
        float S = LevelChunkSize(key.level);
        chunk.transform.position = (Vector3)key.coord * S;
        chunk.cells = CellsPerChunk;
        chunk.cellSize = S / CellsPerChunk.x;
        chunk.densitySampling = 1;
        chunk.isoLevel = IsoLevel;
        chunk.densityField = _renderField;
        chunk.physicsDensityField = _physicsField;

        // Only full-detail chunks need physics; the player can't reach coarser
        // rings before their region is re-generated at level 0.
        chunk.generateCollider = key.level == 0;
    }

    MarchingChunk CreateChunk(ChunkKey key)
    {
        var go = AcquireChunk();
        go.gameObject.name = $"Chunk_L{key.level}_{key.coord.x}_{key.coord.y}_{key.coord.z}";
        go.autoRegenerate = false;
        ApplyLevelSettings(go, key);
        ApplyTerrainMaterial(go);
        ApplyBiomeProps(go);
        go.gameObject.SetActive(true);
        return go;
    }

    [ContextMenu("Refresh All Chunks")]
    void RefreshExistingChunks()
    {
        if (!target)
        {
            Debug.LogWarning("No target set for chunk generation!");
            return;
        }
        SnapTargetToPlanetSurfaceIfStranded(); // same globe trap as GenerateAllChunks -- see its comment
        _generatedNeeds.Clear();
        _boxesValid = false;
        if (BoxesChanged()) { }
        RecomputeTargets();
        ProcessAllJobsNow();
    }

    // The clipmap is centred on `target`, so on a globe the target has to be
    // near the surface or generation produces nothing: every level whose box
    // fits inside the shell is (correctly) skipped as all-solid by
    // PlanetField.TryGetEmptySkip, and every level beyond it as all-air.
    //
    // In Play mode this never comes up, because PlayerBootstrap teleports the
    // player to center + up * SafeSpawnRadius before anything generates. In
    // the Editor nothing does that, and a player parked at a perfectly
    // sensible flat-world position -- near the origin -- is sitting at the
    // PLANET'S CENTRE, a full radius of solid rock away from any surface.
    // That is why "Generate All Chunks" appears to do nothing on a globe
    // while working fine both in Play mode and with useGlobe off.
    //
    // Editor-only, and only when the target is genuinely stranded: a target
    // legitimately above the peaks or inside a cave is left alone.
    void SnapTargetToPlanetSurfaceIfStranded()
    {
        if (Application.isPlaying || !target) return;
        var planet = BaseField as PlanetField;
        if (planet == null) return;
        if (!planet.TryGetSurfaceBand(out float rLo, out float rHi)) return;

        // Criterion: can LEVEL 0 -- the only full-detail, collider-bearing
        // level -- reach the shell at all? If not, the clipmap pyramid is
        // centred in the wrong place and at best a few coarse fragments come
        // out, which is not what anyone pressing this button wants.
        float reach = LevelChunkSize(0) * Extent * 0.5f;
        Vector3 rel = target.position - planet.center;
        float dist = rel.magnitude;
        if (dist > rLo - reach && dist < rHi + reach) return; // level 0 can reach ground; leave the target alone

        Vector3 dir = dist > 1e-6f ? rel / dist : Vector3.up;
        Vector3 dst = planet.center + dir * planet.SafeSpawnRadius();
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(target, "Snap terrain target to planet surface");
#endif
        target.position = dst;
        Debug.Log($"[MCChunkManager] Target sat {dist:F0}m from the planet centre, outside the " +
                  $"{rLo:F0}..{rHi:F0}m surface shell, so level-0 chunks had no ground to find and every " +
                  $"chunk was skipped as all-solid or all-air. Moved it to {dst} -- the same place " +
                  $"PlayerBootstrap drops the player in Play mode, which is why this only ever broke in the " +
                  $"Editor. Undo to put it back.", target);
    }

    [ContextMenu("Generate All Chunks")]
    public void GenerateAllChunks()
    {
        if (!target)
        {
            Debug.LogWarning("No target set for chunk generation!");
            return;
        }
        SnapTargetToPlanetSurfaceIfStranded();
        ClearAllChunks();
        if (BoxesChanged()) { }
        RecomputeTargets();
        ProcessAllJobsNow();
    }

    [ContextMenu("Clear All Chunks")]
    public void ClearAllChunks()
    {
        foreach (var f in _inFlight)
        {
            try { f.task?.Wait(1000); } catch { }
        }
        _inFlight.Clear();
        // GPU-pipeline jobs never reached Task.Run, so there's nothing to
        // wait on -- just drop them (their pooled ChunkMeshJobs are simply
        // not returned to _meshJobPool, which is harmless; GetMeshJob()
        // allocates fresh ones on demand).
        _gpuPending.Clear();
        _readyForMeshing.Clear();
        _gpuInFlightBatches.Clear();
        _densityCache.Clear();
        _gpuGeneration++; // any already-dispatched batch's readback callback is now stale -- see field comment

        foreach (var chunk in _chunks.Values)
            if (chunk != null) ReleaseChunk(chunk);
        foreach (var e in _retiring)
            if (e.chunk != null) ReleaseChunk(e.chunk);
        _chunks.Clear();
        _retiring.Clear();
        _showBlockers.Clear();
        _generatedNeeds.Clear();
        _dirty.Clear();
        _dirtyPhysics.Clear();
        _dirtyNotBefore.Clear();
        _lastDirtyDispatch.Clear();
        _inFlightDirty = 0;
        _jobs.Clear();
        _dirtyJobs.Clear();
        _jobCursor = _dirtyCursor = 0;
        _boxesValid = false;

        var allChildChunks = GetComponentsInChildren<MarchingChunk>();
        foreach (var chunk in allChildChunks)
        {
            if (chunk != null)
            {
#if UNITY_EDITOR
                DestroyImmediate(chunk.gameObject);
#else
                Destroy(chunk.gameObject);
#endif
            }
        }
    }

    void PrewarmPool()
    {
        int count = Mathf.Clamp(prewarm, 0, maxPoolSize);
        for (int i = 0; i < count; i++)
        {
            var ch = Instantiate(chunkPrefab, transform);
            ch.gameObject.name = $"PooledChunk_{i}";
            ch.gameObject.SetActive(false);
            _pool.Push(ch);
        }
    }

    MarchingChunk AcquireChunk()
    {
        if (usePooling && Application.isPlaying && _pool.Count > 0)
            return _pool.Pop();
        return Instantiate(chunkPrefab, transform);
    }

    void ReleaseChunk(MarchingChunk ch)
    {
        if (!ch) return;

        if (usePooling && Application.isPlaying && _pool.Count < maxPoolSize)
        {
            ch.gameObject.SetActive(false);
            ch.gameObject.name = "PooledChunk";
            _pool.Push(ch);
        }
        else
        {
#if UNITY_EDITOR
            DestroyImmediate(ch.gameObject);
#else
            Destroy(ch.gameObject);
#endif
        }
    }

    static Vector3Int FloorCoord(Vector3 worldPos, float size)
    {
        return new Vector3Int(
            Mathf.FloorToInt(worldPos.x / size),
            Mathf.FloorToInt(worldPos.y / size),
            Mathf.FloorToInt(worldPos.z / size));
    }

    static readonly Vector3Int[] FaceDirs =
    {
        new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
        new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
    };

    // Which of the 6 faces border FINER terrain? The coarse chunk owns the
    // transition cells (Transvoxel). Face order: +X, -X, +Y, -Y, +Z, -Z.
    public struct TransitionNeeds
    {
        public bool px, nx, py, ny, pz, nz;
        public bool Any => px || nx || py || ny || pz || nz;

        public bool Face(int f) => f switch
        {
            0 => px, 1 => nx, 2 => py, 3 => ny, 4 => pz, 5 => nz, _ => false
        };

        // Which of the 26 surrounding chunks are covered by a finer level, as
        // a bitmask indexed by NeighborBit(dx,dy,dz). The six face bits above
        // are the axis-aligned subset of this and drive transition CELLS; the
        // edge and corner bits drive nothing geometric -- they exist purely so
        // ChunkMesher can answer "is this boundary grid point also touched by
        // a finer chunk?", which is what decides its band-limiting filter
        // width. See ChunkMesher.PatchRegularBoundaryPlanes.
        public int neighbors;

        public static int NeighborBit(int dx, int dy, int dz) =>
            (dz + 1) * 9 + (dy + 1) * 3 + (dx + 1);

        public bool Neighbor(int dx, int dy, int dz) =>
            (neighbors & (1 << NeighborBit(dx, dy, dz))) != 0;

        // A boundary grid point is identified by which side of the chunk it
        // sits on per axis: -1 = at the minimum face, +1 = at the maximum,
        // 0 = interior to that axis. A neighbour touches the point when, on
        // every axis, it either shares that axis (d == 0) or lies on the same
        // side the point is on.
        public bool FinerTouches(int sx, int sy, int sz)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dz != 0 && dz != sz) continue;
                for (int dy = -1; dy <= 1; dy++)
                {
                    if (dy != 0 && dy != sy) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx != 0 && dx != sx) continue;
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        if (Neighbor(dx, dy, dz)) return true;
                    }
                }
            }
            return false;
        }

        // Enough face grids to cover every point FinerTouches reports: for each
        // finer neighbour, one face on an axis it actually differs along. Any
        // point that neighbour touches shares that side, so it lands on that
        // face's plane.
        public bool NeedsFaceGrid(int f)
        {
            if (Face(f)) return true;
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0 && dz == 0) || !Neighbor(dx, dy, dz)) continue;
                        int sf = dx != 0 ? (dx > 0 ? 0 : 1)
                               : dy != 0 ? (dy > 0 ? 2 : 3)
                                         : (dz > 0 ? 4 : 5);
                        if (sf == f) return true;
                    }
            return false;
        }

        public bool AnyFaceGrid
        {
            get
            {
                for (int f = 0; f < 6; f++) if (NeedsFaceGrid(f)) return true;
                return false;
            }
        }

        public uint Mask => (uint)neighbors; // faces are a subset, so this covers both
    }
}
