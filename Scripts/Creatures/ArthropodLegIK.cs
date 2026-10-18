using System;
using UnityEngine;

// Per-leg data of ArthropodLegIK that does not change (built once per species leg, allocation-free solves).
// See LegSegment for the synergy model:  bend_i(s, beta) = rest_i + s * kappa_i + beta * sigma_i.
public struct ArthropodLegParams
{
    public float contactAngleDeg;   // tarsus to the ground tangent, degrees (0 = default 20)
    public float kneeLiftDeg;       // the swing's largest beta; the monotone check covers [0, kneeLiftDeg]
    public float sMinDeg, sMaxDeg;  // extra limits on s (extension < 0 < flexion); +-infinity = none
    public float elevMinDeg, elevMaxDeg; // in-plane pitch range reported as elevLimited (0, 0 = +-90)
    public int kneeJoint;           // point index reported as the knee (<= 0: the joint with the largest |kappa|)

    public static ArthropodLegParams Default => new ArthropodLegParams
    {
        contactAngleDeg = 20f, kneeLiftDeg = 0f, sMinDeg = float.NegativeInfinity, sMaxDeg = float.PositiveInfinity,
    };
}

public sealed class ArthropodLegModel
{
    public const float DefaultContactDeg = 20f;
    const int kGrid = 256;              // samples of r(s) for the monotone run
    const float kMinRunDeg = 5f;        // a usable s interval narrower than this is an authoring error
    const float kMinSlope = 0.25f;      // usable only where r(s) falls at least this fraction of its steepest rate

    public readonly int SegmentCount;   // including the tarsus
    public readonly int Solved;         // segments placed by the solver (all but the tarsus)
    public int PointCount => SegmentCount + 1; // coxa base, joints, ankle, foot
    public readonly float Total;        // sum of all lengths (scale 1)
    public readonly float TarsusLength;
    public readonly float SLo, SHi;     // usable s at beta = 0 (degrees): limits n knee range n monotone run
    public readonly float ReachLong, ReachShort; // coxa -> ankle distance at SLo / SHi, beta = 0 (scale 1)
    public readonly float CosC, SinC;   // contact angle
    public readonly float KneeLiftDeg, ElevMin, ElevMax; // ElevMin/Max in radians
    public readonly int KneeJoint;
    public readonly string Error;       // null when valid

    internal readonly float[] len, rest, kappa, sigma, lo, hi, tiltCos, tiltSin; // rest/kappa/sigma/lo/hi in degrees
    readonly float sMin, sMax;
    // scratch, main thread only (one solve at a time)
    internal readonly Vector3[] p, axes;

    public ArthropodLegModel(LegSegment[] segs, ArthropodLegParams prm)
    {
        int n = segs != null ? segs.Length : 0;
        SegmentCount = n; Solved = Mathf.Max(0, n - 1);
        len = new float[n]; rest = new float[n]; kappa = new float[n]; sigma = new float[n];
        lo = new float[n]; hi = new float[n]; tiltCos = new float[n]; tiltSin = new float[n];
        p = new Vector3[n + 1]; axes = new Vector3[n];
        float c = prm.contactAngleDeg > 0f ? prm.contactAngleDeg : DefaultContactDeg;
        CosC = Mathf.Cos(c * Mathf.Deg2Rad); SinC = Mathf.Sin(c * Mathf.Deg2Rad);
        KneeLiftDeg = prm.kneeLiftDeg;
        bool elev = prm.elevMinDeg != 0f || prm.elevMaxDeg != 0f;
        ElevMin = (elev ? prm.elevMinDeg : -90f) * Mathf.Deg2Rad; ElevMax = (elev ? prm.elevMaxDeg : 90f) * Mathf.Deg2Rad;
        sMin = float.IsNaN(prm.sMinDeg) ? float.NegativeInfinity : prm.sMinDeg;
        sMax = float.IsNaN(prm.sMaxDeg) ? float.PositiveInfinity : prm.sMaxDeg;
        if (n < 3 || n > 8) { Error = "an arthropod leg needs 3..8 segments"; return; }
        float best = 0f; int knee = 1;
        for (int i = 0; i < n; i++)
        {
            LegSegment s = segs[i];
            if (!(s.length > 0f)) { Error = $"segment {i}: length must be > 0"; return; }
            len[i] = s.length; Total += s.length;
            rest[i] = s.restBendDeg; kappa[i] = s.flexWeight; sigma[i] = s.liftWeight;
            lo[i] = s.minBendDeg; hi[i] = s.maxBendDeg;
            tiltCos[i] = Mathf.Cos(s.axisTiltDeg * Mathf.Deg2Rad); tiltSin[i] = Mathf.Sin(s.axisTiltDeg * Mathf.Deg2Rad);
            if (i < Solved)
            {
                if (!(lo[i] <= rest[i] && rest[i] <= hi[i])) { Error = $"segment {i}: rest bend {rest[i]} outside [{lo[i]}, {hi[i]}]"; return; }
                if (Mathf.Abs(kappa[i]) > best) { best = Mathf.Abs(kappa[i]); knee = i; }
            }
        }
        TarsusLength = len[n - 1];
        if (best <= 0f) { Error = "no segment takes part in the reach synergy (all flexWeight 0)"; return; }
        KneeJoint = prm.kneeJoint > 0 && prm.kneeJoint < n ? prm.kneeJoint : Mathf.Max(1, knee);

        // usable s: joint limits n [sMin, sMax], clipped to the strictly monotone run of r(s) around s = 0
        // at beta = 0 and at the swing's largest beta (one grid cell of margin each side)
        float a, b;
        LimitRange(0f, out a, out b);
        if (!(a < 0f && 0f < b)) { Error = "the rest pose sits on a joint limit (s = 0 must be inside the usable range)"; return; }
        Clip(0f, ref a, ref b);
        if (KneeLiftDeg != 0f)
        {
            LimitRange(KneeLiftDeg, out float la, out float lb);
            float ca = Mathf.Max(a, la), cb = Mathf.Min(b, lb);
            if (ca < cb) { Clip(KneeLiftDeg, ref ca, ref cb); a = Mathf.Max(a, ca); b = Mathf.Min(b, cb); }
        }
        if (!(a < 0f && 0f < b) || b - a < kMinRunDeg)
        {
            Error = $"the reach synergy is not monotone around the rest pose (usable s [{a:F1}, {b:F1}] deg): check flexWeight signs";
            return;
        }
        SLo = a; SHi = b;
        ReachLong = Reach(SLo, 0f); ReachShort = Reach(SHi, 0f);
    }

    // s interval allowed by the joint limits and [sMin, sMax] at a given beta.
    internal void LimitRange(float beta, out float a, out float b)
    {
        a = sMin; b = sMax;
        for (int i = 0; i < Solved; i++)
        {
            float k = kappa[i];
            if (Mathf.Abs(k) < 1e-6f) continue;
            float bse = rest[i] + beta * sigma[i];
            float x = (lo[i] - bse) / k, y = (hi[i] - bse) / k;
            a = Mathf.Max(a, Mathf.Min(x, y)); b = Mathf.Min(b, Mathf.Max(x, y));
        }
        if (float.IsInfinity(a)) a = -180f;
        if (float.IsInfinity(b)) b = 180f;
    }

    // Clips [a, b] to the run of grid cells around s = 0 where r(s) falls strictly AND steeply: at least
    // kMinSlope of the run's steepest fall. Where r(s) flattens (the leg near full stretch, or folding back on
    // itself) a small foot move needs a large change of s - every joint would race, as a two-bone leg snaps
    // at full stretch - so the leg counts as out of reach there instead.
    void Clip(float beta, ref float a, ref float b)
    {
        float step = (b - a) / kGrid;
        var r = new float[kGrid + 1];                 // construction only
        for (int i = 0; i <= kGrid; i++) r[i] = Reach(a + step * i, beta);
        int i0 = Mathf.Clamp(Mathf.FloorToInt(-a / step), 0, kGrid - 1); // cell holding s = 0
        int lo = i0, hi = i0;                         // cells [lo, hi]: cell i spans samples i..i+1
        if (!(r[i0 + 1] < r[i0])) { a = b = 0f; return; }
        while (hi + 1 < kGrid && r[hi + 2] < r[hi + 1]) hi++;
        while (lo > 0 && r[lo] < r[lo - 1]) lo--;
        float steep = 0f;
        for (int i = lo; i <= hi; i++) steep = Mathf.Max(steep, r[i] - r[i + 1]);
        while (lo < i0 && r[lo] - r[lo + 1] < kMinSlope * steep) lo++;
        while (hi > i0 && r[hi] - r[hi + 1] < kMinSlope * steep) hi--;
        float na = a + step * Mathf.Min(lo + 1, i0), nb = a + step * Mathf.Max(hi, i0 + 1);   // one cell of margin
        a = na; b = nb;
    }

    internal float Reach(float s, float beta) { FK(s, beta, out Vector3 e, out _); return e.magnitude; }

    // Forward kinematics of the solved chain in the leg's local frame (x outward, y up, z = x cross y) at
    // pitch 0: fills p[0..Solved], axes[0..Solved-1]; returns the ankle e and de/ds (per degree of s).
    internal void FK(float s, float beta, out Vector3 e, out Vector3 dEds)
    {
        Vector3 d = new Vector3(1f, 0f, 0f), a = new Vector3(0f, 0f, 1f), q = Vector3.zero;
        p[0] = q;
        for (int i = 0; i < Solved; i++)
        {
            if (tiltSin[i] != 0f) a = a * tiltCos[i] + Vector3.Cross(d, a) * tiltSin[i];
            float th = (rest[i] + s * kappa[i] + beta * sigma[i]) * Mathf.Deg2Rad;
            d = d * Mathf.Cos(th) + Vector3.Cross(a, d) * Mathf.Sin(th);
            axes[i] = a;
            q += d * len[i];
            p[i + 1] = q;
        }
        e = q;
        Vector3 de = Vector3.zero;
        for (int i = 0; i < Solved; i++)
            if (kappa[i] != 0f) de += Vector3.Cross(axes[i], e - p[i]) * (kappa[i] * Mathf.Deg2Rad);
        dEds = de;
    }

    // The rest pose's foot relative to the coxa base on level ground (x outward, y up), scale 1.
    public Vector2 RestFootLocal()
    {
        FK(0f, 0f, out Vector3 e, out _);
        return new Vector2(e.x + TarsusLength * CosC, e.y - TarsusLength * SinC);
    }
}

public struct ArthropodLegPose
{
    public float s, beta, yawDeg, pitchDeg; // yaw: Vector3.SignedAngle(rest direction, leg direction, up)
    public bool clamped;                    // the foot was out of reach (the foot point moved)
    public bool elevLimited, tarsusLimited; // pitch or the tarsus joint past its range (step triggers)
    public Vector3 planeNormal;             // the leg plane's normal (world): bones' hinge reference
}

// Synergy ("shape template") IK for N-segment arthropod legs. Stateless: the same inputs give the same pose,
// so it cannot pop or flip; the reach synergy is strictly monotone on the usable s interval (one root), yaw
// and pitch are closed-form. Allocation-free; main thread only (the model holds scratch).
public static class ArthropodLegIK
{
    const int kMaxIter = 16;
    const float kTol = 1e-6f;         // x Total: reach tolerance of the solve
    const float kYawBlend = 0.1f;     // x Total x scale: below this horizontal foot distance the yaw eases to rest
    const float kYawBehind = 135f;    // degrees: a foot further round than this behind the rest direction eases the yaw to rest

    // Writes PointCount points into pts[offset..]: coxa base, the joints, the ankle, the foot.
    // up: the leg's up (gravity or the climbing up field); restYawDir: the leg's rest outward direction (world);
    // footNormal: the ground's normal at the foot; scale: the creature's size scale.
    public static void Solve(ArthropodLegModel m, Vector3 coxa, Vector3 foot, Vector3 footNormal, Vector3 up,
                             Vector3 restYawDir, float beta, float scale, Vector3[] pts, int offset, out ArthropodLegPose pose)
    {
        pose = default;
        pose.beta = beta;
        float u2 = up.sqrMagnitude;
        Vector3 U = u2 > 1e-12f ? up / Mathf.Sqrt(u2) : Vector3.up;
        Vector3 rh = restYawDir - U * Vector3.Dot(restYawDir, U);
        if (rh.sqrMagnitude < 1e-10f) rh = Vector3.Cross(U, Mathf.Abs(U.x) < 0.9f ? Vector3.right : Vector3.forward);
        rh.Normalize();

        // yaw toward the foot, in angle space: eased to the rest direction near the coxa's axis (kYawBlend) and
        // for a foot almost straight behind the rest direction (|angle| 135..180 deg), so the yaw is continuous
        // everywhere - a foot circling the coxa's axis never spins the leg through a flip
        Vector3 h = foot - coxa; h -= U * Vector3.Dot(h, U);
        float hl = h.magnitude;
        float yawTo = hl > 1e-9f ? Vector3.SignedAngle(rh, h, U) : 0f;
        float w = Mathf.SmoothStep(0f, 1f, hl / (kYawBlend * m.Total * scale))
                * (1f - Mathf.SmoothStep(0f, 1f, (Mathf.Abs(yawTo) - kYawBehind) / (180f - kYawBehind)));
        float th = yawTo * w * Mathf.Deg2Rad;
        Vector3 X = (rh * Mathf.Cos(th) + Vector3.Cross(U, rh) * Mathf.Sin(th)).normalized;
        Vector3 Z = Vector3.Cross(X, U).normalized;

        // tarsus: in the plane of X and the ground normal, at the contact angle below the ground tangent
        float n2 = footNormal.sqrMagnitude;
        Vector3 nrm = n2 > 1e-12f ? footNormal / Mathf.Sqrt(n2) : U;
        Vector3 gt = X - nrm * Vector3.Dot(X, nrm);
        if (gt.sqrMagnitude < 1e-8f) gt = X;
        gt.Normalize();
        Vector3 tdir = gt * m.CosC - nrm * m.SinC;
        Vector3 ankle = foot - tdir * (m.TarsusLength * scale);

        Vector3 v = (ankle - coxa) / scale;
        Vector3 t = new Vector3(Vector3.Dot(v, X), Vector3.Dot(v, U), Vector3.Dot(v, Z));
        float d = t.magnitude;

        float lo, hi, rLo, rHi;
        if (beta == 0f) { lo = m.SLo; hi = m.SHi; rLo = m.ReachLong; rHi = m.ReachShort; }
        else
        {
            m.LimitRange(beta, out lo, out hi);
            lo = Mathf.Max(lo, m.SLo); hi = Mathf.Min(hi, m.SHi);
            if (hi < lo) { float mid = 0.5f * (lo + hi); lo = hi = mid; }
            rLo = m.Reach(lo, beta); rHi = m.Reach(hi, beta);
        }
        float tol = kTol * m.Total;
        float s;
        Vector3 e, de;
        if (d >= rLo) { s = lo; pose.clamped = d > rLo + tol; m.FK(s, beta, out e, out de); }
        else if (d <= rHi) { s = hi; pose.clamped = d < rHi - tol; m.FK(s, beta, out e, out de); }
        else
        {
            float a = lo, b = hi;
            s = lo < 0f && 0f < hi ? 0f : 0.5f * (lo + hi);
            m.FK(s, beta, out e, out de);
            for (int it = 0; it < kMaxIter; it++)
            {
                float r = e.magnitude, f = r - d;
                if (Mathf.Abs(f) <= tol) break;
                if (f > 0f) a = s; else b = s;           // r falls as s rises: too long -> more flexion
                float deriv = r > 1e-9f ? Vector3.Dot(e, de) / r : 0f;
                float sn = deriv < 0f ? s - f / deriv : 0.5f * (a + b);
                s = sn > a && sn < b ? sn : 0.5f * (a + b);
                m.FK(s, beta, out e, out de);
            }
        }

        // pitch about local z to the target's elevation, then yaw about up onto its horizontal direction. The
        // pitched chain's outward coordinate takes the sign of the target's (a target behind the coxa pitches the
        // leg over rather than yawing it half a turn), so a planar leg never yaws here and nothing flips.
        float el = e.magnitude;
        Vector3 tt = d > 1e-9f ? t * (el / d) : e;
        float xe = Mathf.Sqrt(Mathf.Max(0f, tt.x * tt.x + tt.z * tt.z - e.z * e.z));
        if (tt.x < 0f) xe = -xe;
        float phi = Mathf.Atan2(tt.y, xe) - Mathf.Atan2(e.y, e.x);
        float psi = Mathf.Atan2(tt.z, tt.x) - Mathf.Atan2(e.z, xe);
        float cp = Mathf.Cos(phi), sp = Mathf.Sin(phi), cy = Mathf.Cos(psi), sy = Mathf.Sin(psi);

        for (int k = 0; k <= m.Solved; k++)
        {
            Vector3 q = m.p[k];
            float x1 = q.x * cp - q.y * sp, y1 = q.x * sp + q.y * cp, z1 = q.z;
            float x2 = x1 * cy - z1 * sy, z2 = x1 * sy + z1 * cy;
            pts[offset + k] = coxa + (X * x2 + U * y1 + Z * z2) * scale;
        }
        pts[offset] = coxa;
        Vector3 Xf = X * cy + Z * sy;
        pose.planeNormal = Z * cy - X * sy;
        Vector3 ankleOut = pts[offset + m.Solved];
        Vector3 footOut = pose.clamped ? ankleOut + tdir * (m.TarsusLength * scale) : foot;
        pts[offset + m.Solved + 1] = footOut;

        pose.s = s; pose.pitchDeg = phi * Mathf.Rad2Deg;
        pose.yawDeg = Vector3.SignedAngle(rh, Xf, U);
        pose.elevLimited = phi < m.ElevMin || phi > m.ElevMax;
        Vector3 dPrev = ankleOut - pts[offset + m.Solved - 1], dTar = footOut - ankleOut;
        float tb = Vector3.SignedAngle(dPrev, dTar, pose.planeNormal);
        int ti = m.SegmentCount - 1;
        pose.tarsusLimited = tb < m.lo[ti] || tb > m.hi[ti];
    }

    // +Z along the segment, +Y in the leg plane on the raised side (planeNormal x dir).
    public static Quaternion SegmentRotation(Vector3 from, Vector3 to, Vector3 planeNormal)
    {
        Vector3 dir = to - from;
        if (dir.sqrMagnitude < 1e-12f) return Quaternion.identity;
        dir.Normalize();
        Vector3 up = Vector3.Cross(planeNormal, dir);
        if (up.sqrMagnitude < 1e-10f) up = Vector3.ProjectOnPlane(Vector3.up, dir);
        if (up.sqrMagnitude < 1e-10f) up = Vector3.ProjectOnPlane(Vector3.right, dir);
        return Quaternion.LookRotation(dir, up.normalized);
    }

    // One bone per segment: dst[first + k] = TRS(point k, SegmentRotation, scale).
    public static void WriteBones(Matrix4x4[] dst, int first, Vector3[] pts, int offset, int segmentCount, Vector3 planeNormal, float scale)
    {
        Vector3 sc = new Vector3(scale, scale, scale);
        for (int k = 0; k < segmentCount; k++)
            dst[first + k] = Matrix4x4.TRS(pts[offset + k], SegmentRotation(pts[offset + k], pts[offset + k + 1], planeNormal), sc);
    }
}
