using System;
using UnityEngine;

public struct LegState
{
    public Vector3 shoulder, elbow, foot, footNormal, stepFrom, stepTo;
    public Vector3 stepFromNormal, stepToNormal; // the foot's normal at lift-off and at the landing point
    public bool stepping; public float stepT;
}

// Leg logic of a CreatureSim: rest points, gait groups, stepping, IK, bone output.
// Port of Leg.update / Leg.ik in video_manim/lizard.py. Allocation-free after construction.
sealed class CreatureLegs
{
    const float kProbeWindow = 0.5f;
    const float kIdleSpeed = 0.05f;
    const float kIdleTidyFraction = 0.3f;
    const int kRestSlideIters = 2;         // probes for sliding an uphill rest point outward
    const float kRestSlack = 0.002f;       // a rest point this much nearer than the flat stance is left alone
    const float kStrideReach = 0.9f, kStrideUse = 0.5f;
    const float kLandReach = 0.9f;
    const float kOverextend = 0.91f;       // planted foot at this fraction of full reach becomes eligible to step
    const float kSlowAt = 0.92f, kStopAt = 0.985f; // reach fractions where the body starts to slow and stops
    const float kStepGain = 0.005f;        // a stretch-triggered step must shorten the foot's reach by this much
    const float kDroopTarget = 0.97f, kDroopSlack = 0.02f, kDroopMaxFrac = 0.18f, kDroopRate = 0.3f;
    // Joint-range limits come from the species (LegSpec forward/back swing, knee range; attach-joint frame),
    // so a planted foot never drags the limb into a pose the skinned mesh cannot follow: the upper bone's
    // swing from its rest direction, and the knee bend's deviation from its rest bend. Past a limit the foot
    // steps; a little further the body slows and stops.
    const float kJointGainDeg = 2f;                 // a joint-triggered step must improve the angle by this much
    const float kJointSlowDeg = 2f, kJointStopDeg = 10f; // throttle ramp, degrees past the limit
    // The throttle acts on the angle predicted this far ahead from its current rate: in a tight coil the
    // body can swing a planted limb by ~5 deg per frame, faster than the accel-limited speed can brake.
    const float kJointLeadSec = 0.04f;
    // Trunk keep-out (final review): in a tight turn the curled trunk sweeps over the inner feet. Every
    // foot keeps a plan-view clearance from the trunk outline (the spine's segments widened by the joints'
    // half-widths), as fractions of the leg's rest gap (rest point's lateral offset minus its girdle's
    // half-width, species data): landing points and the swing are pushed out to kTrunkKeepFrac, and a
    // planted foot the trunk has swung over to within kTrunkStepFrac steps. (Slowing the body as well, like
    // the joint-range throttle, stalled the tight circle and broke its joint-range and skinning bounds.)
    const float kTrunkKeepFrac = 0.35f, kTrunkStepFrac = 0.6f;
    // A trunk check is skipped while the trunk cannot have closed in on the point since the last one: the
    // spine only moves by travelling (follow-the-leader; turning in place moves no joint, the sway fades
    // with speed), so no joint moves more than (kTrunkTravel x speed + kTrunkCreep x size) x dt in a sub-step
    // (the creep covers the sway settling while the body brakes); a moving point (the swing, the landing
    // point) also spends its own motion from the slack the last check left.
    const float kTrunkTravel = 2f, kTrunkCreep = 0.1f;

    readonly CreatureSpecies sp;
    readonly IGroundProbe ground;
    readonly Func<Vector3, Vector3> upAt;
    readonly Vector3[] joints;
    readonly Quaternion[] rotations;
    readonly float scale;
    readonly float timeScale;      // sqrt(scale): step times and the joint lead scale with it (CreatureSim.TimeScale)
    readonly LegSpec[] specs;
    public readonly LegState[] State;
    readonly Vector3[] target;     // intended foot position: planted point, or the moving step point
    readonly Vector3[] rest;       // current ground-projected rest point
    readonly bool[] eligible;
    readonly Vector3[] restUpper;  // rest upper-bone direction in the attach-joint frame
    readonly float[] restKnee;     // rest knee bend, degrees
    readonly float[] fwdLimit, backLimit, kneeRange, kneeFlex; // the species' joint-range limits, degrees
    readonly float[] minReach;     // shoulder-foot distance at the knee's flex limit, unscaled
    readonly float[] restReach;    // shoulder-foot distance of the flat reference stance, unscaled
    readonly float[] stepOver;     // overshoot of the step in flight, fixed at lift-off
    readonly bool[] jointLimited;  // joint-range state from the last IK solve (UpdateThrottle)
    readonly float[] prevSwing, prevKnee; // last planted joint angles (-1 = none), for the angle rate
    int lastGroup = -1;
    readonly float[] trunkKeep, trunkStep; // keep-out and step distances from the trunk outline, unscaled
    readonly int[] trunkLo, trunkHi;       // the spine segments a leg's foot can reach: [lo, hi)
    readonly float[] trunkHalfWidth;       // the widest half-width of those segments' joints, unscaled
    readonly float[] trunkSlack;           // planted foot: how far the trunk may close in before a re-check (0 = check)
    readonly float[] swingSlack, aimSlack; // the same for the swinging foot and its landing point
    readonly Vector3[] swingPrev, aimPrev; // ... and where those were at the last sub-step
    float trunkTravel;                     // the current sub-step's bound on joint motion (kTrunkTravel)
    readonly int[] attachJoints;   // the distinct joints legs attach to
    bool spreadNonZero;

    // Per spine joint: how far the body is lowered toward the ground so the legs on it can reach
    // (a leg whose rest point lies on much lower ground, e.g. one side of a steep face).
    public readonly float[] Droop;       // per attach joint, as computed from its legs
    public readonly float[] DroopSpread; // per spine joint: Droop smoothed over +-3 joints, what the sim applies

    // 1 = free to walk, 0 = a planted foot is at full stretch and the body must wait for its step.
    public float MaxSpeed { get; private set; } = float.PositiveInfinity;
    public float Throttle { get; private set; } = 1f;

    public CreatureLegs(CreatureSpecies sp, IGroundProbe ground, Func<Vector3, Vector3> upAt,
                        Vector3[] joints, Quaternion[] rotations, float scale, float timeScale = 1f)
    {
        this.sp = sp; this.ground = ground; this.upAt = upAt;
        this.joints = joints; this.rotations = rotations; this.scale = scale;
        this.timeScale = timeScale > 0f ? timeScale : 1f;
        specs = sp.legs ?? new LegSpec[0];
        int c = specs.Length;
        Droop = new float[joints.Length]; DroopSpread = new float[joints.Length];
        State = new LegState[c]; target = new Vector3[c]; rest = new Vector3[c]; eligible = new bool[c];
        restUpper = new Vector3[c]; restKnee = new float[c]; minReach = new float[c];
        stepOver = new float[c]; jointLimited = new bool[c]; restReach = new float[c];
        prevSwing = new float[c]; prevKnee = new float[c];
        fwdLimit = new float[c]; backLimit = new float[c]; kneeRange = new float[c]; kneeFlex = new float[c];
        trunkKeep = new float[c]; trunkStep = new float[c]; trunkLo = new int[c]; trunkHi = new int[c]; trunkSlack = new float[c];
        trunkHalfWidth = new float[c];
        swingSlack = new float[c]; aimSlack = new float[c]; swingPrev = new Vector3[c]; aimPrev = new Vector3[c];
        for (int l = 0; l < c; l++) { prevSwing[l] = -1f; prevKnee[l] = -1f; }
        int na = 0; var tmp = new int[c];
        for (int l = 0; l < c; l++)
        {
            bool seen = false;
            for (int m = 0; m < na; m++) if (tmp[m] == specs[l].attachJoint) seen = true;
            if (!seen) tmp[na++] = specs[l].attachJoint;
        }
        attachJoints = new int[na];
        Array.Copy(tmp, attachJoints, na);
        for (int l = 0; l < c; l++)
        {
            // flat reference stance in the attach-joint frame (as StrideLimitedSpeed): independent of the spawn
            LegSpec s = specs[l];
            fwdLimit[l] = s.ForwardSwingLimit; backLimit[l] = s.BackSwingLimit; kneeRange[l] = s.KneeRange; kneeFlex[l] = s.KneeFlex;
            Vector3 sh = Mirror(s.shoulderOffset, l);
            Vector3 rs = Mirror(s.restOffset, l); rs.y = -sp.joints[s.attachJoint].clearance;
            Vector3 el = TwoBoneIK.Solve(sh, rs, s.upperLength, s.lowerLength, Mirror(s.pole, l), out Vector3 ft);
            restUpper[l] = (el - sh).normalized;
            restKnee[l] = Vector3.Angle(el - sh, ft - el);
            restReach[l] = (ft - sh).magnitude;
            float gap = Mathf.Max(0f, Mathf.Abs(s.restOffset.x) - sp.joints[s.attachJoint].halfWidth);
            trunkKeep[l] = kTrunkKeepFrac * gap; trunkStep[l] = kTrunkStepFrac * gap;
            // the spine segments within the foot's reach along the body, +1 for the segment it starts in
            int span = sp.spacing > 0f ? Mathf.CeilToInt((s.upperLength + s.lowerLength + s.overshoot + Mathf.Abs(s.shoulderOffset.z)) / sp.spacing) + 1 : 0;
            trunkLo[l] = Mathf.Max(0, s.attachJoint - span); trunkHi[l] = Mathf.Min(joints.Length - 1, s.attachJoint + span);
            for (int i = trunkLo[l]; i <= trunkHi[l]; i++) trunkHalfWidth[l] = Mathf.Max(trunkHalfWidth[l], sp.joints[i].halfWidth);
            float bend = Mathf.Min(170f, restKnee[l] + kneeFlex[l]) * Mathf.Deg2Rad;
            float a = s.upperLength, b = s.lowerLength;
            minReach[l] = Mathf.Sqrt(Mathf.Max(0f, a * a + b * b + 2f * a * b * Mathf.Cos(bend)));
        }
        for (int l = 0; l < c; l++)
        {
            Vector3 u = Up(l);
            Vector3 so = Mirror(specs[l].restOffset, l) * scale;
            Vector3 near = joints[specs[l].attachJoint] + rotations[specs[l].attachJoint] * so;
            Vector3 p = near; Vector3 n = u;
            ground.Probe(near, u, kProbeWindow * scale, out p, out n);
            rest[l] = p;
            target[l] = p;
            State[l].footNormal = n.sqrMagnitude > 1e-12f ? n.normalized : u;
            State[l].stepFromNormal = State[l].stepToNormal = State[l].footNormal;
        }
        SolveIK(0f);
        MaxSpeed = StrideLimitedSpeed();
    }

    // Fastest steady walk the legs can keep up with: with alternating gait groups a foot gets one
    // step per (groups * stepDuration), and can only be placed within reach, so
    // speed <= usable stride / cycle. Beyond that the feet would drag.
    float StrideLimitedSpeed()
    {
        int c = specs.Length;
        if (c == 0) return float.PositiveInfinity;
        int groups = 0, seen = 0;           // distinct gait groups (group ids are small ints)
        for (int l = 0; l < c; l++)
        {
            int bit = 1 << (specs[l].gaitGroup & 31);
            if ((seen & bit) == 0) { seen |= bit; groups++; }
        }
        float best = float.PositiveInfinity;
        for (int l = 0; l < c; l++)
        {
            LegSpec s = specs[l];
            // Flat reference stance in the attach-joint frame: independent of where the creature spawned.
            Vector3 sh = Mirror(s.shoulderOffset, l) * scale;
            Vector3 rs = Mirror(s.restOffset, l) * scale;
            rs.y = -sp.joints[s.attachJoint].clearance * scale;
            Vector3 v = rs - sh;
            float along = v.z;
            float perp2 = Mathf.Max(0f, v.sqrMagnitude - along * along);
            float reach = kStrideReach * (s.upperLength + s.lowerLength) * scale;
            float half = Mathf.Sqrt(Mathf.Max(0f, reach * reach - perp2));
            float cycle = Mathf.Max(2, groups) * Mathf.Max(1e-3f, s.stepDuration * timeScale);
            best = Mathf.Min(best, kStrideUse * 2f * half / cycle);
        }
        // never a standstill for a stance the reach cannot cover; the throttle handles real ground
        best = Mathf.Max(best, 0.05f);
        return best;
    }

    public Vector3 RestPoint(int l) => rest[l];

    Vector3 Mirror(Vector3 v, int l) { v.x *= specs[l].side; return v; }
    Vector3 Up(int l) { Vector3 u = upAt(joints[specs[l].attachJoint]); return u.sqrMagnitude > 1e-12f ? u.normalized : Vector3.up; }

    // Returns the number of steps started this sub-step.
    public int Update(float dt, float speed)
    {
        int c = specs.Length;
        if (c == 0) return 0;
        bool idle = speed < kIdleSpeed * timeScale; // Froude speeds scale with sqrt(size)
        trunkTravel = (kTrunkTravel * Mathf.Max(0f, speed) + kTrunkCreep * scale) * dt;

        // rest points
        for (int l = 0; l < c; l++)
        {
            LegSpec s = specs[l];
            Vector3 u = Up(l);
            Quaternion r = rotations[s.attachJoint];
            Vector3 near = joints[s.attachJoint] + r * (Mirror(s.restOffset, l) * scale);
            Vector3 sh = joints[s.attachJoint] + r * (Mirror(s.shoulderOffset, l) * scale);
            State[l].shoulder = sh;
            if (!ground.Probe(near, u, kProbeWindow * scale, out Vector3 p, out _)) continue; // hold the last rest
            // Uphill (one side of a cross slope) the ground rises toward the shoulder and the rest stance
            // would fold the knee far past its rest bend: slide the rest point outward along the ground
            // until it is as far from the shoulder as on flat ground.
            float want = restReach[l] * scale;
            Vector3 outward = Vector3.ProjectOnPlane(r * Vector3.right * s.side, u);
            if (outward.sqrMagnitude > 1e-8f)
            {
                outward.Normalize();
                for (int k = 0; k < kRestSlideIters; k++)
                {
                    Vector3 v = p - sh;
                    float d = v.magnitude;
                    if (d >= want - kRestSlack * scale) break;
                    float along = Mathf.Max(Vector3.Dot(v, outward), 0.3f * d);
                    Vector3 q = p + outward * ((want - d) * d / Mathf.Max(1e-5f, along));
                    // a miss keeps the last point that was on the ground (q itself may be in the air)
                    if (!ground.Probe(q, u, kProbeWindow * scale, out Vector3 pq, out _)) break;
                    p = pq;
                }
            }
            rest[l] = p;
        }

        UpdateDroop(dt);

        // advance steps in flight
        for (int l = 0; l < c; l++)
        {
            if (!State[l].stepping) continue;
            trunkSlack[l] = 0f; // the foot lands somewhere new: check it when planted
            LegSpec s = specs[l];
            State[l].stepT += dt / Mathf.Max(1e-4f, s.stepDuration * timeScale);
            // re-aim at where the rest point will be on landing: in a turn the body does not travel straight
            Aim(l, speed, Mathf.Max(0f, 1f - State[l].stepT));
            if (State[l].stepT >= 1f)
            {
                State[l].stepping = false; State[l].stepT = 1f;
                target[l] = State[l].stepTo;
                State[l].footNormal = State[l].stepToNormal;
            }
            else
            {
                float t = State[l].stepT;
                float sm = t * t * (3f - 2f * t);
                // the foot turns from the ground it left to the ground it lands on over the swing
                Vector3 fn = Vector3.Lerp(State[l].stepFromNormal, State[l].stepToNormal, sm);
                State[l].footNormal = fn.sqrMagnitude > 1e-12f ? fn.normalized : State[l].stepToNormal;
                // the swing passes over the rest point at mid-step (quadratic Bezier through it), so the
                // limb swings through its rest pose instead of cutting past the shoulder
                Vector3 a = State[l].stepFrom, b = State[l].stepTo;
                Vector3 ctrl = 2f * rest[l] - 0.5f * (a + b);
                Vector3 path = (1f - sm) * (1f - sm) * a + 2f * (1f - sm) * sm * ctrl + sm * sm * b;
                target[l] = KeepOutOfFlex(l, GatedKeepOff(l, path, ref swingSlack[l], ref swingPrev[l]) + Up(l) * (s.stepLift * scale * Mathf.Sin(Mathf.PI * t)));
            }
        }

        // eligibility
        for (int l = 0; l < c; l++)
        {
            LegSpec s = specs[l];
            float dist = (target[l] - rest[l]).magnitude;
            float thr = s.stepThreshold * scale;
            // Also step when the planted foot nears full extension: with short-limbed rear legs the
            // reach runs out before stepThreshold does, and the IK would drag the foot.
            float reach = (s.upperLength + s.lowerLength) * scale * kOverextend;
            float now = (target[l] - State[l].shoulder).magnitude;
            // only if stepping to the rest point would actually shorten the reach (else it is a zero-length step loop)
            bool stretched = now > reach && (rest[l] - State[l].shoulder).magnitude < now - kStepGain * scale;
            bool overFlexed = !State[l].stepping && jointLimited[l]; // from the last IK solve
            bool underTrunk = !State[l].stepping && TrunkLimited(l, trunkTravel);
            eligible[l] = !State[l].stepping && (dist > thr || stretched || overFlexed || underTrunk || (idle && dist > kIdleTidyFraction * thr));
        }

        // start steps
        int started = 0, startedGroup = -1;
        for (int l = 0; l < c; l++)
        {
            if (!eligible[l]) continue;
            int g = specs[l].gaitGroup;
            bool blocked = false, otherEligible = false;
            for (int m = 0; m < c; m++)
            {
                if (specs[m].gaitGroup == g) continue;
                if (State[m].stepping) blocked = true;
                if (eligible[m] && !State[m].stepping) otherEligible = true;
            }
            if (blocked) continue;
            // Alternate groups. Legs of a group that became eligible together start together, and a leg
            // may join its own group's step while that step is less than half done. Otherwise it waits
            // its turn: joining late would let one group hog the gait and starve the other.
            bool joinable = false;
            for (int m = 0; m < c; m++)
                if (m != l && specs[m].gaitGroup == g && State[m].stepping && State[m].stepT < 0.5f) joinable = true;
            if (lastGroup == g && otherEligible && startedGroup != g && !joinable) continue;
            StartStep(l, speed);
            lastGroup = g; startedGroup = g;
            started++;
        }

        SolveIK(dt);
        return started;
    }

    // Lower the body on a joint while one of its legs has to reach too far for its rest point; raise it
    // again once every leg has slack. Smallest mechanism that keeps feet within reach of lower ground.
    void UpdateDroop(float dt)
    {
        bool anyDroop = false;
        for (int a = 0; a < attachJoints.Length; a++)
        {
            int j = attachJoints[a];
            float need = float.NegativeInfinity; bool any = false;
            for (int l = 0; l < specs.Length; l++)
            {
                LegSpec s = specs[l];
                if (s.attachJoint != j) continue;
                any = true;
                Vector3 v = rest[l] - State[l].shoulder;
                float d = v.magnitude;
                if (d < 1e-5f) { need = Mathf.Max(need, -1f); continue; }
                float reach = (s.upperLength + s.lowerLength) * scale;
                float vert = Mathf.Max(0.3f, Mathf.Abs(Vector3.Dot(v / d, Up(l))));
                need = Mathf.Max(need, (d - kDroopTarget * reach) / vert);
            }
            if (!any) continue;
            float cap = kDroopMaxFrac * sp.joints[j].clearance * scale;
            float step = kDroopRate * scale / timeScale * dt;
            if (need > 0f) Droop[j] = Mathf.Min(cap, Droop[j] + Mathf.Min(need * 0.5f, step));
            else if (need < -kDroopSlack * scale && Droop[j] > 0f) Droop[j] = Mathf.Max(0f, Droop[j] - step);
            if (Droop[j] > 0f) anyDroop = true;
        }
        // no droop anywhere and none spread from before: the spread is all zeros already
        if (!anyDroop && !spreadNonZero) return;
        spreadNonZero = anyDroop;

        // Spread each joint's droop over its neighbours (cosine falloff, 4 joints) so the spine bends
        // smoothly instead of notching and segment lengths stay put.
        for (int i = 0; i < DroopSpread.Length; i++)
        {
            float best = 0f;
            for (int k = -3; k <= 3; k++)
            {
                int j = i + k;
                if (j < 0 || j >= Droop.Length || Droop[j] <= 0f) continue;
                float w = 0.5f * (1f + Mathf.Cos(Mathf.PI * Mathf.Abs(k) / 4f));
                best = Mathf.Max(best, Droop[j] * w);
            }
            DroopSpread[i] = best;
        }
    }

    void StartStep(int l, float speed)
    {
        LegSpec s = specs[l];
        // The species' overshoot, trimmed so the foot can still reach it; fixed for the whole step so
        // re-aiming in flight only follows the body (an idle tidy-up lands exactly on the rest point).
        Vector3 fwd = rotations[s.attachJoint] * Vector3.forward;
        float over = speed < kIdleSpeed * timeScale ? 0f : s.overshoot * scale;
        float maxDist = kLandReach * (s.upperLength + s.lowerLength) * scale;
        for (int i = 0; i < 6 && over > 1e-4f && (rest[l] + fwd * over - State[l].shoulder).sqrMagnitude > maxDist * maxDist; i++)
            over *= 0.6f;
        stepOver[l] = over;
        State[l].stepFrom = target[l];
        State[l].stepFromNormal = State[l].footNormal;
        swingSlack[l] = aimSlack[l] = 0f; // a new swing and landing point: check both
        Aim(l, speed, 1f);
        State[l].stepT = 0f;
        State[l].stepping = true;
    }

    // Sets stepTo (and the landing normal): where the rest point will be when the step lands (the body
    // keeps moving during the remaining fraction of the swing) plus the step's fixed overshoot. Every
    // term is continuous in time (speed is accel-limited), so re-aiming never pops the swinging foot.
    void Aim(int l, float speed, float remaining)
    {
        LegSpec s = specs[l];
        Vector3 fwd = rotations[s.attachJoint] * Vector3.forward;
        float travel = Mathf.Max(0f, speed) * s.stepDuration * timeScale * remaining;
        Vector3 goal = rest[l] + fwd * (travel + stepOver[l]);
        Vector3 u = Up(l);
        Vector3 p = goal, n = u;
        if (!ground.Probe(goal, u, kProbeWindow * scale, out p, out n)) { p = goal; n = State[l].stepToNormal.sqrMagnitude > 0.5f ? State[l].stepToNormal : State[l].footNormal; }
        // a landing point under the trunk (the inner feet of a tight turn) moves out from under it
        Vector3 q = GatedKeepOff(l, p, ref aimSlack[l], ref aimPrev[l]);
        if ((q - p).sqrMagnitude > 1e-12f)
        {
            if (ground.Probe(q, u, kProbeWindow * scale, out Vector3 pq, out Vector3 nq)) { p = pq; n = nq; }
            else p = q;
        }
        State[l].stepTo = p;
        State[l].stepToNormal = n.sqrMagnitude > 1e-12f ? n.normalized : u;
    }

    // A swinging foot passes the shoulder: keep it at least as far away as the knee's flex limit
    // allows, pushing it outward horizontally (the vertical lift is kept).
    Vector3 KeepOutOfFlex(int l, Vector3 p)
    {
        Vector3 sh = State[l].shoulder;
        Vector3 v = p - sh;
        float dMin = minReach[l] * scale;
        if (v.sqrMagnitude >= dMin * dMin) return p;
        Vector3 u = Up(l);
        float vert = Vector3.Dot(v, u);
        if (vert * vert >= dMin * dMin) return p;
        Vector3 h = v - u * vert;
        if (h.sqrMagnitude < 1e-10f) h = rotations[specs[l].attachJoint] * Vector3.right * specs[l].side;
        return sh + u * vert + h.normalized * Mathf.Sqrt(dMin * dMin - vert * vert);
    }

    // Plan-view clearance of p from the trunk outline near leg l (in the plane across the attach joint's up):
    // distance to the nearest spine segment minus its half-width there. away = the unit push direction out
    // of that segment (in the plane), or the leg's side when p is on the spine line.
    float TrunkClearance(int l, Vector3 p, out Vector3 away)
    {
        Vector3 u = Up(l);
        float best = float.PositiveInfinity, bx = 0f, by = 0f, bz = 0f, bd = 0f;
        SpineJoint[] js = sp.joints;
        // from the girdle outward, so the nearest segments come first and the far ones are rejected cheaply:
        // a segment whose start is further than best + its length (+10 %) + the widest half-width cannot be nearer
        int lo = trunkLo[l], hi = trunkHi[l], at = Mathf.Clamp(specs[l].attachJoint, lo, hi), up = hi - at;
        float pad = (1.1f * sp.spacing + trunkHalfWidth[l]) * scale;
        for (int k = 0, count = hi - lo; k < count; k++)
        {
            int i = k < up ? at + k : at - 1 - (k - up);
            Vector3 a = joints[i];
            float apx = p.x - a.x, apy = p.y - a.y, apz = p.z - a.z;
            float pu = apx * u.x + apy * u.y + apz * u.z;
            apx -= u.x * pu; apy -= u.y * pu; apz -= u.z * pu;
            if (best < float.PositiveInfinity)
            {
                float r = best + pad;
                if (r > 0f && apx * apx + apy * apy + apz * apz > r * r) continue;
            }
            Vector3 b = joints[i + 1];
            float abx = b.x - a.x, aby = b.y - a.y, abz = b.z - a.z;
            float au = abx * u.x + aby * u.y + abz * u.z;
            abx -= u.x * au; aby -= u.y * au; abz -= u.z * au;
            float ab2 = abx * abx + aby * aby + abz * abz;
            float t = ab2 > 1e-12f ? (apx * abx + apy * aby + apz * abz) / ab2 : 0f;
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            float dx = apx - abx * t, dy = apy - aby * t, dz = apz - abz * t;
            float d = Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
            float c = d - (js[i].halfWidth + (js[i + 1].halfWidth - js[i].halfWidth) * t) * scale;
            if (c < best) { best = c; bx = dx; by = dy; bz = dz; bd = d; }
        }
        if (bd > 1e-6f) away = new Vector3(bx / bd, by / bd, bz / bd);
        else away = rotations[specs[l].attachJoint] * Vector3.right * specs[l].side;
        return best;
    }

    // Moves p out from under the trunk to the keep-out distance, in the plane (its height is kept).
    Vector3 KeepOffTrunk(int l, Vector3 p)
    {
        float keep = trunkKeep[l] * scale;
        for (int k = 0; k < 2; k++) // a push off one segment may approach the next one of a curl
        {
            float c = TrunkClearance(l, p, out Vector3 away);
            if (c >= keep) break;
            p += away * (keep - c);
        }
        return p;
    }

    // KeepOffTrunk for a point that moves from sub-step to sub-step, measured only once it and the trunk
    // may have used up the slack (clearance beyond the keep-out distance) the last measurement left.
    Vector3 GatedKeepOff(int l, Vector3 p, ref float slack, ref Vector3 prev)
    {
        slack -= (p - prev).magnitude + trunkTravel;
        prev = p;
        if (slack > 0f) return p;
        float c = TrunkClearance(l, p, out _);
        slack = c - trunkKeep[l] * scale;
        return slack >= 0f ? p : KeepOffTrunk(l, p);
    }

    // A planted foot under the trunk: true while it is inside the step distance and its landing point (the
    // rest point, moved off the trunk) would clear the trunk by kStepGain more. travel = how far any spine
    // joint can have moved since the last call (see kTrunkTravel).
    bool TrunkLimited(int l, float travel)
    {
        trunkSlack[l] -= travel;
        if (trunkSlack[l] > 0f) return false;
        float c = TrunkClearance(l, target[l], out _);
        trunkSlack[l] = c - trunkStep[l] * scale;
        if (trunkSlack[l] > 0f) return false;
        float land = Mathf.Max(TrunkClearance(l, rest[l], out _), trunkKeep[l] * scale);
        return land > c + kStepGain * scale;
    }

    void SolveIK(float dt)
    {
        for (int l = 0; l < specs.Length; l++)
        {
            LegSpec s = specs[l];
            Quaternion r = rotations[s.attachJoint];
            Vector3 shoulder = joints[s.attachJoint] + r * (Mirror(s.shoulderOffset, l) * scale);
            Vector3 pole = r * Mirror(s.pole, l);
            State[l].shoulder = shoulder;
            State[l].elbow = TwoBoneIK.Solve(shoulder, target[l], s.upperLength * scale, s.lowerLength * scale, pole, out Vector3 foot);
            State[l].foot = foot;
        }
        UpdateThrottle(dt);
    }

    void UpdateThrottle(float dt)
    {
        float worst = 0f, jointThrottle = 1f;
        for (int l = 0; l < specs.Length; l++)
        {
            jointLimited[l] = false;
            if (State[l].stepping) { prevSwing[l] = prevKnee[l] = -1f; continue; }
            jointThrottle = Mathf.Min(jointThrottle, JointThrottle(l, dt));
            float reach = (specs[l].upperLength + specs[l].lowerLength) * scale;
            float now = (target[l] - State[l].shoulder).magnitude;
            // a stretch no step can cure (rest point itself out of reach) must not stop the body for good
            if ((rest[l] - State[l].shoulder).magnitude >= now - kStepGain * scale) continue;
            worst = Mathf.Max(worst, now / reach);
        }
        Throttle = Mathf.Min(jointThrottle, Mathf.Clamp01((kStopAt - worst) / (kStopAt - kSlowAt)));
    }

    // Swing of the upper bone from its rest direction and knee-bend deviation from rest, in degrees, and
    // the swing limit for the direction it swung in: the forward limit when the bone moved toward the
    // head, the back limit toward the tail, blended by the forward share of the swing (no jump in between).
    // The knee deviation is unsigned; kneeLimit is the flexion or the extension limit, whichever applies.
    void JointAngles(int l, Vector3 shoulder, Vector3 elbow, Vector3 foot, out float swing, out float knee, out float swingLimit, out float kneeLimit)
    {
        Quaternion r = rotations[specs[l].attachJoint];
        Vector3 dir = Quaternion.Inverse(r) * (elbow - shoulder);
        swing = Vector3.Angle(dir, restUpper[l]);
        float dev = Vector3.Angle(elbow - shoulder, foot - elbow) - restKnee[l];
        knee = Mathf.Abs(dev);
        kneeLimit = dev > 0f ? kneeFlex[l] : kneeRange[l];
        Vector3 moved = dir.normalized - restUpper[l];
        float m = moved.magnitude;
        float fwd = m > 1e-6f ? Mathf.Clamp(moved.z / m, -1f, 1f) : 0f;
        swingLimit = Mathf.Lerp(backLimit[l], fwdLimit[l], 0.5f + 0.5f * fwd);
    }

    // Joint-range state of a planted leg, from the pose SolveIK just produced (no extra solve unless a
    // limit is near). Sets jointLimited[l] (the step trigger: past a limit, and stepping to the rest
    // point would improve it by kJointGainDeg, the stretch trigger's hysteresis) and returns the leg's
    // throttle. The throttle ramps on the angle predicted kJointLeadSec ahead, measured past
    // max(limit, the rest stance's own angle): on a slope the ground-projected rest stance may itself be
    // past a limit, and drifting a little beyond it must not stall the body.
    float JointThrottle(int l, float dt)
    {
        LegState st = State[l];
        JointAngles(l, st.shoulder, st.elbow, st.foot, out float swing, out float knee, out float swingLim, out float kneeLim);
        float ps = prevSwing[l], pk = prevKnee[l];
        prevSwing[l] = swing; prevKnee[l] = knee;
        float lead = dt > 0f && ps >= 0f ? kJointLeadSec * timeScale / dt : 0f;
        float swingP = swing + Mathf.Max(0f, swing - ps) * lead;
        float kneeP = knee + Mathf.Max(0f, knee - pk) * lead;
        if (swingP <= swingLim && kneeP <= kneeLim) return 1f;

        LegSpec s = specs[l];
        Vector3 er = TwoBoneIK.Solve(st.shoulder, rest[l], s.upperLength * scale, s.lowerLength * scale,
                                     rotations[s.attachJoint] * Mirror(s.pole, l), out Vector3 fr);
        JointAngles(l, st.shoulder, er, fr, out float swingR, out float kneeR, out float unusedS, out float unusedK);
        jointLimited[l] = (swing > swingLim && swingR < swing - kJointGainDeg)
                       || (knee > kneeLim && kneeR < knee - kJointGainDeg);
        float excess = 0f;
        if (swingP > swingLim && swingR < swingP - kJointGainDeg) excess = swingP - Mathf.Max(swingLim, swingR);
        if (kneeP > kneeLim && kneeR < kneeP - kJointGainDeg) excess = Mathf.Max(excess, kneeP - Mathf.Max(kneeLim, kneeR));
        return Mathf.Clamp01((kJointStopDeg - excess) / (kJointStopDeg - kJointSlowDeg));
    }

    // Writes the 3 bones of every leg (upper, lower, foot) as world matrices.
    public void WriteBones(Matrix4x4[] dst, int offset)
    {
        Vector3 sc = Vector3.one * scale;
        for (int l = 0; l < specs.Length; l++)
        {
            LegSpec s = specs[l];
            ref LegState st = ref State[l];
            Quaternion ar = rotations[s.attachJoint];
            Vector3 pole = ar * Mirror(s.pole, l);
            Quaternion upper = BoneRot(st.elbow - st.shoulder, pole, ar);
            dst[offset + sp.LegBone(l, 0)] = Matrix4x4.TRS(st.shoulder, upper, sc);
            dst[offset + sp.LegBone(l, 1)] = Matrix4x4.TRS(st.elbow, LowerRot(upper, st.foot - st.elbow), sc);

            Vector3 n = st.footNormal.sqrMagnitude > 1e-12f ? st.footNormal.normalized : Vector3.up;
            Vector3 f = Vector3.ProjectOnPlane(ar * Vector3.forward, n);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.ProjectOnPlane(ar * Vector3.up, n);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.ProjectOnPlane(Vector3.forward, n);
            dst[offset + sp.LegBone(l, 2)] = Matrix4x4.TRS(st.foot, Quaternion.LookRotation(f.normalized, n), sc);
        }
    }

    // The lower bone: the upper bone's frame turned about the knee hinge onto the lower direction. Its +Y
    // is still the pole made perpendicular to the bone, but with the upper bone's sign: projecting the pole
    // onto each bone separately flips the lower roll by 180 deg whenever the pole lies between the two
    // bone directions, which collapses the skinned knee.
    static Quaternion LowerRot(Quaternion upper, Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-12f) return upper;
        return Quaternion.FromToRotation(upper * Vector3.forward, dir.normalized) * upper;
    }

    // +Z along the bone, +Y = pole made perpendicular to it.
    static Quaternion BoneRot(Vector3 dir, Vector3 pole, Quaternion attach)
    {
        if (dir.sqrMagnitude < 1e-12f) return attach;
        dir.Normalize();
        Vector3 up = Vector3.ProjectOnPlane(pole, dir);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(attach * Vector3.up, dir);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Vector3.right, dir);
        return Quaternion.LookRotation(dir, up.normalized);
    }
}
