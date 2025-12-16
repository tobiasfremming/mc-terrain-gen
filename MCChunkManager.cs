using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class MCChunkManager : MonoBehaviour
{
    // TODO: LOD levels (densitySampling per distance), use a stitching method like Transvoxel
    public Transform target;              // usually the player/camera
    public MarchingChunk chunkPrefab;
    public WorldConfig worldConfig;

    [Header("Pooling")]
    public bool usePooling = true;
    public int prewarm = 16;             // how many chunks to create up-front
    public int maxPoolSize = 256;        // safety cap

    readonly Dictionary<Vector3Int, MarchingChunk> _chunks = new();
    private readonly Stack<MarchingChunk> _pool = new(); // Pool of available chunks
    Vector3Int _lastCenter;
    private bool _pendingRefresh = false; // Flag to defer refresh operations

    // Properties that read from worldConfig
    Vector3Int CellsPerChunk => worldConfig ? worldConfig.cellsPerChunk : new Vector3Int(32, 32, 32);
    float CellSize => worldConfig ? worldConfig.cellSize : 0.5f;

    int ViewRadius => worldConfig ? worldConfig.viewRadius : 4;
    bool RegenOnMove => worldConfig ? true : true; // worldConfig doesn't have regenOnMove yet
    int DensitySampling => worldConfig ? worldConfig.densitySampling : 1;
    DensityField DefaultDensity => worldConfig ? worldConfig.defaultDensity : null;
    float IsoLevel => worldConfig ? worldConfig.isoLevel : 0f;

    float ChunkWorldSize => CellsPerChunk.x * CellSize;

    private float _lastLodUpdate;
    private Dictionary<Vector3Int, int> _chunkLodLevels = new Dictionary<Vector3Int, int>();

    // LOD properties
    float[] LodDistances => worldConfig ? worldConfig.lodDistances : new float[] { 6f, 12f, 24f, 48f };
    int[] LodSamplings => worldConfig ? worldConfig.lodSamplings : new int[] { 1, 2, 4, 8 };
    Vector3Int[] LodCellCounts => worldConfig ? worldConfig.lodCellCounts :
        new Vector3Int[] 
        { 
            new Vector3Int(32, 32, 32), 
            new Vector3Int(24, 24, 24), 
            new Vector3Int(16, 16, 16), 
            new Vector3Int(8, 8, 8) 
        };
    bool UseAdaptiveLOD => worldConfig ? worldConfig.useAdaptiveLOD : true;
    float LodUpdateInterval => worldConfig ? worldConfig.lodUpdateInterval : 0.5f;


    void Start()
    {
        if (Application.isPlaying && usePooling) PrewarmPool();
        UpdateVisibleChunks(force: true);
    }

    void OnValidate()
    {
        // Defer refresh operations to avoid SendMessage issues during OnValidate
        if (isActiveAndEnabled)
        {
            _pendingRefresh = true;
        }
    }

    [ContextMenu("Refresh Existing Chunks")]
    void RefreshExistingChunks()
    {
        foreach (var c in _chunks.Values)
        {
            if (DefaultDensity != null) c.densityField = DefaultDensity;
            c.cells = CellsPerChunk;
            c.cellSize = CellSize;
            c.densitySampling = DensitySampling;
            var cc = WorldToChunkCoord(c.transform.position);
            TransitionNeeds needs = GetTransitionNeeds(cc);
            c.Generate(needs);
        }
    }

    [ContextMenu("Refresh Child Chunks")]
    void RefreshChildChunks()
    {
        var childChunks = GetComponentsInChildren<MarchingChunk>();
        foreach (var chunk in childChunks)
        {
            if (DefaultDensity != null) chunk.densityField = DefaultDensity;
            chunk.cells = CellsPerChunk;
            chunk.cellSize = CellSize;
            chunk.densitySampling = DensitySampling;
            var cc = WorldToChunkCoord(chunk.transform.position);
            TransitionNeeds needs = GetTransitionNeeds(cc);
            chunk.Generate(needs);
        }
    }

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
            // In edit mode, use coroutine to defer operations
            StartCoroutine(DeferredGenerateAllChunks());
        }
    }

    private IEnumerator DeferredGenerateAllChunks()
    {
        yield return null; // Wait one frame
        Debug.Log("Generating all chunks...");
        ClearAllChunks();
        UpdateVisibleChunks(force: true);
    }

    [ContextMenu("Clear All Chunks")]
    public void ClearAllChunks()
    {
        Debug.Log("Clearing all chunks...");

        // Release all active chunks from the dictionary
        foreach (var chunk in _chunks.Values)
        {
            if (chunk != null) ReleaseChunk(chunk);
        }
        _chunks.Clear();

        // Also find and destroy any child chunks that might not be in the dictionary
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

        // Reset center to force regeneration
        _lastCenter = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);

        Debug.Log($"Cleared {allChildChunks.Length} chunks total");
    }

    void Update()
    {
        // Handle LOD updates
        if (UseAdaptiveLOD && Time.time - _lastLodUpdate > LodUpdateInterval)
        {
            UpdateChunkLOD();
            _lastLodUpdate = Time.time;
        }

        // Handle pending refresh from OnValidate
        if (_pendingRefresh)
        {
            _pendingRefresh = false;
            if (Application.isPlaying && _chunks.Count > 0)
            {
                RefreshExistingChunks();
            }
            else if (!Application.isPlaying)
            {
                StartCoroutine(DeferredRefreshChildChunks());
            }
        }

        if (!RegenOnMove) return;
        var center = WorldToChunkCoord(target.position);
        if (center != _lastCenter) UpdateVisibleChunks(force: false);
    }


    void UpdateChunkLOD()
    {
        if (!target) return;

        Vector3 targetPos = target.position;
        List<Vector3Int> chunksToRefresh = new List<Vector3Int>();

        foreach (var kvp in _chunks)
        {
            Vector3Int chunkCoord = kvp.Key;
            MarchingChunk chunk = kvp.Value;

            if (!chunk) continue;

            Vector3 chunkWorldPos = ChunkOrigin(chunkCoord) + Vector3.one * (ChunkWorldSize * 0.5f);
            float distance = Vector3.Distance(targetPos, chunkWorldPos);

            // Determine LOD level based on distance
            int newLodLevel = CalculateLODLevel(distance);
            int currentLodLevel = _chunkLodLevels.ContainsKey(chunkCoord) ? _chunkLodLevels[chunkCoord] : 0;

            // If LOD changed, mark for refresh
            if (newLodLevel != currentLodLevel)
            {
                _chunkLodLevels[chunkCoord] = newLodLevel;
                chunksToRefresh.Add(chunkCoord);
            }
        }

        // Refresh chunks that changed LOD
        foreach (var chunkCoord in chunksToRefresh)
        {
            if (_chunks.ContainsKey(chunkCoord))
            {
                ApplyLODToChunk(_chunks[chunkCoord], _chunkLodLevels[chunkCoord]);
            }
        }
    }

    int CalculateLODLevel(float distance)

    {
        // basic hysteresis: cache last level and expand its band
        for (int i = 0; i < LodDistances.Length; i++)
        {
            float threshold = LodDistances[i];
            float band = threshold * 0.1f; // 10% band

            if (_lastCenter != default && _chunkLodLevels.TryGetValue(_lastCenter, out var last))
            {
                if (i == last)
                {
                    if (distance <= threshold + band) return i;
                }
            }
            if (distance <= threshold) return i;
        }
        return LodDistances.Length;
    }


    void ApplyLODToChunk(MarchingChunk chunk, int lodLevel)
    {
        lodLevel = Mathf.Clamp(lodLevel, 0, LodSamplings.Length - 1);

        // Temporarily suppress auto
        bool prevAuto = chunk.autoRegenerate;
        chunk.autoRegenerate = false;

        chunk.densitySampling = LodSamplings[lodLevel];
        if (LodCellCounts.Length > lodLevel)
        {
            chunk.cells = LodCellCounts[lodLevel];
            chunk.cellSize = ChunkWorldSize / chunk.cells.x; // keeps world size constant
        }
        var cc = WorldToChunkCoord(chunk.transform.position);
        TransitionNeeds needs = GetTransitionNeeds(cc);
        chunk.Generate(needs);

        chunk.autoRegenerate = prevAuto; // restore

    }




    private IEnumerator DeferredRefreshChildChunks()
    {
        yield return null; // Wait one frame
        RefreshChildChunks();
    }

    void PrewarmPool()
    {
        int count = Mathf.Clamp(prewarm, 0, maxPoolSize);
        for (int i = 0; i < count; i++)
        {
            var ch = Instantiate(chunkPrefab, transform);
            ch.gameObject.name = $"PooledChunk_{i}";
            ch.gameObject.SetActive(false);   // keep inactive in pool
            _pool.Push(ch);
        }
    }

    MarchingChunk AcquireChunk()
    {
        MarchingChunk ch = null;

        if (usePooling && Application.isPlaying && _pool.Count > 0)
        {
            ch = _pool.Pop();
        }
        else
        {
            // In edit mode (or if pool empty), just instantiate
            ch = Instantiate(chunkPrefab, transform);
        }

        return ch;
    }

    void ReleaseChunk(MarchingChunk ch)
    {
        if (!ch) return;

        Vector3Int chunkCoord = WorldToChunkCoord(ch.transform.position);
        _chunkLodLevels.Remove(chunkCoord);

        if (usePooling && Application.isPlaying && _pool.Count < maxPoolSize)
        {
            ch.gameObject.SetActive(false);
            // Optional: clear name to avoid confusion
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

    MarchingChunk CreateChunk(Vector3Int cc)
    {
        var go = AcquireChunk();

        // Position the chunk
        go.transform.position = ChunkOrigin(cc);
        go.gameObject.name = $"Chunk_{cc.x}_{cc.y}_{cc.z}";
        go.gameObject.SetActive(false); // Configure before activating

        // Apply base settings from WorldConfig
        go.cells = CellsPerChunk;
        go.cellSize = CellSize;
        go.densitySampling = DensitySampling;
        go.isoLevel = IsoLevel;
        if (DefaultDensity != null) go.densityField = DefaultDensity;

        // Calculate and apply initial LOD
        if (UseAdaptiveLOD && target)
        {
            Vector3 chunkWorldPos = ChunkOrigin(cc) + Vector3.one * (ChunkWorldSize * 0.5f);
            float distance = Vector3.Distance(target.position, chunkWorldPos);
            int lodLevel = CalculateLODLevel(distance);
            _chunkLodLevels[cc] = lodLevel;

            Debug.Log($"Chunk {cc} at distance {distance:F1} gets LOD level {lodLevel}");
            Debug.Log($"  Target pos: {target.position}, Chunk center: {chunkWorldPos}");
            Debug.Log($"  LOD distances: [{string.Join(", ", LodDistances)}]");

            // Override with LOD-specific settings BEFORE enabling auto-regeneration
            go.autoRegenerate = false; // ensure it's off while configuring
            ApplyLODSettingsToChunk(go, lodLevel);
        }

        //go.autoRegenerate = true; // Set this AFTER LOD is applied
        go.gameObject.SetActive(true); // Activate after configuration

        return go;
    }

    void ApplyLODSettingsToChunk(MarchingChunk chunk, int lodLevel)
    {
        lodLevel = Mathf.Clamp(lodLevel, 0, LodSamplings.Length - 1);

        chunk.densitySampling = LodSamplings[lodLevel];
        if (LodCellCounts.Length > lodLevel)
        {
            chunk.cells = LodCellCounts[lodLevel];
            chunk.cellSize = ChunkWorldSize / chunk.cells.x;
        }

        Debug.Log($"Applied LOD {lodLevel} to chunk {chunk.name}: cells={chunk.cells}, sampling={chunk.densitySampling}");
        Debug.Log($"  LodCellCounts[{lodLevel}]={LodCellCounts[lodLevel]}, LodSamplings[{lodLevel}]={LodSamplings[lodLevel]}");
    }


    Vector3 ChunkOrigin(Vector3Int c) => new Vector3(c.x, c.y, c.z) * ChunkWorldSize;

    void UpdateVisibleChunks(bool force)
    {
        if (!worldConfig || !chunkPrefab) return;

        var center = WorldToChunkCoord(target.position);
        _lastCenter = center;

        // mark which should stay
        var needed = new HashSet<Vector3Int>();
        for (int z = -ViewRadius; z <= ViewRadius; z++)
            for (int y = -ViewRadius/3; y <= ViewRadius/3; y++)
                for (int x = -ViewRadius; x <= ViewRadius; x++)
                {
                    var cc = center + new Vector3Int(x, y, z);
                    needed.Add(cc);
                }

        // PASS 1: Create and register all missing chunks (without generating)
        foreach (var cc in needed)
        {
            if (_chunks.ContainsKey(cc)) continue;

            // Create chunk with LOD settings but don't generate yet
            var go = CreateChunk(cc);
            _chunks.Add(cc, go);
        }

        // PASS 2: Generate all chunks with correct transition needs
        // (now all neighbors are registered in _chunks)
        foreach (var cc in needed)
        {
            if (!_chunks.ContainsKey(cc)) continue; // shouldn't happen but safety check
            
            var ch = _chunks[cc];
            TransitionNeeds needs = GetTransitionNeeds(cc);
            ch.Generate(needs);
        }

        // remove far chunks
        var toRemove = new List<Vector3Int>();
        foreach (var kv in _chunks)
            if (!needed.Contains(kv.Key)) toRemove.Add(kv.Key);

        foreach (var key in toRemove)
        {
            ReleaseChunk(_chunks[key]);
            _chunks.Remove(key);
        }
    }


   


    // Which of the 6 faces need transitions (neighbor is exactly one level coarser)?
    // Order: +X, -X, +Y, -Y, +Z, -Z
    public struct TransitionNeeds
    {
        public bool px, nx, py, ny, pz, nz;
        public bool Any => px || nx || py || ny || pz || nz;
    }

    

    TransitionNeeds GetTransitionNeeds(Vector3Int cc)
    {
        int myLod = _chunkLodLevels.TryGetValue(cc, out var l) ? l : 0;
        TransitionNeeds n = new TransitionNeeds();

        bool Need(Vector3Int nCoord)
        {
            if (!_chunks.ContainsKey(nCoord)) return false;
            int nLod = _chunkLodLevels.TryGetValue(nCoord, out var nl) ? nl : myLod;
            return nLod > myLod; // neighbor is coarser by exactly one level
        }

        n.px = Need(cc + new Vector3Int(1, 0, 0));
        n.nx = Need(cc + new Vector3Int(-1, 0, 0));
        n.py = Need(cc + new Vector3Int(0, 1, 0));
        n.ny = Need(cc + new Vector3Int(0, -1, 0));
        n.pz = Need(cc + new Vector3Int(0, 0, 1));
        n.nz = Need(cc + new Vector3Int(0, 0, -1));
        return n;
    }


    
    


}
