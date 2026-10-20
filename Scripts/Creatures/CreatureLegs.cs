using System;
using UnityEngine;

public struct LegState
{
    public Vector3 shoulder, elbow, foot, footNormal, stepFrom, stepTo;
    // the two-bone limb's lower-bone end as drawn: the foot's contact point raised by the ankle height along
    // footNormal (LegSpec.AnkleHeight; = foot for an arthropod leg, a wing arm or ankleHeight < 0). elbow is the
    // drawn knee for that ankle. The gait (steps, joint limits, throttle) reads the limb with its end on the
    // contact point, as before ankles existed, so an ankle changes the look and not the walk.
    public Vector3 ankle;
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
    // arthropod: the planted ankle's coxa distance, x ReachShort, where the body starts to slow and stops. The solver
    // clamps a foot nearer than ReachShort (the leg cannot fold further) and the clamp then drags the planted foot;
    // the clamp triggers a step, but the tripod may hold that step back a few ticks (the other group's turn), so
    // the body must not run the coxa onto the foot meanwhile (DesertBeetle_FeetPlanted_DoNotMove_WeaveSweep)
    const float kNearSlow = 1.3f, kNearStop = 1.04f;
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
    // Climbing: a rest point whose ground faces the leg's up less than this (a trunk curving away under a wide
    // stance) is replaced by a wrap cast toward the spine's ground point, so the foot hugs the surface.
    const float kGrazing = 0.3f;
    // Climbing grip: on a surface steeper than this steepness a step starts only while at least one foot per
    // girdle and half of all feet stay planted.
    const float kGripSteep = 0.5f;
    const float kTrunkSameFace = 0.8f; // climbing: trunk keep-out only from spine segments whose up is within ~35 deg
    // Climbing: a planted foot stretched past this fraction of its reach steps at once, whatever the gait groups
    // and the grip rule say - waiting for the other group (or for a girdle partner on the bark) the body's turn onto
    // and off the trunk stretched it on past its reach, and the IK dragged it; lifted, it at least swings.
    const float kClimbUrgent = 0.97f;
    const float kClimbRestReach = 0.85f; // climbing: a rest point never further from the shoulder than this x reach
    const float kClimbJointRelax = 1f; // climbing: share of the joint-range brake removed (the steps remain)

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
    readonly bool[] eligible, urgent;
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
    readonly CreatureClimb climb;  // null: walking only

    // Perched grip (a flyer standing on a branch; set by CreatureFlight, default off = the walking code
    // unchanged): a rest or landing point whose probe finds nothing near the body's own support (the ground
    // far below a branch, or a branch flank curving away) is replaced by a cast from it toward the spine's
    // ground point on the real surface (the wrap cast climbing uses), so the feet close round the branch; the
    // uphill rest slide is skipped (it would walk the feet off the branch).
    public bool Grip
    {
        get => grip;
        set { grip = value; for (int k = 0; k < gripValid.Length; k++) gripValid[k] = false; }
    }
    bool grip;
    const float kGripDrop = 0.6f;
    const float kGripFacing = 0.5f;  // a gripped point faces up at least this much (60 deg from the top of a branch)
    const int kGripIters = 5;        // the spine's line, then 4 bisection probes
    const float kGripCacheMove = 0.01f; // m at scale 1 the rest stance may move before the grip point is searched again
    readonly Vector3[] gripNear, gripP, gripN; // per leg: the cached grip rest point and the stance it was found for
    readonly bool[] gripValid;  // x the attach joint's clearance: a rest point lower than the spine's ground by this is off the perch
    readonly int[] footSource;     // per leg: what its foot stands on, for the probe's tie preference

    // Arthropod legs (LegSpec.segments), solved by ArthropodLegIK; a two-bone leg has a null model and takes the
    // original code path unchanged. reachLen is every leg's foot reach (upper + lower for a two-bone leg).
    readonly ArthropodLegModel[] models;
    readonly float[] reachLen;
    readonly float[] ankleH;       // two-bone: ankle height above the contact point, metres at scale 1 (0 = none)
    readonly Vector3[] gaitElbow;  // two-bone: the knee of the limb ending on the contact point (the gait's limb)
    readonly Quaternion[] restUpperRot; // two-bone: the upper bone's rest rotation in the attach frame (UpperRot)
    readonly Vector3[] restYaw;    // arthropod: rest outward direction in the attach frame
    readonly float[] liftDelayT;   // arthropod: lift-off delay within its gait group, in step fractions
    readonly ArthropodLegPose[] poses;
    float feelerPhase;
    // Wave gait (sp.waveGait): the walking legs' distinct gait groups in ascending id - the order they step in -
    // and the place of the group whose turn is next. null = the event-driven gait.
    readonly int[] waveOrder;
    int waveNext;
    // In a wave a planted foot of the group whose turn it is goes along when it has drifted this fraction of its
    // step threshold from its rest point (an eligible foot always does): the rhythm stays even, without stamps.
    const float kWaveJoin = 0.25f;
    // every arthropod leg's points (coxa base, joints, ankle, foot) from PointOffset[l]; the leg plane's normal
    public readonly Vector3[] Points;
    public readonly int[] PointOffset;
    public readonly Vector3[] PlaneNormal;

    // Per spine joint: how far the body is lowered toward the ground so the legs on it can reach
    // (a leg whose rest point lies on much lower ground, e.g. one side of a steep face).
    public readonly float[] Droop;       // per attach joint, as computed from its legs
    public readonly float[] DroopSpread; // per spine joint: Droop smoothed over +-3 joints, what the sim applies

    // 1 = free to walk, 0 = a planted foot is at full stretch and the body must wait for its step.
    public float MaxSpeed { get; private set; } = float.PositiveInfinity;
    float walkMaxSpeed = float.PositiveInfinity; // StrideLimitedSpeed: MaxSpeed while walking
    // Sideways walking (CreatureSim, species sideways): the travel direction in the attach joints' frame (forward, or
    // +-right), which steps aim along; walking to a side, MaxSpeed is the stride limit across the body. Off: as before.
    bool sideways;
    float sideMaxSpeed = float.PositiveInfinity;
    public Vector3 TravelLocal = Vector3.forward;
    public bool Sideways
    {
        get => sideways;
        set { sideways = value; if (value) sideMaxSpeed = SideStrideSpeed(); }
    }
    int gaitGroups;                              // distinct gait groups of the walking legs
    public float Throttle { get; private set; } = 1f;
    // Climbing: the fastest the body may go and still stop (braking at StopDecel) before a planted foot reaches
    // kStopAt of its reach; +inf while walking. The throttle's ramp alone (kSlowAt..kStopAt) is shorter than the
    // braking distance of a lab-sized monitor at its stride-limited speed (8 cm of stretch against 9 cm of braking at
    // 1.3 m/s): the waiting leg was stretched past its reach and the IK dragged the planted foot.
    public float StopSpeed { get; private set; } = float.PositiveInfinity;
    public float StopDecel = 0f;
    float climbStride; bool climbStrideValid;   // ClimbStrideSpeed low-passed (a cap that toggled jerked the body)
    const float kClimbStrideTau = 0.3f;         // seconds (x sqrt scale)

    public CreatureLegs(CreatureSpecies sp, IGroundProbe ground, Func<Vector3, Vector3> upAt,
                        Vector3[] joints, Quaternion[] rotations, float scale, float timeScale = 1f, CreatureClimb climb = null)
    {
        this.sp = sp; this.ground = ground; this.upAt = upAt; this.climb = climb;
        this.joints = joints; this.rotations = rotations; this.scale = scale;
        this.timeScale = timeScale > 0f ? timeScale : 1f;
        specs = sp.legs ?? new LegSpec[0];
        int c = specs.Length;
        Droop = new float[joints.Length]; DroopSpread = new float[joints.Length];
        State = new LegState[c]; target = new Vector3[c]; rest = new Vector3[c]; eligible = new bool[c]; urgent = new bool[c];
        restUpper = new Vector3[c]; restKnee = new float[c]; minReach = new float[c];
        stepOver = new float[c]; jointLimited = new bool[c]; restReach = new float[c];
        prevSwing = new float[c]; prevKnee = new float[c];
        footSource = new int[c];
        gripNear = new Vector3[c]; gripP = new Vector3[c]; gripN = new Vector3[c]; gripValid = new bool[c];
        fwdLimit = new float[c]; backLimit = new float[c]; kneeRange = new float[c]; kneeFlex = new float[c];
        trunkKeep = new float[c]; trunkStep = new float[c]; trunkLo = new int[c]; trunkHi = new int[c]; trunkSlack = new float[c];
        trunkHalfWidth = new float[c];
        swingSlack = new float[c]; aimSlack = new float[c]; swingPrev = new Vector3[c]; aimPrev = new Vector3[c];
        for (int l = 0; l < c; l++) { prevSwing[l] = -1f; prevKnee[l] = -1f; }
        models = new ArthropodLegModel[c]; reachLen = new float[c]; ankleH = new float[c]; gaitElbow = new Vector3[c]; restUpperRot = new Quaternion[c]; restYaw = new Vector3[c]; liftDelayT = new float[c];
        poses = new ArthropodLegPose[c]; PointOffset = new int[c]; PlaneNormal = new Vector3[c];
        int points = 0;
        for (int l = 0; l < c; l++)
        {
            models[l] = sp.LegModel(l);
            reachLen[l] = sp.LegReach(l);
            ankleH[l] = specs[l].AnkleHeight;
            for (int w = 0; w < sp.WingCount; w++) if (sp.wings[w] != null && sp.wings[w].leg == l) ankleH[l] = 0f;   // a wing arm walks on its wrist
            PointOffset[l] = points;
            if (models[l] != null) points += models[l].PointCount;
            Vector3 ry = Mirror(specs[l].restOffset, l); ry.y = 0f;
            restYaw[l] = SurfaceMath.Unit(ry, new Vector3(specs[l].side, 0f, 0f));
        }
        Points = new Vector3[points];
        if (sp.waveGait) waveOrder = WaveOrder(specs);
        // the metachronal wave: within a gait group the rearmost leg lifts first, each leg further forward later
        for (int l = 0; l < c; l++)
        {
            if (models[l] == null || specs[l].liftDelay <= 0f) continue;
            int rank = 0;
            for (int m = 0; m < c; m++)
                if (m != l && !specs[m].feeler && specs[m].gaitGroup == specs[l].gaitGroup && Behind(m, l)) rank++;
            liftDelayT[l] = specs[l].liftDelay * rank;
        }
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
            if (models[l] != null)
            {
                restReach[l] = Mathf.Min((rs - sh).magnitude, reachLen[l]);
                minReach[l] = models[l].ReachShort;
            }
            else
            {
                Vector3 el = TwoBoneIK.Solve(sh, rs, s.upperLength, s.lowerLength, Mirror(s.pole, l), out Vector3 ft);
                restUpper[l] = (el - sh).normalized;
                restUpperRot[l] = BoneRot(restUpper[l], Mirror(s.pole, l), Quaternion.identity);
                restKnee[l] = Vector3.Angle(el - sh, ft - el);
                restReach[l] = (ft - sh).magnitude;
            }
            float gap = Mathf.Max(0f, Mathf.Abs(s.restOffset.x) - sp.joints[s.attachJoint].halfWidth);
            trunkKeep[l] = kTrunkKeepFrac * gap; trunkStep[l] = kTrunkStepFrac * gap;
            // the spine segments within the foot's reach along the body, +1 for the segment it starts in
            int span = sp.spacing > 0f ? Mathf.CeilToInt((reachLen[l] + s.overshoot + Mathf.Abs(s.shoulderOffset.z)) / sp.spacing) + 1 : 0;
            trunkLo[l] = Mathf.Max(0, s.attachJoint - span); trunkHi[l] = Mathf.Min(joints.Length - 1, s.attachJoint + span);
            for (int i = trunkLo[l]; i <= trunkHi[l]; i++) trunkHalfWidth[l] = Mathf.Max(trunkHalfWidth[l], sp.joints[i].halfWidth);
            if (models[l] != null) continue;
            float bend = Mathf.Min(170f, restKnee[l] + kneeFlex[l]) * Mathf.Deg2Rad;
            float a = s.upperLength, b = s.lowerLength;
            minReach[l] = Mathf.Sqrt(Mathf.Max(0f, a * a + b * b + 2f * a * b * Mathf.Cos(bend)));
        }
        for (int l = 0; l < c; l++)
        {
            Vector3 u = Up(l);
            if (specs[l].feeler)
            {
                rest[l] = target[l] = FeelerPoint(l);
                State[l].footNormal = State[l].stepFromNormal = State[l].stepToNormal = u;
                continue;
            }
            Vector3 so = Mirror(specs[l].restOffset, l) * scale;
            Vector3 near = joints[specs[l].attachJoint] + rotations[specs[l].attachJoint] * so;
            Vector3 p = near; Vector3 n = u;
            ground.Probe(near, u, Window(l), out p, out n);
            rest[l] = p;
            target[l] = p;
            State[l].footNormal = SurfaceMath.Unit(n, u);
            State[l].stepFromNormal = State[l].stepToNormal = State[l].footNormal;
        }
        SolveIK(0f);
        MaxSpeed = walkMaxSpeed = StrideLimitedSpeed();
    }

    // Puts every foot down on its rest point under the current body (a flying creature touching down:
    // the feet were tucked, their planted points left far behind). Nothing is stepping afterwards.
    internal void Replant()
    {
        for (int l = 0; l < specs.Length; l++)
        {
            Vector3 u = Up(l);
            if (specs[l].feeler)
            {
                // a feeler (a pedipalp, a folded wing arm) is posed where it always is, never put on the ground
                rest[l] = target[l] = FeelerPoint(l);
                State[l].stepping = false; State[l].stepT = 0f;
                State[l].footNormal = State[l].stepFromNormal = State[l].stepToNormal = u;
                continue;
            }
            Vector3 near = joints[specs[l].attachJoint] + rotations[specs[l].attachJoint] * (Mirror(specs[l].restOffset, l) * scale);
            Vector3 p = near - u * (sp.joints[specs[l].attachJoint].clearance * scale), n = u;
            bool got = ground.Probe(near, u, Window(l), out Vector3 gp, out Vector3 gn);
            if (got) { p = gp; n = gn; }
            if (Grip) GripPoint(l, near, u, got, ref p, ref n);
            rest[l] = p; target[l] = p;
            State[l].stepping = false; State[l].stepT = 0f;
            State[l].footNormal = SurfaceMath.Unit(n, u);
            State[l].stepFromNormal = State[l].stepToNormal = State[l].footNormal;
            prevSwing[l] = -1f; prevKnee[l] = -1f;
        }
        for (int i = 0; i < Droop.Length; i++) { Droop[i] = 0f; DroopSpread[i] = 0f; }
        spreadNonZero = false;
        SolveIK(0f);
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
            if (specs[l].feeler) continue;
            int bit = 1 << (specs[l].gaitGroup & 31);
            if ((seen & bit) == 0) { seen |= bit; groups++; }
        }
        gaitGroups = groups;
        float best = float.PositiveInfinity;
        for (int l = 0; l < c; l++)
        {
            LegSpec s = specs[l];
            if (s.feeler) continue;
            // Flat reference stance in the attach-joint frame: independent of where the creature spawned.
            Vector3 sh = Mirror(s.shoulderOffset, l) * scale;
            Vector3 rs = Mirror(s.restOffset, l) * scale;
            rs.y = -sp.joints[s.attachJoint].clearance * scale;
            Vector3 v = rs - sh;
            float along = v.z;
            float perp2 = Mathf.Max(0f, v.sqrMagnitude - along * along);
            float reach = kStrideReach * reachLen[l] * scale;
            float half = Mathf.Sqrt(Mathf.Max(0f, reach * reach - perp2));
            float cycle = Mathf.Max(2, groups) * Mathf.Max(1e-3f, s.stepDuration * timeScale);
            best = Mathf.Min(best, kStrideUse * 2f * half / cycle);
        }
        // never a standstill for a stance the reach cannot cover; the throttle handles real ground
        best = Mathf.Max(best, 0.05f);
        return best;
    }

    // StrideLimitedSpeed while climbing: a leg waits out the other group's step, which takes longer on a steep surface
    // (StepTimeFactor), and the stride it can take is what its reach leaves beside its rest point now - on a trunk
    // the stance's rest points lie further out across the body (they curve round it) than on flat ground. With the
    // walking cap the body outran a waiting leg (1.2 x its reach at 1.3 m/s, the head back on the ground while the
    // hips were still on the bark) and the IK dragged its planted foot.
    // StrideLimitedSpeed for travel across the body: each foot's path runs along the attach frame's x through its rest
    // point; it can move from there inward to the leg's shortest reach and outward to its stride reach, and the smaller
    // of the two (either way: leading legs pull in, trailing legs push out) is half its usable stride.
    float SideStrideSpeed()
    {
        float best = float.PositiveInfinity;
        int groups = Mathf.Max(2, gaitGroups);
        for (int l = 0; l < specs.Length; l++)
        {
            LegSpec s = specs[l];
            if (s.feeler) continue;
            Vector3 sh = Mirror(s.shoulderOffset, l) * scale;
            Vector3 rs = Mirror(s.restOffset, l) * scale;
            rs.y = -sp.joints[s.attachJoint].clearance * scale;
            Vector3 v = rs - sh;
            float along = Mathf.Abs(v.x);
            float perp2 = v.y * v.y + v.z * v.z;
            float outer = Mathf.Sqrt(Mathf.Max(0f, Sq(kStrideReach * reachLen[l] * scale) - perp2));
            float inner = Mathf.Sqrt(Mathf.Max(0f, Sq(minReach[l] * scale) - perp2));
            float half = Mathf.Max(0f, Mathf.Min(outer - along, along - inner));
            float cycle = groups * Mathf.Max(1e-3f, s.stepDuration * timeScale);
            best = Mathf.Min(best, kStrideUse * 4f * half / cycle);
        }
        return Mathf.Max(best, 0.05f);
    }

    static float Sq(float x) => x * x;

    float ClimbStrideSpeed()
    {
        float best = walkMaxSpeed;
        for (int l = 0; l < specs.Length; l++)
        {
            LegSpec s = specs[l];
            if (s.feeler) continue;
            Vector3 fwd = rotations[s.attachJoint] * Vector3.forward;
            Vector3 v = rest[l] - State[l].shoulder;
            float along = Vector3.Dot(v, fwd);
            float perp2 = Mathf.Max(0f, v.sqrMagnitude - along * along);
            float reach = kStrideReach * reachLen[l] * scale;
            float half = Mathf.Sqrt(Mathf.Max(0f, reach * reach - perp2));
            float cycle = Mathf.Max(2, gaitGroups) * Mathf.Max(1e-3f, s.stepDuration * timeScale * Factor(sp.climb.StepTimeFactor, Steep(l)));
            best = Mathf.Min(best, kStrideUse * 2f * half / cycle);
        }
        return Mathf.Max(best, 0.05f);
    }

    public Vector3 RestPoint(int l) => rest[l];

    Vector3 Mirror(Vector3 v, int l) { v.x *= specs[l].side; return v; }

    // Leg m sits behind leg l (further toward the tail): a later attach joint, or the same one further back.
    bool Behind(int m, int l) => specs[m].attachJoint != specs[l].attachJoint
        ? specs[m].attachJoint > specs[l].attachJoint
        : specs[m].shoulderOffset.z < specs[l].shoulderOffset.z || (specs[m].shoulderOffset.z == specs[l].shoulderOffset.z && m < l);

    // A feeler's (pedipalp's) tip: restOffset in the attach frame, not projected to the ground.
    Vector3 FeelerPoint(int l) => joints[specs[l].attachJoint] + rotations[specs[l].attachJoint] * (Mirror(specs[l].restOffset, l) * scale);
    Vector3 Up(int l)
    {
        if (Climbing) return climb.Up[specs[l].attachJoint];
        Vector3 u = upAt(joints[specs[l].attachJoint]); return SurfaceMath.Unit(u, Vector3.up);
    }

    // ---- climbing ----

    bool Climbing => climb != null && climb.Active;
    // A walking foot probe's window: kProbeWindow, or (a tall stance: the attach joint's clearance near the window
    // or deeper) that clearance plus half the window - with the plain window a probe from a shoulder 0.74 m up
    // missed a window-limited surface (colliders) and the foot stayed in the air. Every species whose clearances
    // are under 0.9 x the window (all but the winged serpent) probes exactly as before.
    float Window(int l)
    {
        float c = sp.joints[specs[l].attachJoint].clearance;
        return (c > 0.9f * kProbeWindow ? c + 0.5f * kProbeWindow : kProbeWindow) * scale;
    }
    // The attach joint's steepness (0 walking, 1 on a vertical surface) and a gait factor lerped by it.
    float Steep(int l) => Climbing ? climb.Steep[specs[l].attachJoint] : 0f;
    static float Factor(float f, float steep) => 1f + (f - 1f) * steep;

    // The foot's ground along u: the plain probe while walking; climbing, the surface probe with the leg's
    // slot (k: 0 rest, 1 rest slide, 2 landing) and its tie preference.
    bool FootProbe(int l, int k, Vector3 near, Vector3 u, out Vector3 p, out Vector3 n)
    {
        if (!Climbing)
        {
            if (climb == null) return ground.Probe(near, u, Window(l), out p, out n);
            // walking near colliders: the same answer as the plain probe, with the leg's slot (cached raycasts)
            bool ok = climb.Surface.Probe(near, u, Window(l), SurfaceHit.NoSource, climb.LegSlot(l, k), out SurfaceHit wh);
            p = wh.point; n = wh.normal;
            return ok;
        }
        if (climb.Surface.Probe(near, u, kProbeWindow * scale, footSource[l], climb.LegSlot(l, k), out SurfaceHit h))
        {
            footSource[l] = h.source; p = h.point; n = h.normal;
            return true;
        }
        p = near; n = u;
        return false;
    }

    // Grip: the probed point p (got: found) stands if it lies on the body's own support (no lower than
    // kGripDrop clearances below the spine's ground point) and faces up (kGripFacing). Otherwise the foot
    // closes in toward the spine: the outermost point between the spine's line and `near` (a few bisection
    // probes along the leg's lateral offset) that is still on that support - on a branch, its flank on this
    // side; on a rock's edge, the edge. A probe miss holds the last answer. Rest points are cached while the
    // stance does not move (cache slot: the leg; -1 = no cache).
    bool GripPoint(int l, Vector3 near, Vector3 u, bool got, ref Vector3 p, ref Vector3 n, bool cache = false)
    {
        int aj = specs[l].attachJoint;
        float clear = sp.joints[aj].clearance * scale;
        Vector3 anchor = joints[aj] - u * clear;
        if (got && OnSupport(p, n, anchor, u, clear)) return true;
        if (cache && gripValid[l] && (near - gripNear[l]).sqrMagnitude < kGripCacheMove * kGripCacheMove * scale * scale)
        {
            p = gripP[l]; n = gripN[l];
            return true;
        }
        Vector3 fwd = rotations[aj] * Vector3.forward;
        Vector3 basePt = joints[aj] + fwd * Vector3.Dot(near - joints[aj], fwd);
        float lo = 0f, hi = 1f;
        bool found = false;
        Vector3 bp = p, bn = n;
        for (int it = 0; it < kGripIters; it++)
        {
            float f = it == 0 ? 0f : 0.5f * (lo + hi);
            // probed from just above the support's level (a tall stance's clearance is deeper than the window)
            Vector3 q = Vector3.Lerp(basePt, near, f) - u * Mathf.Max(0f, clear - 0.5f * kProbeWindow * scale);
            if (ground.Probe(q, u, kProbeWindow * scale, out Vector3 gp, out Vector3 gn) && OnSupport(gp, gn, anchor, u, clear))
            {
                bp = gp; bn = gn; found = true;
                if (it > 0) lo = f;
            }
            else if (it == 0) break; // nothing under the spine itself: no support to grip
            else hi = f;
        }
        if (!found) return got;
        p = bp; n = bn;
        if (cache) { gripValid[l] = true; gripNear[l] = near; gripP[l] = p; gripN[l] = n; }
        return true;
    }

    bool OnSupport(Vector3 p, Vector3 n, Vector3 anchor, Vector3 u, float clear)
        => Vector3.Dot(p - anchor, u) > -kGripDrop * clear && Vector3.Dot(n, u) >= kGripFacing;

    // A foot may lift on a steep surface only if the creature keeps its grip: one planted foot per girdle and
    // half of all feet.
    bool GripHolds(int l)
    {
        int c = specs.Length, planted = 0, walking = c;
        for (int m = 0; m < c; m++)
        {
            if (specs[m].feeler) { walking--; continue; }
            if (m != l && !State[m].stepping) planted++;
        }
        if (2 * planted < walking) return false;
        for (int a = 0; a < attachJoints.Length; a++)
        {
            int j = attachJoints[a];
            bool legs = false, held = false;
            for (int m = 0; m < c; m++)
            {
                if (specs[m].attachJoint != j || specs[m].feeler) continue;
                legs = true;
                if (m != l && !State[m].stepping) held = true;
            }
            if (legs && !held) return false;
        }
        return true;
    }

    // Returns the number of steps started this sub-step.
    public int Update(float dt, float speed)
    {
        int c = specs.Length;
        if (c == 0) return 0;
        bool idle = speed < kIdleSpeed * timeScale; // Froude speeds scale with sqrt(size)
        feelerPhase += dt * (2f + 6f * Mathf.Max(0f, speed) / Mathf.Max(1e-3f, scale)) / timeScale;
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
            if (s.feeler) { rest[l] = target[l] = FeelerPoint(l); State[l].footNormal = u; continue; }
            bool got = FootProbe(l, 0, near, u, out Vector3 p, out Vector3 pn);
            if (Climbing && (!got || Vector3.Dot(pn, u) < kGrazing))
            {
                // the surface curves away under the stance (a trunk): cast toward the spine's own ground point
                int aj = s.attachJoint;
                Vector3 anchor = joints[aj] - u * (sp.joints[aj].clearance * scale * climb.ClearanceScale(aj));
                Vector3 d = anchor - near;
                float len = d.magnitude;
                if (len > 1e-5f && climb.Surface.Cast(near, d / len, 1.5f * len, out SurfaceHit wh))
                {
                    p = wh.point; pn = wh.normal; footSource[l] = wh.source; got = true;
                }
            }
            if (Grip && !Climbing) got = GripPoint(l, near, u, got, ref p, ref pn, true);
            if (!got) continue; // hold the last rest
            if (Grip && !Climbing) { rest[l] = p; continue; }
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
                    if (!FootProbe(l, 1, q, u, out Vector3 pq, out _)) break;
                    p = pq;
                }
            }
            // Climbing, the stance's rest point can lie out of the leg's reach (a trunk curving away under the
            // wide stance, the ground far below a hip in the corner at the trunk's foot): no step could then cure
            // the stretch, the throttle ignored it and the body dragged the planted foot. Slide it in along the
            // surface toward the shoulder until it is within kClimbRestReach of the reach.
            if (Climbing) p = WithinReach(l, p, pn, sh, u);
            rest[l] = p;
        }

        // climbing, the stride-limited speed of the stance the legs have now (MaxSpeed, read by the next sub-step)
        if (Climbing)
        {
            float cs = ClimbStrideSpeed();
            if (!climbStrideValid) climbStride = cs;
            else climbStride += (cs - climbStride) * (1f - Mathf.Exp(-dt / (kClimbStrideTau * timeScale)));
            climbStrideValid = true;
            MaxSpeed = climbStride;
        }
        else { MaxSpeed = sideways && TravelLocal.z == 0f ? sideMaxSpeed : walkMaxSpeed; climbStrideValid = false; }

        UpdateDroop(dt);

        // advance steps in flight
        for (int l = 0; l < c; l++)
        {
            if (!State[l].stepping) continue;
            trunkSlack[l] = 0f; // the foot lands somewhere new: check it when planted
            LegSpec s = specs[l];
            float stepTime = s.stepDuration * timeScale;
            if (Climbing) stepTime *= Factor(sp.climb.StepTimeFactor, Steep(l));
            State[l].stepT += dt / Mathf.Max(1e-4f, stepTime);
            // re-aim at where the rest point will be on landing: in a turn the body does not travel straight
            Aim(l, speed, Mathf.Max(0f, 1f - State[l].stepT));
            if (State[l].stepT < 0f) { target[l] = State[l].stepFrom; continue; } // waiting for its place in the wave
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
                State[l].footNormal = SurfaceMath.Unit(fn, State[l].stepToNormal);
                // the swing passes over the rest point at mid-step (quadratic Bezier through it), so the
                // limb swings through its rest pose instead of cutting past the shoulder
                Vector3 a = State[l].stepFrom, b = State[l].stepTo;
                Vector3 ctrl = 2f * rest[l] - 0.5f * (a + b);
                Vector3 path = (1f - sm) * (1f - sm) * a + 2f * (1f - sm) * sm * ctrl + sm * sm * b;
                if (models[l] != null && s.swingRetract > 0f)
                {
                    // an arthropod draws the foot in toward the coxa mid-swing: the leg flexes and its knee rises
                    Vector3 u = Up(l), v = State[l].shoulder - path;
                    v -= u * Vector3.Dot(v, u);
                    path += v * (s.swingRetract * Mathf.Sin(Mathf.PI * t));
                }
                float lift = s.stepLift * scale;
                if (Climbing) lift *= Factor(sp.climb.LiftFactor, Steep(l));
                target[l] = KeepOutOfFlex(l, GatedKeepOff(l, path, ref swingSlack[l], ref swingPrev[l]) + Up(l) * (lift * Mathf.Sin(Mathf.PI * t)));
            }
        }

        // eligibility
        for (int l = 0; l < c; l++)
        {
            LegSpec s = specs[l];
            if (s.feeler) { eligible[l] = false; continue; }
            float dist = (target[l] - rest[l]).magnitude;
            float thr = s.stepThreshold * scale;
            if (Climbing) thr *= Factor(sp.climb.StrideFactor, Steep(l));
            // Also step when the planted foot nears full extension: with short-limbed rear legs the
            // reach runs out before stepThreshold does, and the IK would drag the foot.
            float reach = reachLen[l] * scale * kOverextend;
            float now = (target[l] - State[l].shoulder).magnitude;
            // only if stepping to the rest point would actually shorten the reach (else it is a zero-length step loop)
            bool stretched = now > reach && (rest[l] - State[l].shoulder).magnitude < now - kStepGain * scale;
            // an arthropod's planted foot out of the solver's reach (the leg could not follow it)
            if (models[l] != null && !State[l].stepping && poses[l].clamped && dist > kStepGain * scale) stretched = true;
            bool overFlexed = !State[l].stepping && jointLimited[l]; // from the last IK solve
            bool underTrunk = !State[l].stepping && TrunkLimited(l, trunkTravel);
            eligible[l] = !State[l].stepping && (dist > thr || stretched || overFlexed || underTrunk || (idle && dist > kIdleTidyFraction * thr));
            // climbing, a planted foot about to be dragged out of its reach steps now, out of turn (see kClimbUrgent)
            urgent[l] = Climbing && eligible[l] && now > kClimbUrgent / kOverextend * reach
                     && (rest[l] - State[l].shoulder).magnitude < now - kStepGain * scale;
        }

        // start steps
        int started = 0, startedGroup = -1;
        if (waveOrder != null) started = StartWave(speed);
        else for (int l = 0; l < c; l++)
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
            if (blocked && !urgent[l]) continue;
            // Alternate groups. Legs of a group that became eligible together start together, and a leg
            // may join its own group's step while that step is less than half done. Otherwise it waits
            // its turn: joining late would let one group hog the gait and starve the other.
            bool joinable = false;
            for (int m = 0; m < c; m++)
                if (m != l && specs[m].gaitGroup == g && State[m].stepping && State[m].stepT < 0.5f) joinable = true;
            if (!urgent[l] && lastGroup == g && otherEligible && startedGroup != g && !joinable) continue;
            if (!urgent[l] && Climbing && Steep(l) > kGripSteep && !GripHolds(l)) continue;
            StartStep(l, speed);
            lastGroup = g; startedGroup = g;
            started++;
        }

        SolveIK(dt);
        return started;
    }

    // ---- wave gait (sp.waveGait, T5.8) ----

    // The walking legs' distinct gait groups, ascending: the order a wave steps them in.
    static int[] WaveOrder(LegSpec[] specs)
    {
        int seen = 0, n = 0;
        for (int l = 0; l < specs.Length; l++) if (!specs[l].feeler) seen |= 1 << (specs[l].gaitGroup & 31);
        for (int g = 0; g < 32; g++) if ((seen & (1 << g)) != 0) n++;
        var order = new int[n];
        n = 0;
        for (int g = 0; g < 32; g++) if ((seen & (1 << g)) != 0) order[n++] = g;
        return order;
    }

    // The wave's step starts, in place of the event-driven rule. One group is in the air at a time, and the groups
    // take their turns strictly in order: when the last group has landed and any foot wants to step (eligible:
    // its stride, reach, joint range or the trunk), the next group in the order lifts - every one of its walking
    // feet that is eligible or has drifted kWaveJoin of its threshold - so the lift-offs run down the body as a
    // travelling wave (Walk.pattern Wave lays the groups out by pair). A group with no foot to move gives its turn
    // to the next. A foot of the group in the air may join while that step is less than half done; a climbing
    // foot about to be dragged (urgent) steps at once, as in the event-driven gait. Returns the steps started.
    int StartWave(float speed)
    {
        int c = specs.Length, started = 0, busy = -1;
        bool demand = false;
        for (int l = 0; l < c; l++)
        {
            if (State[l].stepping) busy = specs[l].gaitGroup;
            if (eligible[l]) demand = true;
        }
        for (int l = 0; l < c; l++)
            if (eligible[l] && urgent[l] && !State[l].stepping) { StartStep(l, speed); started++; }
        if (busy >= 0)
        {
            bool joinable = false;
            for (int m = 0; m < c; m++)
                if (specs[m].gaitGroup == busy && State[m].stepping && State[m].stepT < 0.5f) joinable = true;
            if (!joinable) return started;
            for (int l = 0; l < c; l++)
            {
                if (!eligible[l] || State[l].stepping || specs[l].gaitGroup != busy) continue;
                if (Climbing && Steep(l) > kGripSteep && !GripHolds(l)) continue;
                StartStep(l, speed); started++;
            }
            return started;
        }
        if (!demand) return started;
        for (int k = 0; k < waveOrder.Length; k++)
        {
            int at = (waveNext + k) % waveOrder.Length, g = waveOrder[at], n = 0;
            for (int l = 0; l < c; l++)
            {
                LegSpec s = specs[l];
                if (s.feeler || s.gaitGroup != g || State[l].stepping) continue;
                float thr = s.stepThreshold * scale;
                if (Climbing) thr *= Factor(sp.climb.StrideFactor, Steep(l));
                if (!eligible[l] && (target[l] - rest[l]).sqrMagnitude <= kWaveJoin * kWaveJoin * thr * thr) continue;
                if (Climbing && Steep(l) > kGripSteep && !GripHolds(l)) continue;
                StartStep(l, speed); n++;
            }
            if (n == 0) continue;
            waveNext = (at + 1) % waveOrder.Length;
            lastGroup = g;
            return started + n;
        }
        return started;
    }

    // A climbing rest point p (surface normal pn) slid along the surface toward the shoulder until it is within
    // kClimbRestReach of the leg's reach (kRestSlideIters probes at most; a miss keeps the last point found).
    Vector3 WithinReach(int l, Vector3 p, Vector3 pn, Vector3 sh, Vector3 u)
    {
        float maxR = kClimbRestReach * reachLen[l] * scale;
        for (int k = 0; k < kRestSlideIters; k++)
        {
            Vector3 v = p - sh;
            float d = v.magnitude;
            if (d <= maxR) break;
            // the stretch's part along the surface: moving the point back along it by s shortens d by ~ s |vt| / d
            Vector3 vt = v - pn * Vector3.Dot(v, pn);
            float vt2 = vt.sqrMagnitude;
            if (vt2 < 1e-10f) break;
            Vector3 q = p - vt * Mathf.Min(1f, (d - maxR) * d / vt2);
            if (!FootProbe(l, 1, q, u, out Vector3 pq, out Vector3 nq)) break;
            p = pq; pn = nq;
        }
        return p;
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
                if (s.attachJoint != j || s.feeler) continue;
                any = true;
                Vector3 v = rest[l] - State[l].shoulder;
                float d = v.magnitude;
                if (d < 1e-5f) { need = Mathf.Max(need, -1f); continue; }
                float reach = reachLen[l] * scale;
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
        Vector3 fwd = rotations[s.attachJoint] * (sideways ? TravelLocal : Vector3.forward);
        float over = speed < kIdleSpeed * timeScale ? 0f : s.overshoot * scale;
        if (Climbing) over *= Factor(sp.climb.StrideFactor, Steep(l));
        float maxDist = kLandReach * reachLen[l] * scale;
        for (int i = 0; i < 6 && over > 1e-4f && (rest[l] + fwd * over - State[l].shoulder).sqrMagnitude > maxDist * maxDist; i++)
            over *= 0.6f;
        stepOver[l] = over;
        State[l].stepFrom = target[l];
        State[l].stepFromNormal = State[l].footNormal;
        swingSlack[l] = aimSlack[l] = 0f; // a new swing and landing point: check both
        Aim(l, speed, 1f);
        State[l].stepT = -liftDelayT[l];
        State[l].stepping = true;
    }

    // Sets stepTo (and the landing normal): where the rest point will be when the step lands (the body
    // keeps moving during the remaining fraction of the swing) plus the step's fixed overshoot. Every
    // term is continuous in time (speed is accel-limited), so re-aiming never pops the swinging foot.
    void Aim(int l, float speed, float remaining)
    {
        LegSpec s = specs[l];
        Vector3 fwd = rotations[s.attachJoint] * (sideways ? TravelLocal : Vector3.forward);
        float travel = Mathf.Max(0f, speed) * s.stepDuration * timeScale * remaining;
        if (Climbing) travel *= Factor(sp.climb.StepTimeFactor, Steep(l));
        float over = stepOver[l];
        if (Climbing)
        {
            // the overshoot trimmed (as at lift-off) to the rest point it has now: on a trunk the rest point moves
            // about under the turning stance, and a landing past the reach left the planted foot dragged by the IK
            float maxDist = kLandReach * reachLen[l] * scale;
            for (int i = 0; i < 6 && over > 1e-4f && (rest[l] + fwd * over - State[l].shoulder).sqrMagnitude > maxDist * maxDist; i++)
                over *= 0.6f;
            if ((rest[l] + fwd * over - State[l].shoulder).sqrMagnitude > maxDist * maxDist) over = 0f;
        }
        Vector3 goal = rest[l] + fwd * (travel + over);
        Vector3 u = Up(l);
        Vector3 p = goal, n = u;
        bool landed = FootProbe(l, 2, goal, u, out p, out n);
        if (Grip && !Climbing) landed = GripPoint(l, goal, u, landed, ref p, ref n);
        if (!landed) { p = goal; n = State[l].stepToNormal.sqrMagnitude > 0.5f ? State[l].stepToNormal : State[l].footNormal; }
        // a landing point under the trunk (the inner feet of a tight turn) moves out from under it
        Vector3 q = GatedKeepOff(l, p, ref aimSlack[l], ref aimPrev[l]);
        if ((q - p).sqrMagnitude > 1e-12f)
        {
            if (FootProbe(l, 2, q, u, out Vector3 pq, out Vector3 nq)) { p = pq; n = nq; }
            else if (!Climbing) p = q; // climbing: a point pushed off the surface stays where the surface is
        }
        State[l].stepTo = p;
        State[l].stepToNormal = SurfaceMath.Unit(n, u);
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
            // climbing: a segment on another face of the surface (past a corner) is not under this foot's plane
            if (Climbing && Vector3.Dot(climb.Up[i], u) < kTrunkSameFace) continue;
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
        // climbing (and a perched grip): feet hug the surface the body is on, across corners the plan-view
        // outline means nothing
        if (Climbing || Grip) { prev = p; slack = 0f; return p; }
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
        if (Climbing || Grip) { trunkSlack[l] = 0f; return false; }
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
            if (models[l] != null) { SolveArthropod(l, shoulder, r); continue; }
            float l1 = s.upperLength * scale, l2 = s.lowerLength * scale;
            Vector3 elbow = gaitElbow[l] = TwoBoneIK.Solve(shoulder, target[l], l1, l2, pole, out Vector3 foot);
            State[l].foot = foot;
            if (ankleH[l] > 0f) State[l].elbow = TwoBoneIK.Solve(shoulder, foot + AnkleLift(l), l1, l2, pole, out State[l].ankle);
            else { State[l].elbow = elbow; State[l].ankle = foot; }
        }
        UpdateThrottle(dt);
    }

    void SolveArthropod(int l, Vector3 shoulder, Quaternion r)
    {
        LegSpec s = specs[l];
        ArthropodLegModel m = models[l];
        float beta = 0f;
        if (s.feeler) beta = s.kneeLiftDeg * (0.5f + 0.5f * Mathf.Sin(feelerPhase + 1.7f * l));
        else if (State[l].stepping && State[l].stepT > 0f) beta = s.kneeLiftDeg * Mathf.Sin(Mathf.PI * Mathf.Min(1f, State[l].stepT));
        int o = PointOffset[l];
        ArthropodLegIK.Solve(m, shoulder, target[l], State[l].footNormal, Up(l), r * restYaw[l], beta, scale, Points, o, out poses[l]);
        PlaneNormal[l] = poses[l].planeNormal;
        State[l].elbow = Points[o + m.KneeJoint];
        State[l].foot = State[l].ankle = Points[o + m.SegmentCount];
    }

    void UpdateThrottle(float dt)
    {
        float worst = 0f, jointThrottle = 1f, margin = float.PositiveInfinity;
        for (int l = 0; l < specs.Length; l++)
        {
            jointLimited[l] = false;
            if (specs[l].feeler) continue;
            if (State[l].stepping && State[l].stepT < 0f)
            {
                // an arthropod leg still waiting for its place in the wave is planted: it brakes the body like
                // one, and lifts at once if the body has swung it past its limit
                float jd = ArthropodJointThrottle(l, dt);
                jointThrottle = Mathf.Min(jointThrottle, jd);
                if (jointLimited[l]) State[l].stepT = 0f;
                jointLimited[l] = false;
                continue;
            }
            if (State[l].stepping) { prevSwing[l] = prevKnee[l] = -1f; continue; }
            float jt = models[l] != null ? ArthropodJointThrottle(l, dt) : JointThrottle(l, dt);
            // Climbing over a turn the attach frame itself turns (its up follows the surface), which swings every
            // planted limb in that frame at once: the joint limits still trigger steps, but brake the body only
            // as much as on level ground.
            if (Climbing) jt = Mathf.Lerp(jt, 1f, Steep(l) > 0f || climb.AheadWeight > 0f ? kClimbJointRelax : 0f);
            jointThrottle = Mathf.Min(jointThrottle, jt);
            float reach = reachLen[l] * scale;
            float now = (target[l] - State[l].shoulder).magnitude;
            // a stretch no step can cure (rest point itself out of reach) must not stop the body for good
            if ((rest[l] - State[l].shoulder).magnitude >= now - kStepGain * scale) continue;
            worst = Mathf.Max(worst, now / reach);
            if (Climbing) margin = Mathf.Min(margin, kStopAt * reach - now);
        }
        Throttle = Mathf.Min(jointThrottle, Mathf.Clamp01((kStopAt - worst) / (kStopAt - kSlowAt)));
        StopSpeed = Climbing && StopDecel > 0f ? Mathf.Sqrt(2f * StopDecel * Mathf.Max(0f, margin)) : float.PositiveInfinity;
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
        JointAngles(l, st.shoulder, gaitElbow[l], st.foot, out float swing, out float knee, out float swingLim, out float kneeLim);
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

    // JointThrottle for an arthropod leg: the coxa's yaw from its rest direction against the forward/back swing
    // limits (blended by the forward share of the move, as JointAngles), predicted kJointLeadSec ahead. The
    // reach synergy's range is enforced by the solver (a foot past it is clamped, which triggers a step); the
    // body is braked before the near end of it (NearThrottle; the far end: UpdateThrottle's reach), since that
    // step may wait for the other group; a pitch past elevMin/Max triggers a step too.
    float ArthropodJointThrottle(int l, float dt)
    {
        LegSpec s = specs[l];
        Quaternion r = rotations[s.attachJoint];
        Vector3 u = Up(l), rh = r * restYaw[l];
        rh -= u * Vector3.Dot(rh, u);
        rh = SurfaceMath.Unit(rh, r * restYaw[l]);
        Vector3 xf = Vector3.Cross(u, poses[l].planeNormal);   // the leg's outward direction (plane normal = x cross up)
        float swing = Vector3.Angle(rh, xf);
        Vector3 moved = xf - rh;
        float mm = moved.magnitude;
        float fwd = mm > 1e-6f ? Mathf.Clamp(Vector3.Dot(moved, r * Vector3.forward) / mm, -1f, 1f) : 0f;
        float swingLim = Mathf.Lerp(backLimit[l], fwdLimit[l], 0.5f + 0.5f * fwd);
        float ps = prevSwing[l];
        prevSwing[l] = swing;
        float lead = dt > 0f && ps >= 0f ? kJointLeadSec * timeScale / dt : 0f;
        float swingP = swing + Mathf.Max(0f, swing - ps) * lead;
        bool movesAway = (rest[l] - target[l]).sqrMagnitude > kStepGain * kStepGain * scale * scale;
        if (poses[l].elevLimited && movesAway) jointLimited[l] = true;
        float near = NearThrottle(l);
        if (swingP <= swingLim) return near;
        Vector3 hr = rest[l] - State[l].shoulder;
        hr -= u * Vector3.Dot(hr, u);
        float swingR = hr.sqrMagnitude > 1e-12f ? Vector3.Angle(rh, hr) : 0f;
        if (swing > swingLim && swingR < swing - kJointGainDeg) jointLimited[l] = true;
        float excess = swingR < swingP - kJointGainDeg ? swingP - Mathf.Max(swingLim, swingR) : 0f;
        return Mathf.Min(near, Mathf.Clamp01((kJointStopDeg - excess) / (kJointStopDeg - kJointSlowDeg)));
    }

    // The arthropod's near-reach brake (kNearSlow / kNearStop): 1 while the planted ankle is well out from the
    // coxa, ramping to 0 as it closes on ReachShort - unless stepping to the rest point would not move the foot
    // out (a fold no step can cure must not stop the body for good, as the far-reach throttle).
    float NearThrottle(int l)
    {
        float rs = models[l].ReachShort, d = poses[l].reach;
        if (d >= kNearSlow * rs) return 1f;
        if ((rest[l] - State[l].shoulder).magnitude <= (target[l] - State[l].shoulder).magnitude + kStepGain * scale) return 1f;
        return Mathf.Clamp01((d - kNearStop * rs) / ((kNearSlow - kNearStop) * rs));
    }

    // The upper bone of a two-bone limb: its rest rotation in the attach frame swung onto the current direction
    // by the shortest arc - no roll of its own. A bone rolled toward the pole twists about its axis as the limb
    // swings past the pole, and the haunch rings skinned between it and the body then collapse (linear blend of
    // two rotations far apart); a swing-only bone keeps body and limb within one swing angle of each other.
    internal Quaternion UpperRot(int l, Vector3 dir, Quaternion attach)
    {
        if (dir.sqrMagnitude < 1e-12f) return attach * restUpperRot[l];
        Vector3 d = Quaternion.Inverse(attach) * dir.normalized;
        return attach * (Quaternion.FromToRotation(restUpper[l], d) * restUpperRot[l]);
    }

    // The ankle above the foot's contact point (along its normal), world metres: zero without an ankle height.
    Vector3 AnkleLift(int l)
    {
        if (ankleH[l] <= 0f) return Vector3.zero;
        return SurfaceMath.Unit(State[l].footNormal, Up(l)) * (ankleH[l] * scale);
    }

    // Ankle height of leg l, metres at scale 1 (0 = the lower bone ends on the ground).
    internal float AnkleHeight(int l) => ankleH[l];

    // Writes every leg's bones as world matrices: upper, lower, foot for a two-bone leg; one per segment for
    // an arthropod leg (ArthropodLegIK.WriteBones).
    public void WriteBones(Matrix4x4[] dst, int offset)
    {
        Vector3 sc = Vector3.one * scale;
        for (int l = 0; l < specs.Length; l++)
        {
            LegSpec s = specs[l];
            if (models[l] != null)
            {
                ArthropodLegIK.WriteBones(dst, offset + sp.LegBone(l, 0), Points, PointOffset[l], models[l].SegmentCount, PlaneNormal[l], scale);
                continue;
            }
            ref LegState st = ref State[l];
            Quaternion ar = rotations[s.attachJoint];
            Vector3 pole = ar * Mirror(s.pole, l);
            Quaternion upper = UpperRot(l, st.elbow - st.shoulder, ar);
            dst[offset + sp.LegBone(l, 0)] = Matrix4x4.TRS(st.shoulder, upper, sc);
            dst[offset + sp.LegBone(l, 1)] = Matrix4x4.TRS(st.elbow, LowerRot(upper, st.ankle - st.elbow), sc);

            Vector3 n = SurfaceMath.Unit(st.footNormal, Vector3.up);
            Vector3 f = Vector3.ProjectOnPlane(ar * Vector3.forward, n);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.ProjectOnPlane(ar * Vector3.up, n);
            if (f.sqrMagnitude < 1e-8f) f = Vector3.ProjectOnPlane(Vector3.forward, n);
            Quaternion fr = Quaternion.LookRotation(f.normalized, n);
            Vector3 fo = st.foot;
            float peel = st.stepping && ankleH[l] > 0f && !Climbing ? ToePeel(l, st.stepT) : 0f;
            if (peel != 0f)
            {
                // the foot turns about the ankle: the shin's end stays put, the heel lifts and the toes trail
                Quaternion pr = Quaternion.AngleAxis(peel, fr * Vector3.right);
                fr = pr * fr;
                fo = st.ankle + pr * (st.foot - st.ankle);
            }
            dst[offset + sp.LegBone(l, 2)] = Matrix4x4.TRS(fo, fr, sc);
        }
    }

    // Swing foot pitch, degrees about the foot's right axis (+ = toes down), for a limb with an ankle: the toes
    // trail after lift-off (peeling off the ground, kToePeelDeg near a third of the step) and reach up a little
    // before landing (kToeReachDeg), flat at both ends. Draw-only: the gait never sees it. The trailing toes are
    // kept above the ground the step has lifted the foot by (flat-ground estimate).
    const float kToePeelDeg = 35f, kToeReachDeg = 12f;
    float ToePeel(int l, float t)
    {
        if (t <= 0f || t >= 1f) return 0f;
        if (t >= 0.6f) return -kToeReachDeg * Mathf.Sin(Mathf.PI * (t - 0.6f) / 0.4f);
        LegSpec s = specs[l];
        float lift = s.stepLift * Mathf.Sin(Mathf.PI * t), toe = Mathf.Max(1e-4f, s.footLength);
        float cap = Mathf.Asin(Mathf.Clamp01(lift / toe)) * Mathf.Rad2Deg;
        return Mathf.Min(cap, kToePeelDeg * Mathf.Sin(Mathf.PI * t / 0.6f));
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
