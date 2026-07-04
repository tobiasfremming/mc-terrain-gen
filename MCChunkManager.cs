using System.Collections;
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

    readonly Dictionary<Vector3Int, MarchingChunk> _chunks = new();
    readonly Stack<MarchingChunk> _pool = new();

    // LOD level per active chunk (0 = finest). Adjacent chunks never differ by
    // more than one level so that Transvoxel transition cells can stitch them.
    readonly Dictionary<Vector3Int, int> _chunkLodLevels = new();

    // What each chunk was last generated with, so we only regenerate on change.
    struct GeneratedState { public int lod; public byte needsMask; }
    readonly Dictionary<Vector3Int, GeneratedState> _generatedState = new();

    Vector3Int _lastCenter;
    bool _hasCenter = false;
    bool _pendingRefresh = false;
    float _lastLodUpdate;

    // Properties that read from worldConfig
    Vector3Int CellsPerChunk => worldConfig ? worldConfig.cellsPerChunk : new Vector3Int(32, 32, 32);
    float CellSize => worldConfig ? worldConfig.cellSize : 0.5f;
    int ViewRadius => worldConfig ? worldConfig.viewRadius : 4;
    DensityField DefaultDensity => worldConfig ? worldConfig.defaultDensity : null;
    float IsoLevel => worldConfig ? worldConfig.isoLevel : 0f;
    float[] LodDistances => worldConfig ? worldConfig.lodDistances : new float[] { 6f, 12f, 24f, 48f };
    bool UseAdaptiveLOD => worldConfig ? worldConfig.useAdaptiveLOD : true;
    float LodUpdateInterval => worldConfig ? worldConfig.lodUpdateInterval : 0.5f;

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
        if (Application.isPlaying && usePooling) PrewarmPool();
        UpdateVisibleChunks(force: true);
    }

    void OnValidate()
    {
        if (isActiveAndEnabled) _pendingRefresh = true;
    }

    void Update()
    {
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            if (Application.isPlaying && _chunks.Count > 0)
            {
                UpdateVisibleChunks(force: true);
            }
            else if (!Application.isPlaying)
            {
                StartCoroutine(DeferredRefresh());
            }
            return;
        }

        if (!target) return;

        var center = WorldToChunkCoord(target.position);
        bool moved = !_hasCenter || center != _lastCenter;
        bool lodTick = UseAdaptiveLOD && Time.time - _lastLodUpdate > LodUpdateInterval;

        if (moved || lodTick)
        {
            _lastLodUpdate = Time.time;
            UpdateVisibleChunks(force: false);
        }
    }

    IEnumerator DeferredRefresh()
    {
        yield return null; // wait one frame (edit mode safety)
        UpdateVisibleChunks(force: true);
    }

    [ContextMenu("Refresh All Chunks")]
    void RefreshExistingChunks() => UpdateVisibleChunks(force: true);

    [ContextMenu("Generate All Chunks")]
    public void GenerateAllChunks()
    {
        if (!target)
        {
            Debug.LogWarning("No target set for chunk generation!");
            return;
        }

        if (Application.isPlaying)
        {
            ClearAllChunks();
            UpdateVisibleChunks(force: true);
        }
        else
        {
            StartCoroutine(DeferredGenerateAllChunks());
        }
    }

    IEnumerator DeferredGenerateAllChunks()
    {
        yield return null;
        ClearAllChunks();
        UpdateVisibleChunks(force: true);
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
        _hasCenter = false;
    }

    // ========================================================================
    // Core update: decide which chunks exist, assign LOD levels (clamped so
    // neighbors differ by at most one level), and (re)generate every chunk
    // whose LOD or transition needs changed.
    // ========================================================================
    void UpdateVisibleChunks(bool force)
    {
        if (!worldConfig || !chunkPrefab || !target) return;

        var center = WorldToChunkCoord(target.position);
        _lastCenter = center;
        _hasCenter = true;

        var needed = new HashSet<Vector3Int>();
        for (int z = -ViewRadius; z <= ViewRadius; z++)
            for (int y = -ViewRadius / 3; y <= ViewRadius / 3; y++)
                for (int x = -ViewRadius; x <= ViewRadius; x++)
                    needed.Add(center + new Vector3Int(x, y, z));

        // 1) Desired LOD per chunk from distance.
        _chunkLodLevels.Clear();
        foreach (var cc in needed)
        {
            Vector3 chunkCenter = ChunkOrigin(cc) + Vector3.one * (ChunkWorldSize * 0.5f);
            _chunkLodLevels[cc] = CalculateLODLevel(Vector3.Distance(target.position, chunkCenter));
        }

        // 2) Relax so adjacent chunks never differ by more than one level
        //    (Transvoxel handles exactly 2:1 transitions).
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var cc in needed)
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

        // 3) Create missing chunks (configured but not generated yet).
        foreach (var cc in needed)
        {
            if (!_chunks.ContainsKey(cc))
                _chunks.Add(cc, CreateChunk(cc));
        }

        // 4) (Re)generate chunks whose LOD or transition needs changed.
        foreach (var cc in needed)
        {
            var chunk = _chunks[cc];
            int lod = _chunkLodLevels[cc];
            TransitionNeeds needs = GetTransitionNeeds(cc);

            var state = new GeneratedState { lod = lod, needsMask = needs.Mask };
            if (!force &&
                _generatedState.TryGetValue(cc, out var prev) &&
                prev.lod == state.lod && prev.needsMask == state.needsMask)
                continue;

            ApplyLODSettingsToChunk(chunk, lod);
            chunk.Generate(needs);
            _generatedState[cc] = state;
        }

        // 5) Remove chunks that fell out of range.
        var toRemove = new List<Vector3Int>();
        foreach (var kv in _chunks)
            if (!needed.Contains(kv.Key)) toRemove.Add(kv.Key);

        foreach (var key in toRemove)
        {
            ReleaseChunk(_chunks[key]);
            _chunks.Remove(key);
            _generatedState.Remove(key);
        }
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
