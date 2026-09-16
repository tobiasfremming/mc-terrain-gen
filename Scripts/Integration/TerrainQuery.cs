using UnityEngine;

// Ground queries straight from a density field -- no colliders required, so
// they work for spawn points and for bodies whose ground has not cooked yet.
// Positive density = solid. "Up" is supplied by the caller (PlanetGravity.UpAt
// for anything that must work on the globe too).
public static class TerrainQuery
{
    const float kStep = 0.5f;      // coarse scan step; below the 0.5 m LOD0 cell, so thin ledges are not skipped
    const int kRefine = 14;        // bisection steps: 0.5 m / 2^14 ≈ 0.04 mm

    public static bool TryFindGround(DensityField field, Vector3 from, Vector3 up, float searchUp, float searchDown,
                                     out Vector3 point, out Vector3 normal)
    {
        point = default; normal = up;
        if (field == null || up.sqrMagnitude < 1e-8f) return false;
        up.Normalize();

        float total = searchUp + searchDown;
        Vector3 top = from + up * searchUp;
        float prevT = 0f;
        bool prevSolid = field.Sample(top) > 0f;

        for (float t = kStep; t <= total + 1e-4f; t += kStep)
        {
            float tt = Mathf.Min(t, total);
            bool solid = field.Sample(top - up * tt) > 0f;
            if (solid && !prevSolid)
            {
                float lo = prevT, hi = tt; // lo air, hi solid
                for (int i = 0; i < kRefine; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (field.Sample(top - up * mid) > 0f) hi = mid; else lo = mid;
                }
                point = top - up * (0.5f * (lo + hi));
                Vector3 g = field.Gradient(point, 0.05f, 0f);
                normal = g.sqrMagnitude > 1e-12f ? -g.normalized : up; // density rises into rock, so the outward normal is -grad
                return true;
            }
            prevSolid = solid;
            prevT = tt;
        }
        return false;
    }

    public static bool IsInsideSolid(DensityField field, Vector3 p) => field != null && field.Sample(p) > 0f;

    public static bool TryFindGround(Vector3 from, float searchUp, float searchDown, out Vector3 point, out Vector3 normal)
    {
        var m = MCChunkManager.Instance;
        if (m == null) { point = default; normal = Vector3.up; return false; }
        return TryFindGround(m.PhysicsField, from, PlanetGravity.UpAt(from), searchUp, searchDown, out point, out normal);
    }
}
