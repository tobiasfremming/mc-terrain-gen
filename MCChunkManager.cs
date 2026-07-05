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
    readonly Dictionary<ChunkKey, byte> _generatedNeeds = new();
    readonly HashSet<ChunkKey> _dirty = new();        // must regenerate even if needs match (terrain edits)
    readonly HashSet<ChunkKey> _dirtyPhysics = new(); // subset whose collider must also re-cook

    readonly HashSet<ChunkKey> _needed = new();
    readonly List<ChunkKey> _jobs = new();
    readonly List<ChunkKey> _dirtyJobs = new();
    readonly List<ChunkKey> _removeScratch = new();
    int _jobCursor, _dirtyCursor;

    // Async build pipeline.
    class InFlight
    {
        public ChunkKey key;
        public byte needsMask;
        public ChunkMeshJob job;
        public Task task;
        public bool fromDirty;
    }
    readonly List<InFlight> _inFlight = new();
    readonly Stack<ChunkMeshJob> _meshJobPool = new();
    int _inFlightDirty;

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
        public byte needsMask;
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
            if (worldConfig && worldConfig.defaultDensity) return worldConfig.defaultDensity;
            return chunkPrefab ? chunkPrefab.densityField : null;
        }
    }

    // Public entry point for all terrain edits (StampSphere / StampLine, with
    // an affectsPhysics flag per edit).
    public TerrainModificationSystem Modifications { get { EnsureField(); return _modSystem; } }

    void EnsureField()
    {
        if (_modSystem == null)
        {
            _modSystem = new TerrainModificationSystem(CellSize, modShortTermSeconds) { maxDepth = modMaxDepth };
            _modSystem.RegionModified += OnRegionModified;
        }
        if (_modSystem.baseField == null) _modSystem.baseField = BaseField;
        if (_renderField == null && BaseField != null)
        {
            _renderField = ModifiedDensityField.Create(BaseField, _modSystem.visual, _modSystem.physical);
            _physicsField = ModifiedDensityField.Create(BaseField, _modSystem.physical);
        }
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
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        BuildSync(new ChunkKey { level = 0, coord = c + new Vector3Int(dx, dy, dz) });
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

    void Update()
    {
        if (!target) return;

        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            _generatedNeeds.Clear(); // settings may have changed: regenerate all (streamed)
            _boxesValid = false;
        }

        if (BoxesChanged())
            RecomputeTargets(); // only when the player crosses a clipmap boundary

        ApplyCompletedBuilds();
        DispatchBuilds();
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
                        // FIRST build is currently in flight (it may have
                        // sampled pre-edit data — without this, the edit is
                        // silently lost for that chunk: a permanent crack).
                        if (!_generatedNeeds.ContainsKey(key) && !IsInFlight(key)) continue;
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
        if (IsInFlight(key)) return false;
        if (!_chunks.ContainsKey(key)) return true;
        if (_dirty.Contains(key)) return true;
        if (!_generatedNeeds.TryGetValue(key, out byte prev)) return true;
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
        return new TransitionNeeds
        {
            px = CoveredByFiner(k, q + FaceDirs[0]),
            nx = CoveredByFiner(k, q + FaceDirs[1]),
            py = CoveredByFiner(k, q + FaceDirs[2]),
            ny = CoveredByFiner(k, q + FaceDirs[3]),
            pz = CoveredByFiner(k, q + FaceDirs[4]),
            nz = CoveredByFiner(k, q + FaceDirs[5]),
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

    ChunkMeshJob GetMeshJob() => _meshJobPool.Count > 0 ? _meshJobPool.Pop() : new ChunkMeshJob();

    void DispatchBuilds()
    {
        int max = WorkerCount;
        // Edit rebuilds may not crowd out streaming work (new terrain, LOD
        // swaps): while regular jobs are pending, dirty jobs get half the
        // slots at most. Without this, sprinting (a continuous footprint
        // stream) starves streaming completely and the world stops updating.
        int maxDirty = Mathf.Max(1, max / 2);

        while (_inFlight.Count < max)
        {
            bool dirtyReady = _dirtyCursor < _dirtyJobs.Count &&
                              (!_dirtyNotBefore.TryGetValue(_dirtyJobs[_dirtyCursor], out float nb) || Time.time >= nb);
            bool regularPending = _jobCursor < _jobs.Count;

            ChunkKey key;
            bool fromDirty;
            if (dirtyReady && (_inFlightDirty < maxDirty || !regularPending))
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

            if (IsInFlight(key)) continue;
            var inflight = PrepareBuild(key);
            if (inflight == null) continue;

            inflight.fromDirty = fromDirty;
            if (fromDirty)
            {
                _inFlightDirty++;
                _lastDirtyDispatch[key] = Time.time;
            }

            var job = inflight.job;
            inflight.task = Task.Run(() => ChunkMesher.Build(job));
            _inFlight.Add(inflight);
        }
    }

    // Validates the key still needs work and snapshots all build inputs.
    InFlight PrepareBuild(ChunkKey key)
    {
        if (!_boxesValid || !IsDesired(key)) return null;

        TransitionNeeds needs = ComputeNeeds(key);
        bool dirty = _dirty.Contains(key);
        bool structural = !_generatedNeeds.TryGetValue(key, out byte prev) || prev != needs.Mask;
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

        return new InFlight { key = key, needsMask = needs.Mask, job = job };
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
        if (f.fromDirty) _inFlightDirty = Mathf.Max(0, _inFlightDirty - 1);

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

        job.ResetOutputs();
        _meshJobPool.Push(job);
    }

    // Synchronous build+apply for the initial ground under the player.
    void BuildSync(ChunkKey key)
    {
        var inflight = PrepareBuild(key);
        if (inflight == null) return;
        ChunkMesher.Build(inflight.job);
        ApplyBuildResult(inflight);
    }

    void ProcessAllJobsNow()
    {
        while (_dirtyCursor < _dirtyJobs.Count) BuildSync(_dirtyJobs[_dirtyCursor++]);
        while (_jobCursor < _jobs.Count) BuildSync(_jobs[_jobCursor++]);
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
        go.gameObject.SetActive(true);
        return go;
    }

    [ContextMenu("Refresh All Chunks")]
    void RefreshExistingChunks()
    {
        _generatedNeeds.Clear();
        _boxesValid = false;
        if (BoxesChanged()) { }
        RecomputeTargets();
        ProcessAllJobsNow();
    }

    [ContextMenu("Generate All Chunks")]
    public void GenerateAllChunks()
    {
        if (!target)
        {
            Debug.LogWarning("No target set for chunk generation!");
            return;
        }
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

        public byte Mask =>
            (byte)((px ? 1 : 0) | (nx ? 2 : 0) | (py ? 4 : 0) |
                   (ny ? 8 : 0) | (pz ? 16 : 0) | (nz ? 32 : 0));
    }
}
