using UnityEngine;

// A disc (in the plane perpendicular to up) a creature is kept inside, optionally with obstacle discs
// inside it (a tree trunk) that the creature walks around: obstacleRadii are the obstacles themselves,
// keepOut is the clearance the head's path keeps from them (the body's lateral reach plus a share of its
// length, since a following body cuts inside the head's curve).
public struct EnclosureArea
{
    public Vector3 center, up;
    public float radius, safeRadius;
    public Vector3[] obstacleCenters;
    public float[] obstacleRadii;
    public float keepOut;
    // Climbing: the highest a head may get above the centre along up (0 = no cap; the sim brakes at it, a
    // ClimbingBrain turns back below it), and which obstacles may be climbed (parallel to obstacleCenters;
    // null = none).
    public float maxHeight;
    public bool[] obstacleClimbable;

    public bool IsClimbable(int obstacle) => obstacleClimbable != null && obstacle >= 0 && obstacle < obstacleClimbable.Length && obstacleClimbable[obstacle];

    public int ObstacleCount => obstacleCenters != null ? obstacleCenters.Length : 0;

    // Index of the obstacle whose keep-out disc contains p (planar), or -1.
    public int ObstacleAt(Vector3 p, float extra = 0f)
    {
        for (int i = 0; i < ObstacleCount; i++)
        {
            Vector3 d = p - obstacleCenters[i];
            d -= up * Vector3.Dot(d, up);
            float r = obstacleRadii[i] + keepOut + extra;
            if (d.sqrMagnitude < r * r) return i;
        }
        return -1;
    }

    // The clearance for a creature: its lateral reach plus a quarter of its length, at its size scale.
    public static float KeepOutFor(CreatureSpecies sp, float sizeScale)
        => sizeScale * (sp.MaxLateralReach + 0.25f * sp.BodyLength);

    public static EnclosureArea For(Vector3 center, Vector3 up, float radius, CreatureSpecies sp, float sizeScale, float margin = 0.2f)
    {
        return new EnclosureArea
        {
            center = center,
            up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up,
            radius = radius,
            safeRadius = Mathf.Max(0f, radius - sizeScale * (sp.MaxLateralReach + margin)),
        };
    }

    public float PlanarDistance(Vector3 p)
    {
        Vector3 d = p - center;
        return (d - up * Vector3.Dot(d, up)).magnitude;
    }

    // Planar clamp to safeRadius; keeps the up-component.
    public Vector3 ClampToSafe(Vector3 p)
    {
        Vector3 d = p - center;
        float h = Vector3.Dot(d, up);
        Vector3 planar = d - up * h;
        float m = planar.magnitude;
        if (m <= safeRadius) return p;
        planar = m > 1e-8f ? planar * (safeRadius / m) : Vector3.zero;
        return center + planar + up * h;
    }

    // Uniform in the safe disc outside every obstacle's keep-out disc; rng is xorshift32 state.
    public Vector3 RandomSafePoint(ref uint rng)
    {
        Vector3 p = RandomDiscPoint(ref rng);
        for (int tries = 0; tries < 32 && ObstacleAt(p) >= 0; tries++) p = RandomDiscPoint(ref rng);
        int o = ObstacleAt(p);
        if (o >= 0)
        {
            // every try landed on an obstacle (it covers most of the disc): step out of it radially
            Vector3 d = p - obstacleCenters[o]; d -= up * Vector3.Dot(d, up);
            if (d.sqrMagnitude < 1e-8f) d = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right);
            p = obstacleCenters[o] + d.normalized * (obstacleRadii[o] + keepOut + 0.01f);
            p = ClampToSafe(p);
        }
        return p;
    }

    Vector3 RandomDiscPoint(ref uint rng)
    {
        float u = Next(ref rng), v = Next(ref rng);
        float r = safeRadius * Mathf.Sqrt(u), a = v * 2f * Mathf.PI;
        Vector3 t = Vector3.Cross(up, Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.right).normalized;
        Vector3 b = Vector3.Cross(up, t);
        return center + t * (r * Mathf.Cos(a)) + b * (r * Mathf.Sin(a));
    }

    static float Next(ref uint s)
    {
        if (s == 0) s = 2463534242u;
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s & 0xFFFFFFu) / 16777216f;
    }
}
