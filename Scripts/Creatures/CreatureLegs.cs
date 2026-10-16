using System;
using UnityEngine;

public struct LegState
{
    public Vector3 shoulder, elbow, foot, footNormal, stepFrom, stepTo;
    public bool stepping; public float stepT;
}

// Leg logic of a CreatureSim: rest points, gait groups, stepping, IK, bone output.
// Port of Leg.update / Leg.ik in video_manim/lizard.py. Allocation-free after construction.
sealed class CreatureLegs
{
    const float kProbeWindow = 0.5f;
    const float kIdleSpeed = 0.05f;
    const float kIdleTidyFraction = 0.3f;
    const float kStrideReach = 0.9f, kStrideUse = 0.5f;
    const float kLandReach = 0.9f;
    const float kOverextend = 0.91f;       // planted foot at this fraction of full reach becomes eligible to step
    const float kSlowAt = 0.92f, kStopAt = 0.985f; // reach fractions where the body starts to slow and stops
    const float kStepGain = 0.005f;        // a stretch-triggered step must shorten the foot's reach by this much
    const float kDroopTarget = 0.97f, kDroopSlack = 0.02f, kDroopMaxFrac = 0.18f, kDroopRate = 0.3f;
    // Joint-range limits (attach-joint frame), so a planted foot never drags the limb into a pose the
    // skinned mesh cannot follow: the upper bone's swing from its rest direction, and the knee bend's
    // deviation from its rest bend. Past a limit the foot steps; a little further the body slows and stops.
    const float kSwingLimitDeg = 55f, kKneeRangeDeg = 35f;
    const float kJointGainDeg = 2f;                 // a joint-triggered step must improve the angle by this much
    const float kJointSlowDeg = 2f, kJointStopDeg = 10f; // throttle ramp, degrees past the limit

    readonly CreatureSpecies sp;
    readonly IGroundProbe ground;
    readonly Func<Vector3, Vector3> upAt;
    readonly Vector3[] joints;
    readonly Quaternion[] rotations;
    readonly float scale;
    readonly LegSpec[] specs;
    public readonly LegState[] State;
    readonly Vector3[] target;     // intended foot position: planted point, or the moving step point
    readonly Vector3[] rest;       // current ground-projected rest point
    readonly bool[] eligible;
    readonly Vector3[] restUpper;  // rest upper-bone direction in the attach-joint frame
    readonly float[] restKnee;     // rest knee bend, degrees
    readonly float[] minReach;     // shoulder-foot distance at the knee's flex limit, unscaled
    int lastGroup = -1;

    // Per spine joint: how far the body is lowered toward the ground so the legs on it can reach
    // (a leg whose rest point lies on much lower ground, e.g. one side of a steep face).
    public readonly float[] Droop;       // per attach joint, as computed from its legs
    public readonly float[] DroopSpread; // per spine joint: Droop smoothed over +-3 joints, what the sim applies

    // 1 = free to walk, 0 = a planted foot is at full stretch and the body must wait for its step.
    public float MaxSpeed { get; private set; } = float.PositiveInfinity;
    public float Throttle { get; private set; } = 1f;

    public CreatureLegs(CreatureSpecies sp, IGroundProbe ground, Func<Vector3, Vector3> upAt,
                        Vector3[] joints, Quaternion[] rotations, float scale)
    {
        this.sp = sp; this.ground = ground; this.upAt = upAt;
        this.joints = joints; this.rotations = rotations; this.scale = scale;
        specs = sp.legs ?? new LegSpec[0];
        int c = specs.Length;
        Droop = new float[joints.Length]; DroopSpread = new float[joints.Length];
        State = new LegState[c]; target = new Vector3[c]; rest = new Vector3[c]; eligible = new bool[c];
        restUpper = new Vector3[c]; restKnee = new float[c]; minReach = new float[c];
        for (int l = 0; l < c; l++)
        {
            // flat reference stance in the attach-joint frame (as StrideLimitedSpeed): independent of the spawn
            LegSpec s = specs[l];
            Vector3 sh = Mirror(s.shoulderOffset, l);
            Vector3 rs = Mirror(s.restOffset, l); rs.y = -sp.joints[s.attachJoint].clearance;
            Vector3 el = TwoBoneIK.Solve(sh, rs, s.upperLength, s.lowerLength, Mirror(s.pole, l), out Vector3 ft);
            restUpper[l] = (el - sh).normalized;
            restKnee[l] = Vector3.Angle(el - sh, ft - el);
            float bend = Mathf.Min(170f, restKnee[l] + kKneeRangeDeg) * Mathf.Deg2Rad;
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
        }
        SolveIK();
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
            float cycle = Mathf.Max(2, groups) * Mathf.Max(1e-3f, s.stepDuration);
            best = Mathf.Min(best, kStrideUse * 2f * half / cycle);
        }
        // never a standstill for a stance the reach cannot cover; the throttle handles real ground
        best = Mathf.Max(best, 0.05f);
        return best;
    }

    Vector3 Mirror(Vector3 v, int l) { v.x *= specs[l].side; return v; }
    Vector3 Up(int l) { Vector3 u = upAt(joints[specs[l].attachJoint]); return u.sqrMagnitude > 1e-12f ? u.normalized : Vector3.up; }

    // Returns the number of steps started this sub-step.
    public int Update(float dt, float speed)
    {
        int c = specs.Length;
        if (c == 0) return 0;
        bool idle = speed < kIdleSpeed;

        // rest points
        for (int l = 0; l < c; l++)
        {
            LegSpec s = specs[l];
            Vector3 u = Up(l);
            Vector3 near = joints[s.attachJoint] + rotations[s.attachJoint] * (Mirror(s.restOffset, l) * scale);
            if (ground.Probe(near, u, kProbeWindow * scale, out Vector3 p, out _)) rest[l] = p; // else hold the last rest
            State[l].shoulder = joints[s.attachJoint] + rotations[s.attachJoint] * (Mirror(s.shoulderOffset, l) * scale);
        }

        UpdateDroop(dt);

        // advance steps in flight
        for (int l = 0; l < c; l++)
        {
            if (!State[l].stepping) continue;
            LegSpec s = specs[l];
            State[l].stepT += dt / Mathf.Max(1e-4f, s.stepDuration);
            // re-aim at where the rest point will be on landing: in a turn the body does not travel straight
            Aim(l, speed, Mathf.Max(0f, 1f - State[l].stepT));
            if (State[l].stepT >= 1f)
            {
                State[l].stepping = false; State[l].stepT = 1f;
                target[l] = State[l].stepTo;
            }
            else
            {
                float t = State[l].stepT;
                float sm = t * t * (3f - 2f * t);
                // the swing passes over the rest point at mid-step (quadratic Bezier through it), so the
                // limb swings through its rest pose instead of cutting past the shoulder
                Vector3 a = State[l].stepFrom, b = State[l].stepTo;
                Vector3 ctrl = 2f * rest[l] - 0.5f * (a + b);
                Vector3 path = (1f - sm) * (1f - sm) * a + 2f * (1f - sm) * sm * ctrl + sm * sm * b;
                target[l] = KeepOutOfFlex(l, path + Up(l) * (s.stepLift * scale * Mathf.Sin(Mathf.PI * t)));
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
            bool overFlexed = !State[l].stepping && JointLimited(l, out _);
            eligible[l] = !State[l].stepping && (dist > thr || stretched || overFlexed || (idle && dist > kIdleTidyFraction * thr));
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

        SolveIK();
        return started;
    }

    // Lower the body on a joint while one of its legs has to reach too far for its rest point; raise it
    // again once every leg has slack. Smallest mechanism that keeps feet within reach of lower ground.
    void UpdateDroop(float dt)
    {
        for (int j = 0; j < Droop.Length; j++)
        {
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
            float step = kDroopRate * scale * dt;
            if (need > 0f) Droop[j] = Mathf.Min(cap, Droop[j] + Mathf.Min(need * 0.5f, step));
            else if (need < -kDroopSlack * scale && Droop[j] > 0f) Droop[j] = Mathf.Max(0f, Droop[j] - step);
        }

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
        State[l].stepFrom = target[l];
        Aim(l, speed, 1f);
        State[l].stepT = 0f;
        State[l].stepping = true;
    }

    // Sets stepTo (and the landing normal): where the rest point will be when the step lands (the body
    // keeps moving during the remaining fraction of the swing), plus the species' overshoot, trimmed so
    // the foot can still reach it. An idle tidy-up lands exactly on the rest point.
    void Aim(int l, float speed, float remaining)
    {
        LegSpec s = specs[l];
        Quaternion r = rotations[s.attachJoint];
        Vector3 fwd = r * Vector3.forward;
        float travel = speed < kIdleSpeed ? 0f : speed * s.stepDuration * remaining;
        float over = speed < kIdleSpeed ? 0f : s.overshoot * scale;
        float maxDist = kLandReach * (s.upperLength + s.lowerLength) * scale;
        for (int i = 0; i < 6 && over > 1e-4f && (rest[l] + fwd * over - State[l].shoulder).sqrMagnitude > maxDist * maxDist; i++)
            over *= 0.6f;
        Vector3 goal = rest[l] + fwd * (travel + over);
        Vector3 u = Up(l);
        Vector3 p = goal, n = u;
        if (!ground.Probe(goal, u, kProbeWindow * scale, out p, out n)) { p = goal; n = State[l].footNormal; }
        State[l].stepTo = p;
        State[l].footNormal = n.sqrMagnitude > 1e-12f ? n.normalized : u;
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

    void SolveIK()
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
        UpdateThrottle();
    }

    void UpdateThrottle()
    {
        float worst = 0f, jointThrottle = 1f;
        for (int l = 0; l < specs.Length; l++)
        {
            if (State[l].stepping) continue;
            if (JointLimited(l, out float excess))
                jointThrottle = Mathf.Min(jointThrottle, Mathf.Clamp01((kJointStopDeg - excess) / (kJointStopDeg - kJointSlowDeg)));
            float reach = (specs[l].upperLength + specs[l].lowerLength) * scale;
            float now = (target[l] - State[l].shoulder).magnitude;
            // a stretch no step can cure (rest point itself out of reach) must not stop the body for good
            if ((rest[l] - State[l].shoulder).magnitude >= now - kStepGain * scale) continue;
            worst = Mathf.Max(worst, now / reach);
        }
        Throttle = Mathf.Min(jointThrottle, Mathf.Clamp01((kStopAt - worst) / (kStopAt - kSlowAt)));
    }

    // Swing of the upper bone from its rest direction and knee-bend deviation from rest, in degrees.
    void JointAngles(int l, Vector3 shoulder, Vector3 foot, out float swing, out float knee)
    {
        LegSpec s = specs[l];
        Quaternion r = rotations[s.attachJoint];
        Vector3 elbow = TwoBoneIK.Solve(shoulder, foot, s.upperLength * scale, s.lowerLength * scale, r * Mirror(s.pole, l), out Vector3 f);
        swing = Vector3.Angle(Quaternion.Inverse(r) * (elbow - shoulder), restUpper[l]);
        knee = Mathf.Abs(Vector3.Angle(elbow - shoulder, f - elbow) - restKnee[l]);
    }

    // True when the leg's current (planted) pose is past a joint-range limit and stepping to the rest
    // point would improve it (the stretch trigger's hysteresis: no zero-gain re-steps).
    // excess = degrees past the worst such limit.
    bool JointLimited(int l, out float excess)
    {
        excess = 0f;
        Vector3 sh = State[l].shoulder;
        JointAngles(l, sh, target[l], out float swing, out float knee);
        if (swing <= kSwingLimitDeg && knee <= kKneeRangeDeg) return false;
        JointAngles(l, sh, rest[l], out float swingR, out float kneeR);
        bool limited = false;
        if (swing > kSwingLimitDeg && swingR < swing - kJointGainDeg) { limited = true; excess = swing - kSwingLimitDeg; }
        if (knee > kKneeRangeDeg && kneeR < knee - kJointGainDeg) { limited = true; excess = Mathf.Max(excess, knee - kKneeRangeDeg); }
        return limited;
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
