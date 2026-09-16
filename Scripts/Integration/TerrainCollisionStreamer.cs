using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

// Collider-only terrain tiles around TerrainAnchors that the clipmap's LOD0
// box does not cover. Without these, anything further than the LOD0 ring from
// the player has no ground under it. See the integration plan, Phase C.
//
// A tile is exactly what a clipmap LOD0 chunk would collide with: same
// ChunkMesher.Build, same LOD0 resolution, no transition faces (LOD0 is the
// finest level, so a LOD0 chunk never owns any), meshed from the physics
// field (base terrain + physical edits only). Mesher vertices are chunk-local
// (it samples at origin + p and emits p), so the tile sits at coord * size.
//
// Hand-off rule: a tile that has moved inside the clipmap's LOD0 box is only
// released once IsLevel0ColliderReady says the chunk's own collider is bound.
// The two coincident static colliders overlap for a frame or two, which is
// harmless; a gap frame would drop whatever is resting there.
public class TerrainCollisionStreamer : MonoBehaviour
{
    // (worldBounds, mesh or null, localToWorld, added). Main thread only.
    // Raised with added=true every time a tile's collider is (re)bound -- a
    // rebuild after an edit raises it again for the same bounds -- and with
    // added=false when a bound collider goes away (released, or rebuilt empty).
    //
    // The Mesh is NOT a snapshot. A rebuild rewrites it in place, and a
    // released tile's GameObject and Mesh are pooled and reused for some other
    // coord. Read it synchronously, inside the handler or at the latest the
    // same frame (e.g. NavMeshBuildSource collection), and do not hold on to
    // it as a record of this tile's geometry.
    public static event Action<Bounds, Mesh, Matrix4x4, bool> TileChanged;

    sealed class Tile
    {
        public Vector3Int coord;
        public GameObject go;
        public MeshCollider collider;
        public Mesh mesh;
        public Vector3[] positions;  // reused upload buffer (positions only)
        public Task task;            // phase 1: mesh, phase 2: bake
        public ChunkMeshJob job;     // owned by this tile; never shared while task runs
        public bool dirty;           // a physical edit landed after the last build started
        public bool bound;           // has completed at least one build
        public int phase;            // 0 idle, 1 meshing, 2 baking
    }

    static readonly Dictionary<Vector3Int, Tile> s_tiles = new();

    public static IEnumerable<(Mesh mesh, Matrix4x4 localToWorld, Bounds bounds)> ActiveTiles
    {
        get
        {
            foreach (var t in s_tiles.Values)
                if (t.bound && t.collider.enabled && t.collider.sharedMesh != null)
                    yield return (t.mesh, t.go.transform.localToWorldMatrix, TileBounds(t.coord));
        }
    }

    static float s_tileSize = 16f;
    static Bounds TileBounds(Vector3Int c) => new((Vector3)c * s_tileSize + Vector3.one * (s_tileSize * 0.5f), Vector3.one * s_tileSize);

    [Tooltip("Debug only: build tiles even where the clipmap's LOD0 box already has colliders (used to compare tile and chunk geometry).")]
    public bool ignoreClipmapCoverage;

    readonly List<AnchorSphere> _anchors = new();
    readonly HashSet<Vector3Int> _wanted = new();
    readonly List<Vector3Int> _scratch = new();
    readonly Stack<Tile> _pool = new();
    // Nearest-anchor distance per wanted coord, computed once per frame after
    // planning. Both the tile cap ranking and the build order read it.
    readonly Dictionary<Vector3Int, float> _dist = new();
    readonly List<Vector3Int> _ranked = new();
    Comparison<Vector3Int> _byDistance;
    MCChunkManager _m;
    // Which TerrainModificationSystem OnEdit is subscribed to. The manager
    // (and its Modifications) can be replaced by a scene reload, and this
    // component may be enabled before the manager is -- so the subscription
    // is re-checked every Update, the same way TerrainPhysicsWaker does it.
    TerrainModificationSystem _boundMods;
    // Worker tasks (mesh or bake) allowed to run at once. maxTileBuildsPerFrame
    // only limits starts per frame: a burst of new anchors would otherwise
    // keep starting builds every frame and flood the thread pool, starving
    // the clipmap's own jobs -- the player's ground -- for seconds.
    int _maxInFlight = 1;

    // Cached so planning does not allocate a delegate per frame.
    // A coord counts as covered only if it is at least 2 chunks inside the
    // LOD0 box on every axis: the coord itself and coord +-1, +-2 along x, y
    // and z must all be in the box. The box snaps to 2-chunk alignment
    // (MCChunkManager.ComputeBoxes), so when the player walks away it slides
    // off TWO chunk layers in a single step; a 1-chunk margin would leave the
    // second layer uncovered with no tile under it. With 2 chunks, an anchor
    // near the box face keeps tiles pre-built for everything one box step can
    // uncover, so the tile under a resting NPC already exists instead of
    // taking 2-3 frames to mesh and bake (the reverse hand-off).
    Func<Vector3Int, bool> _coveredByClipmap;
    static readonly Func<Vector3Int, bool> s_neverCovered = _ => false;

    static readonly VertexAttributeDescriptor[] kLayout = { new(VertexAttribute.Position, VertexAttributeFormat.Float32, 3) };
    const MeshUpdateFlags kUploadFlags = MeshUpdateFlags.DontValidateIndices |
                                         MeshUpdateFlags.DontRecalculateBounds |
                                         MeshUpdateFlags.DontResetBoneBounds;

    void OnEnable()
    {
        // Two cores left for the main and render threads; never more than 6,
        // so the clipmap's own workers always get a share of the pool.
        _maxInFlight = Mathf.Clamp(SystemInfo.processorCount - 2, 1, 6);
    }

    void OnDisable()
    {
        if (_boundMods != null) _boundMods.RegionModified -= OnEdit;
        _boundMods = null;
        ReleaseAll();
    }

    void OnDestroy()
    {
        while (_pool.Count > 0) DestroyTile(_pool.Pop());
    }

    void Update()
    {
        if (_m == null) _m = MCChunkManager.Instance;
        if (_m == null) _m = FindFirstObjectByType<MCChunkManager>();
        if (_m == null || _m.worldConfig == null) return;
        EnsureBound();

        var cfg = _m.worldConfig;
        float size = _m.Level0ChunkSize;
        if (_m.PhysicsField == null || _m.Modifications == null || size <= 0f) return;
        if (size != s_tileSize)
        {
            // Chunk size changed under us (config edit): every tile's coord
            // now means a different box. Start over.
            ReleaseAll();
            s_tileSize = size;
        }

        // 1. plan
        _anchors.Clear();
        foreach (var a in TerrainAnchor.All)
            if (a.physicsRadius > 0f) _anchors.Add(new AnchorSphere { position = a.Position, radius = a.physicsRadius });
        if (_coveredByClipmap == null) _coveredByClipmap = CoveredWithNeighbours;
        CollisionTilePlanner.Plan(_anchors, s_tileSize, ignoreClipmapCoverage ? s_neverCovered : _coveredByClipmap, _wanted);

        _dist.Clear();
        foreach (var c in _wanted) _dist[c] = NearestAnchorSq(c);
        if (_byDistance == null) _byDistance = (a, b) => _dist[a].CompareTo(_dist[b]);

        // Cap: the nearest maxCollisionTiles coords win. Dropping the rest
        // from the wanted set BEFORE release means a far tile gives up its
        // slot through the normal release path below, so an anchor that
        // moves never waits behind tiles nobody is standing near.
        int cap = Mathf.Max(0, cfg.maxCollisionTiles);
        if (_wanted.Count > cap)
        {
            _ranked.Clear();
            _ranked.AddRange(_wanted);
            _ranked.Sort(_byDistance);
            for (int i = cap; i < _ranked.Count; i++) _wanted.Remove(_ranked[i]);
        }

        // 2. release tiles no longer wanted (clipmap-covered ones only once the
        //    clipmap's own collider is ready -- see the hand-off rule above)
        _scratch.Clear();
        foreach (var kv in s_tiles)
        {
            if (_wanted.Contains(kv.Key)) continue;
            if (_m.IsInLevel0Box(kv.Key) && !_m.IsLevel0ColliderReady(kv.Key)) continue;
            if (kv.Value.task != null && !kv.Value.task.IsCompleted) continue; // let the running phase finish first
            _scratch.Add(kv.Key);
        }
        foreach (var c in _scratch) { Release(s_tiles[c]); s_tiles.Remove(c); }

        // 3. advance completed phases (phase 1 -> upload + start bake, phase 2 -> bind)
        //    -- but only for wanted tiles. A tile still here only because it
        //    is waiting for the clipmap's collider (hand-off) must not upload,
        //    bake or raise added=true just to be released a frame later: that
        //    is a spurious add/remove pair for NavMesh listeners. Its finished
        //    result is dropped instead, which also makes it releasable; it is
        //    marked dirty so that, if it becomes wanted again, it rebuilds
        //    rather than keeping a stale (or never-bound) collider.
        foreach (var t in s_tiles.Values)
        {
            if (t.task == null || !t.task.IsCompleted) continue;
            if (_wanted.Contains(t.coord)) { Advance(t); continue; }
            if (t.task.IsFaulted) Debug.LogException(t.task.Exception);
            t.task = null;
            t.phase = 0;
            t.dirty = true;
        }

        // 4. start new builds and dirty rebuilds, nearest to any anchor first,
        //    within the per-frame budget, the in-flight cap and the tile cap.
        //    A tile only (re)starts when it has no task at all, so its pooled
        //    job is never handed to a second worker while the first still owns
        //    it. After step 3 every completed task has been advanced or
        //    dropped, so a non-null task here is one still running (including
        //    phase-2 bakes step 3 just started). New tiles and dirty rebuilds
        //    share the in-flight cap; nearest-first order decides who waits.
        _scratch.Clear();
        int inFlight = 0;
        foreach (var t in s_tiles.Values) if (t.task != null) inFlight++;
        foreach (var c in _wanted)
            if (!s_tiles.TryGetValue(c, out var t) || (t.dirty && t.task == null)) _scratch.Add(c);
        if (_scratch.Count == 0) return;
        _scratch.Sort(_byDistance); // every coord here is in _wanted, so in _dist
        int started = 0;
        foreach (var c in _scratch)
        {
            if (started >= cfg.maxTileBuildsPerFrame || inFlight >= _maxInFlight) break;
            // At the cap only NEW tiles are refused; a dirty rebuild of an
            // existing tile further down the list must still get through.
            if (!s_tiles.ContainsKey(c) && s_tiles.Count >= cfg.maxCollisionTiles) continue;
            StartBuild(c, cfg);
            started++;
            inFlight++;
        }
    }

    void EnsureBound()
    {
        var mods = _m.Modifications;
        if (mods == _boundMods) return;
        if (_boundMods != null) _boundMods.RegionModified -= OnEdit;
        if (mods != null) mods.RegionModified += OnEdit;
        _boundMods = mods;
    }

    bool CoveredWithNeighbours(Vector3Int c)
    {
        if (_m == null || !_m.IsInLevel0Box(c)) return false;
        // The box is axis-aligned, so checking the axis-wise extremes is as
        // strong as checking the whole 5x5x5 neighbourhood; the +-1 samples
        // are implied by +-2 and the coord but kept for clarity of intent.
        for (int d = 1; d <= 2; d++)
        {
            if (!_m.IsInLevel0Box(c + new Vector3Int(d, 0, 0)) || !_m.IsInLevel0Box(c + new Vector3Int(-d, 0, 0))) return false;
            if (!_m.IsInLevel0Box(c + new Vector3Int(0, d, 0)) || !_m.IsInLevel0Box(c + new Vector3Int(0, -d, 0))) return false;
            if (!_m.IsInLevel0Box(c + new Vector3Int(0, 0, d)) || !_m.IsInLevel0Box(c + new Vector3Int(0, 0, -d))) return false;
        }
        return true;
    }

    float NearestAnchorSq(Vector3Int c)
    {
        Vector3 centre = TileBounds(c).center;
        float best = float.MaxValue;
        foreach (var a in _anchors) best = Mathf.Min(best, (a.position - centre).sqrMagnitude);
        return best;
    }

    void StartBuild(Vector3Int c, WorldConfig cfg)
    {
        if (!s_tiles.TryGetValue(c, out var t))
        {
            t = _pool.Count > 0 ? _pool.Pop() : NewTile();
            t.coord = c;
            t.go.name = $"CollisionTile_{c.x}_{c.y}_{c.z}";
            t.go.layer = cfg.terrainLayer;
            t.go.transform.position = (Vector3)c * s_tileSize;
            s_tiles[c] = t;
        }
        t.dirty = false;

        // Same inputs MCChunkManager.PrepareBuild gives a LOD0 chunk, minus
        // everything that exists for the render mesh: renderField IS the
        // physics field, no transition faces, no separate collision mesh. A
        // fresh or reused job carries no GPU-prefetched density (those flags
        // are only ever set by the manager's GPU staging, and are one-shot),
        // so Build samples the field on the CPU.
        t.job ??= new ChunkMeshJob();
        var job = t.job;
        job.origin = (Vector3)c * s_tileSize;
        job.cells = cfg.cellsPerChunk;
        job.cellSize = s_tileSize / cfg.cellsPerChunk.x;
        job.isoLevel = cfg.isoLevel;
        job.densitySampling = 1;
        job.renderField = _m.PhysicsField;
        job.physicsField = null;
        job.needs = default;
        job.buildCollider = false;
        job.refreshCollider = false;
        job.physicsDiffersNearby = false;
        job.deferColliderBake = false;
        // Same padding as the manager's LOD0 test (trilinear support + gradient
        // eps): a physical edit just outside the box still reaches the halo
        // samples, and the all-air/all-solid skip must not hide a carved cave.
        Vector3 pad = Vector3.one * (cfg.cellSize + 0.5f * job.cellSize);
        job.modsOverlapChunk = _m.Modifications.physical.OverlapsBounds(job.origin - pad, job.origin + Vector3.one * s_tileSize + pad);

        // Phase 1 (worker): mesh. Advance() then uploads on the main thread
        // and starts phase 2 (worker): Physics.BakeMesh. Mesh APIs are main-
        // thread only, and assigning sharedMesh before the bake would cook
        // inline -- the exact main-thread cost MarchingChunk's deferred bake
        // avoids.
        t.phase = 1;
        t.task = Task.Run(() => ChunkMesher.Build(job));
    }

    void Advance(Tile t)
    {
        var job = t.job;
        var finished = t.task;
        t.task = null;
        int phase = t.phase;
        t.phase = 0;

        // A faulted phase-2 bake leaves the tile with its mesh already
        // rewritten but the collider still bound to the OLD cook; it stays that
        // way (not retried, to avoid per-frame exception spam) until the next
        // physical edit marks the tile dirty.
        if (finished.IsFaulted) { Debug.LogException(finished.Exception); return; }

        if (phase == 1)
        {
            if (job.error != null) { Debug.LogException(job.error); return; }

            if (job.vertexCount == 0 || job.indexCount == 0)
            {
                // Built, just empty (all air / all rock, or an edit dug the
                // last surface out).
                bool wasBound = t.bound && t.collider.enabled;
                t.collider.sharedMesh = null;
                t.collider.enabled = false;
                t.mesh.Clear();
                t.bound = true;
                if (wasBound)
                {
                    TileChanged?.Invoke(TileBounds(t.coord), null, Matrix4x4.identity, false);
                    TerrainPhysicsWaker.Wake(TileBounds(t.coord));
                }
                return;
            }

            int n = job.vertexCount;
            if (t.positions == null || t.positions.Length < n) t.positions = new Vector3[Mathf.NextPowerOfTwo(Mathf.Max(n, 64))];
            var positions = t.positions;
            var packed = job.packedVerts;
            for (int i = 0; i < n; i++) positions[i] = packed[i].position;

            // The previous build's collider is deliberately left bound while
            // the mesh underneath it is rewritten and re-baked: the cooked
            // data it holds stays valid, so a rebuild after an edit never
            // leaves a no-collision gap. MarchingChunk.ApplyBuild relies on
            // the same behaviour; the null-then-assign in phase 2 is what
            // actually swaps the cook.
            var mesh = t.mesh;
            mesh.SetVertexBufferParams(n, kLayout);
            mesh.SetVertexBufferData(positions, 0, 0, n, 0, kUploadFlags);
            mesh.SetIndexBufferParams(job.indexCount, IndexFormat.UInt32);
            mesh.SetIndexBufferData(job.packedIndices, 0, 0, job.indexCount, kUploadFlags);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, job.indexCount, MeshTopology.Triangles)
            {
                bounds = job.bounds,
                firstVertex = 0,
                vertexCount = n,
            }, kUploadFlags);
            mesh.bounds = job.bounds; // computed on the worker by PackForUpload

            int id = mesh.GetInstanceID(); // Unity object access: main thread, before the task
            t.phase = 2;
            t.task = Task.Run(() => Physics.BakeMesh(id, false));
            return;
        }

        // phase 2: the cook is cached, so this pair is cheap
        t.collider.sharedMesh = null;
        t.collider.sharedMesh = t.mesh;
        t.collider.enabled = true;
        t.bound = true;
        TileChanged?.Invoke(TileBounds(t.coord), t.mesh, t.go.transform.localToWorldMatrix, true);
        TerrainPhysicsWaker.Wake(TileBounds(t.coord));
    }

    void OnEdit(Bounds b, bool affectsPhysics)
    {
        if (!affectsPhysics) return;
        b.Expand(1f);
        foreach (var t in s_tiles.Values)
            if (TileBounds(t.coord).Intersects(b)) t.dirty = true;
    }

    Tile NewTile()
    {
        var go = new GameObject("CollisionTile") { hideFlags = HideFlags.DontSaveInEditor };
        go.transform.SetParent(transform, false);
        var col = go.AddComponent<MeshCollider>();
        col.enabled = false;
        var mesh = new Mesh { indexFormat = IndexFormat.UInt32, name = "MC_CollisionTile", hideFlags = HideFlags.DontSaveInEditor };
        mesh.MarkDynamic();
        return new Tile { go = go, collider = col, mesh = mesh };
    }

    // Caller guarantees no task is running (Update's release loop checks it).
    void Release(Tile t)
    {
        if (t.bound && t.collider.enabled)
            TileChanged?.Invoke(TileBounds(t.coord), null, Matrix4x4.identity, false);
        t.collider.sharedMesh = null;
        t.collider.enabled = false;
        t.bound = false;
        t.dirty = false;
        t.task = null;
        t.phase = 0;
        if (_pool.Count < 256) _pool.Push(t); else DestroyTile(t);
    }

    // Tears down every live tile, telling listeners about the bound ones.
    // Tasks may still be running here (disable, chunk-size change), so these
    // are destroyed rather than pooled: DestroyTile waits for the task.
    void ReleaseAll()
    {
        foreach (var t in s_tiles.Values)
        {
            if (t.bound && t.collider != null && t.collider.enabled)
                TileChanged?.Invoke(TileBounds(t.coord), null, Matrix4x4.identity, false);
            DestroyTile(t);
        }
        s_tiles.Clear();
    }

    static void DestroyTile(Tile t)
    {
        // A running BakeMesh holds only the mesh's instance ID; freeing the
        // mesh under it is not allowed, so block until it is done.
        try { t.task?.Wait(); } catch { /* already reported, or no longer wanted */ }
        t.task = null;
        if (t.mesh != null) Destroy(t.mesh);
        if (t.go != null) Destroy(t.go);
    }
}
