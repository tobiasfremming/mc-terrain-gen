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
    const float kStep = 0.5f; // TerrainQuery's coarse step, for sample accounting

    readonly DensityField field;
    public int SampleCount; // cumulative field samples, for perf stats

    public FieldGroundProbe(DensityField field) { this.field = field; }

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
        if (field == null) { point = near; normal = up; return false; }

        Vector3 hi = near + up * window, lo = near - up * window;
        SampleCount += 2;
        float fa = field.Sample(hi), fb = field.Sample(lo);
        if (fa <= 0f && fb > 0f)
        {
            // Illinois false position along hi -> lo (a is air, b is solid). A heightfield's density is
            // linear along up, so the first step lands on the surface; a curved field converges
            // superlinearly. Stops once the estimate is within kTol of the surface (density over its slope).
            float a = 0f, b = 2f * window, t = 0.5f * b;
            float slope = (fb - fa) / b;
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
            Vector3 g = field.Gradient(point, 0.05f, 0f);
            SampleCount += 6; // central differences
            normal = g.sqrMagnitude > 1e-12f ? -g.normalized : up;
            return true;
        }

        SampleCount += Mathf.CeilToInt((kFallbackUp + kFallbackDown) / kStep) + 1;
        if (TerrainQuery.TryFindGround(field, near, up, kFallbackUp, kFallbackDown, out point, out normal))
            return true;
        point = near; normal = up;
        return false;
    }
}

// A creature's ground cache over another probe (normally the field probe). On DesertWorld a field sample
// costs ~2.3 us and a narrow probe ~50 us, and a monitor asks ~66 ground queries per frame, so probing the
// field per query cost ~2.6 ms a frame. Here ground heights and normals live on a lattice of `cell` metres
// in world XZ - by default the 0.5 m LOD0 voxel columns, where the meshed sand passes through the field's
// own heights - and a query is a bilinear read of four nodes. A node is probed once, when first needed,
// and kept while the creature stays near it: the lattice is a toroidal size x size window that slides
// with the creature (size from its extent), so walking on only probes the new row ahead.
// A node serves a query whose near point lies within the query's window of the node's height (or close to
// where the node was probed from); otherwise it is probed again from the query (another layer, a cave).
// Only for up = world +Y (flat worlds): any other up passes straight through to the inner probe.
// Terrain edits: Invalidate(bounds) drops the nodes an edit touched (CreatureWorld forwards the physics
// field's RegionModified). Nothing else refreshes a node, which is right for the static field.
public sealed class CachedGroundProbe : IGroundProbe
{
    public const float DefaultCell = 0.5f;

    readonly IGroundProbe inner;
    readonly float cell, invCell;
    readonly int size, mask;
    readonly int[] keyX, keyZ;
    readonly float[] height, probedY;
    readonly Vector3[] nodeNormal;
    readonly bool[] valid;

    public int Queries, NodeProbes; // cumulative, for perf stats and tests

    // the last query's cell: consecutive spine joints mostly share one, so its four slots are reused
    // while no slot has been written since (version) and the query is within the window of all four
    int version, lastVersion = -1, lastIx, lastIz, l00, l10, l01, l11;
    float lastLo, lastHi;

    public CachedGroundProbe(IGroundProbe inner, float extent, float cell = DefaultCell)
    {
        this.inner = inner ?? throw new System.ArgumentNullException(nameof(inner));
        this.cell = cell > 1e-3f ? cell : DefaultCell;
        invCell = 1f / this.cell;
        int need = Mathf.CeilToInt(Mathf.Max(0f, extent) * invCell) + 3;
        size = Mathf.NextPowerOfTwo(Mathf.Max(4, need));
        mask = size - 1;
        int c = size * size;
        keyX = new int[c]; keyZ = new int[c]; height = new float[c]; probedY = new float[c];
        nodeNormal = new Vector3[c]; valid = new bool[c];
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

    public bool Probe(Vector3 near, Vector3 up, float window, out Vector3 point, out Vector3 normal)
    {
        if (!(up.y > 0.99999f)) return inner.Probe(near, up, window, out point, out normal); // not a flat world
        Queries++;
        float gx = near.x * invCell, gz = near.z * invCell;
        int ix = Mathf.FloorToInt(gx), iz = Mathf.FloorToInt(gz);
        float fx = gx - ix, fz = gz - iz;
        int s00, s10, s01, s11;
        if (lastVersion == version && ix == lastIx && iz == lastIz && near.y >= lastHi - window && near.y <= lastLo + window)
        {
            s00 = l00; s10 = l10; s01 = l01; s11 = l11;
        }
        else
        {
            if (!Node(ix, iz, near.y, window, out s00) || !Node(ix + 1, iz, near.y, window, out s10) ||
                !Node(ix, iz + 1, near.y, window, out s01) || !Node(ix + 1, iz + 1, near.y, window, out s11))
            {
                point = near; normal = Vector3.up;
                return false;
            }
            lastVersion = version; lastIx = ix; lastIz = iz; l00 = s00; l10 = s10; l01 = s01; l11 = s11;
            lastLo = Mathf.Min(Mathf.Min(height[s00], height[s10]), Mathf.Min(height[s01], height[s11]));
            lastHi = Mathf.Max(Mathf.Max(height[s00], height[s10]), Mathf.Max(height[s01], height[s11]));
        }
        float w00 = (1f - fx) * (1f - fz), w10 = fx * (1f - fz), w01 = (1f - fx) * fz, w11 = fx * fz;
        float y = w00 * height[s00] + w10 * height[s10] + w01 * height[s01] + w11 * height[s11];
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

    bool Node(int ix, int iz, float nearY, float window, out int slot)
    {
        slot = (ix & mask) + (iz & mask) * size;
        if (valid[slot] && keyX[slot] == ix && keyZ[slot] == iz &&
            (Mathf.Abs(height[slot] - nearY) <= window || Mathf.Abs(probedY[slot] - nearY) <= 0.5f * window))
            return true;
        NodeProbes++;
        version++;
        var at = new Vector3(ix * cell, nearY, iz * cell);
        if (!inner.Probe(at, Vector3.up, window, out Vector3 p, out Vector3 n)) { valid[slot] = false; return false; }
        keyX[slot] = ix; keyZ[slot] = iz;
        height[slot] = p.y; probedY[slot] = nearY;
        nodeNormal[slot] = n; valid[slot] = true;
        return true;
    }

    // Drops every cached node inside the world-space bounds (grown by one cell): the next query re-probes it.
    public void Invalidate(Bounds b)
    {
        Vector3 lo = b.min, hi = b.max;
        float m = cell;
        for (int s = 0; s < valid.Length; s++)
        {
            if (!valid[s]) continue;
            float x = keyX[s] * cell, z = keyZ[s] * cell, y = height[s];
            if (x >= lo.x - m && x <= hi.x + m && z >= lo.z - m && z <= hi.z + m && y >= lo.y - m && y <= hi.y + m)
                valid[s] = false;
        }
        version++;
    }

    public void Clear() { for (int s = 0; s < valid.Length; s++) valid[s] = false; version++; }
}
