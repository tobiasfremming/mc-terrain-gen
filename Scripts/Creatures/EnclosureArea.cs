using UnityEngine;

// A disc (in the plane perpendicular to up) a creature is kept inside.
public struct EnclosureArea
{
    public Vector3 center, up;
    public float radius, safeRadius;

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

    // Uniform in the safe disc; rng is xorshift32 state.
    public Vector3 RandomSafePoint(ref uint rng)
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
