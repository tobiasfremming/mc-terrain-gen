using System.Collections.Generic;
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
//     RNG is seeded from the plot id, and the ground is found by bisecting
//     the density field, which is itself a pure function of position.
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
//     at cullDistance, so set that far enough that the pop is a few pixels.
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

    [Tooltip("New plots populated per frame. Finding the ground costs ~60 density samples per candidate, and a cold radius is hundreds of plots -- spreading the work stops that landing as one multi-second freeze. Already-built plots are free.")]
    [Range(1, 128)] public int plotsBuiltPerFrame = 12;

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
        public int species;
        public int variant;
    }

    sealed class Plot
    {
        public Instance[] items;
        public static readonly Plot Empty = new Plot { items = new Instance[0] };
    }

    readonly Dictionary<long, Plot> _plots = new Dictionary<long, Plot>();
    readonly List<Instance> _buildScratch = new List<Instance>(64);

    // Instance matrices bucketed by (species, variant, lod). Cleared and
    // refilled each frame; capacity is reused so a settled camera allocates
    // nothing.
    readonly Dictionary<int, List<Matrix4x4>> _batches = new Dictionary<int, List<Matrix4x4>>();
    const int kMaxPerCall = 1023; // Unity's per-call instancing limit

    // The region: every plot key within `radius` of the eye, from the last
    // flood fill. Re-filled only when the eye has moved a quarter plot, or
    // while any plot in it is still waiting for build budget.
    readonly List<long> _region = new List<long>();
    readonly HashSet<long> _regionSet = new HashSet<long>();
    readonly Queue<long> _floodQueue = new Queue<long>();
    readonly List<long> _evictScratch = new List<long>();
    Vector3 _regionEye;
    float _regionRadius = -1f;
    bool _regionValid;
    bool _regionHadPending;
    const int kMaxRegionPlots = 65536; // safety cap on the flood fill, never hit with sane radii

    MCChunkManager _manager;
    PlanetField _planet;
    Vector3 _planetCentre;
    int _cellsPerFace;
    float _cellInv;      // 2 / cellsPerFace: width of one cell in cube-face [-1,1] units
    float _filterWidth;  // sample spacing to band-limit the field to: LOD0's cell size
    Bounds _drawBounds;
    int _budget;
    int _settingsHash;

    void Update()
    {
        _visiblePlots = 0; _instances = 0; _drawCalls = 0; _plotsPending = 0;
        _budget = plotsBuiltPerFrame;

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

        int hash = PlacementHash(world, _filterWidth);
        if (hash != _settingsHash) { _settingsHash = hash; ClearCache(); }

        Transform v = ResolveViewer();
        if (v == null) { _status = "no viewer and no target"; return; }
        Vector3 eye = v.position;

        if (_planet != null && (eye - _planet.center).sqrMagnitude < 1e-6f) { _status = "viewer is at the planet centre"; return; }

        float maxCull = 0f;
        foreach (PlantSpecies sp in world.species)
            if (sp != null && sp.enabled) maxCull = Mathf.Max(maxCull, sp.cullDistance);

        // Bounds for the instanced draws, in WORLD space around the eye: on a
        // globe the plants sit 100 km from the origin, so a box around this
        // component's transform would silently cull every draw.
        _drawBounds = new Bounds(eye, Vector3.one * (maxCull * 2f + 100f));

        _status = "OK";
        EnsureRegion(world, eye);
        Gather(world, field, eye);
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
    long Neighbour(long key, int da, int db, float size)
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
        bool stale = !_regionValid
                  || _regionHadPending
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
                long n = Neighbour(key, d == 0 ? 1 : d == 1 ? -1 : 0, d == 2 ? 1 : d == 3 ? -1 : 0, size);
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

    void Gather(PlantWorld world, DensityField field, Vector3 eye)
    {
        float size = PlotSize(world);
        float radiusSqr = world.radius * world.radius;
        Vector3 up = _planet != null ? (eye - _planet.center).normalized : Vector3.up;

        foreach (var kv in _batches) kv.Value.Clear();
        _regionHadPending = false;

        for (int r = 0; r < _region.Count; r++)
        {
            Plot plot = GetPlot(world, field, _region[r], size);
            if (plot == null) { _plotsPending++; _regionHadPending = true; continue; }
            if (plot.items.Length == 0) continue;
            _visiblePlots++;

            for (int i = 0; i < plot.items.Length; i++)
            {
                Instance inst = plot.items[i];
                Vector3 p = inst.pos;

                // Region: distance from the eye's vertical axis, height
                // ignored, so a player on a rise keeps the plants at their
                // feet. Measured from the eye rather than the planet centre
                // so the numbers stay small and float-exact on a 100 km globe.
                Vector3 rel = p - eye;
                Vector3 tangential = rel - up * Vector3.Dot(rel, up);
                if (tangential.sqrMagnitude > radiusSqr) continue;

                // Draw distance and LOD: true distance to the eye, because a
                // plant far below really is small on screen.
                float d2 = (p - eye).sqrMagnitude;
                PlantSpecies sp = world.species[inst.species];
                if (d2 > sp.cullDistance * sp.cullDistance) continue;

                int lod = !sp.useLod ? 0
                        : d2 > sp.lod2Distance * sp.lod2Distance ? 2
                        : d2 > sp.lod1Distance * sp.lod1Distance ? 1 : 0;
                Batch(inst.species, inst.variant, lod).Add(inst.trs);
                _instances++;
            }
        }
    }

    List<Matrix4x4> Batch(int species, int variant, int lod)
    {
        int k = (species << 16) | (variant << 4) | lod;
        if (!_batches.TryGetValue(k, out List<Matrix4x4> list))
        {
            list = new List<Matrix4x4>(256);
            _batches[k] = list;
        }
        return list;
    }

    // Returns null when this frame's build budget is spent rather than
    // blocking; the caller counts those as pending and they resolve over the
    // next frames.
    Plot GetPlot(PlantWorld world, DensityField field, long key, float size)
    {
        if (_plots.TryGetValue(key, out Plot cached)) return cached;
        if (_budget <= 0) return null;
        _budget--;
        _plotsBuiltTotal++;

        Plot built = BuildPlot(world, field, key, size);
        _plots[key] = built;
        return built;
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

    // ---- building ----------------------------------------------------------

    Plot BuildPlot(PlantWorld world, DensityField field, long key, float size)
    {
        _buildScratch.Clear();
        Unpack(key, out int face, out int a, out int b);
        bool globe = face != kFlatFace;

        uint plotHash = TerrainNoise.Hash(unchecked((uint)(face * 73856093 + a)), unchecked((uint)b),
                                          unchecked((uint)world.seed));

        // Cube-sphere cells are not equal-area: dA = R^2 du dv / (1+u^2+v^2)^1.5,
        // so a corner cell is a fifth the size of a centre cell. Scale the
        // expected count by the cell's real area over the nominal plotSize^2
        // so density is uniform over the sphere.
        float areaFactor = 1f;
        float rLo = 0f, rHi = 0f;
        if (globe)
        {
            float uc = -1f + (a + 0.5f) * _cellInv, vc = -1f + (b + 0.5f) * _cellInv;
            float s = 1f + uc * uc + vc * vc;
            float edge = _planet.radius * _cellInv;
            areaFactor = edge * edge / (s * Mathf.Sqrt(s)) / (size * size);
            if (!_planet.TryGetSurfaceBand(out rLo, out rHi))
            { rLo = _planet.radius * 0.5f; rHi = _planet.radius * 1.5f; }
        }

        for (int si = 0; si < world.species.Length; si++)
        {
            PlantSpecies sp = world.species[si];
            if (sp == null || !sp.enabled || sp.prototypes == null || sp.prototypes.variants == null
                || sp.prototypes.variants.Length == 0) continue;

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
                float scale = rng.Range(Mathf.Min(sp.scaleRange.x, sp.scaleRange.y),
                                        Mathf.Max(sp.scaleRange.x, sp.scaleRange.y));
                float yaw = rng.Range(0f, 360f);
                float leanX = rng.Range(-sp.leanDegrees, sp.leanDegrees);
                float leanZ = rng.Range(-sp.leanDegrees, sp.leanDegrees);
                int variant = (int)(rng.NextUInt() % (uint)sp.prototypes.variants.Length);

                Vector3 pos;
                Quaternion rot;
                if (globe)
                {
                    Vector3 dir = FaceUVToDir(face, -1f + (a + fa) * _cellInv, -1f + (b + fb) * _cellInv);
                    if (!TrySurfaceRadial(_planet, dir, rLo, rHi, _filterWidth, out float rad, out Vector3 normal)) continue;
                    float height = rad - _planet.radius;
                    if (height < sp.minHeight || height > sp.maxHeight) continue;
                    if (Vector3.Dot(normal, dir) < sp.minUpness) continue; // "upright" is radial here

                    // Stand the plant along the local up, then spin and lean.
                    rot = Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, yaw, 0f);
                    if (sp.leanDegrees > 0f) rot = rot * Quaternion.Euler(leanX, 0f, leanZ);
                    pos = _planet.center + dir * (rad - sp.sink * scale);
                }
                else
                {
                    float x = (a + fa) * size, z = (b + fb) * size;
                    if (!TrySurface(field, x, z, _filterWidth, out float y, out Vector3 normal)) continue;
                    if (y < sp.minHeight || y > sp.maxHeight) continue;
                    if (normal.y < sp.minUpness) continue;

                    rot = Quaternion.Euler(0f, yaw, 0f);
                    if (sp.leanDegrees > 0f) rot = Quaternion.Euler(leanX, 0f, leanZ) * rot;
                    pos = new Vector3(x, y - sp.sink * scale, z);
                }

                _buildScratch.Add(new Instance
                {
                    trs = Matrix4x4.TRS(pos, rot, Vector3.one * scale),
                    pos = pos,
                    species = si,
                    variant = variant,
                });
            }
        }

        return _buildScratch.Count == 0 ? Plot.Empty : new Plot { items = _buildScratch.ToArray() };
    }

    // Finds the ground by bisecting the density sign change on a vertical
    // line. Positive density is SOLID in this project, so walking down from
    // open air the surface is where a sample first goes non-negative.
    static bool TrySurface(DensityField field, float x, float z, float fw, out float y, out Vector3 normal)
    {
        y = 0f; normal = Vector3.up;

        float top, bottom;
        if (field.TryGetHeightBounds(out float minH, out float maxH)) { bottom = minH - 1f; top = maxH + 1f; }
        else { bottom = -128f; top = 256f; }
        if (top <= bottom) return false;

        const int kSteps = 48;
        float step = (top - bottom) / kSteps;
        if (field.Sample(new Vector3(x, top, z), fw) > 0f) return false; // starts inside solid

        float prevY = top;
        bool found = false;
        float lo = 0f, hi = 0f;
        for (int i = 1; i <= kSteps; i++)
        {
            float cy = top - i * step;
            if (field.Sample(new Vector3(x, cy, z), fw) >= 0f) { lo = cy; hi = prevY; found = true; break; }
            prevY = cy;
        }
        if (!found) return false;

        for (int i = 0; i < 12; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (field.Sample(new Vector3(x, mid, z), fw) >= 0f) lo = mid; else hi = mid;
        }
        y = 0.5f * (lo + hi);

        // Density rises into the ground, so the outward normal is -gradient.
        Vector3 g = field.Gradient(new Vector3(x, y, z), 0.25f, fw);
        normal = g.sqrMagnitude < 1e-10f ? Vector3.up : (-g).normalized;
        return true;
    }

    // The same bisection, walked along the radius instead of Y.
    static bool TrySurfaceRadial(PlanetField planet, Vector3 dir, float rLo, float rHi, float fw, out float rad, out Vector3 normal)
    {
        rad = 0f; normal = dir;
        const int kSteps = 48;
        float step = (rHi - rLo) / kSteps;
        if (step <= 0f) return false;
        if (planet.Sample(planet.center + dir * rHi, fw) > 0f) return false; // starts underground

        float prev = rHi;
        bool found = false; float lo = 0f, hi = 0f;
        for (int i = 1; i <= kSteps; i++)
        {
            float cr = rHi - i * step;
            if (planet.Sample(planet.center + dir * cr, fw) >= 0f) { lo = cr; hi = prev; found = true; break; }
            prev = cr;
        }
        if (!found) return false;

        for (int i = 0; i < 12; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (planet.Sample(planet.center + dir * mid, fw) >= 0f) lo = mid; else hi = mid;
        }
        rad = 0.5f * (lo + hi);

        Vector3 g = planet.Gradient(planet.center + dir * rad, 0.25f, fw);
        normal = g.sqrMagnitude < 1e-10f ? dir : (-g).normalized;
        return true;
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
        foreach (var kv in _batches)
        {
            List<Matrix4x4> list = kv.Value;
            if (list.Count == 0) continue;

            int species = (kv.Key >> 16) & 0xFFFF;
            int variant = (kv.Key >> 4) & 0xFFF;
            int lod = kv.Key & 0xF;
            if (species >= world.species.Length) continue;

            PlantSpecies sp = world.species[species];
            PlantPrototypeSet set = sp.prototypes;
            if (set == null) continue;
            PlantVariant pv = set.Variant(variant);
            Mesh mesh = pv != null ? pv.Lod(lod) : null;
            if (mesh == null) continue;

            DrawSubmesh(mesh, 0, set.barkMaterial, list);
            if (mesh.subMeshCount > 1) DrawSubmesh(mesh, 1, set.leafMaterial, list);
        }
    }

    void DrawSubmesh(Mesh mesh, int submesh, Material mat, List<Matrix4x4> list)
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
            shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
            receiveShadows = true,
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
    }

    // Deliberately empty: nothing on this component changes where a plant
    // stands. Clearing here meant any inspector touch threw away the forest.
    void OnValidate() { }

    // Everything that feeds placement, hashed each frame. PlantWorld is a
    // separate asset, so edits to it never reach this component's OnValidate;
    // watching the values is the only way to make its inspector live. Draw
    // distances and LOD are deliberately NOT in here -- they change nothing
    // about where a plant is.
    static int PlacementHash(PlantWorld world, float filterWidth)
    {
        unchecked
        {
            int h = world.seed;
            h = h * 31 + world.plotSize.GetHashCode();
            h = h * 31 + filterWidth.GetHashCode();
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
