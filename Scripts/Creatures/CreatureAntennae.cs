using CreatureKit;
using UnityEngine;

// The spring chains of a species' antennae (CreatureSpecies.antennae, the kit's Antenna part): whiskers, barbels,
// feelers. The kit's part runtime of every Antenna (IPartRuntime, stage AfterLimbs: stepped by CreatureSim once per
// sub-step after the body, ground and air modes alike), one system of flat arrays for all of a species' strands.
// Per point (position-based, semi-implicit):
//   a = w^2 (rest target - x) - 2 zeta w (v - v_target) - drag v    (still air: the drag trails the strand;
//                                                                    damped against the target's own motion)
//   w = 2 pi stiffnessHz / sqrt(s) x (1 - 0.5 k / N)                  (stiffest at the root)
// then exact segment lengths from the root out, each segment inside a bend cone around the previous one (the
// first around the root direction). The rest target is the rest curve (direction, droop) in the root joint's
// frame. Froude-scaled: lengths x s, rates / sqrt(s). Allocation-free after construction; main thread.
// Probing (probeDeg > 0): the rest curve itself sweeps about the root - side to side by probeDeg (mirrored, so the
// pair sweeps in and out together), up and down by 0.6 x it at 1.37 x the rate, the right strand a quarter cycle
// behind the left - so the antennae keep feeling ahead, walking or still. probeDeg 0: the curve is still (bit-identical).
public sealed class CreatureAntennae : IPartRuntime
{
    const float kTipSoft = 0.5f;    // the pull falls off by this fraction toward the tip

    readonly AntennaSpec[] specs;
    readonly int[] first;           // first point of antenna a in the flat arrays (N + 1 points each)
    readonly Vector3[] local;       // rest curve points in the joint frame, scale 1 (offset included)
    readonly Vector3[] pts, prev, targetPrev;
    readonly float[] cosBend, sinBend;
    readonly Vector3[] probeAxis;   // per antenna: the up-down sweep's axis (joint frame), 0 = not probing
    float clock;
    readonly float scale, T;
    readonly CreatureSpecies sp;          // the runtime species (bone layout); null: bones not written (tests)
    readonly Vector3[] joints;            // the sim's arrays (stepped and posed from them)
    readonly Quaternion[] rotations;
    public int Count => specs.Length;

    public CreatureAntennae(CreatureSpecies sp, Vector3[] joints, Quaternion[] rotations, float sizeScale)
        : this(sp.antennae, joints, rotations, sizeScale) { this.sp = sp; }

    public CreatureAntennae(AntennaSpec[] antennae, Vector3[] joints, Quaternion[] rotations, float sizeScale)
    {
        specs = antennae;
        this.joints = joints; this.rotations = rotations;
        scale = Mathf.Max(1e-3f, sizeScale); T = Mathf.Sqrt(scale);
        first = new int[specs.Length];
        int total = 0;
        for (int a = 0; a < specs.Length; a++) { first[a] = total; total += specs[a].Segments + 1; }
        local = new Vector3[total]; pts = new Vector3[total]; prev = new Vector3[total]; targetPrev = new Vector3[total];
        cosBend = new float[specs.Length]; sinBend = new float[specs.Length];
        probeAxis = new Vector3[specs.Length];
        for (int a = 0; a < specs.Length; a++)
        {
            AntennaSpec s = specs[a];
            int N = s.Segments, o = first[a];
            Vector3 off = s.offset, d0 = s.direction; off.x *= Mirror(s); d0.x *= Mirror(s);
            d0 = d0.sqrMagnitude > 1e-10f ? d0.normalized : Vector3.forward;
            Vector3 axis = Vector3.Cross(d0, Vector3.down);
            axis = axis.sqrMagnitude > 1e-8f ? axis.normalized : Vector3.right;
            float seg = s.length / N;
            local[o] = off;
            for (int k = 1; k <= N; k++)
            {
                // the rest curve droops toward the tip (each segment turned down a little more)
                Vector3 dk = Quaternion.AngleAxis(s.droopDeg * (k - 0.5f) / N, axis) * d0;
                local[o + k] = local[o + k - 1] + dk * seg;
            }
            float b = s.MaxBend * Mathf.Deg2Rad;
            cosBend[a] = Mathf.Cos(b); sinBend[a] = Mathf.Sin(b);
            if (s.probeDeg > 0f) probeAxis[a] = axis;
        }
        Reset(joints, rotations);
    }

    static float Mirror(in AntennaSpec s) => s.side < 0 ? -1f : 1f;

    // Every strand on its rest curve, at rest (construction, teleports).
    public void Reset(Vector3[] joints, Quaternion[] rotations)
    {
        for (int a = 0; a < specs.Length; a++)
        {
            int N = specs[a].Segments, o = first[a];
            Quaternion r = rotations[specs[a].joint];
            Vector3 j = joints[specs[a].joint];
            for (int k = 0; k <= N; k++) { pts[o + k] = j + r * (local[o + k] * scale); prev[o + k] = pts[o + k]; targetPrev[o + k] = pts[o + k]; }
        }
    }

    // The probing turn of antenna a now (joint frame, about its root); identity when it does not probe.
    public Quaternion Probe(int a)
    {
        AntennaSpec s = specs[a];
        if (!(s.probeDeg > 0f)) return Quaternion.identity;
        float w = 2f * Mathf.PI * s.probeHz / T * clock, lag = s.side > 0 ? 0.5f * Mathf.PI : 0f;
        float yaw = Mirror(s) * s.probeDeg * Mathf.Sin(w + lag);
        float pitch = 0.6f * s.probeDeg * Mathf.Sin(1.37f * w + 2f * lag + 0.7f);
        return Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, probeAxis[a]);
    }

    public void Step(float h, Vector3[] joints, Quaternion[] rotations)
    {
        if (!(h > 0f)) return;
        clock += h;
        for (int a = 0; a < specs.Length; a++)
        {
            AntennaSpec s = specs[a];
            int N = s.Segments, o = first[a];
            Quaternion r = rotations[s.joint];
            Vector3 j = joints[s.joint];
            Vector3 root = j + r * (local[o] * scale);
            bool probing = s.probeDeg > 0f;
            Quaternion pq = probing ? Probe(a) : Quaternion.identity;
            float w0 = 2f * Mathf.PI * s.stiffnessHz / T, zeta = Mathf.Max(0f, s.damping), drag = Mathf.Max(0f, s.airDrag) / T;
            pts[o] = root; prev[o] = root;
            for (int k = 1; k <= N; k++)
            {
                int i = o + k;
                Vector3 target = probing ? j + r * ((local[o] + pq * (local[i] - local[o])) * scale) : j + r * (local[i] * scale);
                Vector3 x = pts[i];
                Vector3 v = (x - prev[i]) / h;
                Vector3 vTarget = (target - targetPrev[i]) / h;
                targetPrev[i] = target;
                float w = w0 * (1f - kTipSoft * k / N);
                Vector3 acc = (target - x) * (w * w) - (v - vTarget) * (2f * zeta * w) - v * drag;
                v += acc * h;
                prev[i] = x;
                pts[i] = x + v * h;
            }
            // exact lengths from the root out, inside the bend cones
            float seg = s.length / N * scale;
            Vector3 pd = probing ? r * (pq * (local[o + 1] - local[o])).normalized : r * (local[o + 1] - local[o]).normalized;
            for (int k = 1; k <= N; k++)
            {
                int i = o + k;
                Vector3 d = pts[i] - pts[i - 1];
                float m = d.magnitude;
                d = m > 1e-9f ? d / m : pd;
                d = Cone(pd, d, cosBend[a], sinBend[a]);
                pts[i] = pts[i - 1] + d * seg;
                pd = d;
            }
        }
    }

    // ---- IPartRuntime (the sim's own Joints / Rotations arrays)
    public PartStage Stage => PartStage.AfterLimbs;
    public void Step(in StepCtx c, float dt) => Step(dt, joints, rotations);
    public void WriteBones(Matrix4x4[] dst, int offset) { if (sp != null) WriteBones(dst, offset, sp, rotations); }

    public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
    {
        for (int i = 0; i < pts.Length; i++) { lo = Vector3.Min(lo, pts[i]); hi = Vector3.Max(hi, pts[i]); }
    }

    public void DrawDebug(ICreatureDebugDraw d)
    {
        for (int a = 0; a < specs.Length; a++)
            for (int k = 0; k < specs[a].Segments; k++) d.Line(pts[first[a] + k], pts[first[a] + k + 1], new Color(0.9f, 0.6f, 1f));
    }

    // Bone k of antenna a: at point k, +Z along the segment, up transported from the root joint's up.
    public void WriteBones(Matrix4x4[] dst, int offset, CreatureSpecies sp, Quaternion[] rotations)
    {
        Vector3 sc = Vector3.one * scale;
        for (int a = 0; a < specs.Length; a++)
        {
            int N = specs[a].Segments, o = first[a];
            Vector3 u = rotations[specs[a].joint] * Vector3.up;
            for (int k = 0; k < N; k++)
            {
                Vector3 f = pts[o + k + 1] - pts[o + k];
                f = f.sqrMagnitude > 1e-18f ? f.normalized : rotations[specs[a].joint] * Vector3.forward;
                Vector3 up = u - f * Vector3.Dot(u, f);
                if (up.sqrMagnitude < 1e-10f) up = Vector3.Cross(f, Vector3.right);
                u = up.normalized;
                dst[offset + sp.AntennaBone(a, k)] = Matrix4x4.TRS(pts[o + k], CreatureSim.Basis(f, u), sc);
            }
        }
    }

    public int FirstPoint(int a) => first[a];
    public Vector3[] Points => pts;
    public AntennaSpec Spec(int a) => specs[a];

    // The tip's distance from where the rest curve puts it, metres (tests, debug).
    public float TipLag(int a, Vector3[] joints, Quaternion[] rotations)
    {
        int N = specs[a].Segments, o = first[a];
        Vector3 target = joints[specs[a].joint] + rotations[specs[a].joint] * (local[o + N] * scale);
        return (pts[o + N] - target).magnitude;
    }

    static Vector3 Cone(Vector3 a, Vector3 d, float c, float s)
    {
        float ca = Vector3.Dot(a, d);
        if (ca >= c) return d;
        Vector3 perp = d - a * ca;
        float m = perp.magnitude;
        if (m < 1e-9f)
        {
            perp = Vector3.Cross(a, Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.right);
            m = perp.magnitude;
        }
        return a * c + perp * (s / m);
    }
}
