using UnityEngine;

// Ground queries for creatures. Field-based, never colliders.
public interface IGroundProbe
{
    // near: a point close to the ground; window: half-extent searched along up.
    bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal);
}

// Flat world at y = height, up = +Y.
public sealed class FlatGroundProbe : IGroundProbe
{
    readonly float height;
    public FlatGroundProbe(float height = 0f) { this.height = height; }

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        point = new Vector3(near.x, height, near.z);
        normal = Vector3.up;
        return true;
    }
}

public sealed class FieldGroundProbe : IGroundProbe
{
    const int kMaxIter = 12;           // root-finding iterations at most (plain bisection needed all 12)
    const float kTol = 1e-4f;          // metres along up
    const float kFallbackUp = 8f, kFallbackDown = 16f;
    const float kStep = 0.5f;          // fallback scan step: TerrainQuery's, the 0.5 m LOD0 cell
    const float kFineStep = 0.125f;    // scan step within the window: a dug hole's lip can be thinner than a cell

    DensityField field;
    public int SampleCount; // cumulative field samples, for perf stats

    public FieldGroundProbe(DensityField field) { this.field = field; }

    // The field probed. CreatureWorld re-points it when the terrain rebuilds its physics field (a new object),
    // so the caches layered over this probe keep working on the live field.
    public DensityField Field { get => field; set => field = value; }

    // The up-facing surface (air above, solid below) NEAREST to `near` along up: the first one below it
    // when near is in air, the first one above it when near is inside solid. A body that probes from its
    // previous ground point therefore stays on the surface it stands on, also in a column with several of
    // them (a dug hole under an overhanging lip, a cave under a roof): it never jumps to another layer just
    // because a window or a top-down scan happened to reach that one first (R25). The scan steps by
    // min(window, 12.5 cm) out to the window, then by 0.5 m on to the fallback range (16 m down through
    // air, 8 m up through solid); the bracketing step is refined by false position. A miss returns false.
    // On ground the near point is within a step of the surface, so a probe costs ~4 samples + the normal.
    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
        if (field == null) { point = near; normal = up; return false; }

        SampleCount++;
        float f0 = field.Sample(near);
        bool air = f0 <= 0f;
        float dir = air ? -1f : 1f; // down through air to the ground, up through solid to its top
        float step = window > 1e-4f ? Mathf.Min(window, kFineStep) : kStep;
        float limit = air ? kFallbackDown : kFallbackUp;
        int count = Mathf.CeilToInt(Mathf.Max(window, limit) / step);
        float prevT = 0f, fPrev = f0;
        for (int k = 1; k <= count; k++)
        {
            // the window at its own step, then the fallback range at the coarse step
            float t = k * step;
            if (t > window && step < kStep) t = window + (t - window) * (kStep / step);
            t = Mathf.Min(t, Mathf.Max(window, limit));
            SampleCount++;
            float f = field.Sample(near + up * (dir * t));
            if ((f > 0f) == air)
            {
                if (air) Refine(near - up * prevT, up, t - prevT, fPrev, f, out point);
                else Refine(near + up * t, up, t - prevT, f, fPrev, out point);
                Vector3 g = field.Gradient(point, 0.05f, 0f);
                SampleCount += 6; // central differences
                normal = g.sqrMagnitude > 1e-12f ? -g.normalized : up;
                return true;
            }
            prevT = t; fPrev = f;
            if (t >= Mathf.Max(window, limit)) break;
        }
        point = near; normal = up;
        return false;
    }

    // Illinois false position along hi -> hi - up * len (fa at hi is air, fb at the far end is solid). A
    // heightfield's density is linear along up, so the first step lands on the surface; a curved field
    // converges superlinearly. Stops once the estimate is within kTol of the surface.
    void Refine(Vector3 hi, Vector3 up, float len, float fa, float fb, out Vector3 point)
    {
        float a = 0f, b = len, t = 0.5f * b;
        float slope = (fb - fa) / Mathf.Max(1e-6f, b);
        int side = 0;
        for (int i = 0; i < kMaxIter; i++)
        {
            t = (a * fb - b * fa) / (fb - fa);
            if (!(t > a && t < b)) t = 0.5f * (a + b); // degenerate line: bisect
            SampleCount++;
            float ft = field.Sample(hi - up * t);
            if (ft > 0f) { b = t; fb = ft; if (side < 0) fa *= 0.5f; side = -1; }
            else { a = t; fa = ft; if (side > 0) fb *= 0.5f; side = 1; }
            if (Mathf.Abs(ft) <= kTol * slope || b - a <= kTol) break;
        }
        point = hi - up * t;
    }
}

// A creature's ground cache over another probe (normally the field probe). On DesertWorld a field sample
// costs ~2.3 us and a narrow probe ~50 us, and a monitor asks ~66 ground queries per frame, so probing the
// field per query cost ~2.6 ms a frame. Here ground heights and normals live on a lattice of `cell` metres
// in world XZ - by default the 0.5 m LOD0 voxel columns, where the meshed sand passes through the field's
// own heights - and a query is a cubic Hermite read of four nodes (their heights and, from their normals,
// their slopes: exact on planes, close on curved sand, a smooth S over a step). A node is probed once, when first needed,
// and kept while the creature stays near it: the lattice is a toroidal size x size window that slides
// with the creature (size from its extent), so walking on only probes the new row ahead.
// Same answer as the inner probe (R25): the inner probe returns the surface nearest to the query's near
// point, so a node remembers the band of heights around its surface it is known to answer for (from the
// probes that found it, plus a margin; a query is carried to the node along the node's tangent plane) and
// is probed again from a query outside that band - the other surface of a column with several, e.g. the
// floor of a dug hole under its lip, or a cave roof.
// Each cell also checks that its nodes' heights agree with their slopes. Where they do not by more than
// kExactTol (a cliff or a dug hole's wall between the nodes, a sharp brink) the patch is not the ground
// and a query goes to the inner probe exactly - except through Smooth (the spine), which always reads the
// patch: a body bridges a rim on the smooth S, it does not drop off it.
// Only for up = world +Y (flat worlds): any other up passes straight through to the inner probe.
// Terrain edits: Invalidate(bounds) drops the nodes an edit touched (CreatureWorld forwards the physics
// field's RegionModified). Nothing else refreshes a node, which is right for the static field.
public sealed class CachedGroundProbe : IGroundProbe
{
    public const float DefaultCell = 0.5f;
    // A node answers near heights within these fractions of a cell beyond the ones it was probed from,
    // below and above its surface: the surface just below a probed near point is the nearest one there
    // as well. Above the surface (in the air over it) the band is wider - queries come from up to ~0.5 m
    // above the ground (a rolled body's rest points), and a second surface that close above is rare;
    // below it is narrow, since a query a little under a dug hole's thin lip is in the hole, not the lip.
    const float kMarginCells = 0.25f, kMarginUpCells = 1f;
    const float kSameSurface = 0.01f; // a re-probe within 1 cm of the node's height found the same surface
    // A height step between neighbouring nodes that their slopes miss by more than this (m) is a step the
    // patch cannot follow: exact probes there. A node steeper than kMaxNy (a wall) makes the cell a step.
    // 5 cm: no dune cell of the lab (sharp brinks included) reaches it, every dug hole's rim exceeds it.
    const float kExactTol = 0.05f, kMaxNy = 0.3f;

    readonly IGroundProbe inner;
    readonly float cell, invCell, margin, marginUp;
    readonly int size, mask;
    readonly int[] keyX, keyZ;
    readonly float[] height, offLo, offHi; // the node answers near heights height + offLo .. height + offHi
    readonly Vector3[] nodeNormal;
    readonly float[] slopeX, slopeZ; // dh/dx, dh/dz from the node's normal (the Hermite tangents)
    readonly bool[] valid;

    public int Queries, NodeProbes, ExactQueries; // cumulative, for perf stats and tests

    // the last query's cell: consecutive spine joints mostly share one, so its four slots are reused
    // while no slot has been written since (version) and the query's height above the patch is within
    // all four nodes' bands
    int version, lastVersion = -1, lastIx, lastIz, l00, l10, l01, l11;
    float lastLo, lastHi;
    bool lastStep;
    // The last cell's Hermite tangents per edge (x slopes on its z = 0 / z = 1 edges, z slopes on its x = 0 /
    // x = 1 edges) and its node height range. On an edge with a step the node slopes are limited to the
    // edge's secant (Fritsch-Carlson: same sign, at most 3x), so the smooth S stays between the two nodes
    // instead of overshooting a dug rim by up to ~25 cm; a step cell's patch is also clamped to its node range.
    // Limiting per edge (from the edge's two nodes only) keeps the patch continuous across cells.
    float ex00, ex10, ex01, ex11, ez00, ez01, ez10, ez11, lastMin, lastMax;

    public CachedGroundProbe(IGroundProbe inner, float extent, float cell = DefaultCell)
    {
        this.inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        this.cell = cell > 1e-3f ? cell : DefaultCell;
        invCell = 1f / this.cell;
        margin = kMarginCells * this.cell; marginUp = kMarginUpCells * this.cell;
        int need = Mathf.CeilToInt(Mathf.Max(0f, extent) * invCell) + 3;
        size = Mathf.NextPowerOfTwo(Mathf.Max(4, need));
        mask = size - 1;
        int c = size * size;
        keyX = new int[c]; keyZ = new int[c]; height = new float[c]; offLo = new float[c]; offHi = new float[c];
        nodeNormal = new Vector3[c]; valid = new bool[c]; slopeX = new float[c]; slopeZ = new float[c];
    }

    // Sized for a creature: everything it touches lies within its body length plus a leg's reach and the
    // step overshoot on each side.
    public static CachedGroundProbe ForCreature(IGroundProbe inner, CreatureSpecies sp, float sizeScale, float cell = DefaultCell)
    {
        float over = 0f;
        if (sp.legs != null)
            for (int l = 0; l < sp.legs.Length; l++)
                if (sp.legs[l] != null) over = Mathf.Max(over, sp.legs[l].overshoot);
        return new CachedGroundProbe(inner, (sp.BodyLength + 2f * (sp.MaxLateralReach + over)) * sizeScale, cell);
    }

    public int WindowSize => size;
    public float Cell => cell;

    // The same cache read without the exact cells: a rim comes back as the patch's smooth S between its
    // nodes. For a body that rests on the ground over its own width (the spine), where an exact step under
    // one joint would only make it jerk; feet use the exact probe.
    public IGroundProbe Smooth => smooth ?? (smooth = new SmoothView(this));
    SmoothView smooth;

    sealed class SmoothView : IGroundProbe
    {
        readonly CachedGroundProbe cache;
        public SmoothView(CachedGroundProbe cache) { this.cache = cache; }
        public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
            => cache.Probe(near, up, window, false, out point, out normal);
    }

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
        => Probe(near, up, window, true, out point, out normal);

    bool Probe(Vector3 near, Vector3 up, float window, bool exactSteps, out Vector3 point, out Vector3 normal)
    {
        if (!(up.y > 0.99999f)) return inner.Probe(near, up, window, out point, out normal); // not a flat world
        Queries++;
        float gx = near.x * invCell, gz = near.z * invCell;
        int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz);
        float fx = gx - ix, fz = gz - iz;
        int s00 = l00, s10 = l10, s01 = l01, s11 = l11;
        float y = 0f;
        bool hit = lastVersion == version && ix == lastIx && iz == lastIz;
        if (hit)
        {
            y = Patch(fx, fz, s00, s10, s01, s11);
            float o = near.y - y;
            hit = o >= lastLo && o <= lastHi;
        }
        if (!hit)
        {
            if (!Node(ix, iz, near, window, out s00) || !Node(ix + 1, iz, near, window, out s10) ||
                !Node(ix, iz + 1, near, window, out s01) || !Node(ix + 1, iz + 1, near, window, out s11))
            {
                // a node without ground here (past the probe's range, over a void): the query itself decides
                ExactQueries++;
                return inner.Probe(near, up, window, out point, out normal);
            }
            lastVersion = version; lastIx = ix; lastIz = iz; l00 = s00; l10 = s10; l01 = s01; l11 = s11;
            lastLo = Mathf.Max(Mathf.Max(offLo[s00], offLo[s10]), Mathf.Max(offLo[s01], offLo[s11]));
            lastHi = Mathf.Min(Mathf.Min(offHi[s00], offHi[s10]), Mathf.Min(offHi[s01], offHi[s11]));
            lastStep = StepMiss(s00, s10, s01, s11) > kExactTol;
            SetCellTangents(s00, s10, s01, s11);
            y = Patch(fx, fz, s00, s10, s01, s11);
        }
        if (lastStep && exactSteps) { ExactQueries++; return inner.Probe(near, up, window, out point, out normal); }
        float w00 = (1f - fx) * (1f - fz), w10 = fx * (1f - fz), w01 = (1f - fx) * fz, w11 = fx * fz;
        Vector3 n0 = nodeNormal[s00], n1 = nodeNormal[s10], n2 = nodeNormal[s01], n3 = nodeNormal[s11];
        float nx = w00 * n0.x + w10 * n1.x + w01 * n2.x + w11 * n3.x;
        float ny = w00 * n0.y + w10 * n1.y + w01 * n2.y + w11 * n3.y;
        float nz = w00 * n0.z + w10 * n1.z + w01 * n2.z + w11 * n3.z;
        float m2 = nx * nx + ny * ny + nz * nz;
        if (m2 > 1e-12f) { float inv = 1f / Mathf.Sqrt(m2); normal = new Vector3(nx * inv, ny * inv, nz * inv); }
        else normal = Vector3.up;
        point = new Vector3(near.x, y, near.z);
        return true;
    }

    // Bicubic Hermite with zero twist over the cell at (fx, fz): value basis a = 1 - 3t^2 + 2t^3 and
    // b = 1 - a, slope basis c = t (1 - t)^2 and d = t^2 (t - 1), times the cell. Exact on planes. The
    // tangents are the cell's edge tangents (SetCellTangents); a step cell is clamped to its node range.
    float Patch(float fx, float fz, int s00, int s10, int s01, int s11)
    {
        float ax = 1f - fx * fx * (3f - 2f * fx), bx = 1f - ax, cx = cell * fx * (1f - fx) * (1f - fx), dx = cell * fx * fx * (fx - 1f);
        float az = 1f - fz * fz * (3f - 2f * fz), bz = 1f - az, cz = cell * fz * (1f - fz) * (1f - fz), dz = cell * fz * fz * (fz - 1f);
        float y = az * (ax * height[s00] + bx * height[s10] + cx * ex00 + dx * ex10)
                + bz * (ax * height[s01] + bx * height[s11] + cx * ex01 + dx * ex11)
                + cz * (ax * ez00 + bx * ez10) + dz * (ax * ez01 + bx * ez11);
        return lastStep ? Mathf.Clamp(y, lastMin, lastMax) : y;
    }

    // The cell's edge tangents and node range (see ex00..). An edge whose slopes miss its height step by
    // more than kExactTol, or that ends on a wall node, is a step edge: its tangents are limited.
    void SetCellTangents(int s00, int s10, int s01, int s11)
    {
        float h0 = height[s00], h1 = height[s10], h2 = height[s01], h3 = height[s11];
        lastMin = Mathf.Min(Mathf.Min(h0, h1), Mathf.Min(h2, h3));
        lastMax = Mathf.Max(Mathf.Max(h0, h1), Mathf.Max(h2, h3));
        ex00 = slopeX[s00]; ex10 = slopeX[s10]; ex01 = slopeX[s01]; ex11 = slopeX[s11];
        ez00 = slopeZ[s00]; ez01 = slopeZ[s01]; ez10 = slopeZ[s10]; ez11 = slopeZ[s11];
        if (!lastStep) return;
        LimitEdge(s00, s10, ref ex00, ref ex10);
        LimitEdge(s01, s11, ref ex01, ref ex11);
        LimitEdge(s00, s01, ref ez00, ref ez01);
        LimitEdge(s10, s11, ref ez10, ref ez11);
    }

    // Fritsch-Carlson on one edge a -> b with tangents ma, mb (dh per metre along the edge), if it is a step
    // edge: a tangent against the secant's sign becomes 0, one steeper than 3x the secant is cut to 3x. The
    // edge's cubic is then monotone, inside [h_a, h_b].
    void LimitEdge(int a, int b, ref float ma, ref float mb)
    {
        float step = height[b] - height[a];
        bool wall = nodeNormal[a].y < kMaxNy || nodeNormal[b].y < kMaxNy;
        if (!wall && Mathf.Abs(step - 0.5f * cell * (ma + mb)) <= kExactTol) return;
        float d = step * invCell;
        if (Mathf.Abs(d) < 1e-6f) { ma = mb = 0f; return; }
        ma = d > 0f ? Mathf.Clamp(ma, 0f, 3f * d) : Mathf.Clamp(ma, 3f * d, 0f);
        mb = d > 0f ? Mathf.Clamp(mb, 0f, 3f * d) : Mathf.Clamp(mb, 3f * d, 0f);
    }

    // The node height range and step flag of the cell the last query read (tests).
    internal bool LastCell(out float min, out float max) { min = lastMin; max = lastMax; return lastStep; }

    // How far do the nodes' height steps disagree with their slopes? Along each cell edge the end nodes'
    // slopes predict the step (trapezoid rule); a step they do not explain is a cliff or a hole's wall
    // between the nodes (or a sharp brink), which no patch through the nodes follows.
    float StepMiss(int s00, int s10, int s01, int s11)
    {
        if (nodeNormal[s00].y < kMaxNy || nodeNormal[s10].y < kMaxNy || nodeNormal[s01].y < kMaxNy || nodeNormal[s11].y < kMaxNy)
            return float.PositiveInfinity;
        float h = 0.5f * cell;
        return Mathf.Max(Mathf.Max(Mathf.Abs(height[s10] - height[s00] - h * (slopeX[s00] + slopeX[s10])),
                                   Mathf.Abs(height[s11] - height[s01] - h * (slopeX[s01] + slopeX[s11]))),
                         Mathf.Max(Mathf.Abs(height[s01] - height[s00] - h * (slopeZ[s00] + slopeZ[s01])),
                                   Mathf.Abs(height[s11] - height[s10] - h * (slopeZ[s10] + slopeZ[s11]))));
    }

    // The node at (ix, iz) for a query at near: valid if near, carried to the node along the node's tangent
    // plane, lies within the node's band; otherwise probed from there (the same surface widens the band,
    // another one replaces the node).
    bool Node(int ix, int iz, Vector3 near, float window, out int slot)
    {
        slot = (ix & mask) + (iz & mask) * size;
        float nx = ix * cell, nz = iz * cell;
        bool mine = valid[slot] && keyX[slot] == ix && keyZ[slot] == iz;
        float from = near.y;
        if (mine)
        {
            float o = near.y - (height[slot] + slopeX[slot] * (near.x - nx) + slopeZ[slot] * (near.z - nz));
            if (o >= offLo[slot] && o <= offHi[slot]) return true;
            from = height[slot] + o;
        }
        NodeProbes++;
        version++;
        if (!inner.Probe(new Vector3(nx, from, nz), Vector3.up, window, out Vector3 p, out Vector3 n)) { valid[slot] = false; return false; }
        float off = from - p.y;
        float a = Mathf.Min(0f, off) - margin, b = Mathf.Max(0f, off) + marginUp;
        if (mine && Mathf.Abs(p.y - height[slot]) <= kSameSurface)
        {
            // the same surface from another height: it answers for both
            offLo[slot] = Mathf.Min(offLo[slot], a); offHi[slot] = Mathf.Max(offHi[slot], b);
            return true;
        }
        keyX[slot] = ix; keyZ[slot] = iz;
        height[slot] = p.y; offLo[slot] = a; offHi[slot] = b;
        nodeNormal[slot] = n; valid[slot] = true;
        // tangents for the patch; a wall's (n.y small) are capped, its cell is a step anyway
        float ny = Mathf.Max(n.y, kMaxNy);
        slopeX[slot] = -n.x / ny; slopeZ[slot] = -n.z / ny;
        return true;
    }

    // Drops every cached node inside the world-space bounds (grown by one cell): the next query re-probes it.
    public void Invalidate(Bounds b)
    {
        Vector3 bl = b.min, bh = b.max;
        float m = cell;
        for (int s = 0; s < valid.Length; s++)
        {
            if (!valid[s]) continue;
            float x = keyX[s] * cell, z = keyZ[s] * cell, y = height[s];
            if (x >= bl.x - m && x <= bh.x + m && z >= bl.z - m && z <= bh.z + m && y >= bl.y - m && y <= bh.y + m)
                valid[s] = false;
        }
        version++;
    }

    public void Clear() { for (int s = 0; s < valid.Length; s++) valid[s] = false; version++; }
}
