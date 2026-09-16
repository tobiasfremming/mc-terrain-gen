using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// Runtime NavMesh over the streamed terrain. Sources are terrain COLLIDER
// meshes (clipmap LOD0 chunks + collision tiles): render meshes of coarser
// rings may exist only on the GPU. See the integration plan, Task 10.
//
// NavPatchPlanner decides where NavMesh lives (one flat box around the
// anchors, or quantised tangent patches on a planet); this component keeps
// one NavMeshData per (patch, agent type) in step with that plan and rebuilds
// a patch whenever terrain collision inside it changes. Agents (Task 11)
// watch PatchVersion to know when NavMesh under them may have been removed.
public class TerrainNavMesh : MonoBehaviour
{
    public static TerrainNavMesh Instance { get; private set; }

    // (sources, patchWorldBounds). Raised right before a patch is (re)built;
    // game code and flora append obstacles/buildings for that region.
    public static event Action<List<NavMeshBuildSource>, Bounds> CollectExtraSources;

    // Bumped when NavMesh instances are removed (a patch retired, a patch's
    // data dropped for an agent-type change, or this component disabled);
    // agents should re-validate (isOnNavMesh). NavMesh appearing needs no
    // bump because unplaced agents poll. Deliberately NOT bumped on patch
    // add, first build or flat-bounds growth: Task 11's guard re-plans every
    // agent on a bump, and the flat patch moves every ~32 m of travel.
    public int PatchVersion { get; private set; }
    public bool IsBuilding => _ops.Count > 0;

    sealed class Live
    {
        public NavPatch patch;
        // Index-aligned with the agentIds this patch was created for. A slot
        // stays null (and its instance default) while that id has no valid
        // NavMesh settings.
        public readonly List<NavMeshData> data = new();
        public readonly List<NavMeshDataInstance> instances = new();
        public int[] agentIds;
        public readonly List<AsyncOperation> ops = new();
        public bool dirty = true;
        public bool built;       // at least one build finished -- replacements must be built before what they replace is retired
    }

    readonly NavPatchPlanner _planner = new();
    readonly Dictionary<int, Live> _live = new();
    readonly List<AsyncOperation> _ops = new();
    readonly List<NavAnchor> _anchors = new();
    readonly List<NavMeshBuildSource> _sources = new();
    readonly List<NavMeshBuildMarkup> _noMarkups = new();
    readonly List<NavMeshBuildSource> _gameSources = new();
    // Per-frame scratch, hoisted so Update allocates nothing in steady state.
    readonly HashSet<int> _keep = new();
    readonly List<int> _dead = new();
    readonly HashSet<int> _warnedAgentIds = new();
    int[] _cfgAgentIds = Array.Empty<int>();
    bool _cfgHeightMesh;
    float _lastUpdate;
    bool _anyDirty = true;
    MCChunkManager _m;

    void OnEnable()
    {
        Instance = this;
        MarchingChunk.ColliderChanged += OnChunk;
        TerrainCollisionStreamer.TileChanged += OnTile;
        _anyDirty = true;
    }

    void OnDisable()
    {
        MarchingChunk.ColliderChanged -= OnChunk;
        TerrainCollisionStreamer.TileChanged -= OnTile;
        foreach (var l in _live.Values) RemoveLive(l);
        _live.Clear();
        // Running builds cannot be cancelled; they finish into NavMeshData
        // nobody references any more and are collected with it.
        _ops.Clear();
        if (Instance == this) Instance = null;
        PatchVersion++;
    }

    void OnChunk(MarchingChunk c) => MarkDirty(c.WorldBounds);
    void OnTile(Bounds b, Mesh m, Matrix4x4 t, bool added) => MarkDirty(b);

    void MarkDirty(Bounds world)
    {
        foreach (var l in _live.Values)
            if (WorldBounds(l.patch).Intersects(world)) { l.dirty = true; _anyDirty = true; }
    }

    void Update()
    {
        // Resolved lazily: this OnEnable can run before MCChunkManager's, when
        // Instance is still null. Explicit null checks -- `??` bypasses Unity's
        // destroyed-object equality.
        if (_m == null) _m = MCChunkManager.Instance;
        if (_m == null) _m = FindFirstObjectByType<MCChunkManager>();
        if (_m == null || _m.worldConfig == null) return;
        var cfg = _m.worldConfig;

        // A config change of the agent set or height-mesh flag invalidates every patch.
        var ids = cfg.navAgentTypeIds ?? Array.Empty<int>();
        if (!SameIds(ids, _cfgAgentIds) || cfg.navBuildHeightMesh != _cfgHeightMesh)
        {
            _cfgAgentIds = (int[])ids.Clone();
            _cfgHeightMesh = cfg.navBuildHeightMesh;
            foreach (var l in _live.Values) l.dirty = true;
            _anyDirty = true;
        }

        _ops.RemoveAll(o => o.isDone);
        foreach (var l in _live.Values)
            if (l.ops.Count > 0 && l.ops.TrueForAll(o => o.isDone)) { l.ops.Clear(); l.built = true; }

        // 1. anchors -> patches
        _anchors.Clear();
        if (_m.target)
        {
            float radius = _m.target.TryGetComponent<TerrainAnchor>(out var ta) ? ta.navRadius : 40f;
            _anchors.Add(new NavAnchor { position = _m.target.position, radius = radius, primary = true });
        }
        foreach (var a in TerrainAnchor.All)
            if (a.navRadius > 0f && (!_m.target || a.transform != _m.target))
                _anchors.Add(new NavAnchor { position = a.Position, radius = a.navRadius });

        _planner.radial = _m.TryGetPlanetCenter(out Vector3 pc, out _);
        _planner.planetCenter = pc;
        var patches = _planner.Plan(_anchors);

        // 2. reconcile: ids that disappeared are removed AFTER their replacements exist
        _keep.Clear();
        bool removed = false;
        foreach (var p in patches)
        {
            _keep.Add(p.id);
            if (_live.TryGetValue(p.id, out var l))
            {
                // Only the flat patch changes bounds under a fixed id (origin is
                // always zero there); radial patches get a new id instead.
                if (l.patch.localBounds != p.localBounds) { l.patch = p; l.dirty = true; _anyDirty = true; }
            }
            else { _live[p.id] = new Live { patch = p }; _anyDirty = true; }
        }

        // 3. build/update dirty patches, throttled, one async batch at a time.
        //    `_ops.Count == 0` also guarantees no patch ever has two builds running.
        if (_anyDirty && _ops.Count == 0 && Time.unscaledTime - _lastUpdate >= cfg.navUpdateInterval)
        {
            // unscaled: edits made while the game is paused still re-bake
            _lastUpdate = Time.unscaledTime;
            _anyDirty = false;
            foreach (var l in _live.Values)
            {
                if (!_keep.Contains(l.patch.id) || !l.dirty) continue;
                l.dirty = false;
                Build(l, cfg);
            }
        }

        // 4. retire patches the planner dropped, but only once every kept patch
        //    overlapping them has finished a build (no NavMesh gap under agents)
        //    and their own last build has landed.
        _dead.Clear();
        foreach (var kv in _live)
        {
            if (_keep.Contains(kv.Key) || kv.Value.ops.Count > 0) continue;
            Bounds wb = WorldBounds(kv.Value.patch);
            bool covered = true;
            foreach (var other in _live.Values)
                if (_keep.Contains(other.patch.id) && !other.built && WorldBounds(other.patch).Intersects(wb)) { covered = false; break; }
            if (covered) _dead.Add(kv.Key);
        }
        foreach (var id in _dead) { removed |= RemoveLive(_live[id]); _live.Remove(id); }

        if (removed) PatchVersion++;
    }

    void Build(Live l, WorldConfig cfg)
    {
        // The agent set this patch was created for no longer matches the
        // config: drop its data and rebuild from scratch, keeping the lists
        // index-aligned with the new id array.
        if (l.agentIds == null || !SameIds(l.agentIds, _cfgAgentIds))
        {
            if (RemoveLive(l)) PatchVersion++;
            l.agentIds = (int[])_cfgAgentIds.Clone();
            for (int i = 0; i < l.agentIds.Length; i++) { l.data.Add(null); l.instances.Add(default); }
        }

        Bounds world = WorldBounds(l.patch);
        bool collected = false;

        for (int i = 0; i < l.agentIds.Length; i++)
        {
            int id = l.agentIds[i];
            var settings = NavMesh.GetSettingsByID(id);
            if (settings.agentTypeID == -1)
            {
                if (_warnedAgentIds.Add(id))
                    Debug.LogWarning($"TerrainNavMesh: agent type id {id} in WorldConfig.navAgentTypeIds has no NavMesh settings (Navigation window > Agents); skipped.", this);
                continue;
            }
            settings.buildHeightMesh = cfg.navBuildHeightMesh;

            if (l.data[i] == null)
            {
                var d = new NavMeshData(settings.agentTypeID) { position = l.patch.origin, rotation = l.patch.rotation };
                l.data[i] = d;
                l.instances[i] = NavMesh.AddNavMeshData(d, l.patch.origin, l.patch.rotation);
            }

            // Sources are collected immediately before the call, same frame.
            // ASSUMPTION (unverified offline): UpdateNavMeshDataAsync reads the
            // source Mesh geometry while it is being called, on the main thread,
            // so chunk/tile meshes the terrain rewrites in place on later frames
            // cannot corrupt a build already in flight. If that is wrong, an
            // edit during a running build could crash or bake garbage
            // triangles -- see the Task 10 report's deferred checks.
            if (!collected) { CollectSources(world, cfg); collected = true; }
            var op = NavMeshBuilder.UpdateNavMeshDataAsync(l.data[i], settings, _sources, l.patch.localBounds);
            _ops.Add(op);
            l.ops.Add(op);
        }

        // Nothing to build (no valid agent types): count it as landed, or the
        // patches it replaces could never retire.
        if (l.ops.Count == 0) l.built = true;
    }

    void CollectSources(Bounds world, WorldConfig cfg)
    {
        _sources.Clear();
        foreach (var c in MarchingChunk.Active)
        {
            var mesh = c.ColliderMesh;
            if (mesh == null || !c.WorldBounds.Intersects(world)) continue;
            _sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = c.transform.localToWorldMatrix, area = 0 });
        }
        foreach (var (mesh, l2w, b) in TerrainCollisionStreamer.ActiveTiles)
        {
            if (!b.Intersects(world)) continue;
            _sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = l2w, area = 0 });
        }
        if (cfg.navGameLayers.value != 0)
        {
            _gameSources.Clear();
            NavMeshBuilder.CollectSources(world, cfg.navGameLayers, NavMeshCollectGeometry.PhysicsColliders, 0, _noMarkups, _gameSources);
            // The terrain's own MeshColliders are already in the list above;
            // if the game layers include the terrain layer (e.g. both left on
            // Default) they would be baked twice, so drop them here.
            foreach (var s in _gameSources)
                if (!IsTerrainCollider(s.component) && !IsAgentCollider(s.component)) _sources.Add(s);
        }
        CollectExtraSources?.Invoke(_sources, world);
    }

    static bool IsTerrainCollider(Component c)
    {
        if (c == null) return false;
        if (c.TryGetComponent<MarchingChunk>(out _)) return true;
        var parent = c.transform.parent;
        return parent != null && parent.TryGetComponent<TerrainCollisionStreamer>(out _);
    }

    // Agents and the player move; baking their capsules would punch holes in
    // the NavMesh under themselves whenever their layer is in navGameLayers.
    static bool IsAgentCollider(Component c)
    {
        if (c == null) return false;
        return c.TryGetComponent<NavMeshAgent>(out _) || c.TryGetComponent<CharacterController>(out _);
    }

    static bool SameIds(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    static Bounds WorldBounds(NavPatch p)
    {
        // conservative world AABB of the rotated local box
        Vector3 c = p.origin + p.rotation * p.localBounds.center;
        Vector3 e = p.localBounds.extents;
        Matrix4x4 r = Matrix4x4.Rotate(p.rotation);
        Vector3 we = new(
            Mathf.Abs(r.m00) * e.x + Mathf.Abs(r.m01) * e.y + Mathf.Abs(r.m02) * e.z,
            Mathf.Abs(r.m10) * e.x + Mathf.Abs(r.m11) * e.y + Mathf.Abs(r.m12) * e.z,
            Mathf.Abs(r.m20) * e.x + Mathf.Abs(r.m21) * e.y + Mathf.Abs(r.m22) * e.z);
        return new Bounds(c, we * 2f);
    }

    // Returns true if any NavMesh instance was actually removed.
    static bool RemoveLive(Live l)
    {
        bool any = false;
        foreach (var inst in l.instances) if (inst.valid) { inst.Remove(); any = true; }
        l.instances.Clear();
        l.data.Clear();
        l.ops.Clear();
        l.agentIds = null;
        return any;
    }
}
