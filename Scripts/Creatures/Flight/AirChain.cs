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
//     FlightSpec.airNeckStance (opt-in, a species bound at its stance): the cones are centred on that share
//     of the stance's own neck bends, so a steeply raised neck lowers toward the flight line by at most its
//     bend limits from them (its bind's bends were far outside the limits: the chest folded through the skin).
//     The head target stays on the flight line: a target raised toward the stance's head left the neck too
//     little slack and the head bobbed with the shoulders.
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
    // FlightSpec.airNeckStance: per neck joint, the cone's centre - the share of the stance's bend there (cos, sin of
    // the pitch turn toward the up); null = off (the shipped chain: the cones around a straight neck).
    readonly float[] restCos, restSin;
    readonly FlightSpec f;
    public readonly Vector3[] Up;
    public readonly Vector3[] Forward;
    public float Time { get; private set; }
    // Share of the flight path's climb or descent the trunk ignores (0 = it trails the path: the shipped chain).
    // FlightSpec.airLevel; a behaviour may change it at run time (a stoop dives along its path: 0).
    public float Level;

    // head stabiliser: a smoothed girdle (position + velocity), no lag in steady flight
    Vector3 smoothPos, smoothVel, smoothAcc;
    public Vector3 HeadTarget { get; private set; }
    // FlightSpec.headStabiliseBounded: the trunk's lead velocity (the girdle's, low-passed over kHeadingTau)
    Vector3 leadVel, leadAcc;

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
        Level = Mathf.Clamp01(flight.airLevel);
        float share = Mathf.Clamp01(flight.airNeckStance);
        if (share > 0f && girdle > 0)
        {
            // the stance: each segment spacing long between its joints' clearances (CreatureSim.RestRun), pitched
            // toward the head by p[k] (segment k = joint k-1 to k); the bend at joint j turns p[j+1] into p[j]
            var p = new float[n];
            for (int k = 1; k < n; k++)
                p[k] = Mathf.Atan2(sp.joints[k - 1].clearance - sp.joints[k].clearance, CreatureSim.RestRun(sp, k, sp.spacing, 1f));
            restCos = new float[n]; restSin = new float[n];
            for (int j = 0; j < n; j++)
            {
                float a = j >= 1 && j <= girdle && j + 1 < n ? share * (p[j] - p[j + 1]) : 0f;
                restCos[j] = Mathf.Cos(a); restSin[j] = Mathf.Sin(a);
            }
        }
        if (flight.twist.Enabled) Twist = new AirTwist(flight.twist, n, seg, scale, cosLimit, sinLimit, girdle);
    }

    public float SegmentLength => seg;
    public int Girdle => girdle;

    // Start the stabiliser at the current girdle state (call before the first Step).
    public void Reset(Vector3 girdlePos, Vector3 girdleVel)
    {
        smoothPos = girdlePos; smoothVel = girdleVel; smoothAcc = Vector3.zero; Time = 0f;
        leadVel = girdleVel; leadAcc = Vector3.zero;
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
        // (Level > 0: the trunk leads along the flight line with its climb or descent taken out by that share - a
        // short-bodied flyer holds its body level in a glide or a flare instead of hanging along a steep path)
        Vector3 gv = Level > 0f ? girdleVel - up * (Vector3.Dot(girdleVel, up) * Level) : girdleVel;
        if (f.headStabiliseBounded)
        {
            // the trunk leads along the girdle's velocity low-passed over kHeadingTau: the containment's cut of the
            // outward speed turned the velocity up to 90 deg in one sub-step, and the trunk (and the neck laid from
            // it) with it
            float tl = Mathf.Max(1e-4f, kHeadingTau * timeScale);
            float wl = 1f / tl, kl = Mathf.Min(h, 0.5f * tl);
            leadAcc += (wl * wl * (gv - leadVel) - 2f * wl * leadAcc) * kl;
            leadVel += leadAcc * kl;
            gv = leadVel;
        }
        Vector3 vh = Norm(gv, Norm(joints[girdle] - joints[Mathf.Min(girdle + 1, n - 1)], Vector3.forward));
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
            if (f.headStabiliseBounded) StabiliseBounded(h, girdlePos, girdleVel, up);
            else
            {
                float tau = Mathf.Max(1e-4f, f.headStabilise * timeScale);
                float w0 = 1f / tau, hk = Mathf.Min(h, 0.5f * tau);
                smoothAcc += (w0 * w0 * (girdleVel - smoothVel) - 2f * w0 * smoothAcc) * hk;
                smoothVel += smoothAcc * hk;
                smoothPos += smoothVel * h;
                smoothPos += (girdlePos - smoothPos) * Mathf.Min(1f, kDriftRate / timeScale * h);
            }
            Vector3 dir = Norm(smoothVel, -leader);
            Vector3 trunkDir = n > girdle + 1 ? Norm(joints[girdle] - joints[girdle + 1], dir) : dir;
            // FlightSpec.headStabiliseBounded: the head looks at most kMaxHeadTurnDeg off the trunk's line (in the neck's reach)
            if (f.headStabiliseBounded) dir = Cone(trunkDir, dir, kMaxHeadTurnCos, kMaxHeadTurnSin);
            Vector3 target = smoothPos + dir * (kNeckSlack * seg * girdle);
            // the neck's sagittal plane: across it the girdle's banked side (airNeckStance turns the neck up in it)
            Vector3 across = restCos != null ? Norm(Vector3.Cross(vh, bankedUp), side) : side;
            HeadTarget = target;
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
                // the cone's centre: straight on, or (airNeckStance) the stance's share of the bend at joint i + 1
                Vector3 axis = pd;
                if (restCos != null && restSin[i + 1] != 0f)
                    axis = Norm(pd * restCos[i + 1] + Norm(Vector3.Cross(across, pd), up) * restSin[i + 1], pd);
                d = Cone(axis, d, cosLimit[i + 1], sinLimit[i + 1]);
                joints[i] = joints[i + 1] + d * seg;
                pd = d;
            }
        }

        UpdateFrames(joints, bankedUp);
    }

    // FlightSpec.headStabiliseBounded: the stabiliser split along up. The bob (a vertical oscillation of the girdle's
    // velocity) goes through the headStabilise low-pass as before; the horizontal velocity - the turns, and the
    // containment's cut of the outward speed - through a short one (kHeadingTau, at most headStabilise), so the head
    // target follows a turn instead of trailing it by v x tau. The smoothed girdle stays within kMaxOffset neck lengths
    // of the real one (the filter state itself is held there: no wind-up). Off, a long headStabilise left the target
    // many metres behind the flyer in a turn, beyond the bend-limited neck's reach, and the neck flipped to and fro.
    const float kHeadingTau = 0.15f;  // s at scale 1
    const float kMaxOffset = 0.25f;   // of the neck's length (girdle x spacing)
    const float kMaxHeadTurnDeg = 40f; // the head target's direction from the trunk's line, at most
    static readonly float kMaxHeadTurnCos = Mathf.Cos(kMaxHeadTurnDeg * Mathf.Deg2Rad), kMaxHeadTurnSin = Mathf.Sin(kMaxHeadTurnDeg * Mathf.Deg2Rad);
    void StabiliseBounded(float h, Vector3 girdlePos, Vector3 girdleVel, Vector3 up)
    {
        float tauV = Mathf.Max(1e-4f, f.headStabilise * timeScale);
        float tauH = Mathf.Max(1e-4f, Mathf.Min(f.headStabilise, kHeadingTau) * timeScale);
        Vector3 err = girdleVel - smoothVel;
        Vector3 errV = up * Vector3.Dot(err, up), errH = err - errV;
        Vector3 accV = up * Vector3.Dot(smoothAcc, up), accH = smoothAcc - accV;
        float wV = 1f / tauV, kV = Mathf.Min(h, 0.5f * tauV);
        float wH = 1f / tauH, kH = Mathf.Min(h, 0.5f * tauH);
        accV += (wV * wV * errV - 2f * wV * accV) * kV;
        accH += (wH * wH * errH - 2f * wH * accH) * kH;
        smoothAcc = accV + accH;
        smoothVel += accV * kV + accH * kH;
        smoothPos += smoothVel * h;
        smoothPos += (girdlePos - smoothPos) * Mathf.Min(1f, kDriftRate / timeScale * h);
        float r = kMaxOffset * seg * girdle;
        Vector3 e = smoothPos - girdlePos;
        float m = e.magnitude;
        if (m > r) smoothPos = girdlePos + e * (r / m);
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

    // d (unit) limited to joint j's bend cone around the unit axis a (FlightSpec.launchTailBySegment's launch blend).
    internal Vector3 ConeAt(int j, Vector3 a, Vector3 d) => Cone(a, d, cosLimit[j], sinLimit[j]);

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
