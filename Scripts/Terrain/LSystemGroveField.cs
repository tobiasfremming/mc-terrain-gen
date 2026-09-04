using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using LSystems;
using UnityEngine;

// Rolling ground with branching stone spires grown by an L-system.
//
// This is the first field whose SHAPE comes from a grammar rather than from
// noise, and it is the worked example of the path the L-system README
// describes: grammar -> word -> LSkeleton -> capsule SDFs -> density. The
// skeleton is the hinge. Nothing below this line knows what a production is,
// and nothing in Scripts/LSystem knows what a DensityField is.
//
// HOW IT TILES
// ------------
// The world is cut into square plots. Each plot's contents are a pure function
// of (seed, plotX, plotZ), so the field stays what everything here requires:
// a pure function of world position, identical at every LOD, with no state
// that depends on the order chunks happen to be meshed in.
//
// A sample only ever consults its own plot and the eight around it, which is
// only correct if a structure cannot reach further than one plot. That is
// enforced, not assumed: after a skeleton is built it is uniformly scaled down
// if it exceeds either half a plot horizontally or maxHeight vertically (see
// Fit). The same clamp is what makes TryGetHeightBounds honest, and without
// that bound the mesher loses its empty-chunk skip for the entire world.
//
// PERFORMANCE
// -----------
// Chunk meshing runs on Task.Run workers, so Sample is called from several
// threads at once: the plot cache is a ConcurrentDictionary and plot
// construction uses [ThreadStatic] scratch. Building a plot twice under a race
// is harmless because the build is deterministic.
//
// A grove plot holds a few hundred capsules and a sample must not test them
// all, so each plot carries a 2D bucket grid over XZ. Capsules are inserted
// into every cell their footprint touches, inflated by radius + blend, which
// is the range beyond which a capsule cannot change the smooth union near a
// surface -- so the bucketing never moves the extracted isosurface.
//
// GPU
// ---
// Half of this field is closed-form (the ground) and half is not: a grove is a
// variable-length buffer of capsules produced by deriving a grammar, and there
// is no sane way to run an L-system in HLSL. So the work is split rather than
// refused -- the CPU builds each plot's capsules once and caches them, and the
// GPU evaluates the round-cone SDF over them, which is the half that runs per
// voxel and therefore the half that costs. See GpuType / BuildGpuAtlas below,
// Shaders/Compute/DensityGrove.hlsl for the evaluation, and
// TerrainGpuSampler.EnsureGroveAtlas for the upload.
//
// Reporting a real GpuType matters beyond this field: per
// BiomeDensityField.TryBuildGpuLeaves, ONE non-GPU biome drops the WHOLE world
// to CPU meshing.
[CreateAssetMenu(fileName = "GroveField", menuName = "Marching Cubes/Volume (L-System Grove)")]
public class LSystemGroveField : DensityField
{
    [Header("World")]
    public int seed = 8123;

    [Header("Ground")]
    [Tooltip("Height the ground sits at with no noise applied.")]
    public float groundHeight = 0f;
    [Tooltip("Peak-to-mean height of the rolling ground.")]
    public float groundAmp = 9f;
    [Tooltip("Horizontal size (m) of one ground undulation.")]
    public float groundScale = 260f;
    [Range(1, 8)] public int groundOctaves = 4;

    [Header("Grammar")]
    [Tooltip("The L-system that grows one spire. Leave empty and the field is just ground.")]
    public LSystemGrammarAsset grammar;
    [Tooltip("-1 uses the grammar's own #iterations or the asset's iterations field.")]
    [Range(-1, 12)] public int iterations = -1;
    [Tooltip("Turtle defaults for symbols that carry no parameter. Heading should point up for spires.")]
    public TurtleSettings turtle = TurtleSettings.Default;

    [Header("Grove layout")]
    [Tooltip("Size (m) of one plot. Also the hard cap on how far a single structure may spread: anything wider is scaled down to fit.")]
    public float plotSize = 90f;
    [Tooltip("Chance that a given plot grows anything at all.")]
    [Range(0f, 1f)] public float plotOccupancy = 0.75f;
    [Tooltip("How many structures an occupied plot grows.")]
    [Range(1, 6)] public int structuresPerPlot = 2;
    [Tooltip("Hard cap on a structure's height (m). Taller ones are scaled down, which is what keeps TryGetHeightBounds true.")]
    public float maxHeight = 44f;
    [Tooltip("Random size multiplier range applied per structure.")]
    public Vector2 scaleRange = new Vector2(0.7f, 1.25f);
    [Tooltip("Maximum lean from vertical (degrees) applied to a whole structure.")]
    [Range(0f, 45f)] public float leanDegrees = 9f;
    [Tooltip("How far (m) a structure's base is sunk below the ground surface, so it merges instead of resting on it.")]
    public float rootDepth = 2.5f;

    [Header("Shape")]
    [Tooltip("Multiplies every radius the grammar's ! produces. Skeleton width is read as a diameter, so radius = 0.5 * width * this.")]
    public float radiusScale = 1f;
    [Tooltip("Smallest radius (m) a branch may taper to. Thin branches below the voxel size just alias.")]
    public float minRadius = 0.35f;
    [Tooltip("Smooth-union fillet (m) where branches meet each other and the ground.")]
    public float blend = 1.6f;
    [Tooltip("Radius multiplier for the blob placed at each tip marker. 0 disables tip caps.")]
    public float tipCapScale = 1.15f;

    [Header("Cache")]
    [Tooltip("Plots kept built. Exceeding this clears the cache; plot builds are deterministic so nothing is lost but the work.")]
    public int maxCachedPlots = 1024;
    [Tooltip("Bucket grid resolution per plot. Higher = fewer capsules tested per sample, more memory per plot.")]
    [Range(2, 32)] public int gridResolution = 10;

    // ---- sampling -------------------------------------------------------

    public override float Sample(Vector3 p, float fw)
    {
        float d = Ground(p.x, p.z, fw) - p.y;
        if (grammar == null) return d;

        int px = Mathf.FloorToInt(p.x / PlotSize);
        int pz = Mathf.FloorToInt(p.z / PlotSize);
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                Plot plot = GetPlot(px + dx, pz + dz);
                if (plot.Capsules.Length == 0) continue;
                d = plot.Accumulate(p, d, blend);
            }
        return d;
    }

    // Fills a vertical column. Worth overriding for exactly the reason the
    // base class says: the ground height and -- much bigger here -- the set of
    // capsules that can possibly matter are functions of (x, z) alone, so both
    // are resolved once instead of once per voxel. A 32-tall column goes from
    // 32 grid lookups to one.
    public override void AddDensityColumn(float wx, float wz, float yStart, float yStep, int count,
                                          float weight, float[] dest, int destIndex, int destStride,
                                          float fw)
    {
        float h = Ground(wx, wz, fw);

        List<Capsule> candidates = null;
        if (grammar != null)
        {
            candidates = _columnScratch ??= new List<Capsule>(64);
            candidates.Clear();
            int px = Mathf.FloorToInt(wx / PlotSize);
            int pz = Mathf.FloorToInt(wz / PlotSize);
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    GetPlot(px + dx, pz + dz).Gather(wx, wz, candidates);
        }

        int n = candidates != null ? candidates.Count : 0;
        for (int i = 0; i < count; i++)
        {
            float y = yStart + i * yStep;
            float d = h - y;
            for (int c = 0; c < n; c++)
            {
                Capsule cap = candidates[c];
                // Same padded-range reject as Plot.Accumulate's per-cell Y
                // bounds, applied per capsule because a gathered column spans
                // many heights. Both paths must cull identically or the column
                // fast path and Sample would disagree.
                float pad = cap.MaxRadius + blend;
                float ay = cap.A.y, by = cap.B.y;
                float lo = (ay < by ? ay : by) - pad, hi = (ay > by ? ay : by) + pad;
                if (y < lo || y > hi) continue;
                d = SMax(d, -cap.Distance(wx, y, wz), blend);
            }
            dest[destIndex] += weight * d;
            destIndex += destStride;
        }
    }

    [System.ThreadStatic] static List<Capsule> _columnScratch;

    float Ground(float wx, float wz, float fw)
    {
        float s = groundScale > 0.01f ? groundScale : 0.01f;
        float n = TerrainNoise.Fbm(wx / s, wz / s, groundOctaves, unchecked((uint)seed) + 4919u, fw / s);
        return groundHeight + n * groundAmp;
    }

    // Conservative bounds. Only honest because Fit clamps every structure to
    // maxHeight -- if that clamp goes, so does this, and with it the mesher's
    // ability to skip the empty chunks that make up most of a clipmap column.
    public override bool TryGetHeightBounds(out float minH, out float maxH)
    {
        float top = groundHeight + groundAmp + blend;
        minH = groundHeight - groundAmp - rootDepth - blend;
        maxH = grammar != null ? top + maxHeight : top;
        return true;
    }

    protected override void OnValidate()
    {
        base.OnValidate();
        _plots = null;      // tunables changed: every cached plot is stale
        _version++;         // ...and so is any atlas the GPU sampler uploaded
    }

    float PlotSize => Mathf.Max(8f, plotSize);

    // ---- geometry -------------------------------------------------------

    // A tapered capsule (round cone). Degenerates to a sphere when its two
    // ends coincide, which is how tip markers are stored without a second
    // primitive type.
    readonly struct Capsule
    {
        public readonly Vector3 A, B;
        public readonly float RA, RB;

        public Capsule(Vector3 a, Vector3 b, float ra, float rb) { A = a; B = b; RA = ra; RB = rb; }

        public float MaxRadius => RA > RB ? RA : RB;

        // Signed distance to the round cone, negative inside. Inigo Quilez's
        // exact sdRoundCone, ported (technique, not code); the branchy form is
        // what makes it exact rather than a bound, which matters because the
        // value is used directly as a density in meters.
        public float Distance(float px, float py, float pz)
        {
            float bax = B.x - A.x, bay = B.y - A.y, baz = B.z - A.z;
            float l2 = bax * bax + bay * bay + baz * baz;

            float pax = px - A.x, pay = py - A.y, paz = pz - A.z;

            if (l2 < 1e-8f)                       // sphere
                return Mathf.Sqrt(pax * pax + pay * pay + paz * paz) - RA;

            float rr = RA - RB;
            float a2 = l2 - rr * rr;
            float il2 = 1f / l2;

            float y = pax * bax + pay * bay + paz * baz;
            float z = y - l2;

            float xpx = pax * l2 - bax * y;
            float xpy = pay * l2 - bay * y;
            float xpz = paz * l2 - baz * y;
            float x2 = xpx * xpx + xpy * xpy + xpz * xpz;

            float y2 = y * y * l2;
            float z2 = z * z * l2;

            float k = Mathf.Sign(rr) * rr * rr * x2;
            if (Mathf.Sign(z) * a2 * z2 > k) return Mathf.Sqrt(x2 + z2) * il2 - RB;
            if (Mathf.Sign(y) * a2 * y2 < k) return Mathf.Sqrt(x2 + y2) * il2 - RA;
            return (Mathf.Sqrt(Mathf.Max(0f, x2 * a2 * il2)) + y * rr) * il2 - RA;
        }
    }

    // Same smooth maximum the other volume fields use. Note it returns the
    // exact max once the two arguments are more than k apart, which is why
    // folding hundreds of far-away capsules in cannot inflate the ground.
    static float SMax(float a, float b, float k)
    {
        if (k <= 0f) return a > b ? a : b;
        float h = Mathf.Clamp01(0.5f + 0.5f * (a - b) / k);
        return b * (1f - h) + a * h + h * (1f - h) * k;
    }

    // ---- plots ----------------------------------------------------------

    sealed class Plot
    {
        public static readonly Plot Empty = new Plot
        {
            Capsules = new Capsule[0],
            CellStart = new int[1],
            CellItems = new int[0],
            CellMinY = new float[0],
            CellMaxY = new float[0],
        };

        public Capsule[] Capsules;
        public int[] CellStart;         // CSR offsets, length res*res + 1
        public int[] CellItems;
        // Per-cell vertical extent, ALREADY inflated by each capsule's
        // radius + blend -- the range outside which nothing in the cell can
        // move the surface. The bucket grid is XZ-only, so without this a
        // voxel 40 m under a spire still evaluated every capsule in the
        // spire's column. Most voxels in a chunk are nowhere near the
        // geometry vertically, so this is the cheapest large win available.
        public float[] CellMinY, CellMaxY;
        public int Res;
        public float MinX, MinZ, InvCell;
        public float MaxX, MaxZ;

        public int Cell(float wx, float wz)
        {
            if (Res <= 0 || Capsules.Length == 0) return -1;
            if (wx < MinX || wx > MaxX || wz < MinZ || wz > MaxZ) return -1;
            int cx = Mathf.Clamp((int)((wx - MinX) * InvCell), 0, Res - 1);
            int cz = Mathf.Clamp((int)((wz - MinZ) * InvCell), 0, Res - 1);
            return cz * Res + cx;
        }

        public float Accumulate(Vector3 p, float d, float blend)
        {
            int cell = Cell(p.x, p.z);
            if (cell < 0) return d;
            if (p.y < CellMinY[cell] || p.y > CellMaxY[cell]) return d; // exact: bounds already padded
            int end = CellStart[cell + 1];
            for (int i = CellStart[cell]; i < end; i++)
                d = SMax(d, -Capsules[CellItems[i]].Distance(p.x, p.y, p.z), blend);
            return d;
        }

        // No Y test here: AddDensityColumn gathers once for a whole column and
        // the column spans many heights. The per-voxel Y reject happens in
        // AddDensityColumn itself, against each capsule's own padded range.
        public void Gather(float wx, float wz, List<Capsule> into)
        {
            int cell = Cell(wx, wz);
            if (cell < 0) return;
            int end = CellStart[cell + 1];
            for (int i = CellStart[cell]; i < end; i++)
                into.Add(Capsules[CellItems[i]]);
        }
    }

    ConcurrentDictionary<long, Plot> _plots;

    Plot GetPlot(int px, int pz)
    {
        var plots = _plots;
        if (plots == null)
        {
            // Racy by design: two threads may each make a dictionary and one
            // wins. Plots are pure functions of their coordinates, so the
            // loser's work is wasted, never wrong.
            plots = new ConcurrentDictionary<long, Plot>();
            _plots = plots;
        }
        else if (plots.Count > maxCachedPlots)
        {
            plots.Clear();
        }

        long key = ((long)px << 32) ^ (uint)pz;
        if (plots.TryGetValue(key, out Plot cached)) return cached;

        Plot built = BuildPlot(px, pz);
        plots.TryAdd(key, built);
        return built;
    }

    [System.ThreadStatic] static LSystemRewriter _rewriter;
    [System.ThreadStatic] static LSkeleton _skeleton;
    [System.ThreadStatic] static List<Capsule> _buildScratch;

    Plot BuildPlot(int px, int pz)
    {
        LSystemGrammar g = grammar != null ? grammar.Grammar : null;
        if (g == null) return Plot.Empty;

        uint plotHash = TerrainNoise.Hash(unchecked((uint)px), unchecked((uint)pz), unchecked((uint)seed));
        var rng = new LRandom(plotHash, 0x9E37u);
        if (rng.NextFloat() > plotOccupancy) return Plot.Empty;

        var caps = _buildScratch ??= new List<Capsule>(512);
        caps.Clear();

        var rewriter = _rewriter ??= new LSystemRewriter();
        float size = PlotSize;
        float originX = px * size, originZ = pz * size;
        int n = grammar.EffectiveIterations;
        if (iterations >= 0) n = iterations;

        for (int s = 0; s < structuresPerPlot; s++)
        {
            // Each structure gets its own RNG sequence off the plot hash, so
            // changing structuresPerPlot does not reshuffle the ones already
            // there.
            uint sequence = plotHash ^ (uint)(s * 0x27D4EB2Du);
            var place = new LRandom(plotHash, sequence);

            float bx = originX + place.Range(0.12f, 0.88f) * size;
            float bz = originZ + place.Range(0.12f, 0.88f) * size;
            float by = Ground(bx, bz, 0f) - rootDepth;

            // A lean, so a grove does not look like a row of flagpoles. Note
            // this tilts the whole structure and therefore widens its
            // horizontal reach -- Fit runs after, and accounts for it.
            var t = turtle;
            t.origin = Vector3.zero;
            Quaternion lean = Quaternion.Euler(
                place.Range(-leanDegrees, leanDegrees), place.Range(0f, 360f), place.Range(-leanDegrees, leanDegrees));

            LModuleString word = rewriter.Rewrite(g, n, plotHash, sequence);
            _skeleton = TurtleInterpreter.Build(word, t, _skeleton);

            float scale = place.Range(Mathf.Min(scaleRange.x, scaleRange.y), Mathf.Max(scaleRange.x, scaleRange.y));
            Emit(_skeleton, new Vector3(bx, by, bz), lean, scale, caps);
        }

        return caps.Count == 0 ? Plot.Empty : Bucket(caps, originX, originZ, size);
    }

    // Skeleton -> capsules, placed in the world. This is the entire
    // grammar-to-terrain adapter; everything above it is generic.
    void Emit(LSkeleton skel, Vector3 basePos, Quaternion lean, float scale, List<Capsule> into)
    {
        scale *= Fit(skel, lean, scale);

        var nodes = skel.Nodes;
        var segments = skel.Segments;
        for (int i = 0; i < segments.Count; i++)
        {
            LSkeletonSegment seg = segments[i];
            Vector3 a = basePos + lean * (nodes[seg.From].Position * scale);
            Vector3 b = basePos + lean * (nodes[seg.To].Position * scale);
            float ra = Radius(seg.WidthFrom, scale);
            float rb = Radius(seg.WidthTo, scale);
            if ((a - b).sqrMagnitude < 1e-6f) continue;
            into.Add(new Capsule(a, b, ra, rb));
        }

        // Tip markers become blobs. The turtle does not know what K means and
        // does not need to: it records the symbol and its parameters, and this
        // is the one place that decides they are knobbly spire tips. A second
        // decoration type costs one more case here and one line of grammar.
        if (tipCapScale > 0f)
        {
            var markers = skel.Markers;
            for (int i = 0; i < markers.Count; i++)
            {
                LSkeletonMarker mk = markers[i];
                if (mk.Symbol != 'K') continue;
                float r = Radius(skel.GetMarkerParam(mk, 0, mk.Width), scale) * tipCapScale;
                Vector3 c = basePos + lean * (mk.Position * scale);
                into.Add(new Capsule(c, c, r, r));
            }
        }
    }

    float Radius(float width, float scale) => Mathf.Max(minRadius, 0.5f * width * radiusScale * scale);

    // Extra scale factor (<= 1) that keeps a structure inside the one-plot
    // reach the 3x3 sampling neighbourhood assumes, and inside maxHeight so
    // TryGetHeightBounds stays true. Measured on the LEANED skeleton, because
    // leaning is what turns a tall thin thing into a wide one.
    float Fit(LSkeleton skel, Quaternion lean, float scale)
    {
        float hMax = 0f, yMax = 0f;
        var nodes = skel.Nodes;
        for (int i = 0; i < nodes.Count; i++)
        {
            Vector3 v = lean * (nodes[i].Position * scale);
            float h = Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.z));
            if (h > hMax) hMax = h;
            if (v.y > yMax) yMax = v.y;
        }

        float margin = Mathf.Max(0.5f, blend + 1f);
        float allowedH = PlotSize * 0.5f - margin;
        float allowedY = Mathf.Max(1f, maxHeight - rootDepth - margin);

        float f = 1f;
        if (hMax > allowedH && hMax > 1e-4f) f = Mathf.Min(f, allowedH / hMax);
        if (yMax > allowedY && yMax > 1e-4f) f = Mathf.Min(f, allowedY / yMax);
        return f;
    }

    // Buckets capsules into a CSR grid. Each capsule goes into every cell its
    // footprint touches once inflated by radius + blend -- the range past
    // which it can no longer move a nearby surface -- so the bucketing is a
    // pure speedup and never shifts the isosurface.
    //
    // The grid's bounds come from those inflated footprints rather than from
    // the plot rectangle, which is what makes Plot.Cell's "outside the grid ->
    // this plot cannot matter" early-out exactly true instead of true-for-the-
    // parameters-I-happened-to-pick.
    Plot Bucket(List<Capsule> caps, float originX, float originZ, float size)
    {
        float minX = float.MaxValue, minZ = float.MaxValue;
        float maxX = float.MinValue, maxZ = float.MinValue;
        for (int i = 0; i < caps.Count; i++)
        {
            Capsule c = caps[i];
            float pad = c.MaxRadius + blend;
            minX = Mathf.Min(minX, Mathf.Min(c.A.x, c.B.x) - pad);
            maxX = Mathf.Max(maxX, Mathf.Max(c.A.x, c.B.x) + pad);
            minZ = Mathf.Min(minZ, Mathf.Min(c.A.z, c.B.z) - pad);
            maxZ = Mathf.Max(maxZ, Mathf.Max(c.A.z, c.B.z) + pad);
        }

        float extent = Mathf.Max(Mathf.Max(maxX - minX, maxZ - minZ), 0.001f);

        // Resolution ADAPTS to how fat the capsules actually are, instead of
        // trusting gridResolution to suit them. A capsule is inserted into
        // every cell its inflated footprint touches, so when that footprint is
        // large next to the cell size each capsule lands in many cells and the
        // grid stops separating anything: at radiusScale 9.75 the fixed 10x10
        // grid gave 19.3 cells per capsule and 199 capsules to test per voxel.
        // Sizing cells to the mean footprint keeps duplication near 1x whatever
        // the radii are, which is what makes the field robust to tuning rather
        // than quietly quadratic in it.
        double meanPad = 0.0;
        for (int i = 0; i < caps.Count; i++) meanPad += caps[i].MaxRadius + blend;
        meanPad = caps.Count > 0 ? meanPad / caps.Count : 1.0;
        int adaptive = Mathf.CeilToInt(extent / Mathf.Max(0.001f, (float)(2.0 * meanPad)));
        int res = Mathf.Clamp(Mathf.Max(adaptive, gridResolution), 2, 64);

        float invCell = res / extent;

        var counts = new int[res * res + 1];
        var cellMinY = new float[res * res];
        var cellMaxY = new float[res * res];
        for (int i = 0; i < res * res; i++) { cellMinY[i] = float.MaxValue; cellMaxY[i] = float.MinValue; }
        var lo = new int[caps.Count * 4];   // (x0,z0,x1,z1) per capsule

        for (int i = 0; i < caps.Count; i++)
        {
            Capsule c = caps[i];
            float pad = c.MaxRadius + blend;
            float ax = Mathf.Min(c.A.x, c.B.x) - pad, bx = Mathf.Max(c.A.x, c.B.x) + pad;
            float az = Mathf.Min(c.A.z, c.B.z) - pad, bz = Mathf.Max(c.A.z, c.B.z) + pad;

            int x0 = Mathf.Clamp((int)((ax - minX) * invCell), 0, res - 1);
            int x1 = Mathf.Clamp((int)((bx - minX) * invCell), 0, res - 1);
            int z0 = Mathf.Clamp((int)((az - minZ) * invCell), 0, res - 1);
            int z1 = Mathf.Clamp((int)((bz - minZ) * invCell), 0, res - 1);
            lo[i * 4] = x0; lo[i * 4 + 1] = z0; lo[i * 4 + 2] = x1; lo[i * 4 + 3] = z1;

            // Same pad vertically as horizontally, so "outside the cell's Y
            // range" means "cannot move the surface", exactly.
            float y0 = Mathf.Min(c.A.y, c.B.y) - pad, y1 = Mathf.Max(c.A.y, c.B.y) + pad;
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int ci = z * res + x;
                    counts[ci + 1]++;
                    if (y0 < cellMinY[ci]) cellMinY[ci] = y0;
                    if (y1 > cellMaxY[ci]) cellMaxY[ci] = y1;
                }
        }

        for (int i = 0; i < res * res; i++) counts[i + 1] += counts[i];

        var items = new int[counts[res * res]];
        var cursor = new int[res * res];
        for (int i = 0; i < caps.Count; i++)
        {
            int x0 = lo[i * 4], z0 = lo[i * 4 + 1], x1 = lo[i * 4 + 2], z1 = lo[i * 4 + 3];
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    int c = z * res + x;
                    items[counts[c] + cursor[c]++] = i;
                }
        }

        return new Plot
        {
            Capsules = caps.ToArray(),
            CellStart = counts,
            CellItems = items,
            CellMinY = cellMinY,
            CellMaxY = cellMaxY,
            Res = res,
            MinX = minX,
            MinZ = minZ,
            MaxX = minX + extent,
            MaxZ = minZ + extent,
            InvCell = invCell,
        };
    }

    // ---- GPU ------------------------------------------------------------

    // Unlike the other leaves this field is only HALF expressible as fixed
    // params: the ground is closed-form noise, but the spires are a
    // variable-length capsule set produced by deriving a grammar, which has no
    // sane HLSL form. So the split is CPU-builds-geometry / GPU-evaluates-SDF.
    // The capsules travel separately as an atlas (BuildGpuAtlas below), which
    // TerrainGpuSampler uploads per dispatch.
    //
    // Reporting a real type here is what keeps a world containing this biome on
    // the GPU at all: BiomeDensityField.TryBuildGpuLeaves fails if ANY biome
    // resolves to None, and takes the whole world down to CPU sampling with it.
    public override GpuFieldType GpuType => GpuFieldType.Grove;

    public override LeafGpuParams ToGpuLeafParams() => new LeafGpuParams
    {
        grove = new GroveGpuParams
        {
            seed = seed,
            groundHeight = groundHeight,
            groundAmp = groundAmp,
            groundScale = groundScale > 0.01f ? groundScale : 0.01f,
            groundOctaves = groundOctaves,
            blend = blend,
            plotSize = PlotSize,
        }
    };

    // Bumped whenever cached plots are thrown away, so the sampler can tell
    // that an atlas it uploaded for the same plot rectangle is nonetheless
    // stale and must be rebuilt.
    public int Version => _version;
    int _version;

    // Flattens the plots covering [pxMin, pxMin+plotsX) x [pzMin, pzMin+plotsZ)
    // into the caller's lists, in ROW-MAJOR order so the shader can index a
    // plot as (pz - pzMin) * plotsX + (px - pxMin) with no search.
    //
    // Cell items stay plot-relative; the shader adds capsuleBase. That keeps
    // this a copy rather than a renumbering pass.
    public bool BuildGpuAtlas(int pxMin, int pzMin, int plotsX, int plotsZ,
                              List<GroveCapsuleGpu> capsules, List<GrovePlotGpu> plots,
                              List<int> cellStart, List<int> cellItems, List<Vector2> cellY)
    {
        capsules.Clear();
        plots.Clear();
        cellStart.Clear();
        cellItems.Clear();
        cellY.Clear();
        if (grammar == null || plotsX <= 0 || plotsZ <= 0) return false;

        int total = plotsX * plotsZ;
        var resolved = new Plot[total];

        // A plot is a pure function of (px, pz, seed) and GetPlot is already
        // thread-safe -- ConcurrentDictionary plus [ThreadStatic] scratch -- so
        // warming the range in parallel is free correctness-wise and turns a
        // first-entry hitch into a fraction of one. Repeat dispatches over the
        // same ground hit the cache and do no derivation at all.
        Parallel.For(0, total, i => resolved[i] = GetPlot(pxMin + (i % plotsX), pzMin + (i / plotsX)));

        for (int i = 0; i < total; i++)
        {
            Plot p = resolved[i];
            if (p == null || p.Res <= 0 || p.Capsules.Length == 0)
            {
                plots.Add(default); // res = 0: the shader skips it
                continue;
            }

            var rec = new GrovePlotGpu
            {
                minX = p.MinX,
                minZ = p.MinZ,
                maxX = p.MaxX,
                maxZ = p.MaxZ,
                invCell = p.InvCell,
                res = p.Res,
                cellStartBase = cellStart.Count,
                itemBase = cellItems.Count,
                capsuleBase = capsules.Count,
                cellYBase = cellY.Count,
            };

            for (int c = 0; c < p.Capsules.Length; c++)
            {
                Capsule cap = p.Capsules[c];
                capsules.Add(new GroveCapsuleGpu { a = cap.A, ra = cap.RA, b = cap.B, rb = cap.RB });
            }
            for (int c = 0; c < p.CellStart.Length; c++) cellStart.Add(p.CellStart[c]);
            for (int c = 0; c < p.CellItems.Length; c++) cellItems.Add(p.CellItems[c]);
            for (int c = 0; c < p.CellMinY.Length; c++) cellY.Add(new Vector2(p.CellMinY[c], p.CellMaxY[c]));

            plots.Add(rec);
        }

        return capsules.Count > 0;
    }

    // Exposed for the offline verification harness only: the round-cone SDF
    // and the smooth max are the two pieces of pure math here that decide
    // where the surface actually lands, and they cannot be reached from the
    // EditMode tests (those live in TerrainGen.LSystem.Tests, which by design
    // cannot reference Assembly-CSharp).
    internal static float DistanceToCone(Vector3 p, Vector3 a, Vector3 b, float ra, float rb)
        => new Capsule(a, b, ra, rb).Distance(p.x, p.y, p.z);

    internal static float SmoothMax(float a, float b, float k) => SMax(a, b, k);
}
