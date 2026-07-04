using System.Collections.Generic;
using UnityEngine;


public class MCChunkManager : MonoBehaviour
{
    public Transform target;              // usually the player/camera
    public MarchingChunk chunkPrefab;
    public WorldConfig worldConfig;

    [Header("Pooling")]
    public bool usePooling = true;
    public int prewarm = 16;             // how many chunks to create up-front
    public int maxPoolSize = 256;        // safety cap

    [Header("Streaming")]
    [Tooltip("Max milliseconds per frame spent (re)generating chunk meshes. At least one chunk is processed per frame while work is queued.")]
    public float generationBudgetMs = 6f;

    readonly Dictionary<Vector3Int, MarchingChunk> _chunks = new();
    readonly Stack<MarchingChunk> _pool = new();

    // Target LOD level per chunk coordinate (0 = finest). Derived purely from
    // CHUNK-GRID distance to the player's chunk, so it only ever changes when
    // the player crosses a chunk boundary. Adjacent chunks never differ by more
    // than one level so Transvoxel transition cells can stitch them.
    readonly Dictionary<Vector3Int, int> _chunkLodLevels = new();

    // What each chunk was last generated with, so we only regenerate on change.
    struct GeneratedState { public int lod; public byte needsMask; }
    readonly Dictionary<Vector3Int, GeneratedState> _generatedState = new();

    // Chunks needing (re)generation, sorted near-to-far, drained a few per frame.
    readonly HashSet<Vector3Int> _needed = new();
    readonly List<Vector3Int> _jobs = new();
    readonly List<Vector3Int> _removeScratch = new();
    int _jobCursor;

    Vector3Int _lastCenter;
    bool _hasCenter;
    bool _pendingRefresh;

    // Properties that read from worldConfig
    Vector3Int CellsPerChunk => worldConfig ? worldConfig.cellsPerChunk : new Vector3Int(32, 32, 32);
    float CellSize => worldConfig ? worldConfig.cellSize : 0.5f;
    int ViewRadius => worldConfig ? worldConfig.viewRadius : 4;
    DensityField DefaultDensity => worldConfig ? worldConfig.defaultDensity : null;
    float IsoLevel => worldConfig ? worldConfig.isoLevel : 0f;
    float[] LodDistances => worldConfig ? worldConfig.lodDistances : new float[] { 6f, 12f, 24f, 48f };

    float ChunkWorldSize => CellsPerChunk.x * CellSize;

    // Highest LOD level such that the effective grid stays at least 2 cells per
    // axis (each LOD level halves the resolution -> exact 2:1 for Transvoxel).
    int MaxLodLevel
    {
        get
        {
            int maxByCells = 0;
            while ((CellsPerChunk.x >> (maxByCells + 1)) >= 2) maxByCells++;
            return Mathf.Min(LodDistances.Length, maxByCells);
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
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    ExecuteJob(_lastCenter + new Vector3Int(dx, dy, dz));
    }

    // Chunks that were generated in edit mode get serialized into the scene but
    // aren't tracked in _chunks at play start; destroy them so they don't
    // overlap the freshly generated terrain.
    void CleanupStaleChildren()
    {
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
            _generatedState.Clear(); // settings may have changed: regenerate all (streamed)
            RecomputeTargets();
        }
        else
        {
            var center = WorldToChunkCoord(target.position);
            if (!_hasCenter || center != _lastCenter)
                RecomputeTargets(); // only when the player crosses a chunk border
        }

        ProcessJobs();
    }

    [ContextMenu("Refresh All Chunks")]
    void RefreshExistingChunks()
    {
        _generatedState.Clear();
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
        RecomputeTargets();
        ProcessAllJobsNow();
    }

    [ContextMenu("Clear All Chunks")]
    public void ClearAllChunks()
    {
        foreach (var chunk in _chunks.Values)
        {
            if (chunk != null) ReleaseChunk(chunk);
        }
        _chunks.Clear();
        _chunkLodLevels.Clear();
        _generatedState.Clear();
        _jobs.Clear();
        _jobCursor = 0;
        _hasCenter = false;

        // Also destroy any stray child chunks not in the dictionary.
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

    // ========================================================================
    // Target computation. Runs only when the player enters a different chunk
    // (or settings change): decides which chunks should exist and at what LOD,
    // releases out-of-range chunks and queues the ones whose state changed.
    // ========================================================================
    void RecomputeTargets()
    {
        if (!worldConfig || !chunkPrefab || !target) return;

        var center = WorldToChunkCoord(target.position);
        _lastCenter = center;
        _hasCenter = true;

        _needed.Clear();
        for (int z = -ViewRadius; z <= ViewRadius; z++)
            for (int y = -ViewRadius / 3; y <= ViewRadius / 3; y++)
                for (int x = -ViewRadius; x <= ViewRadius; x++)
                    _needed.Add(center + new Vector3Int(x, y, z));

        // 1) Desired LOD per chunk from chunk-grid distance (quantized: stable
        //    while the player stays inside one chunk).
        _chunkLodLevels.Clear();
        float chunkSize = ChunkWorldSize;
        foreach (var cc in _needed)
        {
            float dist = Vector3Int.Distance(cc, center) * chunkSize;
            _chunkLodLevels[cc] = CalculateLODLevel(dist);
        }

        // 2) Relax so adjacent chunks never differ by more than one level
        //    (Transvoxel handles exactly 2:1 transitions).
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var cc in _needed)
            {
                int lod = _chunkLodLevels[cc];
                foreach (var dir in FaceDirs)
                {
                    if (_chunkLodLevels.TryGetValue(cc + dir, out int nLod) && lod > nLod + 1)
                    {
                        _chunkLodLevels[cc] = nLod + 1;
                        changed = true;
                        break;
                    }
                }
            }
        }

        // 3) Release chunks that fell out of range.
        _removeScratch.Clear();
        foreach (var kv in _chunks)
            if (!_needed.Contains(kv.Key)) _removeScratch.Add(kv.Key);
        foreach (var key in _removeScratch)
        {
            ReleaseChunk(_chunks[key]);
            _chunks.Remove(key);
            _generatedState.Remove(key);
        }

        // 4) Queue chunks whose desired state differs from what they have,
        //    nearest first so the terrain settles around the player.
        _jobs.Clear();
        _jobCursor = 0;
        foreach (var cc in _needed)
            if (NeedsWork(cc)) _jobs.Add(cc);
        _jobs.Sort((a, b) => (a - center).sqrMagnitude.CompareTo((b - center).sqrMagnitude));
    }

    bool NeedsWork(Vector3Int cc)
    {
        if (!_chunks.ContainsKey(cc)) return true;
        if (!_generatedState.TryGetValue(cc, out var prev)) return true;
        int lod = _chunkLodLevels[cc];
        return prev.lod != lod || prev.needsMask != GetTransitionNeeds(cc).Mask;
    }

    // ========================================================================
    // Streaming: drain the job queue under a per-frame time budget.
    // ========================================================================
    void ProcessJobs()
    {
        if (_jobCursor >= _jobs.Count) return;

        float t0 = Time.realtimeSinceStartup;
        int done = 0;
        while (_jobCursor < _jobs.Count)
        {
            if (done > 0 && (Time.realtimeSinceStartup - t0) * 1000f >= generationBudgetMs)
                break;
            ExecuteJob(_jobs[_jobCursor++]);
            done++;
        }
    }

    void ProcessAllJobsNow()
    {
        while (_jobCursor < _jobs.Count)
            ExecuteJob(_jobs[_jobCursor++]);
    }

    void ExecuteJob(Vector3Int cc)
    {
        if (!_needed.Contains(cc)) return; // dropped out of range meanwhile
        if (!_chunkLodLevels.TryGetValue(cc, out int lod)) return;

        if (!_chunks.TryGetValue(cc, out var chunk) || chunk == null)
        {
            chunk = CreateChunk(cc);
            _chunks[cc] = chunk;
        }

        TransitionNeeds needsNow = GetTransitionNeeds(cc);
        if (_generatedState.TryGetValue(cc, out var prev) &&
            prev.lod == lod && prev.needsMask == needsNow.Mask)
            return; // already up to date (e.g. generated synchronously at Start)

        ApplyLODSettingsToChunk(chunk, lod);
        chunk.Generate(needsNow);
        _generatedState[cc] = new GeneratedState { lod = lod, needsMask = needsNow.Mask };
    }

    int CalculateLODLevel(float distance)
    {
        var dists = LodDistances;
        for (int i = 0; i < dists.Length; i++)
            if (distance <= dists[i]) return Mathf.Min(i, MaxLodLevel);
        return MaxLodLevel;
    }

    // Each LOD level halves the grid resolution while keeping the chunk's world
    // size constant, giving the exact 2:1 ratio Transvoxel needs. We do NOT use
    // densitySampling or lodCellCounts here to avoid compounding scale factors.
    void ApplyLODSettingsToChunk(MarchingChunk chunk, int lodLevel)
    {
        lodLevel = Mathf.Clamp(lodLevel, 0, MaxLodLevel);
        var baseCells = CellsPerChunk;
        chunk.cells = new Vector3Int(
            Mathf.Max(1, baseCells.x >> lodLevel),
            Mathf.Max(1, baseCells.y >> lodLevel),
            Mathf.Max(1, baseCells.z >> lodLevel));
        chunk.cellSize = ChunkWorldSize / chunk.cells.x;
        chunk.densitySampling = 1;

        // Only full-detail chunks need physics; the player can't reach coarser
        // rings before they are re-generated at LOD 0.
        chunk.generateCollider = lodLevel == 0;
    }

    MarchingChunk CreateChunk(Vector3Int cc)
    {
        var go = AcquireChunk();

        go.transform.position = ChunkOrigin(cc);
        go.gameObject.name = $"Chunk_{cc.x}_{cc.y}_{cc.z}";

        go.isoLevel = IsoLevel;
        if (DefaultDensity != null) go.densityField = DefaultDensity;
        go.autoRegenerate = false;

        int lod = _chunkLodLevels.TryGetValue(cc, out var l) ? l : 0;
        ApplyLODSettingsToChunk(go, lod);

        go.gameObject.SetActive(true);
        return go;
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

    Vector3Int WorldToChunkCoord(Vector3 worldPos)
    {
        float s = ChunkWorldSize;
        return new Vector3Int(
            Mathf.FloorToInt(worldPos.x / s),
            Mathf.FloorToInt(worldPos.y / s),
            Mathf.FloorToInt(worldPos.z / s)
        );
    }

    Vector3 ChunkOrigin(Vector3Int c) => new Vector3(c.x, c.y, c.z) * ChunkWorldSize;

    static readonly Vector3Int[] FaceDirs =
    {
        new Vector3Int( 1, 0, 0), new Vector3Int(-1, 0, 0),
        new Vector3Int( 0, 1, 0), new Vector3Int( 0,-1, 0),
        new Vector3Int( 0, 0, 1), new Vector3Int( 0, 0,-1),
    };

    // Which of the 6 faces border a FINER neighbor? The coarse chunk owns the
    // transition cells (Transvoxel), so it needs to know where the fine
    // neighbors are. Face order: +X, -X, +Y, -Y, +Z, -Z.
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

    TransitionNeeds GetTransitionNeeds(Vector3Int cc)
    {
        int myLod = _chunkLodLevels.TryGetValue(cc, out var l) ? l : 0;

        bool Finer(Vector3Int dir) =>
            _chunkLodLevels.TryGetValue(cc + dir, out var nLod) && nLod < myLod;

        return new TransitionNeeds
        {
            px = Finer(FaceDirs[0]),
            nx = Finer(FaceDirs[1]),
            py = Finer(FaceDirs[2]),
            ny = Finer(FaceDirs[3]),
            pz = Finer(FaceDirs[4]),
            nz = Finer(FaceDirs[5]),
        };
    }
}
