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

    [Tooltip("Optional. Shaders/Compute/TerrainMesh.compute -- runs Marching Cubes itself on the GPU for chunks that qualify (see TerrainGpuMesher.CanMesh), so their mesh is never marched on a worker thread. Requires densityCompute. Unset, or for any chunk that does not qualify, the CPU mesher runs exactly as before.")]
    public ComputeShader meshCompute;

    [Tooltip("Master switch for GPU meshing, for A/B comparison against the CPU mesher. Off means every chunk goes through ChunkMesher, as it did before TerrainMesh.compute existed.")]
    public bool gpuMeshing = true;

    [Tooltip("Lets GPU-meshed chunks write straight into their Mesh's graphics buffers instead of being read back and re-uploaded. Off falls back to the readback path -- same geometry, ~260 KB per chunk across the bus in each direction.")]
    public bool gpuMeshDirectUpload = true;

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
    TerrainGpuMesher _gpuMesher;

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
        // The arrays are reused across saves and so can be LONGER than the
        // data in them; the counts are the authority.
        public float[] regular;
        public int regularCount;
        public readonly float[][] faces = new float[6][];
        public readonly int[] faceCounts = new int[6];
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
        public readonly List<ChunkKey> required = new();     // replacements: generated hidden, revealed on release
        public readonly List<ChunkKey> participants = new(); // already-visible neighbours whose mesh must change WITH the swap
    }

    // THE TRANSIENT-CRACK FIX.
    //
    // ChunkMesher.ApplySecondaryOffset pulls a chunk's boundary vertices
    // INWARD by a quarter cell, but only on faces bordering a finer region
    // (Lengyel Eq. 4.2) -- that is what makes room for the transition sheet.
    // ComputeNeeds flips that flag from _boxMin the instant the clipmap moves.
    //
    // The staged swap is atomic for REPLACEMENTS: new fine chunks are built
    // hidden and revealed the same frame their retiring chunk is released. But
    // a neighbouring chunk whose needs mask merely CHANGED belongs to no swap
    // group. It rebuilds asynchronously and ApplyBuildResult swaps its mesh in
    // the moment it lands -- so its boundary vertices move inward while the
    // volume on the far side of that face is still being drawn by the RETIRING
    // chunk's old, un-offset geometry. A gap of up to 0.25 * step, lasting
    // until the swap completes.
    //
    // Fix: park such a rebuild instead of applying it, and apply it in the
    // same frame SweepRetiring releases the chunk it is offsetting for. The
    // chunk stays visible with its previous mesh the whole time -- parking
    // NEVER hides anything, so the worst failure here is a mesh that updates
    // slightly late, never a hole.
    readonly Dictionary<ChunkKey, int> _heldKeys = new();        // key -> #retiring chunks that still need it held
    readonly Dictionary<ChunkKey, InFlight> _heldBuilds = new(); // completed builds waiting for their swap
    readonly Dictionary<ChunkKey, float> _heldSince = new();
    readonly List<ChunkKey> _heldScratch = new();

    // A parked build must not be stranded if its swap never completes.
    const float kMaxHoldSeconds = 1.5f;
    readonly List<RetiringChunk> _retiring = new();
    readonly Dictionary<ChunkKey, int> _showBlockers = new(); // key -> #retiring chunks waiting on it

    // Replacement chunks for retiring chunks near the player. These jump the
    // job queue: until they generate, the player is looking at (and leaving
    // footprints "under") a stale retired mesh that never remeshes.
    readonly HashSet<ChunkKey> _priorityKeys = new();

    // Chunks with an off-thread PhysX cook outstanding (see MarchingChunk's
    // _bakeTask). Pumped once per frame from Update -- deliberately a manager-
    // owned list rather than an Update() on MarchingChunk itself, which would
    // put a per-frame managed->native callback on all ~1300 chunk objects to
    // service the handful that are actually baking.
    // GPU-meshing track, parallel to the density track above. A chunk goes
    // down exactly one of the two: either the GPU produces its finished mesh
    // (and it never touches a worker thread at all), or the GPU fills its
    // density grid and ChunkMesher marches it. TerrainGpuMesher.CanMesh picks.
    //   _gpuMeshPending -> admitted, not yet dispatched
    //   _gpuMeshBatches -> dispatched, waiting on the two-hop readback
    //   _readyToApply   -> mesh data is in the job; nothing left but the upload
    readonly List<InFlight> _gpuMeshPending = new();
    class GpuMeshBatch { public readonly List<InFlight> jobs = new(); public int generation; }
    readonly List<GpuMeshBatch> _gpuMeshBatches = new();
    readonly List<InFlight> _readyToApply = new();

    readonly List<MarchingChunk> _pendingBakes = new();
    // True only for the duration of BuildBatchSync, which must not defer.
    bool _applyingSync;

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
        // An editor domain reload destroys _runtimeMaterial (it is DontSave)
        // while the chunks referencing it survive -- they would render with a
        // missing material until something else rebuilt it. Re-create and
        // rebind here, the one callback guaranteed to fire afterwards.
        RefreshTerrainMaterial();
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

    // A RUNTIME COPY of the terrain material, shared by every chunk renderer.
    //
    // This used to be a MaterialPropertyBlock stamped onto each renderer via
    // SetPropertyBlock. That silently disabled the SRP Batcher for all ~1300
    // chunk renderers: SandTerrain.shader is batcher-compatible (it declares a
    // proper CBUFFER_START(UnityPerMaterial)), but a per-renderer property
    // block forces that renderer off the batched path entirely -- and the
    // block held no per-chunk data at all. Every chunk got the SAME values,
    // so the whole override mechanism was buying nothing and costing the
    // batcher.
    //
    // An instance (not the asset) keeps the original "no asset mutation"
    // property: the .mat on disk is never written to, the copy lives and dies
    // with this manager. Since all chunks share this ONE material, the SRP
    // Batcher can now put them in one batch.
    Material _runtimeMaterial;
    Material _runtimeMaterialSource; // what _runtimeMaterial was copied from, so a changed source is detected

    static readonly string[] ChannelSuffix = { "0", "1", "2", "3" };

    // The material a chunk should render with if we cannot make a runtime
    // copy: the BiomeWorld choice, else whatever the prefab itself carries.
    Material SourceTerrainMaterial
    {
        get
        {
            var world = ActiveBiomeWorld;
            if (world != null && world.terrainMaterial != null) return world.terrainMaterial;
            if (chunkPrefab == null) return null;
            var r = chunkPrefab.GetComponent<MeshRenderer>();
            return r != null ? r.sharedMaterial : null;
        }
    }

    // Creates/refreshes the shared runtime material and pushes the current
    // biome data into it. Replaces BuildBiomeMaterialProps + ApplyBiomeProps.
    void RefreshTerrainMaterial()
    {
        Material source = SourceTerrainMaterial;
        if (source == null) { DestroyRuntimeMaterial(); return; }

        // The null test also catches the Unity-destroyed case (editor domain
        // reload), which is why OnEnable calls this.
        bool created = false;
        if (_runtimeMaterial == null || _runtimeMaterialSource != source)
        {
            DestroyRuntimeMaterial();
            _runtimeMaterial = new Material(source)
            {
                name = source.name + " (Terrain Runtime)",
                hideFlags = HideFlags.DontSave, // never serialized into the scene
            };
            _runtimeMaterialSource = source;
            created = true;
        }

        WriteBiomeMaterialProps(_runtimeMaterial);

        // Existing chunks still point at the previous instance (or a destroyed
        // one, after a domain reload). Rebinding is cheap and only happens on
        // field (re)creation / enable, never per frame.
        if (created) RebindTerrainMaterial();
    }

    void RebindTerrainMaterial()
    {
        foreach (var chunk in _chunks.Values) ApplyTerrainMaterial(chunk);
        foreach (var e in _retiring) if (e.chunk != null) ApplyTerrainMaterial(e.chunk);
    }

    void DestroyRuntimeMaterial()
    {
        if (_runtimeMaterial == null) { _runtimeMaterialSource = null; return; }
#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(_runtimeMaterial);
        else Destroy(_runtimeMaterial);
#else
        Destroy(_runtimeMaterial);
#endif
        _runtimeMaterial = null;
        _runtimeMaterialSource = null;
    }

    // Push per-biome material data (palette, style, detail textures) from the
    // Biome assets onto the shared runtime material — no asset
    // mutation; the .mat on disk is never touched.
    //
    // DATA-DRIVEN by design: biomes[i] maps to vertex-color CHANNEL i (0 =
    // implicit remainder, 1 = R, 2 = G, 3 = B) — that mapping is fixed and
    // just reflects how BiomeDensityField.GetVertexColor bakes weights. But
    // WHICH SHADER MODULE renders channel i is read from that biome's own
    // Biome.surfaceStyle, not hardcoded per channel. Swap which Biome asset
    // sits at an index and its style follows it to whatever channel it lands
    // on — SandTerrain.shader's EVALUATE_CHANNEL macro dispatches on this
    // per-channel style tag at runtime.
    void WriteBiomeMaterialProps(Material m)
    {
        var world = ActiveBiomeWorld;
        if (m == null || world == null || world.biomes == null) return;

        // Planet mode: tell the shader to use radial "up" instead of world Y
        // for slope tinting / sediment banding (see SandTerrain.shader's
        // Frag). Left at the shader's defaults (flat world) otherwise.
        var planet = ActivePlanet;
        // Explicit else: unlike a property block (rebuilt from scratch every
        // time), the material persists, so leaving these unset would strand a
        // stale planet setup on it after switching back to a flat world.
        if (planet != null)
        {
            m.SetFloat("_UseSphericalUp", 1f);
            m.SetVector("_PlanetCenter", new Vector4(planet.center.x, planet.center.y, planet.center.z, 0f));
        }
        else
        {
            m.SetFloat("_UseSphericalUp", 0f);
            m.SetVector("_PlanetCenter", Vector4.zero);
        }

        int n = Mathf.Min(world.biomes.Length, ChannelSuffix.Length);
        for (int i = 0; i < n; i++)
        {
            var b = world.biomes[i];
            if (b == null) continue;
            string suf = ChannelSuffix[i];

            m.SetFloat("_Chan" + suf + "Style", (float)b.surfaceStyle);
            m.SetFloat("_Chan" + suf + "Sharpness", Mathf.Max(0.01f, b.blendSharpness));
            m.SetColor("_Chan" + suf + "Flat", b.colorFlat);
            m.SetColor("_Chan" + suf + "Steep", b.colorSteep);

            // Style-specific extras that stay global material properties
            // (only one active source per style — see Biome.albedo's tooltip).
            switch (b.surfaceStyle)
            {
                case Biome.SurfaceStyle.Sand:
                    if (b.albedo != null) m.SetTexture("_MainTex", b.albedo);
                    if (b.normalMap != null) m.SetTexture("_NormalTex", b.normalMap);
                    break;
                case Biome.SurfaceStyle.Canyon:
                    m.SetColor("_CanyonFloorColor", b.colorFlat);
                    break;
                case Biome.SurfaceStyle.Alien:
                    if (b.albedo != null) m.SetTexture("_PebbleTex", b.albedo);
                    if (b.normalMap != null) m.SetTexture("_PebbleNormal", b.normalMap);
                    break;
                case Biome.SurfaceStyle.Frost:
                    // fully procedural (BiomeFrost.hlsl) -- _Chan{i}Flat/Steep
                    // above already cover its ice palette.
                    break;
            }
        }
    }

    // Lets a BiomeWorld asset pick which material (and therefore shader) the
    // whole world renders with, instead of that being fixed on the chunk
    // prefab. Falls back to the prefab's own material when unset.
    void ApplyTerrainMaterial(MarchingChunk chunk)
    {
        if (chunk == null) return;
        var r = chunk.GetComponent<MeshRenderer>();
        if (r == null) return;

        // A pooled chunk (or one left over from a previous session) can still
        // be carrying the property block this used to set. One stale block is
        // enough to keep that renderer out of the SRP Batcher, so clear it
        // unconditionally -- a no-op when there is none.
        r.SetPropertyBlock(null);

        if (_runtimeMaterial != null) r.sharedMaterial = _runtimeMaterial;
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
            RefreshTerrainMaterial();
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

        // Created once and kept: its own availability check consults the
        // sampler every call, so a world that only becomes GPU-capable later
        // still picks it up.
        if (_gpuMesher == null && meshCompute != null)
            _gpuMesher = new TerrainGpuMesher(meshCompute, _gpuSampler);
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
        DestroyRuntimeMaterial();
        // Let in-flight tasks finish so pooled jobs aren't mutated mid-build.
        foreach (var f in _inFlight)
        {
            try { f.task?.Wait(1000); } catch { }
        }
        _inFlight.Clear();

        DestroyField(ref _renderField);
        DestroyField(ref _physicsField);
        _gpuMesher?.Dispose();
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
        RefreshTerrainMaterial(); // ditto for the shader-side biome palette / planet params
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
        PumpColliderBakes();
        DispatchBuilds();
        StageGpuBatch();
        StageGpuMeshBatch();
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

        CollectSwapParticipants();

        // Rescue anything the rebuild above just orphaned. Clearing and
        // recomputing _showBlockers can drop the entry that was holding a
        // chunk hidden, and resurrection (just above) removes a retiring chunk
        // without ever running its reveal -- both leave a hidden chunk with
        // nothing left to show it. Reveal-only, so it cannot hide anything;
        // and this is the one place orphaning happens, so checking right after
        // it covers the case exactly.
        foreach (var kv in _chunks) RevealIfAllowed(kv.Key, kv.Value);

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

    // Safety valve: a swap that never completes must not strand a rebuild
    // forever. Late-but-applied beats permanently stale.
    void ExpireHeldBuilds()
    {
        if (_heldBuilds.Count == 0) return;
        _heldScratch.Clear();
        foreach (var kv in _heldSince)
            if (Time.time - kv.Value >= kMaxHoldSeconds) _heldScratch.Add(kv.Key);
        foreach (var k in _heldScratch)
        {
            _heldKeys.Remove(k);
            ApplyHeldBuild(k);
        }
    }

    bool IsShowBlocked(ChunkKey key) => _showBlockers.TryGetValue(key, out int b) && b > 0;

    bool IsHeldKey(ChunkKey key) => _heldKeys.ContainsKey(key);

    readonly List<ChunkKey> _coverScratch = new();
    readonly HashSet<ChunkKey> _participantScratch = new();

    // Which already-visible chunks must change their mesh AT THE SAME MOMENT
    // as each pending swap -- see the _heldKeys comment for why.
    //
    // Resolved through ResolveCovering rather than by scanning same-level
    // neighbours, because the mirror case matters too: when a region COARSENS,
    // the chunk whose mask changes sits at a different level from the chunks
    // retiring. ResolveCovering answers "who covers this neighbouring volume
    // now" in both directions.
    //
    // Recomputed wholesale each recompute, like _showBlockers. Any build parked
    // for a key that is no longer held is released immediately, so a stale hold
    // cannot survive a box move.
    void CollectSwapParticipants()
    {
        _heldKeys.Clear();
        foreach (var e in _retiring)
        {
            e.participants.Clear();
            _participantScratch.Clear();

            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0 && dz == 0) continue;
                        _coverScratch.Clear();
                        ResolveCovering(e.key.level, e.key.coord + new Vector3Int(dx, dy, dz), _coverScratch);
                        foreach (var nk in _coverScratch)
                        {
                            if (!_participantScratch.Add(nk)) continue;       // reachable from several offsets
                            if (!_chunks.TryGetValue(nk, out var c) || c == null || !c.IsVisible) continue;
                            uint want = ComputeNeeds(nk).Mask;
                            if (_generatedNeeds.TryGetValue(nk, out uint have) && have == want) continue; // mesh already matches
                            e.participants.Add(nk);
                        }
                    }

            foreach (var k in e.participants)
                _heldKeys[k] = _heldKeys.TryGetValue(k, out int h) ? h + 1 : 1;
        }

        ReleaseUnheldBuilds();
    }

    // Applies any parked build whose key stopped being held (its swap
    // completed, or a box move dropped it from every participant list).
    void ReleaseUnheldBuilds()
    {
        if (_heldBuilds.Count == 0) return;
        _heldScratch.Clear();
        foreach (var kv in _heldBuilds)
            if (!IsHeldKey(kv.Key)) _heldScratch.Add(kv.Key);
        foreach (var k in _heldScratch) ApplyHeldBuild(k);
    }

    void ApplyHeldBuild(ChunkKey key)
    {
        if (!_heldBuilds.TryGetValue(key, out var f)) return;
        _heldBuilds.Remove(key);
        _heldSince.Remove(key);
        ApplyBuildResult(f, allowHold: false);
    }

    // Chunk visibility is MONOTONE: hidden -> visible, never the reverse. The
    // only place it is lowered is CreateChunk, before the chunk has a mesh.
    //
    // _showBlockers exists to delay a freshly generated chunk's FIRST
    // appearance so the chunk it replaces can retire in the same frame -- it
    // was never meant to take an on-screen chunk away. But ApplyBuildResult
    // used to evaluate SetVisible(!IsShowBlocked(key)) unconditionally, so an
    // EXISTING, already-visible chunk that happened to be blocked when it got
    // rebuilt was hidden too. Only SweepRetiring's release could bring it
    // back, and that release is not guaranteed to come: a retiring chunk can
    // be resurrected (removed from _retiring without ever releasing), and
    // RecomputeTargets clears and rebuilds _showBlockers from scratch, which
    // silently drops entries. Either way the chunk stayed generated, present,
    // and invisible for good.
    void RevealIfAllowed(ChunkKey key, MarchingChunk chunk)
    {
        if (chunk == null || chunk.IsVisible) return;
        if (!IsShowBlocked(key)) chunk.SetVisible(true);
    }

    // Release retiring chunks whose replacements are all generated, and reveal
    // those replacements in the same frame — no visible hole, no overlap.
    void SweepRetiring()
    {
        ExpireHeldBuilds();
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

            // The whole point: this chunk's replacements become visible below,
            // so every neighbour that was offsetting itself for them applies
            // its new mesh in the SAME frame.
            foreach (var k in e.participants)
            {
                if (!_heldKeys.TryGetValue(k, out int h)) continue;
                if (h > 1) { _heldKeys[k] = h - 1; continue; }
                _heldKeys.Remove(k);
                ApplyHeldBuild(k);
            }
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
        if (_heldBuilds.ContainsKey(key)) return true; // parked, not lost
        for (int i = 0; i < _gpuMeshPending.Count; i++)
            if (_gpuMeshPending[i].key.Equals(key)) return true;
        for (int i = 0; i < _readyToApply.Count; i++)
            if (_readyToApply[i].key.Equals(key)) return true;
        for (int b = 0; b < _gpuMeshBatches.Count; b++)
        {
            var mjobs = _gpuMeshBatches[b].jobs;
            for (int i = 0; i < mjobs.Count; i++)
                if (mjobs[i].key.Equals(key)) return true;
        }
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
        int pipelineDepth = _gpuPending.Count + _readyForMeshing.Count + _inFlight.Count +
                            _gpuMeshPending.Count + _readyToApply.Count;
        foreach (var batch in _gpuInFlightBatches) pipelineDepth += batch.jobs.Count;
        foreach (var batch in _gpuMeshBatches) pipelineDepth += batch.jobs.Count;

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
            else if (UseGpuMeshing && TerrainGpuMesher.CanMesh(inflight.job))
            {
                // Skips the density readback AND ChunkMesher entirely.
                _gpuMeshPending.Add(inflight);
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

    // Copies the cache's density INTO the job's own buffers. It used to hand
    // out Clone()s of the cached arrays, which allocated a fresh ~171 KB
    // regular grid (plus face grids) on every edit-driven rebuild -- i.e. on
    // every footprint, several times a second while walking.
    //
    // Copying rather than aliasing is still required: the job's buffers are
    // reused and the cached ones must stay pristine for the next rebuild.
    static void ApplyDensityCache(ChunkMeshJob job, DensityCacheEntry cached)
    {
        job.ClearGpuRawFlags();

        // Guard against a cache entry from a different grid shape (someone
        // edited cellsPerChunk, say). needsMask matching does not cover size,
        // and ChunkMesher copies the CURRENT grid's element count out of
        // whatever buffer it is handed -- a short one would be read past the
        // end. Leaving every flag false just makes it re-sample on the CPU.
        ChunkMesher.GetEffectiveGrid(job, out int nx, out int ny, out int nz, out _);
        int need = ChunkMesher.RegularGridCount(nx) * ChunkMesher.RegularGridCount(ny) *
                   ChunkMesher.RegularGridCount(nz);
        if (cached.regularCount != need) return; // face grids derive from the same shape, so they match too

        Array.Copy(cached.regular, job.RentGpuRawRegular(cached.regularCount), cached.regularCount);
        for (int i = 0; i < 6; i++)
        {
            if (cached.faces[i] == null) continue;
            Array.Copy(cached.faces[i], job.RentGpuRawFace(i, cached.faceCounts[i]), cached.faceCounts[i]);
        }
    }

    // Snapshots a job's freshly GPU-filled raw density into the cache, for
    // future edit-only rebuilds of this same chunk to reuse. Call right
    // after a successful GPU fill, before ChunkMesher.Build consumes (nulls)
    // the job's own gpuRaw* buffers.
    void SaveDensityCache(InFlight inflight)
    {
        var job = inflight.job;
        if (!job.hasGpuRawRegular) return; // GPU wasn't used/failed for this job

        ChunkMesher.GetEffectiveGrid(job, out int nx, out int ny, out int nz, out _);
        int regularCount = ChunkMesher.RegularGridCount(nx) * ChunkMesher.RegularGridCount(ny) *
                           ChunkMesher.RegularGridCount(nz);

        // Reuse the entry (and its arrays) already sitting under this key --
        // the overwhelmingly common case is the SAME chunk being rebuilt
        // repeatedly at the same size, and re-allocating its cache arrays
        // every time is the other half of the churn ApplyDensityCache just
        // stopped paying.
        if (!_densityCache.TryGetValue(inflight.key, out var entry))
            _densityCache[inflight.key] = entry = new DensityCacheEntry();
        entry.needsMask = inflight.needsMask;

        CopyIntoCache(ref entry.regular, job.gpuRawRegular, regularCount);
        entry.regularCount = regularCount;

        // A face grid's length is whatever the planner asked for, and the job
        // buffer may be longer than that, so cache exactly what was requested.
        // Planned ONCE for all six faces -- per-face planning would rebuild
        // the whole request list six times over.
        for (int i = 0; i < 6; i++) { entry.faces[i] = null; entry.faceCounts[i] = 0; }
        ChunkMesher.PlanGridRequests(job, _planScratch);
        foreach (var r in _planScratch)
        {
            if (r.kind != ChunkMesher.PlannedRequest.Kind.Face) continue;
            if (!job.hasGpuRawFace[r.face]) continue;
            int n = r.countX * r.countY * r.countZ;
            CopyIntoCache(ref entry.faces[r.face], job.gpuRawFaces[r.face], n);
            entry.faceCounts[r.face] = n;
        }
    }

    static void CopyIntoCache(ref float[] dst, float[] src, int count)
    {
        if (dst == null || dst.Length < count) dst = new float[count];
        Array.Copy(src, dst, count);
    }

    // Scratch for the PlanGridRequests overload that does not allocate.
    // Main-thread only, and every user consumes it fully before returning.
    readonly List<ChunkMesher.PlannedRequest> _planScratch = new();

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

    // Only the flags: the arrays themselves are the job's own reusable
    // scratch now (see ChunkMeshJob's gpuRaw comment), so throwing them away
    // here would reintroduce exactly the per-rebuild allocation they exist to
    // avoid.
    static void ClearGpuRaw(ChunkMeshJob job)
    {
        job.ClearGpuRawFlags();
        // Every completion path routes through here, which is exactly the
        // property the GPU output slot's reference counting needs.
        job.ReleaseGpuMesh();
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
            ChunkMesher.PlanGridRequests(job, _planScratch);
            foreach (var req in _planScratch)
            {
                int len = req.countX * req.countY * req.countZ;
                float[] dest = req.kind switch
                {
                    ChunkMesher.PlannedRequest.Kind.Regular => job.RentGpuRawRegular(len),
                    ChunkMesher.PlannedRequest.Kind.Face => job.RentGpuRawFace(req.face, len),
                    _ => job.RentGpuRawCollision(len),
                };
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

    // Dispatches one GPU-meshing batch, if there is work and a free output
    // slot. Deliberately not gated by WorkerCount: this path never occupies a
    // CPU worker at all, which is the entire point of it.
    void StageGpuMeshBatch()
    {
        if (!UseGpuMeshing || _gpuMeshPending.Count == 0) return;
        if (!_gpuMesher.HasFreeSlot) return;

        int take = GatherHomogeneous(_gpuMeshPending, 0, _batchIdxScratch, out bool needsReadback);
        if (take == 0) return;
        var batch = new GpuMeshBatch { generation = _gpuGeneration };
        _meshJobsScratch.Clear();
        for (int i = 0; i < take; i++)
        {
            var f = _gpuMeshPending[_batchIdxScratch[i]];
            // Re-validate: staleness can only have grown since admission.
            // RequeueOrDrop touches _jobs and the job pool, never
            // _gpuMeshPending, so the gathered indices stay valid.
            if (!_boxesValid || !IsDesired(f.key)) { RequeueOrDrop(f); continue; }
            batch.jobs.Add(f);
            _meshJobsScratch.Add(f.job);
        }
        RemoveGathered(_gpuMeshPending, _batchIdxScratch);
        if (batch.jobs.Count == 0) return;

        _gpuMeshBatches.Add(batch);
        // SubmitBatchAsync copies the job list before returning, so reusing
        // _meshJobsScratch afterwards is safe.
        // Collider chunks cannot direct-upload: PhysX cooks from CPU-side data,
        // which a compute-written mesh does not have. See TerrainGpuMesher.CanMesh.
        bool direct = gpuMeshDirectUpload && !needsReadback;
        if (!_gpuMesher.SubmitBatchAsync(_meshJobsScratch, direct,
                                         meshed => OnGpuMeshBatchDone(batch, meshed)))
        {
            _gpuMeshBatches.Remove(batch);
            foreach (var f in batch.jobs) _readyForMeshing.Add(f); // never started: mesh it on the CPU
        }
    }

    void OnGpuMeshBatchDone(GpuMeshBatch batch, bool[] meshed)
    {
        _gpuMeshBatches.Remove(batch);

        // Same staleness rule as StageGpuBatch's readback: the world was
        // cleared or its GPU params rebuilt after this batch was dispatched,
        // so the geometry it produced may not reflect the current world.
        if (batch.generation != _gpuGeneration)
        {
            foreach (var f in batch.jobs) RequeueOrDrop(f);
            return;
        }

        for (int i = 0; i < batch.jobs.Count; i++)
        {
            var f = batch.jobs[i];
            // Per-chunk fallback: one chunk can overflow the kernel's output
            // capacity while the rest of the batch is fine. Its gpuRaw*
            // buffers are null, so ChunkMesher re-samples the grid itself.
            if (!meshed[i]) { _readyForMeshing.Add(f); continue; }
            if (!_boxesValid || !IsDesired(f.key)) { RequeueOrDrop(f); continue; }
            _readyToApply.Add(f);
        }
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
        float t0 = Time.realtimeSinceStartup;
        int done = 0;

        // GPU-meshed chunks first: their data is already sitting in the job,
        // so there is nothing to wait on and they are the cheapest thing the
        // budget can spend itself on. They share the budget with the CPU
        // results below rather than getting their own -- the point of the
        // budget is a frame-time ceiling, and it does not care which half of
        // the pipeline the work came from.
        while (_readyToApply.Count > 0)
        {
            if (done > 0 && (Time.realtimeSinceStartup - t0) * 1000f >= generationBudgetMs) break;
            var g = _readyToApply[0];
            _readyToApply.RemoveAt(0); // oldest-first, same fairness rule as below
            ApplyBuildResult(g);
            done++;
        }

        if (_inFlight.Count == 0) return;

        // OLDEST-FIRST (FIFO). New dispatches append at the end, so iterating
        // from the back would apply the newest completions first — with a tight
        // budget and a steady stream of fast dirty-rebuilds (footprints while
        // walking), the oldest entry starves and its chunk stays stale for a
        // long time. Forward iteration guarantees fairness.
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

    // Binds finished off-thread collider cooks. Unbudgeted on purpose: the
    // work here is the cheap half (a cache-warm sharedMesh assignment); the
    // expensive half already happened on a worker thread.
    void PumpColliderBakes()
    {
        for (int i = _pendingBakes.Count - 1; i >= 0; i--)
        {
            var ch = _pendingBakes[i];
            if (ch == null || ch.FinishColliderBake())
            {
                if (ch != null) ch.bakeTracked = false;
                _pendingBakes.RemoveAt(i);
            }
        }
    }

    void ApplyBuildResult(InFlight f, bool allowHold = true)
    {
        // Park it: this chunk's new mesh is offset for fine chunks that are
        // not on screen yet. Applying now would open the very seam the hold
        // exists to prevent. It keeps rendering its previous mesh meanwhile.
        // gpuMeshOwner != null means the geometry is still sitting in a GPU
        // output slot, which parking would pin for the whole hold -- one of
        // only three, so the mesher would stall. Batches are normally routed
        // to readback for held keys (see NeedsReadback); this covers the race
        // where a chunk was already dispatched when the hold appeared. It just
        // applies as it used to, keeping the old brief seam for that one chunk.
        if (allowHold && IsHeldKey(f.key) && f.job.error == null && f.job.gpuMeshOwner == null &&
            _chunks.TryGetValue(f.key, out var held) && held != null && held.IsVisible)
        {
            if (f.countedInFlightDirty) _inFlightDirty = Mathf.Max(0, _inFlightDirty - 1);
            f.countedInFlightDirty = false;
            if (_heldBuilds.TryGetValue(f.key, out var prev) && prev != f)
            {
                // A newer build superseded one already parked for this key.
                ClearGpuRaw(prev.job);
                prev.job.ResetOutputs();
                _meshJobPool.Push(prev.job);
            }
            _heldBuilds[f.key] = f;
            _heldSince[f.key] = Time.time;
            return;
        }

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
            // Only the streaming path defers: BuildBatchSync's callers expect
            // collision to be live the moment it returns, and edit mode never
            // reaches Update() to pump the bakes anyway.
            job.deferColliderBake = !_applyingSync && Application.isPlaying;
            chunk.ApplyBuild(job);
            if (chunk.ColliderBakePending && !chunk.bakeTracked)
            {
                chunk.bakeTracked = true;
                _pendingBakes.Add(chunk);
            }
            _generatedNeeds[f.key] = f.needsMask;
            RevealIfAllowed(f.key, chunk);
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
        _applyingSync = true;
        try { BuildBatchSyncInner(keys); }
        finally { _applyingSync = false; }
    }

    void BuildBatchSyncInner(List<ChunkKey> keys)
    {
        // Same admission checks DispatchBuilds applies to the async path --
        // without these, every chunk in a sync batch (initial spawn burst,
        // "Generate/Refresh All Chunks") paid full GPU dispatch cost even
        // when provably empty or already covered by a cached base density,
        // which the async streaming path has skipped since the GPU port.
        var toGpu = new List<InFlight>();
        var toGpuMesh = new List<InFlight>();
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
            // Chunks the GPU can take end to end skip BOTH the density
            // readback and ChunkMesher. Everything else keeps the existing
            // "GPU fills the grid, CPU marches it" path.
            if (UseGpuMeshing && TerrainGpuMesher.CanMesh(f.job)) toGpuMesh.Add(f);
            else toGpu.Add(f);
        }

        // Drains by gathering, for the same reason as the async path: a bulk
        // regen interleaves collider and non-collider chunks, and taking
        // contiguous runs would submit one-chunk batches through most of it.
        while (toGpuMesh.Count > 0)
        {
            int count = GatherHomogeneous(toGpuMesh, 0, _batchIdxScratch, out bool syncReadback);
            if (count == 0) break;
            _batchPickScratch.Clear();
            for (int i = 0; i < count; i++) _batchPickScratch.Add(toGpuMesh[_batchIdxScratch[i]]);
            RemoveGathered(toGpuMesh, _batchIdxScratch);
            RunGpuMeshSync(_batchPickScratch, syncReadback);
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

    bool UseGpuMeshing => gpuMeshing && _gpuMesher != null && _gpuMesher.IsAvailable;

    // How many entries starting at `from` share the first one's buildCollider,
    // capped at one batch. Batches have to be homogeneous in it: collider
    // chunks take the readback path and everything else direct upload, and a
    // mixed batch would mean tracking two different output-slot lifetimes at
    // once. Admission order is nearest-first, so LOD0 clusters anyway and the
    // usual cost of this is nothing.
    // How many entries starting at `from` share the first one's readback
    // requirement, capped at one batch. Batches must be homogeneous in it:
    // direct upload leaves the geometry in a GPU output slot, and mixing that
    // with readback chunks in one batch would mean two different slot
    // lifetimes at once.
    //
    // Two reasons a chunk needs readback:
    //  * it builds a COLLIDER -- PhysX cooks from CPU-side data, which a
    //    compute-written mesh does not have.
    //  * it is a SWAP PARTICIPANT -- its build gets parked until the swap
    //    completes (see _heldKeys), and a parked job holding a slot would pin
    //    one of only three for as long as the hold lasts, stalling the mesher.
    //    Read back, its data sits in packedVerts and parking is free.
    // GATHERS one batch of entries sharing the head's readback requirement,
    // scanning the whole queue, and reports their indices in `into` (ascending).
    //
    // It gathers rather than taking a contiguous run, and that distinction is
    // the whole point. Homogeneity used to be keyed on buildCollider alone,
    // which clusters -- colliders are LOD0-only and admission is nearest-first
    // -- so a contiguous run reliably returned a full batch. Keying it on
    // NeedsReadback broke that: held keys are scattered through the queue by
    // construction, because CollectSwapParticipants marks the neighbours of
    // every retiring chunk, so held and unheld interleave. A run stopping at
    // the first mismatch then returns 1 whenever anything is retiring -- the
    // entire time the player moves -- and since StageGpuMeshBatch dispatches
    // exactly one batch per Update, GPU meshing collapsed from 4 chunks/frame
    // to 1. Gathering keeps batches full without giving up homogeneity.
    int GatherHomogeneous(List<InFlight> src, int from, List<int> into, out bool needsReadback)
    {
        into.Clear();
        needsReadback = false;
        if (from >= src.Count) return 0;
        needsReadback = NeedsReadback(src[from]);
        for (int i = from; i < src.Count && into.Count < TerrainGpuMesher.MaxChunksPerBatch; i++)
            if (NeedsReadback(src[i]) == needsReadback) into.Add(i);
        return into.Count;
    }

    // Back to front, so the indices still to be removed stay valid.
    static void RemoveGathered(List<InFlight> src, List<int> idx)
    {
        for (int i = idx.Count - 1; i >= 0; i--) src.RemoveAt(idx[i]);
    }

    readonly List<int> _batchIdxScratch = new();
    readonly List<InFlight> _batchPickScratch = new();

    bool NeedsReadback(InFlight f) => f.job.buildCollider || IsHeldKey(f.key);

    // Scratch for RunGpuMeshSync -- this runs in a loop over hundreds of
    // chunks during a bulk regen, so it does not allocate per batch.
    readonly List<ChunkMeshJob> _meshJobsScratch = new();
    bool[] _meshedScratch;

    void RunGpuMeshSync(List<InFlight> inflights, bool needsReadback)
    {
        _meshJobsScratch.Clear();
        foreach (var f in inflights) _meshJobsScratch.Add(f.job);
        if (_meshedScratch == null || _meshedScratch.Length < inflights.Count)
            _meshedScratch = new bool[TerrainGpuMesher.MaxChunksPerBatch];
        Array.Clear(_meshedScratch, 0, _meshedScratch.Length);

        // Direct upload is safe here even though this path is synchronous: every
        // chunk is applied (and so releases its slot reference) before this
        // method returns.
        bool ok = _gpuMesher.MeshBatchSync(_meshJobsScratch, _meshedScratch,
                                           gpuMeshDirectUpload && !needsReadback);

        for (int i = 0; i < inflights.Count; i++)
        {
            // Per-chunk fallback, not per-batch: a single chunk can overflow
            // the kernel's output capacity while the rest of the batch is
            // fine. ChunkMesher.Build re-samples the grid itself (gpuRaw* are
            // null here), so the fallback is correct, just slower.
            if (!ok || !_meshedScratch[i]) ChunkMesher.Build(inflights[i].job);
            ApplyBuildResult(inflights[i]);
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
            ChunkMesher.PlanGridRequests(job, _planScratch);
            foreach (var req in _planScratch)
            {
                int len = req.countX * req.countY * req.countZ;
                float[] dest = req.kind switch
                {
                    ChunkMesher.PlannedRequest.Kind.Regular => job.RentGpuRawRegular(len),
                    ChunkMesher.PlannedRequest.Kind.Face => job.RentGpuRawFace(req.face, len),
                    _ => job.RentGpuRawCollision(len),
                };
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
        // A chunk is derived data: it is rebuilt from the density field
        // whenever the clipmap wants it, so serializing it into the scene
        // stores nothing that cannot be regenerated -- and it stores a LOT.
        // Generating in the editor and saving once left 2859 chunk meshes in
        // SampleScene.unity, 203 MB, which costs frame rate forever after; and
        // because chunk throughput is frame-rate bound (one GPU batch per
        // Update, a 6 ms apply budget), a heavy scene slows GENERATION too.
        //
        // DontSaveInEditor, not DontSave: the latter also opts the object out
        // of being destroyed on scene load, which would leak chunks instead.
        go.gameObject.hideFlags = HideFlags.DontSaveInEditor;
        go.autoRegenerate = false;
        // A pooled chunk carries whatever renderer state it was released with,
        // and visibility is only ever raised after this point (see
        // RevealIfAllowed) -- so this is the one place it gets lowered, while
        // the chunk still has no mesh to show.
        go.SetVisible(false);
        ApplyLevelSettings(go, key);
        ApplyTerrainMaterial(go);
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
        // Same treatment for the GPU-meshing track: its in-flight readbacks
        // are dropped by the _gpuGeneration bump below, exactly like the
        // density track's.
        foreach (var f in _readyToApply) f.job.ReleaseGpuMesh();
        _gpuMeshPending.Clear();
        _gpuMeshBatches.Clear();
        _readyToApply.Clear();
        foreach (var kv in _heldBuilds) kv.Value.job.ReleaseGpuMesh();
        _heldBuilds.Clear();
        _heldSince.Clear();
        _heldKeys.Clear();
        // Anything still holding an output slot is gone now; force them free
        // rather than wait for reference counts that will never be decremented.
        _gpuMesher?.ResetSlots();
        _densityCache.Clear();
        _gpuGeneration++; // any already-dispatched batch's readback callback is now stale -- see field comment

        foreach (var ch in _pendingBakes)
            if (ch != null) ch.bakeTracked = false;
        _pendingBakes.Clear();

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
        // Anything still baking for this chunk is for terrain it no longer
        // represents; drop the result (the Task itself is harmless and runs
        // out on its own -- see MarchingChunk.CancelPendingColliderBake).
        ch.CancelPendingColliderBake();

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
