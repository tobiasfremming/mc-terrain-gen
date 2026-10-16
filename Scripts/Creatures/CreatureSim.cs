using System;
using UnityEngine;

// Procedural creature spine: a steered head drags a bend-limited chain that rides the ground.
// Port of the 2D LizardSim; legs live in CreatureLegs.
public sealed class CreatureSim
{
    const float kMaxDt = 0.1f;
    const float kGroundWindow = 0.5f;
    const float kGroundTau = 0.06f;
    const float kMaxSink = 0.005f;
    const float kSwayFull = 0.75f;  // girdle reach difference (leg lengths) that gives full sway
    const float kSwayTau = 0.08f;   // smoothing of the sway and roll signals, seconds
    const int kRollSpread = 3;      // a girdle's roll fades out over this many joints each way

    public readonly CreatureSpecies Species;
    public readonly float SizeScale;
    public readonly Vector3[] Joints;
    public readonly Quaternion[] Rotations;
    public readonly Vector3[] GroundNormals;
    public EnclosureArea? Bounds;

    public readonly LegState[] Legs;
    public int StepsTaken;     // cumulative, for tests/stats

    // Actual head speed (after the Bounds clamp), not the commanded one.
    public float Speed { get; private set; }
    public float Time { get; private set; }

    readonly IGroundProbe ground;
    readonly Func<Vector3, Vector3> upAt;
    readonly Vector3[] groundPoint;
    // The follow-the-leader body path (chain + ground). Joints is this path with the gait sway applied.
    internal readonly Vector3[] Path;
    readonly int[] girdles;          // attach joints with legs on both sides, ascending
    readonly float[] swaySignal, rollSignal, swayAmp; // per girdle: smoothed -1..1 signals, amplitude (m)
    readonly float[] swayY, rollDeg; // per joint: lateral offset (m, +right) and roll (deg)
    readonly CreatureLegs legs;
    readonly int n;
    Vector3 heading;
    float commanded;

    public CreatureSim(CreatureSpecies sp, IGroundProbe ground, Func<Vector3, Vector3> upAt,
                       Vector3 position, Vector3 forward, float sizeScale = 1f, int seed = 0)
    {
        Species = sp; this.ground = ground; this.upAt = upAt; SizeScale = sizeScale;
        n = sp.JointCount;
        Joints = new Vector3[n]; Rotations = new Quaternion[n];
        GroundNormals = new Vector3[n]; groundPoint = new Vector3[n];

        Vector3 up = Norm(upAt(position), Vector3.up);
        heading = Tangent(forward, up);
        float step = sp.spacing * sizeScale;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = position - heading * (step * i);
            Vector3 u = Norm(upAt(p), up);
            float clear = sp.joints[i].clearance * sizeScale;
            if (!ground.Probe(p, u, kGroundWindow * sizeScale, out Vector3 g, out Vector3 nrm)) { g = p - u * clear; nrm = u; }
            groundPoint[i] = g; GroundNormals[i] = Norm(nrm, u);
            Joints[i] = g + u * clear;
        }
        Path = (Vector3[])Joints.Clone();
        swayY = new float[n]; rollDeg = new float[n];
        girdles = FindGirdles(sp);
        swaySignal = new float[girdles.Length]; rollSignal = new float[girdles.Length]; swayAmp = new float[girdles.Length];
        float tanSway = Mathf.Tan(Mathf.Clamp(sp.gaitSwayDeg, 0f, 45f) * Mathf.Deg2Rad);
        for (int g = 0; g < girdles.Length; g++)
        {
            // half the trunk to the nearest node: the neighbouring girdle, or the head for a lone girdle
            int d = girdles[g];
            if (g > 0) d = Mathf.Min(d, girdles[g] - girdles[g - 1]);
            if (g + 1 < girdles.Length) d = Mathf.Min(d, girdles[g + 1] - girdles[g]);
            // smoothstep between nodes peaks at slope 1.5 * 2A / D = tan(gaitSwayDeg)
            swayAmp[g] = d * step * tanSway / 3f;
        }
        UpdateFrames();
        legs = new CreatureLegs(sp, ground, upAt, Joints, Rotations, sizeScale);
        Legs = legs.State;
    }

    static int[] FindGirdles(CreatureSpecies sp)
    {
        int c = sp.legs != null ? sp.legs.Length : 0, count = 0;
        var tmp = new int[c];
        for (int j = 0; j < sp.JointCount; j++)
        {
            bool left = false, right = false;
            for (int l = 0; l < c; l++)
                if (sp.legs[l].attachJoint == j) { if (sp.legs[l].side < 0) left = true; else right = true; }
            if (left && right) tmp[count++] = j;
        }
        var r = new int[count];
        Array.Copy(tmp, r, count);
        return r;
    }

    public void Tick(float dt, Vector3 target)
    {
        if (!(dt > 0f)) dt = 0f;
        dt = Mathf.Clamp(dt, 0f, kMaxDt);
        int sub = Mathf.Max(1, Species.subSteps);
        float h = dt / sub;
        for (int s = 0; s < sub; s++) SubStep(h, target);
        UpdateFrames();
    }

    void SubStep(float dt, Vector3 target)
    {
        if (!(dt > 0f)) return;
        Time += dt;
        var sp = Species;

        // 1. head steering
        Vector3 head = Path[0];
        Vector3 up = Norm(upAt(head), Vector3.up);
        heading = Tangent(heading, up);
        Vector3 to = target - head;
        float dist = to.magnitude;
        Vector3 desired = Vector3.ProjectOnPlane(to, up);
        float turnAngle = 0f;
        if (desired.sqrMagnitude > 1e-10f)
        {
            desired.Normalize();
            // a standing creature does not wiggle: fade the sway in with travel speed
            float sway = sp.speed > 1e-4f ? Mathf.Clamp01(commanded / (0.3f * sp.speed)) : 1f;
            float und = sway * sp.undulationDeg * Mathf.Sin(2f * Mathf.PI * sp.undulationHz * Time);
            if (und != 0f) desired = Quaternion.AngleAxis(und, up) * desired;
            float ang = Vector3.SignedAngle(heading, desired, up);
            turnAngle = Mathf.Abs(ang);
            // Turning in place swings the rigid head and drags the front of the body, so a leg at its
            // stretch or joint-range limit throttles the turn as well as the speed.
            float maxTurn = sp.turnRateDeg * dt * legs.Throttle;
            heading = Quaternion.AngleAxis(Mathf.Clamp(ang, -maxTurn, maxTurn), up) * heading;
        }
        float want = Mathf.Min(sp.speed, 2.5f * dist) * (1f - sp.turnSlowdown * turnAngle / 180f);
        // A creature cannot outrun its legs: the gait caps the speed, and slows it when a planted foot
        // nears full stretch. The cap is approached with accel-limited easing (braking 3x harder).
        float cap = Mathf.Min(sp.speed * legs.Throttle, legs.MaxSpeed);
        float goal = Mathf.Max(0f, want);
        float rate = sp.accel;
        if (cap < goal) { goal = cap; if (goal < commanded) rate = 3f * sp.accel; }
        commanded = Mathf.MoveTowards(commanded, goal, rate * dt);
        Vector3 start = head;
        head += heading * (commanded * dt);
        if (Bounds.HasValue) head = Bounds.Value.ClampToSafe(head);
        Speed = (head - start).magnitude / dt;
        Path[0] = head;

        // 2. chain
        float seg = sp.spacing * SizeScale;
        Vector3 prevBack = -heading;
        for (int i = 1; i < n; i++)
        {
            Vector3 d = Path[i] - Path[i - 1];
            d = d.sqrMagnitude > 1e-12f ? d.normalized : prevBack;
            float lim = sp.joints[i].rigid ? 0f : sp.joints[i].bendLimitDeg * Mathf.Deg2Rad;
            d = Vector3.RotateTowards(prevBack, d, lim, 0f);
            Path[i] = Path[i - 1] + d * seg;
            prevBack = d;
        }

        // 3. ground: smooth height offset and normal; hold on probe miss
        float k = 1f - Mathf.Exp(-dt / kGroundTau);
        for (int i = 0; i < n; i++)
        {
            Vector3 u = Norm(upAt(Path[i]), up);
            float clear = sp.joints[i].clearance * SizeScale;
            Vector3 near = Path[i] - u * clear;
            float hPrev = Vector3.Dot(groundPoint[i] - near, u);
            float hs = hPrev;
            Vector3 nrm = GroundNormals[i];
            if (ground.Probe(near, u, kGroundWindow * SizeScale, out Vector3 gp, out Vector3 gn))
            {
                float hProbe = Vector3.Dot(gp - near, u);
                hs = Mathf.Lerp(hPrev, hProbe, k);
                // smoothing may lag a rising slope, but never let the body sink more than 5 mm into it
                if (hs < hProbe - kMaxSink) hs = hProbe - kMaxSink;
                nrm = Norm(Vector3.Lerp(nrm, gn, k), u);
            }
            groundPoint[i] = near + u * hs;
            GroundNormals[i] = nrm;
            Path[i] = groundPoint[i] + u * (clear - legs.DroopSpread[i]);
        }

        // 4. gait sway: the displayed body is the path bent toward the reaching legs
        ApplySway(dt);

        // 5. legs (use the final joint positions; frames are refreshed first)
        UpdateFrames();
        StepsTaken += legs.Update(dt, Speed);
    }

    void UpdateFrames()
    {
        for (int i = 0; i < n; i++)
        {
            Vector3 f = i == 0 ? heading : Joints[i - 1] - Joints[i];
            f = f.sqrMagnitude > 1e-12f ? f.normalized : (i == 0 ? heading : Vector3.forward);
            Vector3 u = Vector3.ProjectOnPlane(GroundNormals[i], f);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.up, f);
            if (u.sqrMagnitude < 1e-8f) u = Vector3.ProjectOnPlane(Vector3.right, f);
            u.Normalize();
            if (rollDeg != null && rollDeg[i] != 0f) u = Quaternion.AngleAxis(rollDeg[i], f) * u;
            Rotations[i] = Quaternion.LookRotation(f, u);
        }
    }

    // Gait-coupled spine motion (sprawling-lizard pattern), driven by the feet themselves: each girdle
    // swings toward the leg that reaches furthest forward and rolls over its planted side while the other
    // side steps. Diagonal gait groups then give a standing S-wave with the head near a node. Fades out
    // with the walking speed like the head undulation. Joints = the path with each segment turned about
    // up by the sway's slope, within the bend limits; segment lengths are the path's own, exactly.
    void ApplySway(float dt)
    {
        var sp = Species;
        int G = girdles.Length;
        if (G == 0 || (sp.gaitSwayDeg <= 0f && sp.gaitRollDeg <= 0f))
        {
            Array.Copy(Path, Joints, n);
            return;
        }
        float fade = sp.speed > 1e-4f ? Mathf.Clamp01(commanded / (0.3f * sp.speed)) : 0f;
        float k = 1f - Mathf.Exp(-dt / kSwayTau);
        LegSpec[] specs = sp.legs;
        for (int g = 0; g < G; g++)
        {
            int j = girdles[g];
            Vector3 fwd = j == 0 ? heading : Path[j - 1] - Path[j];
            fwd = fwd.sqrMagnitude > 1e-12f ? fwd.normalized : heading;
            float sumR = 0f, sumL = 0f, roll = 0f; int cR = 0, cL = 0;
            for (int l = 0; l < specs.Length; l++)
            {
                LegSpec s = specs[l];
                if (s.attachJoint != j) continue;
                float reach = Vector3.Dot(Legs[l].foot - Path[j], fwd) / ((s.upperLength + s.lowerLength) * SizeScale);
                if (s.side > 0) { sumR += reach; cR++; } else { sumL += reach; cL++; }
                if (Legs[l].stepping) roll += s.side * Mathf.Sin(Mathf.PI * Legs[l].stepT);
            }
            float sway = Mathf.Clamp((sumR / cR - sumL / cL) / kSwayFull, -1f, 1f) * fade;
            swaySignal[g] = Mathf.Lerp(swaySignal[g], sway, k);
            rollSignal[g] = Mathf.Lerp(rollSignal[g], Mathf.Clamp(roll, -1f, 1f) * fade, k);
        }

        // lateral offset profile: node at the head, the girdles' offsets, a node one trunk behind the last
        // girdle; smoothstep between control points
        for (int i = 0; i < n; i++) { swayY[i] = 0f; rollDeg[i] = 0f; }
        int prevJ = 0; float prevY = 0f;
        for (int g = 0; g <= G; g++)
        {
            int j; float y;
            if (g < G) { j = girdles[g]; y = swayAmp[g] * swaySignal[g]; }
            else { j = Mathf.Min(n - 1, girdles[G - 1] + Mathf.Max(1, G > 1 ? girdles[G - 1] - girdles[G - 2] : girdles[G - 1])); y = 0f; }
            for (int i = prevJ + 1; i <= j; i++)
            {
                float t = (i - prevJ) / (float)(j - prevJ);
                swayY[i] = Mathf.Lerp(prevY, y, t * t * (3f - 2f * t));
            }
            prevJ = j; prevY = y;
            if (j >= n - 1) break;
        }
        for (int g = 0; g < G; g++)
            for (int d = -kRollSpread; d <= kRollSpread; d++)
            {
                int i = girdles[g] + d;
                if (i < 0 || i >= n) continue;
                float w = 0.5f * (1f + Mathf.Cos(Mathf.PI * d / (kRollSpread + 1f)));
                rollDeg[i] += sp.gaitRollDeg * rollSignal[g] * w;
            }

        float seg = sp.spacing * SizeScale;
        Joints[0] = Path[0];
        Vector3 prevBack = -heading;
        for (int i = 1; i < n; i++)
        {
            Vector3 v = Path[i] - Path[i - 1];
            float len = v.magnitude;
            Vector3 d = len > 1e-9f ? v / len : prevBack;
            // moving joint i to the right of joint i-1 turns the backward segment toward -right
            float turn = -Mathf.Rad2Deg * Mathf.Asin(Mathf.Clamp((swayY[i] - swayY[i - 1]) / seg, -0.5f, 0.5f));
            if (turn != 0f) d = Quaternion.AngleAxis(turn, Norm(upAt(Path[i]), Vector3.up)) * d;
            float lim = sp.joints[i].rigid ? 0f : sp.joints[i].bendLimitDeg * Mathf.Deg2Rad;
            d = Vector3.RotateTowards(prevBack, d, lim, 0f).normalized;
            Joints[i] = Joints[i - 1] + d * len;
            prevBack = d;
        }
    }

    // Spine, leg and eye bones in world space, in the species bone order.
    public void WriteBoneWorld(Matrix4x4[] dst, int offset)
    {
        var sp = Species;
        Vector3 s = Vector3.one * SizeScale;
        for (int i = 0; i < n; i++) dst[offset + i] = Matrix4x4.TRS(Joints[i], Rotations[i], s);
        legs.WriteBones(dst, offset);

        Quaternion hr = Rotations[Mathf.Min(1, n - 1)];
        Quaternion er = Rotations[sp.eyeJoint];
        for (int e = 0; e < 2; e++)
        {
            Vector3 eo = sp.eyeOffset; eo.x *= e == 0 ? -1f : 1f; // eye 0 left (-x), 1 right
            dst[offset + sp.EyeBone(e)] = Matrix4x4.TRS(Joints[sp.eyeJoint] + er * eo * SizeScale, hr, s);
        }
    }

    static Vector3 Norm(Vector3 v, Vector3 fallback) => v.sqrMagnitude > 1e-12f ? v.normalized : fallback;

    static Vector3 Tangent(Vector3 v, Vector3 up)
    {
        Vector3 t = Vector3.ProjectOnPlane(v, up);
        if (t.sqrMagnitude < 1e-10f) t = Vector3.ProjectOnPlane(Mathf.Abs(up.y) < 0.99f ? Vector3.up : Vector3.forward, up);
        return t.normalized;
    }
}
