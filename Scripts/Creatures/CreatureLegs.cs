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
            if (State[l].stepT >= 1f)
            {
                State[l].stepping = false; State[l].stepT = 1f;
                target[l] = State[l].stepTo;
            }
            else
            {
                float t = State[l].stepT;
                float sm = t * t * (3f - 2f * t);
                target[l] = Vector3.Lerp(State[l].stepFrom, State[l].stepTo, sm)
                            + Up(l) * (s.stepLift * scale * Mathf.Sin(Mathf.PI * t));
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
            eligible[l] = !State[l].stepping && (dist > thr || stretched || (idle && dist > kIdleTidyFraction * thr));
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
        LegSpec s = specs[l];
        Quaternion r = rotations[s.attachJoint];
        // Where the rest point will be when the step lands (the body keeps moving during the swing),
        // plus the species' overshoot, trimmed so the foot can still reach it. An idle tidy-up
        // lands exactly on the rest point.
        Vector3 fwd = r * Vector3.forward;
        float travel = speed < kIdleSpeed ? 0f : speed * s.stepDuration;
        float over = speed < kIdleSpeed ? 0f : s.overshoot * scale;
        float maxDist = kLandReach * (s.upperLength + s.lowerLength) * scale;
        for (int i = 0; i < 6 && over > 1e-4f && (rest[l] + fwd * over - State[l].shoulder).sqrMagnitude > maxDist * maxDist; i++)
            over *= 0.6f;
        Vector3 goal = rest[l] + fwd * (travel + over);
        Vector3 u = Up(l);
        Vector3 p = goal, n = u;
        if (!ground.Probe(goal, u, kProbeWindow * scale, out p, out n)) { p = goal; n = State[l].footNormal; }
        State[l].stepFrom = target[l];
        State[l].stepTo = p;
        State[l].footNormal = n.sqrMagnitude > 1e-12f ? n.normalized : u;
        State[l].stepT = 0f;
        State[l].stepping = true;
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
        float worst = 0f;
        for (int l = 0; l < specs.Length; l++)
        {
            if (State[l].stepping) continue;
            float reach = (specs[l].upperLength + specs[l].lowerLength) * scale;
            float now = (target[l] - State[l].shoulder).magnitude;
            // a stretch no step can cure (rest point itself out of reach) must not stop the body for good
            if ((rest[l] - State[l].shoulder).magnitude >= now - kStepGain * scale) continue;
            worst = Mathf.Max(worst, now / reach);
        }
        Throttle = Mathf.Clamp01((kStopAt - worst) / (kStopAt - kSlowAt));
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
            dst[offset + sp.LegBone(l, 0)] = Matrix4x4.TRS(st.shoulder, BoneRot(st.elbow - st.shoulder, pole, ar), sc);
            dst[offset + sp.LegBone(l, 1)] = Matrix4x4.TRS(st.elbow, BoneRot(st.foot - st.elbow, pole, ar), sc);

            Vector3 n = st.footNormal.sqrMagnitude > 1e-12f ? st.footNormal.normalized : Vector3.up;
            Vector3 f = Vector3.ProjectOnPlane(ar * Vector3.forward, n);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.ProjectOnPlane(ar * Vector3.up, n);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.ProjectOnPlane(Vector3.forward, n);
            dst[offset + sp.LegBone(l, 2)] = Matrix4x4.TRS(st.foot, Quaternion.LookRotation(f.normalized, n), sc);
        }
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
