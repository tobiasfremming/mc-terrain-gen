using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using LSystems;
using UnityEngine;

// Scatters baked plant prototypes over the terrain and draws them instanced.
//
// THE CONTRACT: what grows where is a function of the world and never of the
// viewer. Concretely --
//
//   * Plot identity is world-anchored. Flat world: floor(x / plotSize),
//     floor(z / plotSize). Globe: a cell of a cube-sphere grid (face, u, v).
//     Neither knows the eye exists.
//   * A plot's contents are a pure function of (PlantWorld, plot id): the
//     RNG is seeded from the plot id, and the ground is found by walking the
//     density field, which is itself a pure function of position.
//   * The set of plots considered each frame is EVERY plot within `radius`
//     of the eye, found by an exact flood fill over plot adjacency. The
//     previous version found plots by sampling a lattice of directions laid
//     out in the eye's tangent plane. Cube-sphere cells shrink to half their
//     size towards a face corner, so away from a face centre the lattice
//     stepped over cells -- and WHICH cells it stepped over depended on where
//     the eye was, so whole plots blinked in and out as the player walked.
//     Simulated with the real numbers (R = 100 km, 24 m plots): at 45
//     degrees from a face centre it missed 111 of 240 in-range plots and
//     ~1,000 plots toggled over a 40 m walk. A flood fill cannot miss.
//   * Distance to the eye decides only whether a plant is drawn and which
//     LOD mesh it uses. Nothing about a plant -- position, rotation, scale,
//     variant -- reads the eye. The old "fade band" scaled every plant toward
//     zero over the last 18 m before the cull, which turned a 10 m tree into
//     a 5 m tree at 90 m that grew as you approached. It is gone; plants pop
//     at cullDistance, so set that far enough that the pop is a few pixels,
//     or bake impostors and let a card carry the species to the horizon.
//
// WHERE THE TIME GOES. Finding the ground for one plant costs tens of
// density samples, a plot is ~16 plants, and a 750 m radius on a globe is
// two to five thousand plots. Measured on the main thread that was 7 ms a
// plot and 170 ms frames for the fifteen seconds it took a cold start to
// fill -- and the same hitch on every new row of plots while walking. So
// plots are built on worker threads: the main thread hands out plot ids
// nearest-first and drains finished plots, and never samples the field
// itself. Every density field already runs on the mesher's workers, so it is
// safe to call from here too; the only rule is that nothing in a build may
// touch a Unity extern (Quaternion.Euler, Matrix4x4.TRS), which is why the
// small rotation/matrix helpers at the bottom exist.
//
// The workers are dedicated Threads, NOT Task.Run. A worker here lives for
// the whole session and spends most of it blocked on the request queue, and
// parking long-lived blocking work on threadpool threads makes Mono's
// threadpool monitor inject and retire threads to compensate -- a path with
// a refcount bug that took the Editor down mid-session
// ("mono_refcount_decrement: cannot decrement a ref with value 0" in
// threadpool-worker-default.c, crash 2026-09-05). The mesher's Task.Run use is
// fine because its jobs are short; these are not.
//
// NOTHING IS A GAMEOBJECT. A forest is instance matrices grouped by
// (species, variant, LOD) and handed to Graphics.RenderMeshInstanced -- one
// draw call per group, per submesh.
[ExecuteAlways]
public class PlantScatter : MonoBehaviour
{
    [Tooltip("Last-resort eye, used only when Viewer is empty, the MCChunkManager has no target, and there is no main camera. It does NOT define the coordinate frame -- placement is absolute world space by construction.")]
    public Transform target;

    [Tooltip("What draw distances are measured FROM, and what the populated region is centred on -- the player. Left empty, the MCChunkManager's current target is used (PlayerBootstrap points that at the player on Play), then the main camera, then Target.")]
    public Transform viewer;

    [Tooltip("Read for enablePlants, plantWorld, cellSize and the density field. Falls back to the MCChunkManager's.")]
    public WorldConfig worldConfig;

    [Tooltip("Draw in the editor as well as in play mode. Off keeps the scene view cheap while editing terrain.")]
    public bool drawInEditMode = true;

    [Tooltip("Plots handed to the background builders per frame, nearest first. Building happens off the main thread, so this only bounds how far ahead the queue runs -- a small number keeps it responsive to the player turning around.")]
    [Range(1, 128)] public int plotsBuiltPerFrame = 24;

    [Tooltip("Worker threads building plots. 0 = auto (cores - 2, clamped 1..4). They share the machine with the terrain mesher's own workers.")]
    [Range(0, 8)] public int workerThreads = 0;

    // Live stats, deliberately NOT serialized (writing serialized fields
    // every frame dirties the component every frame; PlantScatterEditor reads
    // these properties instead).
    int _plotsPending, _visiblePlots, _instances, _drawCalls, _plotsBuiltTotal;
    string _status = "";

    public int PlotsPending => _plotsPending;
    public int VisiblePlots => _visiblePlots;
    public int Instances => _instances;
    public int DrawCalls => _drawCalls;
    // Cumulative. Standing still, this must stop climbing once the radius is
    // filled; if it keeps rising, plots are being thrown away and rebuilt.
    public int PlotsBuiltTotal => _plotsBuiltTotal;
    public string Status => _status;

    // One placed plant. A struct in a flat array: a forest is tens of
    // thousands of these.
    struct Instance
    {
        public Matrix4x4 trs;
        public Vector3 pos;
        public int variant;
    }

    // Instances are stored species-major: species s occupies
    // items[runStart[s] .. runStart[s+1]). That lets the gather skip a whole
    // species in a plot with one distance test instead of one per plant --
    // which matters, because a 750 m tree radius holds thousands of plots
    // whose ground cover is culled at 220 m.
    sealed class Plot
    {
        public Instance[] items;
        public int[] runStart;
        public Vector3 centre;   // mean instance position
        public float reach;      // farthest instance from centre
        public static readonly Plot Empty = new Plot { items = new Instance[0], runStart = new int[0] };
    }

    readonly Dictionary<long, Plot> _plots = new Dictionary<long, Plot>();

    // ---- draw batches -------------------------------------------------------
    // One list per (species, variant, lod), addressed by index rather than a
    // dictionary: the hot loop touches this once per drawn plant.
    const int kMaxVariants = 64;             // PlantPrototypeBaker clamps to 64
    const int kLodSlots = 4;                 // mesh LOD 0..2 + impostor
    const int kImpostorLod = 3;
    const int kMaxPerCall = 1023;            // Unity's per-call instancing limit
    List<Matrix4x4>[] _batches = new List<Matrix4x4>[0];

    // Per-species numbers precomputed once per frame for the hot loop.
    float[] _cull2 = new float[0], _imp2 = new float[0], _lod1sq = new float[0], _lod2sq = new float[0], _cull = new float[0];
    bool[] _useLod = new bool[0], _hasImpostor = new bool[0];

    // ---- region ---------------------------------------------------------------
    // Every plot key within `radius` of the eye, from the last flood fill.
    // Re-filled only when the eye has moved a quarter plot or the radius
    // changed; pending plots are re-requested by Gather on every pass.
    readonly List<long> _region = new List<long>();
    readonly HashSet<long> _regionSet = new HashSet<long>();
    readonly Queue<long> _floodQueue = new Queue<long>();
    readonly List<long> _evictScratch = new List<long>();
    Vector3 _regionEye;
    float _regionRadius = -1f;
    bool _regionValid;
    const int kMaxRegionPlots = 65536; // safety cap on the flood fill, never hit with sane radii

    // ---- background building ------------------------------------------------
    struct BuildRequest { public long key; public BuildContext ctx; }
    struct BuildResult { public long key; public int generation; public Plot plot; }

    BlockingCollection<BuildRequest> _requests;
    readonly ConcurrentQueue<BuildResult> _results = new ConcurrentQueue<BuildResult>();
    readonly HashSet<long> _inFlight = new HashSet<long>();
    CancellationTokenSource _cts;
    Thread[] _workers;
    int _generation;                          // bumped by ClearCache; stale results are dropped
    BuildContext _ctx;                        // snapshot handed to workers; rebuilt when settings change
    const int kMaxInFlight = 256;             // keeps the queue short, so it stays nearest-first as the eye moves

    MCChunkManager _manager;
    PlanetField _planet;
    Vector3 _planetCentre;
    int _cellsPerFace;
    float _cellInv;      // 2 / cellsPerFace: width of one cell in cube-face [-1,1] units
    float _filterWidth;  // sample spacing to band-limit the field to: LOD0's cell size
    Bounds _drawBounds;
    int _settingsHash;

    void OnDisable() { StopWorkers(); }
    void OnDestroy() { StopWorkers(); }

    void Update()
    {
        _visiblePlots = 0; _instances = 0; _drawCalls = 0; _plotsPending = 0;

        if (!Application.isPlaying && !drawInEditMode) { _status = "edit-mode drawing off"; return; }

        WorldConfig cfg = ResolveConfig();
        if (cfg == null) { _status = "no WorldConfig"; return; }
        if (!cfg.enablePlants) { _status = "disabled in WorldConfig"; return; }

        PlantWorld world = cfg.plantWorld;
        if (world == null || world.species == null || world.species.Length == 0) { _status = "no PlantWorld / no species"; return; }

        DensityField field = cfg.EffectiveDensity;
        if (field == null) { _status = "no density field"; return; }

        // The ground is found on the field band-limited to LOD0's spacing --
        // the surface the player actually stands on. Coarser rings differ
        // from it by less than the sink depth, and only far away.
        _filterWidth = Mathf.Max(0.01f, cfg.cellSize);

        PlanetField planet = field as PlanetField;
        int cellsPerFace = 0;
        Vector3 planetCentre = Vector3.zero;
        if (planet != null)
        {
            // Quarter circumference / plot size: the MEAN cell edge is plotSize.
            cellsPerFace = Mathf.Max(1, Mathf.CeilToInt(planet.radius * (Mathf.PI * 0.5f) / PlotSize(world)));
            planetCentre = planet.center;
        }
        if ((planet != null) != (_planet != null) || cellsPerFace != _cellsPerFace || planetCentre != _planetCentre) ClearCache();
        _planet = planet;
        _planetCentre = planetCentre;
        _cellsPerFace = cellsPerFace;
        _cellInv = cellsPerFace > 0 ? 2f / cellsPerFace : 0f;

        int hash = PlacementHash(world, field, _filterWidth);
        if (hash != _settingsHash) { _settingsHash = hash; ClearCache(); }
        if (_ctx == null || _ctx.generation != _generation) _ctx = BuildContext.Capture(world, field, _planet, _cellInv, _filterWidth, _generation);

        Transform v = ResolveViewer();
        if (v == null) { _status = "no viewer and no target"; return; }
        Vector3 eye = v.position;

        if (_planet != null && (eye - _planet.center).sqrMagnitude < 1e-6f) { _status = "viewer is at the planet centre"; return; }

        PrepareSpecies(world);
        float maxCull = 0f;
        for (int i = 0; i < _cull.Length; i++) maxCull = Mathf.Max(maxCull, _cull[i]);

        // Bounds for the instanced draws, in WORLD space around the eye: on a
        // globe the plants sit 100 km from the origin, so a box around this
        // component's transform would silently cull every draw.
        _drawBounds = new Bounds(eye, Vector3.one * (maxCull * 2f + 100f));

        _status = "OK";
        DrainResults();
        EnsureRegion(world, eye);
        Gather(world, eye);
        Draw(world);
        EvictFarPlots(world);
    }

    static float PlotSize(PlantWorld world) => Mathf.Max(2f, world.plotSize);

    WorldConfig ResolveConfig()
    {
        if (worldConfig != null) return worldConfig;
        if (_manager == null) _manager = FindFirstObjectByType<MCChunkManager>();
        return _manager != null ? _manager.worldConfig : null;
    }

    // The eye: what the region is centred on and what draw distances are
    // measured from. Left empty this lands on the manager's target, which
    // PlayerBootstrap points at the player on Play. It decides only where the
    // window onto the field sits, never what is under it.
    Transform ResolveViewer()
    {
        if (viewer != null) return viewer;
        if (_manager == null) _manager = FindFirstObjectByType<MCChunkManager>();
        if (_manager != null && _manager.target != null) return _manager.target;
        return Camera.main != null ? Camera.main.transform : target;
    }

    void PrepareSpecies(PlantWorld world)
    {
        int n = world.species.Length;
        if (_cull.Length != n)
        {
            _cull = new float[n]; _cull2 = new float[n]; _imp2 = new float[n]; _lod1sq = new float[n]; _lod2sq = new float[n];
            _useLod = new bool[n]; _hasImpostor = new bool[n];
            _batches = new List<Matrix4x4>[n * kMaxVariants * kLodSlots];
        }
        for (int i = 0; i < n; i++)
        {
            PlantSpecies sp = world.species[i];
            bool on = sp != null && sp.enabled && sp.prototypes != null;
            _cull[i] = on ? sp.cullDistance : 0f;
            _cull2[i] = _cull[i] * _cull[i];
            _imp2[i] = on ? sp.impostorDistance * sp.impostorDistance : 0f;
            _lod1sq[i] = on ? sp.lod1Distance * sp.lod1Distance : 0f;
            _lod2sq[i] = on ? sp.lod2Distance * sp.lod2Distance : 0f;
            _useLod[i] = on && sp.useLod;
            // Unity-object null checks are not free; ask once per species per
            // frame rather than once per instance.
            _hasImpostor[i] = on && sp.prototypes.HasImpostor;
        }
    }

    // ---- plot ids ---------------------------------------------------------
    //
    // Flat:  key = (px, pz)          -- packed as face 0x7F (an impossible face)
    // Globe: key = (face, cu, cv)    -- cube-sphere cell, face 0..5
    //
    // Both are 3 ints in one long, so the flood fill, the cache and eviction
    // never care which world they are in.

    const int kFlatFace = 0x7F;

    // Flat coordinates are signed and can exceed 28 bits in theory but not in
    // practice (2^27 plots * 24 m = 3.2 million km); globe cells never are
    // negative. Signed values are stored as their low 28 bits and sign-
    // extended on the way out.
    static void Unpack(long key, out int face, out int a, out int b)
    {
        face = (int)(key >> 56) & 0xFF;
        a = (int)((key >> 28) & 0x0FFFFFFF);
        b = (int)(key & 0x0FFFFFFF);
        if (face == kFlatFace)
        {
            if ((a & 0x08000000) != 0) a |= unchecked((int)0xF0000000);
            if ((b & 0x08000000) != 0) b |= unchecked((int)0xF0000000);
        }
    }

    static long PackFlat(int px, int pz) => ((long)kFlatFace << 56) | ((long)(px & 0x0FFFFFFF) << 28) | (uint)(pz & 0x0FFFFFFF);
    static long PackGlobe(int face, int cu, int cv) => ((long)face << 56) | ((long)cu << 28) | (uint)cv;

    long CellOf(Vector3 p, float size)
    {
        if (_planet == null)
            return PackFlat(Mathf.FloorToInt(p.x / size), Mathf.FloorToInt(p.z / size));
        DirToFaceCell((p - _planet.center).normalized, _cellsPerFace, out int face, out int cu, out int cv);
        return PackGlobe(face, cu, cv);
    }

    // The cell one step away along an axis of THIS cell's face. Past a face
    // edge the (u, v) leave [-1, 1]; FaceUVToDir still returns a valid
    // direction there (it is just a point on the cube beyond the edge), and
    // re-projecting it yields the neighbouring face's cell that contains it.
    // That is the whole seam handling: no edge tables, no corner cases.
    long Neighbour(long key, int da, int db)
    {
        Unpack(key, out int face, out int a, out int b);
        if (face == kFlatFace) return PackFlat(a + da, b + db);
        float u = -1f + (a + da + 0.5f) * _cellInv;
        float v = -1f + (b + db + 0.5f) * _cellInv;
        DirToFaceCell(FaceUVToDir(face, u, v), _cellsPerFace, out int nf, out int nu, out int nv);
        return PackGlobe(nf, nu, nv);
    }

    // Horizontal (flat) or tangential (globe) distance from the eye's vertical
    // axis to the plot's centre. Height is deliberately ignored, in both
    // worlds: a player on a rise must not lose the plants at their feet.
    float CentreDistance(long key, Vector3 eye, Vector3 up, float size)
    {
        Unpack(key, out int face, out int a, out int b);
        if (face == kFlatFace)
        {
            float dx = (a + 0.5f) * size - eye.x, dz = (b + 0.5f) * size - eye.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
        Vector3 dir = FaceUVToDir(face, -1f + (a + 0.5f) * _cellInv, -1f + (b + 0.5f) * _cellInv);
        // Chord between unit directions times R: equals the tangential
        // distance to first order and, unlike acos(dot), keeps its precision
        // for the tiny angles a 100 km sphere produces.
        return (dir - up).magnitude * _planet.radius;
    }

    // Longest edge a cell can have. Globe cells are largest at a face centre,
    // where one cell spans R * cellInv metres of arc.
    float CellDiagonal(float size)
    {
        float edge = _planet != null ? _planet.radius * _cellInv : size;
        return edge * 1.4143f;
    }

    // ---- region -----------------------------------------------------------

    void EnsureRegion(PlantWorld world, Vector3 eye)
    {
        float size = PlotSize(world);
        // Pending plots do NOT make the region stale: the list is the same
        // set of keys until the eye moves, and Gather re-requests whatever is
        // still missing on every pass. Re-flooding while pending cost 2.3 ms
        // a frame for the whole cold fill, for nothing.
        bool stale = !_regionValid
                  || !Mathf.Approximately(_regionRadius, world.radius)
                  || (eye - _regionEye).sqrMagnitude > (size * 0.25f) * (size * 0.25f);
        if (!stale) return;

        _region.Clear();
        _regionSet.Clear();
        _floodQueue.Clear();

        Vector3 up = _planet != null ? (eye - _planet.center).normalized : Vector3.up;
        float reach = world.radius + CellDiagonal(size);

        long start = CellOf(eye, size);
        _regionSet.Add(start);
        _floodQueue.Enqueue(start);
        while (_floodQueue.Count > 0 && _region.Count < kMaxRegionPlots)
        {
            long key = _floodQueue.Dequeue();
            _region.Add(key);
            for (int d = 0; d < 4; d++)
            {
                long n = Neighbour(key, d == 0 ? 1 : d == 1 ? -1 : 0, d == 2 ? 1 : d == 3 ? -1 : 0);
                if (_regionSet.Contains(n)) continue;
                if (CentreDistance(n, eye, up, size) > reach) continue;
                _regionSet.Add(n);
                _floodQueue.Enqueue(n);
            }
        }

        _regionEye = eye;
        _regionRadius = world.radius;
        _regionValid = true;
    }

    // ---- gathering ---------------------------------------------------------

    void Gather(PlantWorld world, Vector3 eye)
    {
        float radiusSqr = world.radius * world.radius;
        Vector3 up = _planet != null ? (eye - _planet.center).normalized : Vector3.up;
        int speciesCount = world.species.Length;

        for (int i = 0; i < _batches.Length; i++) _batches[i]?.Clear();
        int requested = 0;

        for (int r = 0; r < _region.Count; r++)
        {
            long key = _region[r];
            if (!_plots.TryGetValue(key, out Plot plot))
            {
                // Not built yet: queue it (nearest first, since the region is
                // in flood-fill order) and count it as pending this frame.
                _plotsPending++;
                if (requested < plotsBuiltPerFrame && _inFlight.Count < kMaxInFlight && !_inFlight.Contains(key))
                {
                    EnsureWorkers();
                    _inFlight.Add(key);
                    _requests.Add(new BuildRequest { key = key, ctx = _ctx });
                    requested++;
                }
                continue;
            }
            if (plot.items.Length == 0) continue;
            _visiblePlots++;

            // Whole-plot rejection: farther than a species' cull, nothing of
            // that species in it draws.
            float plotDist = (plot.centre - eye).magnitude - plot.reach;

            Instance[] items = plot.items;
            int[] runs = plot.runStart;
            for (int si = 0; si < speciesCount; si++)
            {
                int start = runs[si], end = runs[si + 1];
                if (start == end) continue;
                if (plotDist > _cull[si]) continue;
                float cull2 = _cull2[si], imp2 = _imp2[si], l1 = _lod1sq[si], l2 = _lod2sq[si];
                bool useLod = _useLod[si], hasImpostor = _hasImpostor[si];
                int batchBase = si * kMaxVariants * kLodSlots;

                for (int i = start; i < end; i++)
                {
                    ref Instance inst = ref items[i];

                    // Draw distance and LOD: true distance to the eye, because
                    // a plant far below really is small on screen. Tested
                    // first because it rejects most of what the region holds.
                    float rx = inst.pos.x - eye.x, ry = inst.pos.y - eye.y, rz = inst.pos.z - eye.z;
                    float d2 = rx * rx + ry * ry + rz * rz;
                    if (d2 > cull2) continue;

                    // Region: distance from the eye's vertical axis, height
                    // ignored, so a player on a rise keeps the plants at their
                    // feet. Measured from the eye rather than the planet
                    // centre so the numbers stay float-exact on a 100 km globe.
                    float along = rx * up.x + ry * up.y + rz * up.z;
                    if (d2 - along * along > radiusSqr) continue;

                    // Far: the baked card, if the set has one. Nearer: mesh
                    // LODs, or LOD0 throughout when the species opts out.
                    int lod;
                    if (hasImpostor && d2 > imp2) lod = kImpostorLod;
                    else lod = !useLod ? 0 : d2 > l2 ? 2 : d2 > l1 ? 1 : 0;

                    int b = batchBase + inst.variant * kLodSlots + lod;
                    List<Matrix4x4> list = _batches[b];
                    if (list == null) _batches[b] = list = new List<Matrix4x4>(256);
                    list.Add(inst.trs);
                    _instances++;
                }
            }
        }
    }

    // Drops plots outside the region instead of emptying the cache, so the
    // plots being looked at are never thrown away and trickled back.
    void EvictFarPlots(PlantWorld world)
    {
        if (_plots.Count <= world.maxCachedPlots) return;
        _evictScratch.Clear();
        foreach (var kv in _plots)
            if (!_regionSet.Contains(kv.Key)) _evictScratch.Add(kv.Key);
        for (int i = 0; i < _evictScratch.Count; i++) _plots.Remove(_evictScratch[i]);
    }

    // ---- background building ------------------------------------------------

    void EnsureWorkers()
    {
        if (_workers != null) return;
        int n = workerThreads > 0 ? workerThreads : Mathf.Clamp(SystemInfo.processorCount - 2, 1, 4);
        _requests = new BlockingCollection<BuildRequest>(new ConcurrentQueue<BuildRequest>());
        _cts = new CancellationTokenSource();
        _workers = new Thread[n];
        var requests = _requests; var results = _results; var token = _cts.Token;
        for (int i = 0; i < n; i++)
        {
            _workers[i] = new Thread(() => WorkerLoop(requests, results, token))
            {
                IsBackground = true,           // never keeps the process alive
                Name = "PlantScatter worker " + i,
                Priority = System.Threading.ThreadPriority.BelowNormal,
            };
            _workers[i].Start();
        }
        // OnDisable runs before a domain reload for an ExecuteAlways component,
        // but belt and braces: a thread blocked on the queue must never outlive
        // the domain that owns the delegates it would call.
        System.AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;
        System.AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;
    }

    void OnDomainUnload(object sender, System.EventArgs e) => StopWorkers();

    void StopWorkers()
    {
        if (_workers == null) return;
        _cts.Cancel();
        _requests.CompleteAdding();
        foreach (Thread t in _workers) t.Join(200); // they wake on the cancel; do not hang the editor if one is mid-plot
        _workers = null;
        _cts = null;
        _requests = null;
        _inFlight.Clear();
        while (_results.TryDequeue(out _)) { }
        System.AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;
    }

    static void WorkerLoop(BlockingCollection<BuildRequest> requests, ConcurrentQueue<BuildResult> results, CancellationToken token)
    {
        var scratch = new List<Instance>(64);
        var runs = new List<int>(8);
        try
        {
            foreach (BuildRequest req in requests.GetConsumingEnumerable(token))
            {
                Plot plot;
                try { plot = BuildPlot(req.ctx, req.key, scratch, runs); }
                catch (System.Exception e) { Debug.LogException(e); plot = Plot.Empty; }
                results.Enqueue(new BuildResult { key = req.key, generation = req.ctx.generation, plot = plot });
            }
        }
        catch (System.OperationCanceledException) { }
    }

    void DrainResults()
    {
        while (_results.TryDequeue(out BuildResult r))
        {
            _inFlight.Remove(r.key);
            if (r.generation != _generation) continue; // built against settings since replaced
            _plots[r.key] = r.plot;
            _plotsBuiltTotal++;
        }
    }

    // Everything a worker needs, copied out of the Unity objects on the main
    // thread. Workers read only this and the density field.
    sealed class BuildContext
    {
        public int generation;
        public int seed;
        public float size, cellInv, filterWidth;
        public DensityField field;
        public PlanetField planet;      // null on a flat world
        public Vector3 planetCentre;
        public float planetRadius, rLo, rHi;
        public float flatBottom, flatTop;
        public Species[] species;

        public struct Species
        {
            public bool enabled;
            public int variants, perPlot;
            public float plotChance, minUpness, minHeight, maxHeight, scaleMin, scaleMax, lean, sink;
        }

        public static BuildContext Capture(PlantWorld world, DensityField field, PlanetField planet, float cellInv, float fw, int generation)
        {
            var c = new BuildContext
            {
                generation = generation,
                seed = world.seed,
                size = PlotSize(world),
                cellInv = cellInv,
                filterWidth = fw,
                field = field,
                planet = planet,
                species = new Species[world.species.Length],
            };
            if (planet != null)
            {
                c.planetCentre = planet.center;
                c.planetRadius = planet.radius;
                if (!planet.TryGetSurfaceBand(out c.rLo, out c.rHi)) { c.rLo = planet.radius * 0.5f; c.rHi = planet.radius * 1.5f; }
            }
            else
            {
                if (field.TryGetHeightBounds(out float minH, out float maxH)) { c.flatBottom = minH - 1f; c.flatTop = maxH + 1f; }
                else { c.flatBottom = -128f; c.flatTop = 256f; }
            }
            for (int i = 0; i < world.species.Length; i++)
            {
                PlantSpecies sp = world.species[i];
                bool on = sp != null && sp.enabled && sp.prototypes != null && sp.prototypes.variants != null && sp.prototypes.variants.Length > 0;
                c.species[i] = new Species
                {
                    enabled = on,
                    variants = on ? sp.prototypes.variants.Length : 0,
                    perPlot = on ? sp.perPlot : 0,
                    plotChance = on ? sp.plotChance : 0f,
                    minUpness = on ? sp.minUpness : 0f,
                    minHeight = on ? sp.minHeight : 0f,
                    maxHeight = on ? sp.maxHeight : 0f,
                    scaleMin = on ? Mathf.Min(sp.scaleRange.x, sp.scaleRange.y) : 1f,
                    scaleMax = on ? Mathf.Max(sp.scaleRange.x, sp.scaleRange.y) : 1f,
                    lean = on ? sp.leanDegrees : 0f,
                    sink = on ? sp.sink : 0f,
                };
            }
            return c;
        }
    }

    // Worker thread. Pure function of (ctx, key); no Unity externs.
    static Plot BuildPlot(BuildContext ctx, long key, List<Instance> scratch, List<int> runs)
    {
        scratch.Clear();
        runs.Clear();
        Unpack(key, out int face, out int a, out int b);
        bool globe = face != kFlatFace;
        float size = ctx.size;

        uint plotHash = TerrainNoise.Hash(unchecked((uint)(face * 73856093 + a)), unchecked((uint)b), unchecked((uint)ctx.seed));

        // Cube-sphere cells are not equal-area: dA = R^2 du dv / (1+u^2+v^2)^1.5,
        // so a corner cell is a fifth the size of a centre cell. Scale the
        // expected count by the cell's real area over the nominal plotSize^2
        // so density is uniform over the sphere.
        float areaFactor = 1f;
        if (globe)
        {
            float uc = -1f + (a + 0.5f) * ctx.cellInv, vc = -1f + (b + 0.5f) * ctx.cellInv;
            float s = 1f + uc * uc + vc * vc;
            float edge = ctx.planetRadius * ctx.cellInv;
            areaFactor = edge * edge / (s * Mathf.Sqrt(s)) / (size * size);
        }

        for (int si = 0; si < ctx.species.Length; si++)
        {
            runs.Add(scratch.Count);
            BuildContext.Species sp = ctx.species[si];
            if (!sp.enabled) continue;

            // Each species gets its own stream off the plot hash, so adding a
            // species does not reshuffle the ones already placed.
            var rng = new LRandom(plotHash, unchecked((uint)(si * 0x9E3779B9u)));
            if (rng.NextFloat() > sp.plotChance) continue;

            float expected = sp.perPlot * areaFactor;
            int count = Mathf.FloorToInt(expected);
            if (rng.NextFloat() < expected - count) count++;

            for (int i = 0; i < count; i++)
            {
                float fa = rng.NextFloat(), fb = rng.NextFloat();
                // Draw every random number a candidate can consume BEFORE the
                // ground test, so a rejected candidate never shifts the stream
                // of the ones after it -- a plant's look then depends only on
                // its own index, not on how its neighbours fared.
                float scale = rng.Range(sp.scaleMin, sp.scaleMax);
                float yaw = rng.Range(0f, 360f);
                float leanX = rng.Range(-sp.lean, sp.lean);
                float leanZ = rng.Range(-sp.lean, sp.lean);
                int variant = (int)(rng.NextUInt() % (uint)sp.variants);

                Vector3 pos;
                Quaternion rot;
                if (globe)
                {
                    Vector3 dir = FaceUVToDir(face, -1f + (a + fa) * ctx.cellInv, -1f + (b + fb) * ctx.cellInv);
                    if (!FindSurface(ctx.field, ctx.planetCentre + dir * ctx.rHi, -dir, ctx.rHi - ctx.rLo, ctx.filterWidth,
                                     out float depth, out Vector3 normal)) continue;
                    float rad = ctx.rHi - depth;
                    float height = rad - ctx.planetRadius;
                    if (height < sp.minHeight || height > sp.maxHeight) continue;
                    if (Vector3.Dot(normal, dir) < sp.minUpness) continue; // "upright" is radial here

                    // Stand the plant along the local up, then spin and lean.
                    rot = FromTo(Vector3.up, dir) * AxisAngle(Vector3.up, yaw);
                    if (sp.lean > 0f) rot = rot * AxisAngle(Vector3.right, leanX) * AxisAngle(Vector3.forward, leanZ);
                    pos = ctx.planetCentre + dir * (rad - sp.sink * scale);
                }
                else
                {
                    float x = (a + fa) * size, z = (b + fb) * size;
                    if (!FindSurface(ctx.field, new Vector3(x, ctx.flatTop, z), Vector3.down, ctx.flatTop - ctx.flatBottom, ctx.filterWidth,
                                     out float depth, out Vector3 normal)) continue;
                    float y = ctx.flatTop - depth;
                    if (y < sp.minHeight || y > sp.maxHeight) continue;
                    if (normal.y < sp.minUpness) continue;

                    rot = AxisAngle(Vector3.up, yaw);
                    if (sp.lean > 0f) rot = AxisAngle(Vector3.right, leanX) * AxisAngle(Vector3.forward, leanZ) * rot;
                    pos = new Vector3(x, y - sp.sink * scale, z);
                }

                scratch.Add(new Instance { trs = TRS(pos, rot, scale), pos = pos, variant = variant });
            }
        }
        runs.Add(scratch.Count);

        if (scratch.Count == 0) return Plot.Empty;

        var plot = new Plot { items = scratch.ToArray(), runStart = runs.ToArray() };
        Vector3 c = Vector3.zero;
        for (int i = 0; i < plot.items.Length; i++) c += plot.items[i].pos;
        c /= plot.items.Length;
        float reach = 0f;
        for (int i = 0; i < plot.items.Length; i++) reach = Mathf.Max(reach, (plot.items[i].pos - c).magnitude);
        plot.centre = c;
        plot.reach = reach;
        return plot;
    }

    // Walks a ray from open air into the ground and returns how far along it
    // the surface is, plus the outward normal there.
    //
    // Positive density is SOLID in this project, and every field here is
    // shaped like "height - y" plus bounded detail, so |density| in open air
    // is roughly the distance to the ground. The walk steps by half of that
    // (sphere tracing, with a floor so it cannot stall and a ceiling so an
    // erosion term steeper than 1:1 cannot fling it), then bisects the last
    // air/solid pair. A typical plant is found in ~10 samples plus 8
    // bisections; the fixed 48-step march it replaces took 60.
    //
    // A step longer than the local detail can jump over a thin overhang and
    // land the plant on the ground beneath it. That is a plant under a rock
    // bridge, not a plant in the air, and it is rare; accepted.
    static bool FindSurface(DensityField field, Vector3 origin, Vector3 down, float length, float fw,
                            out float depth, out Vector3 normal)
    {
        depth = 0f; normal = -down;
        if (length <= 0f) return false;

        const float kMinStep = 0.25f, kMaxStep = 24f;
        float t = 0f;
        float d = field.Sample(origin, fw);
        if (d > 0f) return false; // starts inside solid: overhang roof or a very tall field

        float tAir = 0f;
        bool found = false;
        for (int i = 0; i < 64 && t < length; i++)
        {
            float step = Mathf.Clamp(-d * 0.5f, kMinStep, kMaxStep);
            float tn = Mathf.Min(t + step, length);
            float dn = field.Sample(origin + down * tn, fw);
            if (dn >= 0f) { tAir = t; t = tn; found = true; break; }
            t = tn; d = dn;
        }
        if (!found) return false;

        float lo = tAir, hi = t; // lo is air, hi is solid
        for (int i = 0; i < 8; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (field.Sample(origin + down * mid, fw) >= 0f) hi = mid; else lo = mid;
        }
        depth = 0.5f * (lo + hi);

        // Density rises into the ground, so the outward normal is -gradient.
        Vector3 g = field.Gradient(origin + down * depth, 0.25f, fw);
        normal = g.sqrMagnitude < 1e-10f ? -down : (-g).normalized;
        return true;
    }

    // ---- worker-safe rotation and matrix helpers -----------------------------
    // Quaternion.Euler / AngleAxis / FromToRotation and Matrix4x4.TRS are
    // engine externs and are not to be called off the main thread. These are
    // the same maths in managed code.

    static Quaternion AxisAngle(Vector3 unitAxis, float degrees)
    {
        float half = degrees * (Mathf.Deg2Rad * 0.5f);
        float s = Mathf.Sin(half);
        return new Quaternion(unitAxis.x * s, unitAxis.y * s, unitAxis.z * s, Mathf.Cos(half));
    }

    static Quaternion FromTo(Vector3 from, Vector3 to)
    {
        Vector3 c = Vector3.Cross(from, to);
        float w = 1f + Vector3.Dot(from, to);
        if (w < 1e-6f)
        {
            // Opposite directions: 180 degrees about anything perpendicular.
            Vector3 axis = Vector3.Cross(from, Mathf.Abs(from.x) < 0.9f ? Vector3.right : Vector3.up).normalized;
            return new Quaternion(axis.x, axis.y, axis.z, 0f);
        }
        float inv = 1f / Mathf.Sqrt(c.x * c.x + c.y * c.y + c.z * c.z + w * w);
        return new Quaternion(c.x * inv, c.y * inv, c.z * inv, w * inv);
    }

    static Matrix4x4 TRS(Vector3 p, Quaternion q, float s)
    {
        float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
        float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
        float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
        var m = new Matrix4x4();
        m.m00 = (1f - 2f * (yy + zz)) * s; m.m01 = 2f * (xy - wz) * s;        m.m02 = 2f * (xz + wy) * s;        m.m03 = p.x;
        m.m10 = 2f * (xy + wz) * s;        m.m11 = (1f - 2f * (xx + zz)) * s; m.m12 = 2f * (yz - wx) * s;        m.m13 = p.y;
        m.m20 = 2f * (xz - wy) * s;        m.m21 = 2f * (yz + wx) * s;        m.m22 = (1f - 2f * (xx + yy)) * s; m.m23 = p.z;
        m.m30 = 0f;                        m.m31 = 0f;                        m.m32 = 0f;                        m.m33 = 1f;
        return m;
    }

    // ---- cube-sphere mapping -----------------------------------------------
    //
    // Standard cube-map projection. Any consistent bijection works; this only
    // has to be stable and seam-free. FaceUVToDir is the exact inverse of
    // DirToFaceCell's (face, u, v) for every face -- and it stays a valid
    // direction for |u| or |v| > 1, which is what Neighbour relies on.

    static void DirToFaceCell(Vector3 d, int cellsPerFace, out int face, out int cu, out int cv)
    {
        float ax = Mathf.Abs(d.x), ay = Mathf.Abs(d.y), az = Mathf.Abs(d.z);
        float ma, sc, tc;
        if (ax >= ay && ax >= az) { face = d.x > 0 ? 0 : 1; ma = ax; sc = d.x > 0 ? -d.z : d.z; tc = d.y; }
        else if (ay >= az)        { face = d.y > 0 ? 2 : 3; ma = ay; sc = d.x;                  tc = d.y > 0 ? d.z : -d.z; }
        else                      { face = d.z > 0 ? 4 : 5; ma = az; sc = d.z > 0 ? d.x : -d.x; tc = d.y; }
        float u = sc / ma, v = tc / ma;
        cu = Mathf.Clamp((int)((u + 1f) * 0.5f * cellsPerFace), 0, cellsPerFace - 1);
        cv = Mathf.Clamp((int)((v + 1f) * 0.5f * cellsPerFace), 0, cellsPerFace - 1);
    }

    static Vector3 FaceUVToDir(int face, float u, float v)
    {
        switch (face)
        {
            case 0:  return new Vector3( 1f,  v,  -u).normalized;
            case 1:  return new Vector3(-1f,  v,   u).normalized;
            case 2:  return new Vector3(  u, 1f,   v).normalized;
            case 3:  return new Vector3(  u, -1f, -v).normalized;
            case 4:  return new Vector3(  u,  v,  1f).normalized;
            default: return new Vector3( -u,  v, -1f).normalized;
        }
    }

    // ---- drawing -----------------------------------------------------------

    void Draw(PlantWorld world)
    {
        for (int b = 0; b < _batches.Length; b++)
        {
            List<Matrix4x4> list = _batches[b];
            if (list == null || list.Count == 0) continue;

            int lod = b % kLodSlots;
            int variant = (b / kLodSlots) % kMaxVariants;
            int species = b / (kLodSlots * kMaxVariants);
            if (species >= world.species.Length) continue;

            PlantSpecies sp = world.species[species];
            PlantPrototypeSet set = sp.prototypes;
            if (set == null) continue;
            PlantVariant pv = set.Variant(variant);
            if (pv == null) continue;

            if (lod == kImpostorLod)
            {
                // One quad, no shadows: a card's shadow is a wall, and at this
                // range the sun's shadow distance has long run out anyway.
                if (pv.impostor != null) DrawSubmesh(pv.impostor, 0, set.impostorMaterial, list, false);
                continue;
            }

            Mesh mesh = pv.Lod(lod);
            if (mesh == null) continue;

            DrawSubmesh(mesh, 0, set.barkMaterial, list, true);
            if (mesh.subMeshCount > 1) DrawSubmesh(mesh, 1, set.leafMaterial, list, true);
        }
    }

    void DrawSubmesh(Mesh mesh, int submesh, Material mat, List<Matrix4x4> list, bool shadows)
    {
        if (mat == null) return;
        // RenderMeshInstanced THROWS on a material without instancing enabled,
        // which would take the whole Update down. Report it instead.
        if (!mat.enableInstancing)
        {
            _status = "material '" + mat.name + "' has instancing disabled -- rebake its prototype set";
            return;
        }
        var rp = new RenderParams(mat)
        {
            worldBounds = _drawBounds,
            shadowCastingMode = shadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off,
            receiveShadows = shadows,
        };

        // Submit straight from the batch's own list: per-batch lists cannot
        // alias each other the way a shared scratch array once did.
        for (int start = 0; start < list.Count; start += kMaxPerCall)
        {
            int n = Mathf.Min(kMaxPerCall, list.Count - start);
            Graphics.RenderMeshInstanced(rp, mesh, submesh, list, n, start);
            _drawCalls++;
        }
    }

    // ---- invalidation ------------------------------------------------------

    [ContextMenu("Clear plot cache")]
    public void ClearCache()
    {
        _plots.Clear();
        _regionValid = false;
        _generation++;      // results still in flight were built for the old world; DrainResults drops them
        _inFlight.Clear();  // ...and their keys must be requested again
    }

    // Deliberately empty: nothing on this component changes where a plant
    // stands. Clearing here meant any inspector touch threw away the forest.
    void OnValidate() { }

    // Everything that feeds placement, hashed each frame. PlantWorld is a
    // separate asset, so edits to it never reach this component's OnValidate;
    // watching the values is the only way to make its inspector live. Draw
    // distances and LOD are deliberately NOT in here -- they change nothing
    // about where a plant is.
    static int PlacementHash(PlantWorld world, DensityField field, float filterWidth)
    {
        unchecked
        {
            int h = world.seed;
            h = h * 31 + world.plotSize.GetHashCode();
            h = h * 31 + filterWidth.GetHashCode();
            h = h * 31 + field.GetInstanceID();
            h = h * 31 + world.species.Length;
            foreach (PlantSpecies sp in world.species)
            {
                if (sp == null) { h = h * 31; continue; }
                h = h * 31 + (sp.enabled ? 1 : 0);
                h = h * 31 + (sp.prototypes != null ? sp.prototypes.GetInstanceID() : 0);
                h = h * 31 + (sp.prototypes != null && sp.prototypes.variants != null ? sp.prototypes.variants.Length : 0);
                h = h * 31 + sp.perPlot;
                h = h * 31 + sp.plotChance.GetHashCode();
                h = h * 31 + sp.minUpness.GetHashCode();
                h = h * 31 + sp.minHeight.GetHashCode();
                h = h * 31 + sp.maxHeight.GetHashCode();
                h = h * 31 + sp.scaleRange.GetHashCode();
                h = h * 31 + sp.leanDegrees.GetHashCode();
                h = h * 31 + sp.sink.GetHashCode();
            }
            return h;
        }
    }
}
