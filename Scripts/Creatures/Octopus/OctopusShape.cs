using System;
using UnityEngine;

// Rest geometry of a cephalopod, in body space at scale 1, shared by the soft lattice (OctopusBody) and
// the mesh (OctopusMeshBuilder) so the two can never disagree.
//
// Body space: +Y dorsal (up), +Z front (the eyes look forward-sideways), +X right. The arms radiate in
// the horizontal plane y = 0 from a ring of radius webRadius. The head/mantle shell is a surface of
// revolution about a curved axis (a cubic Bezier in the YZ plane): from the oral pole just below the arm
// crown up through the head (eyes) and the neck, then tilted back by mantleTiltDeg into the mantle sac,
// ending at the apex. Cross-section angle phi is measured from the front direction N = X x T toward +X.
//
// Lattice (14 particles, design 2.3): ring 0 = web ring (4, phi 45 + 90k, at the arm crown), 12 = head
// front (between the eyes), ring 1 = neck ring (phi 90k), ring 2 = mantle ring (widest), 13 = apex.
// Clusters (shape matching), ordered along the axis: web {ring0, 12}, head {ring0, ring1, 12},
// mantle base {ring1, ring2}, mantle mid {ring1, ring2, 13}, mantle tip {ring2, 13}.
internal static class OctopusShape
{
    public const int LatticeCount = 14, HeadFront = 12, Apex = 13;
    public const int ClusterWeb = 0, ClusterHead = 1, ClusterBase = 2, ClusterMid = 3, ClusterTip = 4, ClusterCount = 5;

    public const float RingWebT = 0.075f, EyeT = 0.21f, RingNeckT = 0.36f, RingMantleT = 0.70f;
    const float kLatticeInset = 0.8f;   // lattice rings sit inside the skin (fraction of the shell radius)
    public const float EyePhiDeg = 62f;  // eyes this far from the front, each side
    public const float SiphonPhiDeg = -112f, SiphonT = 0.33f;

    // members per cluster (flattened); stiffness position 0 = head .. 1 = mantle tip
    public static readonly int[][] Clusters =
    {
        new[] { 0, 1, 2, 3, 12 },
        new[] { 0, 1, 2, 3, 4, 5, 6, 7, 12 },
        new[] { 4, 5, 6, 7, 8, 9, 10, 11 },
        new[] { 4, 5, 6, 7, 8, 9, 10, 11, 13 },
        new[] { 8, 9, 10, 11, 13 },
    };
    public static readonly float[] ClusterStiffnessPos = { 0f, 0f, 0.7f, 0.9f, 1f }; // crown and head firm (they carry the arms), mantle soft
    // relative lattice masses: ring 0 (crown), ring 1 (neck), ring 2 (mantle), head front, apex
    internal static readonly float[] MassWeights = { 1.0f, 0.9f, 1.3f, 1.6f, 1.0f }; // heavy mantle: it lags, stretches, overshoots
    public static readonly bool[] ClusterBreathes = { false, false, true, true, true };

    // ------------------------------------------------------------------ axis

    static void Controls(in CephalopodSpec sp, out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
    {
        float rh = sp.HeadRadius, lm = sp.MantleLength, tilt = sp.MantleTiltDeg * Mathf.Deg2Rad;
        Vector3 mdir = new Vector3(0f, Mathf.Cos(tilt), -Mathf.Sin(tilt));
        p0 = new Vector3(0f, -0.45f * rh, 0.05f * rh);
        p3 = new Vector3(0f, 1.1f * rh, -0.1f * rh) + mdir * (lm + 0.55f * rh);
        p1 = p0 + new Vector3(0f, 2.1f * rh, 0.25f * rh);
        p2 = p3 - mdir * (0.8f * lm);
    }

    public static Vector3 Axis(in CephalopodSpec sp, float t)
    {
        Controls(sp, out var p0, out var p1, out var p2, out var p3);
        float u = 1f - t;
        return u * u * u * p0 + 3f * u * u * t * p1 + 3f * u * t * t * p2 + t * t * t * p3;
    }

    public static Vector3 AxisTangent(in CephalopodSpec sp, float t)
    {
        Controls(sp, out var p0, out var p1, out var p2, out var p3);
        float u = 1f - t;
        Vector3 d = 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
        return OctoMath.Norm(d, Vector3.up);
    }

    // Front direction of the cross-section at t (X x T); the side direction is +X.
    public static Vector3 AxisFront(in CephalopodSpec sp, float t) => OctoMath.Norm(OctoMath.Cross(Vector3.right, AxisTangent(sp, t)), Vector3.forward);

    // ------------------------------------------------------------------ shell

    static float CR(float p0, float p1, float p2, float p3, float t) =>
        0.5f * (2f * p1 + (p2 - p0) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t + (3f * p1 - p0 - 3f * p2 + p3) * t * t * t);

    // Radius keys at t = i / 8 (head radius units up to the neck, mantle radius units beyond).
    // A pear-shaped sac (cephalopod.mantlePear): narrow under the neck, widest low down, a full round end.
    static readonly float[] kPearKeys = { 0.62f, 0.85f, 1.05f, 1.12f, 0.95f };

    static float Key(in CephalopodSpec sp, int i)
    {
        float rh = sp.HeadRadius, rm = sp.MantleRadius;
        if (sp.mantlePear > 0f && i >= 4)
        {
            float k = Mathf.Clamp(i, 4, 8) - 4, w = Mathf.Min(sp.mantlePear, 1f);
            float old = k == 0 ? 0.84f : k == 1 ? 0.98f : k == 2 ? 1.0f : k == 3 ? 0.9f : 0.7f;
            return Mathf.Lerp(old, kPearKeys[(int)k], w) * rm;
        }
        switch (Mathf.Clamp(i, 0, 8))
        {
            case 0: return 0.95f * rh;
            case 1: return 1.0f * rh;
            case 2: return 1.0f * rh;
            case 3: return 0.86f * rh;
            case 4: return 0.84f * rm;
            case 5: return 0.98f * rm;
            case 6: return 1.0f * rm;
            case 7: return 0.9f * rm;
            default: return 0.7f * rm;
        }
    }

    const float kCapBottom = 0.09f, kCapTop = 0.17f;

    // Shell radius along the axis without the eye bulges; rounded (vertical-tangent) ends.
    public static float Profile(in CephalopodSpec sp, float t)
    {
        t = Mathf.Clamp01(t);
        float u = t * 8f;
        int i = Mathf.Min((int)u, 7);
        float r = CR(Key(sp, i - 1), Key(sp, i), Key(sp, i + 1), Key(sp, i + 2), u - i);
        if (t < kCapBottom) { float q = 1f - t / kCapBottom; r *= Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
        if (t > 1f - kCapTop) { float q = 1f - (1f - t) / kCapTop; r *= Mathf.Sqrt(Mathf.Max(0f, 1f - q * q)); }
        return r;
    }

    // Shell radius with the eye bulges and a slightly flattened oral/ventral side.
    public static float Radius(in CephalopodSpec sp, float t, float phi) => Radius(t, phi, Profile(sp, t));

    // Same, with the axial profile r = Profile(sp, t) already evaluated (mesh building caches it per row).
    public static float Radius(float t, float phi, float r)
    {
        float dt = (t - EyeT) / 0.07f;
        float bulge = 0f;
        for (int s = -1; s <= 1; s += 2)
        {
            float dp = Mathf.DeltaAngle(phi * Mathf.Rad2Deg, s * EyePhiDeg) * Mathf.Deg2Rad / 0.5f;
            bulge += Mathf.Exp(-(dt * dt + dp * dp));
        }
        // the head is a little wider than deep around the eyes
        float side = Mathf.Abs(Mathf.Sin(phi));
        float head = Mathf.Exp(-((t - EyeT) / 0.14f) * ((t - EyeT) / 0.14f));
        return r * (1f + 0.32f * bulge + 0.06f * head * side);
    }

    public static Vector3 ShellPoint(in CephalopodSpec sp, float t, float phi, float radiusScale = 1f)
    {
        Vector3 c = Axis(sp, t), n = AxisFront(sp, t);
        float r = Radius(sp, t, phi) * radiusScale;
        return c + (n * Mathf.Cos(phi) + Vector3.right * Mathf.Sin(phi)) * r;
    }

    // Signed distance of a body-space point (scale 1) from the mantle/head shell (+ outside), and the outward
    // direction there: the nearest of `samples` axis points, then the shell radius at that t and the point's angle.
    // Approximate (the nearest axis point, not the nearest shell point) - good to a few mm away from the caps.
    public static float ShellDistance(in CephalopodSpec sp, Vector3 p, out Vector3 outward, int samples = 24)
    {
        float best = float.MaxValue, bt = 0f;
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            float d = (p - Axis(sp, t)).sqrMagnitude;
            if (d < best) { best = d; bt = t; }
        }
        Vector3 c = Axis(sp, bt), n = AxisFront(sp, bt), r = p - c;
        float phi = MathF.Atan2(OctoMath.Dot(r, Vector3.right), OctoMath.Dot(r, n));
        float rl = r.magnitude;
        outward = rl > 1e-6f ? r * (1f / rl) : n;
        return rl - Radius(sp, bt, phi);
    }

    // The same point from a cached row (axis point c, front n, profile radius) at t.
    public static Vector3 ShellPoint(Vector3 c, Vector3 n, float profile, float t, float phi)
    {
        float r = Radius(t, phi, profile), cp = MathF.Cos(phi), sp = MathF.Sin(phi);
        return new Vector3(c.x + (n.x * cp + sp) * r, c.y + n.y * cp * r, c.z + n.z * cp * r);
    }

    // ------------------------------------------------------------------ lattice, arms, eyes, siphon

    public static Vector3[] LatticeRest(in CephalopodSpec sp)
    {
        var p = new Vector3[LatticeCount];
        for (int k = 0; k < 4; k++)
        {
            float a0 = (45f + 90f * k) * Mathf.Deg2Rad, a1 = 90f * k * Mathf.Deg2Rad;
            p[k] = RingPoint(sp, RingWebT, a0);
            p[4 + k] = RingPoint(sp, RingNeckT, a1);
            p[8 + k] = RingPoint(sp, RingMantleT, a1);
        }
        p[HeadFront] = ShellPoint(sp, EyeT, 0f, kLatticeInset);
        p[Apex] = Axis(sp, 1f) - AxisTangent(sp, 1f) * (0.25f * sp.MantleRadius);
        return p;
    }

    static Vector3 RingPoint(in CephalopodSpec sp, float t, float phi)
    {
        Vector3 c = Axis(sp, t), n = AxisFront(sp, t);
        return c + (n * Mathf.Cos(phi) + Vector3.right * Mathf.Sin(phi)) * (kLatticeInset * Profile(sp, t));
    }

    // Lattice masses (relative; OctopusBody scales them to bodyMass): the head and crown carry the most.
    public static float[] LatticeMassWeights()
    {
        var m = new float[LatticeCount];
        float[] w = MassWeights;
        for (int k = 0; k < 4; k++) { m[k] = w[0]; m[4 + k] = w[1]; m[8 + k] = w[2]; }
        m[HeadFront] = w[3]; m[Apex] = w[4];
        return m;
    }

    // Arm a: root on the web ring (between ring-0 particles), radiating horizontally.
    public static float ArmAngle(in CephalopodSpec sp, int a) => (180f / sp.ArmCount + 360f * a / sp.ArmCount) * Mathf.Deg2Rad;

    public static Vector3 ArmDir(in CephalopodSpec sp, int a)
    {
        float phi = ArmAngle(sp, a);
        return new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi));
    }

    public static Vector3 ArmRoot(in CephalopodSpec sp, int a) => ArmDir(sp, a) * sp.WebRadius;

    public static Quaternion ArmRot(in CephalopodSpec sp, int a) => OctoMath.Look(ArmDir(sp, a), Vector3.up);

    // Mouth tentacle m: a ring of MouthRing radius around the oral pole (just inside the shell below the arm
    // crown), pointing down and out; the dorsal side faces out of the ring, so the rest curl turns the tips in
    // toward the beak.
    public static Vector3 MouthRadial(in CephalopodSpec sp, int m)
    {
        float phi = 2f * Mathf.PI * m / Mathf.Max(1, sp.MouthCount);
        return new Vector3(Mathf.Sin(phi), 0f, Mathf.Cos(phi));
    }

    public static Vector3 MouthRoot(in CephalopodSpec sp, int m) =>
        MouthRadial(sp, m) * sp.MouthRing + new Vector3(0f, -0.3f * sp.HeadRadius, 0.05f * sp.HeadRadius);

    public static Vector3 MouthDir(in CephalopodSpec sp, int m) => OctoMath.Norm(MouthRadial(sp, m) * 0.55f + Vector3.down, Vector3.down);

    public static Quaternion MouthRot(in CephalopodSpec sp, int m) => OctoMath.Look(MouthDir(sp, m), MouthRadial(sp, m));

    // Eye e (0 left, 1 right): centre sunk into the bulge by 35% of its radius; +Z = gaze.
    public static void Eye(in CephalopodSpec sp, int e, out Vector3 centre, out Quaternion rot)
    {
        float phi = (e == 0 ? -EyePhiDeg : EyePhiDeg) * Mathf.Deg2Rad;
        Vector3 surf = ShellPoint(sp, EyeT, phi);
        Vector3 c = Axis(sp, EyeT);
        Vector3 outDir = OctoMath.Norm(surf - c, Vector3.forward);
        if (sp.eyeStalk > 0f)
        {
            // on a stalk: out from the head and toward the oral side (up, when the body hangs under its arms)
            Vector3 dir = EyeStalkDir(sp, e);
            centre = surf + dir * sp.eyeStalk;
            rot = OctoMath.Look(dir, Vector3.up);
            return;
        }
        centre = surf - outDir * (0.35f * sp.EyeRadius);
        rot = OctoMath.Look(outDir, Vector3.up);
    }

    // Eye e's stalk: its root on the head shell and its direction (unit).
    public static Vector3 EyeStalkRoot(in CephalopodSpec sp, int e) => ShellPoint(sp, EyeT, (e == 0 ? -EyePhiDeg : EyePhiDeg) * Mathf.Deg2Rad);

    public static Vector3 EyeStalkDir(in CephalopodSpec sp, int e)
    {
        Vector3 surf = EyeStalkRoot(sp, e), c = Axis(sp, EyeT);
        return OctoMath.Norm(OctoMath.Norm(surf - c, Vector3.forward) + new Vector3(0f, -0.35f, 0f), Vector3.forward);
    }

    // Siphon: a short funnel on the left side under the mantle opening; +Z = out along the funnel.
    public static void Siphon(in CephalopodSpec sp, out Vector3 basePoint, out Quaternion rot)
    {
        float phi = SiphonPhiDeg * Mathf.Deg2Rad;
        Vector3 surf = ShellPoint(sp, SiphonT, phi, 0.85f);
        Vector3 outDir = OctoMath.Norm(surf - Axis(sp, SiphonT), Vector3.left);
        Vector3 dir = OctoMath.Norm(outDir + new Vector3(0f, -0.35f, 0.45f), outDir);
        basePoint = surf;
        rot = OctoMath.Look(dir, Vector3.up);
    }

    // Axis parameter nearest to p (coarse search + refinement; construction time only).
    public static float NearestT(in CephalopodSpec sp, Vector3 p)
    {
        float best = 0f, bd = float.MaxValue;
        for (int i = 0; i <= 100; i++)
        {
            float t = i / 100f, d = (Axis(sp, t) - p).sqrMagnitude;
            if (d < bd) { bd = d; best = t; }
        }
        float lo = Mathf.Max(0f, best - 0.01f), hi = Mathf.Min(1f, best + 0.01f);
        for (int i = 0; i <= 40; i++)
        {
            float t = Mathf.Lerp(lo, hi, i / 40f), d = (Axis(sp, t) - p).sqrMagnitude;
            if (d < bd) { bd = d; best = t; }
        }
        return best;
    }
}
