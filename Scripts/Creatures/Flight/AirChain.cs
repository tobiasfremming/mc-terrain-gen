using UnityEngine;

// The spine of a flying creature (design section 3, "Air chain"). The girdle (FlightModel's point) leads:
//   trunk + tail: a 3D pull chain from the girdle back. Each joint is dragged toward its predecessor at the
//     exact segment length, its direction kept inside a 3D bend cone around the previous segment's. The
//     first segment follows the flight direction with a flying-snake yaw wave (airUndulation), the rest
//     inherits it down the chain; a gravity sag (tailSag, growing toward the tail) lets the tail trail.
//     The shoulder bob of the wingbeat travels down the body as a lagging, decaying wave.
//   neck + head: two anchors, the girdle and a low-passed head target ahead of it, joined by a curve the
//     neck joints are laid along from the girdle out (exact lengths, bend cones), so the head glides
//     while the shoulders bob (headStabilise); the head may trail its target slightly.
//   frames: forward toward the head; up starts at the girdle as the banked up and is carried along the
//     chain by projection (parallel transport for the small per-joint bends), then Rotations = (fwd, up).
// Pure C#, flat arrays, allocation-free after construction, deterministic.
public sealed class AirChain
{
    const float kNeckSlack = 0.97f;     // head target at this fraction of the straight neck length
    const float kDriftRate = 1f;        // 1/s: the head stabiliser's pull back onto the girdle

    readonly int n, girdle;
    readonly float seg, timeScale, scale;
    readonly float[] cosLimit, sinLimit; // per joint bend cone (rigid = 0)
    readonly FlightSpec f;
    public readonly Vector3[] Up;
    public readonly Vector3[] Forward;
    public float Time { get; private set; }

    // head stabiliser: a smoothed girdle (position + velocity), no lag in steady flight
    Vector3 smoothPos, smoothVel, smoothAcc;
    public Vector3 HeadTarget { get; private set; }

    // The twisting chain (FlightSpec.twist enabled: the dragon): the body follows the head's path (AirTwist)
    // instead of trailing the girdle. Null otherwise - the pull chain below runs exactly as before.
    public readonly AirTwist Twist;

    public AirChain(CreatureSpecies sp, FlightSpec flight, int girdleJoint, float sizeScale = 1f)
    {
        f = flight;
        n = sp.JointCount; girdle = Mathf.Clamp(girdleJoint, 0, n - 1);
        scale = Mathf.Max(1e-3f, sizeScale); timeScale = Mathf.Sqrt(scale);
        seg = sp.spacing * scale;
        cosLimit = new float[n]; sinLimit = new float[n];
        for (int i = 0; i < n; i++)
        {
            float a = sp.joints[i].rigid ? 0f : Mathf.Max(0f, sp.joints[i].bendLimitDeg) * Mathf.Deg2Rad;
            cosLimit[i] = Mathf.Cos(a); sinLimit[i] = Mathf.Sin(a);
        }
        Up = new Vector3[n]; Forward = new Vector3[n];
        if (flight.twist.Enabled) Twist = new AirTwist(flight.twist, n, seg, scale, cosLimit, sinLimit, girdle);
    }

    public float SegmentLength => seg;
    public int Girdle => girdle;

    // Start the stabiliser at the current girdle state (call before the first Step).
    public void Reset(Vector3 girdlePos, Vector3 girdleVel)
    {
        smoothPos = girdlePos; smoothVel = girdleVel; smoothAcc = Vector3.zero; Time = 0f;
        HeadTarget = girdlePos;
        Twist?.Reset();
    }

    public void Step(float h, Vector3[] joints, Vector3 girdlePos, Vector3 girdleVel, Vector3 bankedUp, Vector3 worldUp)
    {
        if (!(h > 0f)) return;
        Time += h;
        Vector3 up = Norm(worldUp, Vector3.up);
        if (Twist != null) { TwistStep(h, joints, girdlePos, girdleVel, bankedUp, up); return; }
        joints[girdle] = girdlePos;

        // ---- trunk + tail: pull chain from the girdle back
        Vector3 vh = Norm(girdleVel, Norm(joints[girdle] - joints[Mathf.Min(girdle + 1, n - 1)], Vector3.forward));
        Vector3 side = Vector3.Cross(vh, up);
        side = side.sqrMagnitude > 1e-10f ? side.normalized : Vector3.right;
        float und = f.airUndulationDeg * Mathf.Deg2Rad * Mathf.Sin(2f * Mathf.PI * f.airUndulationHz / timeScale * Time);
        Vector3 leader = -(vh * Mathf.Cos(und) + side * Mathf.Sin(und));
        float sag = f.tailSag * timeScale * h;
        Vector3 prevDir = leader;
        for (int i = girdle + 1; i < n; i++)
        {
            joints[i] -= up * (sag * (i - girdle) / n);
            Vector3 d = Norm(joints[i] - joints[i - 1], prevDir);
            // bend at joint i-1 (the leader segment: the cone of the girdle joint around the flight line)
            d = Cone(prevDir, d, cosLimit[i - 1], sinLimit[i - 1]);
            joints[i] = joints[i - 1] + d * seg;
            prevDir = d;
        }

        // ---- neck + head: laid from the girdle toward the stabilised head target
        if (girdle > 0)
        {
            // The girdle velocity through a critically damped 2nd-order low-pass (the bob is a velocity
            // oscillation at the beat frequency), integrated, plus a slow pull back onto the girdle so the
            // target cannot drift: no lag in steady flight or a steady climb, the beat's bob filtered out.
            float tau = Mathf.Max(1e-4f, f.headStabilise * timeScale);
            float w0 = 1f / tau, hk = Mathf.Min(h, 0.5f * tau);
            smoothAcc += (w0 * w0 * (girdleVel - smoothVel) - 2f * w0 * smoothAcc) * hk;
            smoothVel += smoothAcc * hk;
            smoothPos += smoothVel * h;
            smoothPos += (girdlePos - smoothPos) * Mathf.Min(1f, kDriftRate / timeScale * h);
            Vector3 dir = Norm(smoothVel, -leader);
            Vector3 target = smoothPos + dir * (kNeckSlack * seg * girdle);
            HeadTarget = target;
            Vector3 trunkDir = n > girdle + 1 ? Norm(joints[girdle] - joints[girdle + 1], dir) : dir;
            // Two anchors: the girdle (leaving along the trunk) and the head target (arriving along the
            // stabilised flight line), joined by a quadratic Bezier. Joints are placed from the girdle out,
            // each at the exact segment length toward its sample on the curve, inside its bend cone; a rigid
            // joint keeps its two segments in line. (A plain FABRIK buckles into a zigzag against the rigid
            // head joint; this cannot, and costs one pass.)
            Vector3 ctrl = girdlePos + trunkDir * (0.5f * seg * girdle);
            Vector3 pd = trunkDir;
            for (int i = girdle - 1; i >= 0; i--)
            {
                float t = (girdle - i) / (float)girdle, it = 1f - t;
                Vector3 aim = it * it * girdlePos + 2f * it * t * ctrl + t * t * target;
                Vector3 d = Norm(aim - joints[i + 1], pd);
                d = Cone(pd, d, cosLimit[i + 1], sinLimit[i + 1]);
                joints[i] = joints[i + 1] + d * seg;
                pd = d;
            }
        }

        UpdateFrames(joints, bankedUp);
    }

    // The twisting chain: the same head stabiliser (the beat's bob filtered out of the girdle's motion), the head
    // on the plain flight line a neck ahead of it, and AirTwist lays the body along the head's path.
    void TwistStep(float h, Vector3[] joints, Vector3 girdlePos, Vector3 girdleVel, Vector3 bankedUp, Vector3 up)
    {
        float tau = Mathf.Max(1e-4f, f.headStabilise * timeScale);
        float w0 = 1f / tau, hk = Mathf.Min(h, 0.5f * tau);
        smoothAcc += (w0 * w0 * (girdleVel - smoothVel) - 2f * w0 * smoothAcc) * hk;
        smoothVel += smoothAcc * hk;
        smoothPos += smoothVel * h;
        smoothPos += (girdlePos - smoothPos) * Mathf.Min(1f, kDriftRate / timeScale * h);
        // the neck lies level ahead of the girdle (a climb shows in the head's path, not as a pitched neck: at the
        // launch the head then starts where the grounded head is)
        Vector3 dir = Norm(smoothVel, Norm(joints[0] - joints[Mathf.Min(1, n - 1)], Vector3.forward));
        Vector3 level = Norm(dir - up * Vector3.Dot(dir, up), Norm(joints[0] - joints[Mathf.Min(1, n - 1)], Vector3.forward));
        Vector3 head = smoothPos + level * (seg * girdle);
        // the wingbeat's bob: the girdle against its stabilised path (the head path never bobs; AirTwist puts it back
        // on the body, girdleBob)
        Twist.ShoulderBob = Vector3.Dot(girdlePos - smoothPos, up);
        HeadTarget = head;
        Twist.Step(h, joints, head, dir, up, bankedUp, smoothVel.magnitude, Forward, Up);
    }

    // Forward (toward the head) and up per joint; up starts as the banked up at the girdle and is carried
    // along the chain both ways.
    internal void UpdateFrames(Vector3[] joints, Vector3 bankedUp)
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 fw = i > 0 ? joints[i - 1] - joints[i] : joints[0] - joints[Mathf.Min(1, n - 1)];
            Forward[i] = Norm(fw, i > 0 ? Forward[i - 1] : Vector3.forward);
        }
        Vector3 u0 = Ortho(bankedUp, Forward[girdle], Vector3.up);
        Up[girdle] = u0;
        for (int i = girdle + 1; i < n; i++) Up[i] = Ortho(Up[i - 1], Forward[i], u0);
        for (int i = girdle - 1; i >= 0; i--) Up[i] = Ortho(Up[i + 1], Forward[i], u0);
    }

    public void WriteRotations(Quaternion[] rotations)
    {
        for (int i = 0; i < n; i++) rotations[i] = CreatureSim.Basis(Forward[i], Up[i]);
    }

    // d limited to the cone of half-angle acos(c) around the unit axis a.
    static Vector3 Cone(Vector3 a, Vector3 d, float c, float s)
    {
        float ca = Vector3.Dot(a, d);
        if (ca >= c) return d;
        Vector3 perp = d - a * ca;
        float m = perp.magnitude;
        if (m < 1e-4f)
        {
            // (nearly) straight back: pick any perpendicular (a nearly antiparallel d leaves perp as rounding
            // noise, not perpendicular to a, and dividing by its length broke the segment length)
            perp = Vector3.Cross(a, Mathf.Abs(a.y) < 0.9f ? Vector3.up : Vector3.right);
            m = perp.magnitude;
        }
        Vector3 r = a * c + perp * (s / m);
        float rm = r.magnitude;
        return rm > 1e-6f ? r / rm : a;
    }

    static Vector3 Ortho(Vector3 u, Vector3 f, Vector3 fallback)
    {
        Vector3 o = u - f * Vector3.Dot(u, f);
        if (o.sqrMagnitude < 1e-10f) o = fallback - f * Vector3.Dot(fallback, f);
        if (o.sqrMagnitude < 1e-10f) o = Vector3.Cross(f, Vector3.right);
        return o.normalized;
    }

    static Vector3 Norm(Vector3 v, Vector3 fallback)
    {
        float m = v.x * v.x + v.y * v.y + v.z * v.z;
        if (!(m > 1e-12f)) return fallback;
        float inv = 1f / Mathf.Sqrt(m);
        return new Vector3(v.x * inv, v.y * inv, v.z * inv);
    }
}
