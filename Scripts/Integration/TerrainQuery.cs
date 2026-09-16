using UnityEngine;

// Ground queries straight from a density field -- no colliders required, so
// they work for spawn points and for bodies whose ground has not cooked yet.
// Positive density = solid. "Up" is supplied by the caller (PlanetGravity.UpAt
// for anything that must work on the globe too).
public static class TerrainQuery
{
    const float kStep = 0.5f;      // coarse scan step; equals the 0.5 m LOD0 cell, so thin ledges are not skipped
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

        // Iterate by integer step count, not accumulated float t: when total isn't
        // a multiple of kStep, t += kStep would stop short of total (e.g. total
        // 100.3 -> last t is 100.0), silently dropping the final sliver of the
        // range from the scan. Clamping i*kStep to total instead guarantees the
        // last sample is always exactly at total.
        int stepCount = Mathf.CeilToInt(total / kStep);
        for (int step = 1; step <= stepCount; step++)
        {
            float tt = Mathf.Min(step * kStep, total);
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

    // Nearest solid->air crossing straight above `from`, which must itself be
    // inside rock. Unlike TryFindGround (which scans down from a point above
    // and so lands on the TOPMOST surface in range), this walks up from the
    // buried point: under an overhang or inside a cave it leaves through the
    // rock directly overhead, and never lifts a body onto a roof above an
    // air gap. False when `from` is not solid or no exit lies within maxUp.
    public static bool TryFindExitAbove(DensityField field, Vector3 from, Vector3 up, float maxUp, out Vector3 point)
    {
        point = default;
        if (field == null || up.sqrMagnitude < 1e-8f || maxUp <= 0f) return false;
        up.Normalize();
        if (field.Sample(from) <= 0f) return false;

        float prevT = 0f;
        int stepCount = Mathf.CeilToInt(maxUp / kStep); // integer steps, last one clamped to maxUp -- see TryFindGround
        for (int step = 1; step <= stepCount; step++)
        {
            float tt = Mathf.Min(step * kStep, maxUp);
            if (field.Sample(from + up * tt) <= 0f)
            {
                float lo = prevT, hi = tt; // lo solid, hi air
                for (int i = 0; i < kRefine; i++)
                {
                    float mid = 0.5f * (lo + hi);
                    if (field.Sample(from + up * mid) > 0f) lo = mid; else hi = mid;
                }
                point = from + up * (0.5f * (lo + hi));
                return true;
            }
            prevT = tt;
        }
        return false;
    }

    public static bool TryFindGround(Vector3 from, float searchUp, float searchDown, out Vector3 point, out Vector3 normal)
    {
        var m = MCChunkManager.Instance;
        if (m == null) { point = default; normal = Vector3.up; return false; }
        return TryFindGround(m.PhysicsField, from, PlanetGravity.UpAt(from), searchUp, searchDown, out point, out normal);
    }
}
