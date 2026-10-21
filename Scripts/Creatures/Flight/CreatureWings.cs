using UnityEngine;

// LegState-shaped state of one wing. The wrist plays the foot: shoulder, elbow, wrist from TwoBoneIK.
public struct WingState
{
    public Vector3 shoulder, elbow, wrist;
    public Vector3 handOut, handNormal;    // hand frame: along the forearm (outward), wing-plane normal (dorsal)
    public float angle;                    // stroke angle about the stroke axis, rad; 0 = rest (glide pose), + = up
    public float angleRate;                // rad/s, this sub-step
    public float fold;                     // 0 spread .. 1 folded
    public bool upstroke;                  // the "step": swinging back up to the band top
    public float upT, upFrom, upTo;        // upstroke progress 0..1, start and target angles
    public float rise;                     // wrist velocity along the banked up, relative to the girdle (m/s)
    public float tilt;                     // stroke normal tilt toward forward, rad (+ pronation, - feather)
    public float area;                     // effective area fraction this sub-step
    public float lo, hi;                   // this sub-step's band, rad (amplitude-scaled)
    public int beats;                      // upstrokes started (cumulative)
}

// Wings of a winged creature (design section 1, 3): the wingbeat is the leg's step with a vertical
// comfort band. Downstroke = stance: the wrist is driven down the stroke at the controller's rate (the
// air plant in FlightModel turns that into the shoulder rising). When the wrist would leave the band at
// the bottom the wing steps: a smooth upstroke to the band top x (1 + overshootUp), part-folded and
// feathered. Wings of one beat group start their upstrokes together (synchronous beats); asymmetric
// amplitudes (banking) keep them in phase by scaling each wing's downstroke rate with its amplitude.
//
// Geometry (Pose): the wrist turns about the stroke axis through the shoulder (girdle forward pitched up
// by strokePlaneDeg), pulls in with the fold, and TwoBoneIK places the elbow. Fingers (3..5) fan out from the
// wrist in the wing plane; ribs run from the wrist to the trailing edge (rib 0, midway from the last finger's tip to
// the rear flank attach) and to the flank (ribs 1, 2), each scaled (uniformly) to its length.
// Pure C#, allocation-free after construction, main thread.
public sealed class CreatureWings
{
    readonly CreatureSpecies sp;
    readonly WingSpec[] specs;
    readonly Vector3[] joints;
    readonly Quaternion[] rotations;
    readonly float scale, timeScale;
    public readonly WingState[] State;

    // per wing, girdle frame (x mirrored), scale 1
    readonly Vector3[] shoulderLocal, tipLocal, axisLocal, poleLocal;
    readonly float[] reach, dihedral, alphaDown, alphaUp; // circle radius about the axis, rest elevation, band angles
    readonly int[] side, attach;
    readonly bool[] trigger;
    // geometry, per wing: finger directions and tips, rib directions, ups and lengths
    // (fingers of wing w at FingerIndex(w, 0..FingerCount(w) - 1): FingerStart prefix sums, w x 3 for 3-finger wings)
    public readonly Vector3[] FingerDir, FingerTip, RibDir, RibUp, RibTarget;
    readonly int[] fingerStart, fingerCount;
    public int FingerIndex(int w, int k) => fingerStart[w] + k;
    public int FingerCount(int w) => fingerCount[w];
    public readonly float[] RibLength;     // current, metres
    // the arm membrane, per wing: the normal of the sector between the rear flank rib and the forearm, the
    // normal of the patch between the elbow, the shoulder and the rear flank attach, the rear flank attach
    // (fractional joint index)
    public readonly Vector3[] ArmRibUp, PatchUp;
    readonly Quaternion[] handOnArm;
    // per wing: the hand bone's out axis (+Z) as WriteBones writes it - st.handOut, flexed on the ground (wing-walk)
    // about the hand normal so the thumb claw points down onto the foot point (WingSpec.groundWristLift > 0): the
    // wrist stands raised on the claw, as a pterosaur or a vampire bat does. HandFlex: that flex, rad.
    public readonly Vector3[] HandBoneOut;
    public readonly float[] HandFlex;
    // per wing w, FlankPatches(w) + 1 nodes from FlankStart(w): the flank edge of the membrane between the humerus
    // and the body (the rear flank attach, points on the flank between it and the girdle, the shoulder)
    public readonly Vector3[] FlankNode;
    readonly int[] flankStart, flankCount;
    public int FlankStart(int w) => flankStart[w];
    public int FlankPatches(int w) => flankCount[w];
    public readonly Vector3[] FanNormal;   // per wing: the fingers' plane normal (= handNormal unless groundTallStance)
    public readonly Vector3[] FanRearUp;   // per wing: the fan-side rear rib's up (WingBoneLayout.FanRearRib)
    public readonly float[] RearJoint;
    readonly float[] armSign, patchSign;
    readonly float[] groundWeight;         // per wing: how far the current pose is the ground pose (groundLiftDeg)
    readonly float[] flankRise;            // per wing: this pose's flank attach rise (groundFlankRise x ground x fold^2)
    readonly float[] ribRestLength;        // in the rest pose at scale 1

    public CreatureWings(CreatureSpecies sp, WingSpec[] wings, Vector3[] joints, Quaternion[] rotations, float scale = 1f)
    {
        this.sp = sp; specs = wings ?? new WingSpec[0];
        this.joints = joints; this.rotations = rotations;
        this.scale = Mathf.Max(1e-3f, scale); timeScale = Mathf.Sqrt(this.scale);
        int c = specs.Length;
        State = new WingState[c];
        shoulderLocal = new Vector3[c]; tipLocal = new Vector3[c]; axisLocal = new Vector3[c]; poleLocal = new Vector3[c];
        reach = new float[c]; dihedral = new float[c]; alphaDown = new float[c]; alphaUp = new float[c];
        side = new int[c]; attach = new int[c]; trigger = new bool[c];
        fingerStart = new int[c]; fingerCount = new int[c];
        int fingers = 0;
        for (int w = 0; w < c; w++) { fingerStart[w] = fingers; fingerCount[w] = WingBoneLayout.FingerCount(specs[w]); fingers += fingerCount[w]; }
        FingerDir = new Vector3[fingers]; FingerTip = new Vector3[fingers];
        RibDir = new Vector3[c * WingSpec.Ribs]; RibUp = new Vector3[c * WingSpec.Ribs];
        RibTarget = new Vector3[c * WingSpec.Ribs]; RibLength = new float[c * WingSpec.Ribs];
        ribRestLength = new float[c * WingSpec.Ribs];
        ArmRibUp = new Vector3[c]; PatchUp = new Vector3[c]; RearJoint = new float[c]; FanNormal = new Vector3[c]; FanRearUp = new Vector3[c]; handOnArm = new Quaternion[c];
        flankStart = new int[c]; flankCount = new int[c];
        int nodes = 0;
        for (int w = 0; w < c; w++) { flankStart[w] = nodes; flankCount[w] = WingBoneLayout.FlankPatchCount(specs[w]); nodes += flankCount[w] + 1; }
        FlankNode = new Vector3[nodes];
        armSign = new float[c]; patchSign = new float[c]; groundWeight = new float[c];
        flankRise = new float[c];
        HandBoneOut = new Vector3[c]; HandFlex = new float[c];

        for (int w = 0; w < c; w++)
        {
            WingSpec ws = specs[w];
            LegSpec arm = sp.legs[ws.leg];
            side[w] = arm.side; attach[w] = arm.attachJoint;
            shoulderLocal[w] = Mirror(arm.shoulderOffset, w);
            tipLocal[w] = Mirror(ws.restTip, w);
            poleLocal[w] = Mirror(arm.pole, w);
            float p = ws.strokePlaneDeg * Mathf.Deg2Rad;
            Vector3 a = new Vector3(0f, Mathf.Sin(p), Mathf.Cos(p));
            axisLocal[w] = a;
            Vector3 r = tipLocal[w] - shoulderLocal[w];
            Vector3 rp = r - a * Vector3.Dot(r, a);
            reach[w] = Mathf.Max(1e-4f, rp.magnitude);
            Vector3 eUp = (Vector3.up - a * a.y).normalized;   // the circle's up direction
            float cosP = Vector3.Dot(eUp, Vector3.up);          // height per unit along eUp
            float sd = Mathf.Clamp(Vector3.Dot(rp, eUp) / reach[w], -1f, 1f);
            dihedral[w] = Mathf.Asin(sd);
            float lp = reach[w] * Mathf.Max(1e-3f, cosP);
            // band angles from heights: H(angle) - H(0) = L cos p (sin(d + angle) - sin d)
            alphaDown[w] = dihedral[w] - Mathf.Asin(Mathf.Clamp(sd - ws.comfortDown / lp, -1f, 1f));
            alphaUp[w] = Mathf.Asin(Mathf.Clamp(sd + ws.comfortUp / lp, -1f, 1f)) - dihedral[w];
            State[w].area = 1f;
        }

        // rib rest lengths: the rest pose is a straight spine along -Z at the joint clearances, scale 1
        var restJ = new Vector3[sp.JointCount];
        bool straight = StraightBind(sp, specs);  // the bind's straight spine (WingedMeshBuilder.StraightenBind)
        float lowest = float.MaxValue;
        for (int l = 0; l < sp.legs.Length; l++) lowest = Mathf.Min(lowest, sp.joints[sp.legs[l].attachJoint].clearance);
        var restR = new Quaternion[sp.JointCount];
        int g0 = c > 0 ? attach[0] : 0;
        for (int i = 0; i < restJ.Length; i++)
        {
            restJ[i] = new Vector3(0f, straight ? lowest : sp.joints[i].clearance, sp.UniformSpacing ? -sp.spacing * (i - g0) : -(sp.ArcToJoint(i) - sp.ArcToJoint(g0)));
            restR[i] = Quaternion.identity;
        }
        // CreatureSpecies.restAtSpacing (opt-in; FlowBody.restAtStance): the bind keeps the stance - each rest segment one
        // spacing long between the joints' clearances (CreatureSim's rest), never straightened (StraightBind) - so the rib
        // rest lengths come from those joints and their frames (the rest sim's on flat ground), not from the flat run
        if (sp.restAtSpacing)
        {
            float run = 0f;
            for (int i = 0; i < restJ.Length; i++)
            {
                if (i > 0) run += CreatureSim.RestRun(sp, i, sp.SegmentLength(i - 1), 1f);
                restJ[i] = new Vector3(0f, sp.joints[i].clearance, -run);
            }
            for (int i = 1; i < restJ.Length; i++)
            {
                Vector3 f = (restJ[i - 1] - restJ[i]).normalized;
                restR[i] = Quaternion.LookRotation(f, Vector3.ProjectOnPlane(Vector3.up, f).normalized);
            }
        }
        for (int w = 0; w < c; w++)
        {
            PoseWing(w, restJ, restR, 1f);
            for (int k = 0; k < WingSpec.Ribs; k++) ribRestLength[w * WingSpec.Ribs + k] = RibLength[w * WingSpec.Ribs + k];
            handOnArm[w] = Quaternion.Inverse(LowerArm(w, restR)) * Look(State[w].handOut, State[w].handNormal);
        }
        Pose();
    }

    // The forearm bone's rotation (as WriteBones writes it) from the current arm.
    Quaternion LowerArm(int w, Quaternion[] rs)
    {
        ref WingState st = ref State[w];
        Quaternion ar = rs[attach[w]];
        return LowerRot(BoneRot(st.elbow - st.shoulder, ar * poleLocal[w], ar), st.wrist - st.elbow);
    }

    // The bind (rest) pose keeps a straight spine at the lowest leg attach's clearance (WingSpec.groundTallStance).
    internal static bool StraightBind(CreatureSpecies sp, WingSpec[] wings)
    {
        // a species bound at its stance (CreatureSpecies.restAtSpacing: the rest's segments are spacing long between the
        // clearances) keeps it: levelled, those runs would shorten every sloped segment
        if (wings == null || sp.restAtSpacing) return false;
        for (int w = 0; w < wings.Length; w++) if (wings[w] != null && wings[w].groundTallStance) return true;
        return false;
    }

    public int Count => specs.Length;
    public int Side(int w) => side[w];
    public WingSpec Spec(int w) => specs[w];
    public float Reach(int w) => reach[w] * scale;
    public float BandDownAngle(int w) => alphaDown[w];
    public float BandUpAngle(int w) => alphaUp[w];
    public float RibRestLength(int w, int k) => ribRestLength[w * WingSpec.Ribs + k];

    Vector3 Mirror(Vector3 v, int w) { v.x *= side[w]; return v; }

    // Wrist height along the girdle up, relative to the shoulder, at a stroke angle (scaled).
    public float WristHeight(int w, float angle)
    {
        Vector3 a = axisLocal[w];
        Vector3 r = tipLocal[w] - shoulderLocal[w];
        Vector3 v = Rotate(r, a, side[w] * angle);
        return v.y * scale;
    }

    // ---------------------------------------------------------------- wingbeat (physics side)

    public void Advance(float h, FlightModel m)
    {
        int c = specs.Length;
        if (c == 0 || !(h > 0f)) return;
        FlightSpec f = m.Spec;
        float amp = Mathf.Max(1e-4f, m.Amplitude);

        // band and stance trigger: a wing leaving the band at the bottom starts its whole group's upstroke
        for (int w = 0; w < c; w++)
        {
            ref WingState st = ref State[w];
            float ampW = m.WingAmplitude(side[w]);
            st.lo = -ampW * alphaDown[w];
            st.hi = ampW * alphaUp[w];
            trigger[w] = false;
            if (m.Gliding || st.upstroke) continue;
            float rate = m.StrokeRate * ampW / amp;
            if (st.angle - rate * h < st.lo) trigger[w] = true;
        }
        for (int w = 0; w < c; w++)
        {
            if (!trigger[w]) continue;
            for (int o = 0; o < c; o++)
            {
                if (specs[o].beatGroup != specs[w].beatGroup) continue;
                ref WingState so = ref State[o];
                if (so.upstroke) continue;
                so.upstroke = true; so.upT = 0f; so.upFrom = so.angle;
                so.upTo = so.hi * (1f + specs[o].overshootUp);
                so.beats++;
            }
        }

        for (int w = 0; w < c; w++)
        {
            ref WingState st = ref State[w];
            WingSpec ws = specs[w];
            float ampW = m.WingAmplitude(side[w]);
            float next;
            if (m.Gliding)
            {
                // held spread: relax to the rest angle; an unfinished upstroke is dropped (no pop on resume)
                st.upstroke = false;
                next = st.angle + (0f - st.angle) * Mathf.Min(1f, f.glideRelax / timeScale * h);
                st.fold = 0f;
            }
            else if (st.upstroke)
            {
                st.upT += h / Mathf.Max(1e-4f, ws.upstrokeDuration * timeScale);
                float s = Mathf.Min(st.upT, 1f), sm = s * s * (3f - 2f * s);
                next = st.upFrom + (st.upTo - st.upFrom) * sm;
                st.fold = ws.upstrokeFold * Mathf.Sin(Mathf.PI * s);
                if (st.upT >= 1f) { st.upstroke = false; st.fold = 0f; }
            }
            else
            {
                next = st.angle - m.StrokeRate * ampW / amp * h;
                st.fold = 0f;
            }
            st.angleRate = (next - st.angle) / h;
            st.rise = (WristHeight(w, next) - WristHeight(w, st.angle)) / h;
            bool down = st.angleRate < 0f;
            st.tilt = down ? ws.pronationDeg * Mathf.Deg2Rad * m.Thrust : -f.featherDeg * Mathf.Deg2Rad;
            st.area = ws.area * (down ? 1f : f.upstrokeArea * (1f - st.fold));
            // held wings warp: the asymmetry acts through the area in a glide
            if (m.Gliding) st.area *= 1f - side[w] * m.Asymmetry;
            st.angle = next;
        }
    }

    // Test / Phase B hook: set a wing's stroke angle and fold directly (mesh sweeps, ground fold).
    internal void SetPose(int w, float angle, float fold, float tilt = 0f)
    {
        State[w].angle = angle; State[w].fold = Mathf.Clamp01(fold); State[w].tilt = tilt;
    }

    // ---------------------------------------------------------------- geometry

    // Shoulder, elbow, wrist, hand frame, fingers and ribs of every wing from the current joints.
    public void Pose()
    {
        for (int w = 0; w < specs.Length; w++) PoseWing(w, joints, rotations, scale);
    }

    void PoseWing(int w, Vector3[] js, Quaternion[] rs, float sc)
    {
        WingSpec ws = specs[w];
        LegSpec arm = sp.legs[ws.leg];
        ref WingState st = ref State[w];
        Quaternion r = rs[attach[w]];
        Vector3 g = js[attach[w]];
        Vector3 axis = r * axisLocal[w];
        float ang = side[w] * st.angle;
        Vector3 shoulder = g + r * (shoulderLocal[w] * sc);
        Vector3 rel = Rotate(r * (tipLocal[w] - shoulderLocal[w]), axis, ang) * sc;
        Vector3 target = shoulder + rel * (1f - ws.foldReach * st.fold);
        Vector3 pole = r * poleLocal[w];
        Vector3 elbow = TwoBoneIK.Solve(shoulder, target, arm.upperLength * sc, arm.lowerLength * sc, pole, out Vector3 wrist);
        st.shoulder = shoulder; st.elbow = elbow; st.wrist = wrist;

        // hand normal = the girdle up turned with the stroke, then pronated (leading edge down) in the
        // downstroke or feathered in the upstroke
        Vector3 hOut = wrist - elbow;
        hOut = hOut.sqrMagnitude > 1e-12f ? hOut.normalized : r * (Vector3.right * side[w]);
        Vector3 nw = Rotate(r * Vector3.up, axis, ang);
        if (st.tilt != 0f) nw = Rotate(nw, hOut, side[w] * st.tilt);
        groundWeight[w] = 0f;
        PoseHand(w, js, rs, sc, nw);
        HandBoneOut[w] = st.handOut; HandFlex[w] = 0f;
    }

    // Wing-walk / perch (Phase B): the arm comes from elsewhere (CreatureLegs' wrist-as-foot), the wing is
    // folded by `fold`. The hand plane starts from the plane of the forearm and the girdle's backward
    // direction (fingers fold back along the body).
    public void PoseOnArm(int w, Vector3 shoulder, Vector3 elbow, Vector3 wrist, float fold)
        => PoseBetween(w, shoulder, elbow, wrist, fold, 0f);

    // The hand on a given arm, its plane blended from the ground plane (forearm and girdle back) to the
    // stroke-turned flight plane by flightWeight (launch and touchdown hand the arm between the legs and the
    // wing without a pop). Keeps the wing's stroke angle and tilt.
    public void PoseBetween(int w, Vector3 shoulder, Vector3 elbow, Vector3 wrist, float fold, float flightWeight)
    {
        ref WingState st = ref State[w];
        st.shoulder = shoulder; st.elbow = elbow; st.wrist = wrist;
        st.fold = Mathf.Clamp01(fold);
        Quaternion r = rotations[attach[w]];
        Vector3 hOut = wrist - elbow;
        hOut = hOut.sqrMagnitude > 1e-12f ? hOut.normalized : r * (Vector3.right * side[w]);
        Vector3 back = r * Vector3.back;
        back -= hOut * Vector3.Dot(back, hOut);
        if (back.sqrMagnitude < 1e-8f) back = r * Vector3.up - hOut * Vector3.Dot(r * Vector3.up, hOut);
        Vector3 nw = side[w] * Vector3.Cross(hOut, back.normalized);
        if (Vector3.Dot(nw, r * Vector3.up) < 0f) nw = -nw;
        if (flightWeight > 0f)
        {
            Vector3 axis = r * axisLocal[w];
            Vector3 nf = Rotate(r * Vector3.up, axis, side[w] * st.angle);
            if (st.tilt != 0f) nf = Rotate(nf, hOut, side[w] * st.tilt);
            nw = Vector3.Lerp(nw, nf, Mathf.Clamp01(flightWeight));
        }
        else { st.tilt = 0f; st.angleRate = 0f; }
        groundWeight[w] = 1f - Mathf.Clamp01(flightWeight);
        PoseHand(w, joints, rotations, scale, nw);
        FlexHand(w, r);
    }

    const float kThumbForward = 0.35f;  // wing-walk: the thumb claw points down and this much forward
    const float kMaxHandFlex = 2.4f;    // rad

    // The hand bone on the ground (WingSpec.groundWristLift > 0): flexed about the hand normal until the thumb claw
    // (ThumbTip's shape, in the hand's plane) points down and a little forward, blended by the ground weight. The
    // fingers and the membrane keep the unflexed hand frame (they ride bones of their own); only the hand bone (the
    // wrist cap, the knuckle and the thumb) turns.
    void FlexHand(int w, Quaternion r)
    {
        ref WingState st = ref State[w];
        HandBoneOut[w] = st.handOut; HandFlex[w] = 0f;
        float gw = groundWeight[w];
        if (!(gw > 0f) || !(specs[w].groundWristLift > 0f)) return;
        Vector3 n = st.handNormal, o = st.handOut;
        o = (o - n * Vector3.Dot(o, n)).normalized;
        Vector3 fwd = -side[w] * Vector3.Cross(n, o);
        ThumbLocal(w, out float ta, out float tb);
        Vector3 d = -(r * Vector3.up) + kThumbForward * (r * Vector3.forward);
        float want = Mathf.Atan2(Vector3.Dot(d, fwd), Vector3.Dot(d, o)), has = Mathf.Atan2(tb, ta);
        float th = Mathf.DeltaAngle(has * Mathf.Rad2Deg, want * Mathf.Rad2Deg) * Mathf.Deg2Rad;
        th = Mathf.Clamp(th, -kMaxHandFlex, kMaxHandFlex) * gw;
        HandFlex[w] = th;
        HandBoneOut[w] = o * Mathf.Cos(th) + fwd * Mathf.Sin(th);
    }

    // The thumb claw's tip from the wrist in the hand's plane (scale 1): a along the hand's out axis, b along its
    // leading edge (WingedMeshBuilder.Arm's cone; ThumbTip).
    void ThumbLocal(int w, out float a, out float b)
    {
        WingSpec ws = specs[w];
        float rl = Mathf.Max(1e-4f, sp.legs[ws.leg].lowerRadius), len = Mathf.Max(1e-3f, ws.thumbLength);
        float m = Mathf.Sqrt(0.8f * 0.8f + 0.3f * 0.3f + 0.2f * 0.2f);
        a = -0.2f / m * len;
        b = 0.6f * rl + 0.8f / m * len;
    }

    // The flight pose's shoulder and wrist target (before the fold pull-in and the IK), current joints.
    public Vector3 FlightWristTarget(int w, out Vector3 shoulder)
    {
        WingSpec ws = specs[w];
        Quaternion r = rotations[attach[w]];
        Vector3 axis = r * axisLocal[w];
        shoulder = joints[attach[w]] + r * (shoulderLocal[w] * scale);
        Vector3 rel = Rotate(r * (tipLocal[w] - shoulderLocal[w]), axis, side[w] * State[w].angle) * scale;
        return shoulder + rel * (1f - ws.foldReach * State[w].fold);
    }

    public Vector3 ArmPole(int w) => rotations[attach[w]] * poleLocal[w];

    // The thumb claw's tip (WingedMeshBuilder.Arm: a curved cone off the wrist's leading edge, rigid to the
    // hand bone), from the current hand frame: what stands on the ground in a wing-walk (groundWristLift).
    public Vector3 ThumbTip(int w)
    {
        ref WingState st = ref State[w];
        WingSpec ws = specs[w];
        float rl = Mathf.Max(1e-4f, sp.legs[ws.leg].lowerRadius), len = Mathf.Max(1e-3f, ws.thumbLength);
        Vector3 o = HandBoneOut[w];  // the hand bone's frame (flexed on the ground)
        Vector3 x = Vector3.Cross(st.handNormal, o), fwd = -side[w] * x;
        Vector3 dir = (fwd * 0.8f + st.handNormal * 0.3f - o * 0.2f).normalized;
        return st.wrist + (fwd * (0.6f * rl) + dir * len - st.handNormal * (0.25f * len)) * scale;
    }

    // Back to the spread glide pose's stroke state (a launch): angle 0, no upstroke.
    public void ResetStroke(float fold)
    {
        for (int w = 0; w < State.Length; w++)
        {
            State[w].angle = 0f; State[w].angleRate = 0f; State[w].upstroke = false; State[w].upT = 0f;
            State[w].fold = Mathf.Clamp01(fold); State[w].tilt = 0f; State[w].rise = 0f; State[w].area = specs[w].area;
        }
    }

    // Hand frame, fingers and ribs from the arm in State[w] and an unnormalised hand normal.
    void PoseHand(int w, Vector3[] js, Quaternion[] rs, float sc, Vector3 nw)
    {
        WingSpec ws = specs[w];
        ref WingState st = ref State[w];
        Vector3 wrist = st.wrist;
        Quaternion r = rs[attach[w]];
        Vector3 hOut = wrist - st.elbow;
        hOut = hOut.sqrMagnitude > 1e-12f ? hOut.normalized : r * (Vector3.right * side[w]);
        flankRise[w] = ws.groundFlankRise * groundWeight[w] * st.fold * st.fold;

        // Flank ribs (1: rear attach, 2: front attach) from the wrist to the flank. When the wrist comes in
        // line with the body (an arm reaching far forward in a wing-walk step, the body curled in a turn)
        // the two flank ribs close up and the membrane between them would flip; the front attach then
        // slides forward along the flank (up to kFrontSlide joints, continuously: one secant step on the
        // angle) to keep them kFlankGapDeg apart.
        // The rear flank rib also bounds the arm membrane (its sector reaches forward to the forearm): spread,
        // the rear attach slides back along the flank (up to kRearSlide joints, the same secant step) to keep
        // the rib kArmGapDeg behind the forearm in the arm's plane (the gap closing with the fold, none folded: the folded
        // wing lies along the body); and it never comes within kArmMinGapDeg of the forearm's line.
        float baseRear = ws.membraneRearJoint;
        float rearJ = baseRear;
        Vector3 rearT = FlankPoint(w, js, rs, sc, rearJ);
        Vector3 fore = st.elbow - wrist;
        Vector3 armN = Vector3.Cross(fore, st.shoulder - wrist);
        armN = armN.sqrMagnitude > 1e-10f ? armN.normalized : r * Vector3.up;
        float a0 = BehindArm(rearT - wrist, fore, armN);
        float gap = kArmGapDeg * (1f - st.fold);
        if (a0 < gap && gap > 0f) // folded (a wing-walk) it never slides: the folded wing lies along the body
        {
            float far = Mathf.Min(js.Length - 1, baseRear + kRearSlide);
            float a1 = BehindArm(FlankPoint(w, js, rs, sc, far) - wrist, fore, armN);
            float f = a1 > a0 + 1e-3f ? Mathf.Clamp01((gap - a0) / (a1 - a0)) : 1f;
            rearJ = Mathf.Lerp(baseRear, far, f);
            rearT = FlankPoint(w, js, rs, sc, rearJ);
        }
        // never in line with the forearm (the arm sector would collapse): slide the attach further back along
        // the flank until it is not (it stays on the body); only if the flank ends first, turn the rib off it
        float a3 = Vector3.Angle(rearT - wrist, fore);
        // (nearest first, back toward the tail up to 2 x kRearSlide joints past membraneRearJoint, or forward up
        // to the front attach's next joint)
        float lastJ = Mathf.Min(js.Length - 1, baseRear + 2f * kRearSlide), firstJ = ws.membraneFrontJoint + 1f;
        float rj0 = rearJ;
        // (only with the flank split into patches, WingSpec.flankPatches: one patch keeps the turned rib below)
        for (float step = kSlideStep; flankCount[w] > 1 && a3 < kArmMinGapDeg && (rj0 + step <= lastJ || rj0 - step >= firstJ); step += kSlideStep)
            for (int sgn = 1; sgn >= -1 && a3 < kArmMinGapDeg; sgn -= 2)
            {
                float jj = rj0 + sgn * step;
                if (jj > lastJ || jj < firstJ) continue;
                Vector3 tj = FlankPoint(w, js, rs, sc, jj);
                float aj = Vector3.Angle(tj - wrist, fore);
                if (aj >= kArmMinGapDeg) { rearJ = jj; rearT = tj; a3 = aj; }
            }
        if (a3 < kArmMinGapDeg)
        {
            Vector3 ax = Vector3.Cross(fore, rearT - wrist);
            ax = ax.sqrMagnitude > 1e-10f ? ax.normalized : -armN;
            rearT = wrist + Rotate(rearT - wrist, ax, (kArmMinGapDeg - a3) * Mathf.Deg2Rad);
        }
        RearJoint[w] = rearJ;
        SetRib(w * WingSpec.Ribs + 1, wrist, rearT, -hOut);
        // the flank edge's nodes: the rear attach, flank points evenly in joint index toward the girdle, the shoulder
        {
            int P = flankCount[w], f0 = flankStart[w];
            FlankNode[f0] = rearT;
            for (int k = 1; k < P; k++) FlankNode[f0 + k] = FlankPoint(w, js, rs, sc, Mathf.Lerp(rearJ, attach[w], (float)k / P));
            FlankNode[f0 + P] = st.shoulder;
        }
        Vector3 rearD = RibDir[w * WingSpec.Ribs + 1];
        float front = ws.membraneFrontJoint;
        Vector3 frontT = FlankPoint(w, js, rs, sc, front);
        float g0 = Vector3.Angle(rearD, frontT - wrist);
        if (g0 < kFlankGapDeg)
        {
            float far = Mathf.Max(0f, front - kFrontSlide);
            float g1 = Vector3.Angle(rearD, FlankPoint(w, js, rs, sc, far) - wrist);
            float f = g1 > g0 + 1e-3f ? Mathf.Clamp01((kFlankGapDeg - g0) / (g1 - g0)) : 1f;
            frontT = FlankPoint(w, js, rs, sc, Mathf.Lerp(front, far, f));
        }
        SetRib(w * WingSpec.Ribs + 2, wrist, frontT, -hOut);
        // Folding turns the hand plane onto the plane of the two flank ribs, so the folded fingers and the
        // trailing rib lie flat in the flank ribs' fan instead of across it (fold^2: the flight upstroke
        // barely changes). The hand's out axis is the forearm projected into the hand plane.
        Vector3 rr = RibDir[w * WingSpec.Ribs + 1];
        nw = nw.sqrMagnitude > 1e-12f ? nw.normalized : r * Vector3.up;
        float fw = st.fold * st.fold;
        if (fw > 0f)
        {
            // oriented dorsal by the girdle up: the stroke-turned normal can face away in a walk pose
            Vector3 nf = Vector3.Cross(rr, RibDir[w * WingSpec.Ribs + 2]);
            if (Vector3.Dot(nf, r * Vector3.up) < 0f) nf = -nf;
            if (nf.sqrMagnitude > 1e-8f) nw = Vector3.Lerp(nw, nf.normalized, fw).normalized;
        }
        Vector3 ho = hOut - nw * Vector3.Dot(hOut, nw);
        if (ho.sqrMagnitude > 1e-8f) hOut = ho.normalized;
        else { nw -= hOut * Vector3.Dot(nw, hOut); nw = nw.sqrMagnitude > 1e-12f ? nw.normalized : r * Vector3.up; }
        Vector3 hBack = side[w] * Vector3.Cross(nw, hOut);
        // the ground pose's lift (WingSpec.groundLiftDeg): the folded fan turns up about the rear flank rib until
        // its middle finger rises liftDeg above the girdle's plane; the rib, the flank and the forearm stay put
        Vector3 nwFlat = nw; // the fan's plane before the lift (the rear flank rib keeps its roll from it)
        Vector3 hOutArm = hOut;
        float lift = ws.groundLiftDeg * groundWeight[w] * fw;
        if (lift > 0f)
        {
            Vector3 gu = r * Vector3.up;
            int fm = fingerCount[w] / 2;   // the middle finger (1 of 3)
            float a1 = Mathf.Lerp(ws.fingerSpreadDeg[fm], ws.fingerFoldDeg[fm], st.fold) * Mathf.Deg2Rad;
            Vector3 d1 = Mathf.Cos(a1) * hOut + Mathf.Sin(a1) * hBack;
            Vector3 q = d1 - rr * Vector3.Dot(d1, rr);  // in the plane, square to the rib, on the fingers' side
            if (q.sqrMagnitude > 1e-6f)
            {
                q.Normalize();
                float elev = Mathf.Asin(Mathf.Clamp(Vector3.Dot(q, gu), -1f, 1f)) * Mathf.Rad2Deg;
                float turn = (lift - elev) * Mathf.Deg2Rad;
                if (turn > 0f)
                {
                    float sgn = Vector3.Dot(Vector3.Cross(rr, q), gu) >= 0f ? 1f : -1f; // the sense that lifts q
                    nw = Rotate(nw, rr, sgn * turn);
                    hOut = Rotate(hOut, rr, sgn * turn);
                    hBack = side[w] * Vector3.Cross(nw, hOut);
                }
            }
        }
        st.handOut = hOut; st.handNormal = nw;
        // WingSpec.groundTallStance: the hand bone stays on the forearm (only the finger fan lifts), so a steep
        // lift does not kink the wrist (a tall stance on near-vertical arms); the fingers keep the fan's plane
        FanNormal[w] = nw;
        if (ws.groundTallStance && lift > 0f)
        {
            // the hand keeps its bind-pose relation to the forearm bone (its roll too: a hand rolled against the
            // forearm twists the wrist ring between the two bones)
            Quaternion hand = LowerArm(w, rs) * handOnArm[w];
            st.handOut = hand * Vector3.forward; st.handNormal = hand * Vector3.up;
        }

        // Fingers fan out in the wing plane. Order invariant: finger k stays at least (nf + 1 - k) x gap ahead
        // of the rear flank rib, the gap growing from fingerMinGapDeg to fingerFoldGapDeg with the fold
        // (folding swings the fingers back toward it; past it the membrane behind finger 3 would turn
        // inside out). The trailing rib lies between the two.
        float ribAngle = Mathf.Atan2(Vector3.Dot(rr, hBack), Vector3.Dot(rr, hOut)) * Mathf.Rad2Deg;
        if (ribAngle < 0f) ribAngle += 360f;
        int nFing = fingerCount[w];
        for (int k = 0; k < nFing; k++)
        {
            float deg = Mathf.Lerp(ws.fingerSpreadDeg[k], ws.fingerFoldDeg[k], st.fold);
            deg = Mathf.Min(deg, ribAngle - (nFing + 1 - k) * Mathf.Lerp(ws.fingerMinGapDeg, ws.fingerFoldGapDeg, st.fold));
            float a = deg * Mathf.Deg2Rad;
            Vector3 d = Mathf.Cos(a) * hOut + Mathf.Sin(a) * hBack;
            int i = fingerStart[w] + k;
            FingerDir[i] = d;
            FingerTip[i] = wrist + d * (ws.fingerLengths[k] * sc);
        }
        // Trailing rib (0): between finger 3 and the rear flank rib, by angle (slerp) and by length, so
        // both of its zones keep a moderate angle and similar ray lengths. Two-ray skinning of a zone whose
        // rays differ much in length folds over when the wing folds (the short ray turns the far points).
        int f3i = fingerStart[w] + nFing - 1, rr1 = w * WingSpec.Ribs + 1;
        Vector3 tdir = Vector3.Slerp(FingerDir[f3i], RibDir[rr1], ws.trailingRib);
        // Its length scales with the square root of the rear flank rib's stretch (rest: the length lerp),
        // halfway between the rigid finger and the stretching rib, so neither of its zones has to absorb
        // the whole length change.
        float tlen = Mathf.Lerp(ws.fingerLengths[nFing - 1] * sc, RibLength[rr1], ws.trailingRib);
        float rest0 = ribRestLength[w * WingSpec.Ribs], rest1 = ribRestLength[rr1];
        if (rest0 > 0f && rest1 > 0f) tlen = rest0 * sc * Mathf.Sqrt(RibLength[rr1] / (rest1 * sc));
        SetRib(w * WingSpec.Ribs, wrist, wrist + tdir * tlen, -hOut);
        // rib ups: the wing-plane normal made perpendicular to the rib. Continuous whatever the fan does
        // (a fan normal from neighbouring rays flips its roll when two rays line up).
        for (int k = 0; k < WingSpec.Ribs; k++)
        {
            int i = w * WingSpec.Ribs + k;
            // the rear flank rib keeps its roll from the fan's plane before the ground lift: it borders the arm
            // sector, which the lift does not turn (rolled with the fan, the sector's rib column faced away)
            Vector3 nk = k == 1 ? nwFlat : nw;
            Vector3 u = nk - RibDir[i] * Vector3.Dot(nk, RibDir[i]);
            if (u.sqrMagnitude < 1e-10f) u = Vector3.Cross(RibDir[i], hOut);
            RibUp[i] = u.sqrMagnitude > 1e-12f ? u.normalized : Vector3.up;
        }
        // The arm membrane's planes: the sector (rear flank rib x forearm) and the patch (elbow, shoulder, rear
        // flank attach), their signs fixed by the rest pose (dorsal there; continuous while the rays never
        // line up, which the rear attach's rules above keep).
        Vector3 ribMid;
        {
            int i = w * WingSpec.Ribs + 1;
            Vector3 cu = Vector3.Cross(RibDir[i], st.elbow - wrist);
            if (armSign[w] == 0f) armSign[w] = Vector3.Dot(cu, nw) < 0f ? -1f : 1f;
            cu *= armSign[w];
            ArmRibUp[w] = cu.sqrMagnitude > 1e-12f ? cu.normalized : RibUp[i];
            Vector3 pu = Vector3.Cross(st.shoulder - st.elbow, RibTarget[i] - st.elbow);
            if (patchSign[w] == 0f) patchSign[w] = Vector3.Dot(pu, nw) < 0f ? -1f : 1f;
            pu *= patchSign[w];
            PatchUp[w] = pu.sqrMagnitude > 1e-12f ? pu.normalized : ArmRibUp[w];
            // the rear flank rib itself rolls halfway into the sector: the seam between the hand's fan and the
            // arm membrane takes the mean of the two planes (no crease where the arm membrane stands up off
            // the fan, as it does in a wing-walk)
            Vector3 mid = RibUp[i] + ArmRibUp[w];
            if (mid.sqrMagnitude > 1e-8f) RibUp[i] = mid.normalized;
            ribMid = RibUp[i];
            // a tall stance on the ground: the fan beside the rib rides its own bone (FanRearUp below), so the rib
            // borders only the arm sector and takes the sector's roll. Halfway, the narrow sector (the rib and a
            // near-vertical forearm ~10 deg apart, its plane turning over as the rib circles the forearm in tight
            // turns) turned past 90 deg off the rib's frame and folded through itself.
            float tg = ws.groundTallStance ? groundWeight[w] * fw : 0f;
            if (tg > 0f) RibUp[i] = Vector3.Slerp(RibUp[i], ArmRibUp[w], tg).normalized;
        }
        // The rear flank rib as the fan beside it sees it (WingBoneLayout.FanRearRib, WingSpec.groundTallStance):
        // the rib's own roll in the air, rolled into the lifted fan's plane with the ground lift. The rear rib's
        // own roll (half way into the arm sector, from the fan before the lift) left the narrow zone between it
        // and the trailing rib (4-8 deg folded) twisted by 130-150 deg on near-vertical arms with a steep lift:
        // it collapsed into slivers in the wing-walk. The arm zone keeps the rear rib's own.
        {
            int r1 = w * WingSpec.Ribs + 1;
            FanRearUp[w] = ribMid;
            float g = ws.groundTallStance ? (ws.fanRearRollsWithFan ? 1f : groundWeight[w] * fw) : 0f;
            if (g > 0f)
            {
                Vector3 fu = nw - RibDir[r1] * Vector3.Dot(nw, RibDir[r1]);
                if (fu.sqrMagnitude > 1e-10f)
                {
                    Vector3 u = Vector3.Slerp(ribMid, fu.normalized, g);
                    u -= RibDir[r1] * Vector3.Dot(u, RibDir[r1]);
                    if (u.sqrMagnitude > 1e-10f) FanRearUp[w] = u.normalized;
                }
            }
        }
    }

    const float kFlankGapDeg = 15f, kFrontSlide = 3f;
    const float kFlankInside = 0.9f;  // a flank attach stays within this fraction of the body's section
    const float kSlideStep = 0.5f;    // joints: the rear attach's search step off the forearm's line
    const float kArmGapDeg = 14f, kArmMinGapDeg = 10f, kRearSlide = 6f;
    const float kSectorPatchLean = 0.75f; // ArmSector's normal axis: sector normal + this x patch normal

    // Angle of d behind the forearm (away from the shoulder side) in the arm's plane, degrees.
    static float BehindArm(Vector3 d, Vector3 fore, Vector3 armN)
        => -Mathf.Atan2(Vector3.Dot(Vector3.Cross(fore, d), armN), Vector3.Dot(fore, d)) * Mathf.Rad2Deg;

    // The membrane's attach point on the flank at a fractional joint index (interpolated between joints).
    Vector3 FlankPoint(int w, Vector3[] js, Quaternion[] rs, float sc, float joint)
    {
        int j0 = Mathf.Clamp((int)joint, 0, js.Length - 1), j1 = Mathf.Min(j0 + 1, js.Length - 1);
        float f = Mathf.Clamp01(joint - j0);
        // risen up the flank (groundFlankRise), the attach moves in so it stays inside the body's elliptical
        // section (inset^2 + rise^2 <= kFlankInside^2): risen at the full inset it stood off the body
        float ins = specs[w].membraneInset, ri = flankRise[w];
        if (flankCount[w] > 1 && ins * ins + ri * ri > kFlankInside * kFlankInside) ins = Mathf.Sqrt(Mathf.Max(0f, kFlankInside * kFlankInside - ri * ri));
        float inset = ins * sc * side[w], rise = ri * sc;
        Vector3 p0 = js[j0] + rs[j0] * new Vector3(inset * sp.joints[j0].halfWidth, rise * sp.joints[j0].halfHeight, 0f);
        if (f <= 0f) return p0;
        Vector3 p1 = js[j1] + rs[j1] * new Vector3(inset * sp.joints[j1].halfWidth, rise * sp.joints[j1].halfHeight, 0f);
        return Vector3.Lerp(p0, p1, f);
    }

    void SetRib(int i, Vector3 wrist, Vector3 target, Vector3 fallback)
    {
        Vector3 d = target - wrist;
        float len = d.magnitude;
        RibTarget[i] = target;
        RibLength[i] = len;
        RibDir[i] = len > 1e-6f ? d / len : fallback;
    }

    // Writes the arm (into its leg bones), finger and rib bones of every wing, world matrices.
    //   leg bones: upper, lower (CreatureLegs' knee-hinge rule), hand (+Z out along the forearm, +Y wing normal)
    //   wing bones (WingBoneLayout): fingers (+Z along the finger), ribs (+Z along the rib, scaled uniformly
    //   by length / rest length so the rib always ends on the flank and the membrane scales radially from
    //   the wrist; a uniform scale leaves normals exact and does not shear a zone, which an axis-only
    //   scale does when the wing folds)
    public void WriteBones(Matrix4x4[] dst, int offset)
    {
        Vector3 sc = Vector3.one * scale;
        for (int w = 0; w < specs.Length; w++)
        {
            ref WingState st = ref State[w];
            int leg = specs[w].leg;
            Quaternion ar = rotations[attach[w]];
            Vector3 pole = ar * poleLocal[w];
            Quaternion upper = BoneRot(st.elbow - st.shoulder, pole, ar);
            dst[offset + sp.LegBone(leg, 0)] = Matrix4x4.TRS(st.shoulder, upper, sc);
            dst[offset + sp.LegBone(leg, 1)] = Matrix4x4.TRS(st.elbow, LowerRot(upper, st.wrist - st.elbow), sc);
            dst[offset + sp.LegBone(leg, 2)] = Matrix4x4.TRS(st.wrist, Look(HandBoneOut[w], st.handNormal), sc);
            for (int k = 0; k < fingerCount[w]; k++)
                dst[offset + sp.WingBone(w, k)] =
                    Matrix4x4.TRS(st.wrist, Look(FingerDir[fingerStart[w] + k], FanNormal[w]), sc);
            int firstRib = WingBoneLayout.FirstRibOf(specs[w]), armRib = WingBoneLayout.ArmRibOf(specs[w]);
            for (int k = 0; k < WingSpec.Ribs; k++)
            {
                int i = w * WingSpec.Ribs + k;
                int b = offset + sp.WingBone(w, firstRib + k);
                if (firstRib + k == armRib) { dst[b] = ArmSector(w); continue; }
                float stretch = RibLength[i] / Mathf.Max(1e-6f, ribRestLength[i] * scale);
                dst[b] = Matrix4x4.TRS(st.wrist, Look(RibDir[i], RibUp[i]), Vector3.one * (scale * stretch));
            }
            for (int p = 0; p < flankCount[w]; p++)
                dst[offset + sp.WingBone(w, WingBoneLayout.HumerusPatchOf(specs[w]) + p)] = FlankPatch(w, p);
            int fr = WingBoneLayout.FanRearRib(specs[w]);
            if (fr >= 0)
            {
                int i = w * WingSpec.Ribs + 1;
                float stretch = RibLength[i] / Mathf.Max(1e-6f, ribRestLength[i] * scale);
                dst[offset + sp.WingBone(w, fr)] = Matrix4x4.TRS(st.wrist, Look(RibDir[i], FanRearUp[w]), Vector3.one * (scale * stretch));
            }
        }
    }

    // The humerus patch bone (WingBoneLayout.HumerusPatch): an affine frame at the elbow on the rear flank attach,
    // the shoulder and their plane's normal (x scale). The patch is a fan inside the triangle elbow, rear flank
    // attach, shoulder, so it maps exactly onto the current triangle: its edge on the arm sector's crease (the
    // elbow -> rear attach chord, which the sector maps exactly too) and its edge on the humerus. Its normal axis
    // is the triangle's own (the frame's determinant never changes sign): the patch cannot turn inside out.
    // Split along the flank (WingSpec.flankPatches): patch p is the triangle elbow, flank node p, flank
    // node p + 1; patch 0 starts at the rear attach, the last ends at the shoulder.
    public Matrix4x4 FlankPatch(int w, int p)
    {
        ref WingState st = ref State[w];
        int f0 = flankStart[w] + p;
        Vector3 c0 = FlankNode[f0] - st.elbow, c1 = FlankNode[f0 + 1] - st.elbow;
        Vector3 cn = Vector3.Cross(c0, c1);
        Vector3 c2 = (cn.sqrMagnitude > 1e-12f ? cn.normalized : -patchSign[w] * PatchUp[w]) * scale;
        Vector3 o = st.elbow;
        var m = Matrix4x4.identity;
        m.m00 = c0.x; m.m01 = c1.x; m.m02 = c2.x; m.m03 = o.x;
        m.m10 = c0.y; m.m11 = c1.y; m.m12 = c2.y; m.m13 = o.y;
        m.m20 = c0.z; m.m21 = c1.z; m.m22 = c2.z; m.m23 = o.z;
        return m;
    }

    // The arm sector bone (WingBoneLayout.ArmRib): an affine frame at the wrist on the rear flank rib (to its
    // flank end), the forearm (to the elbow) and a normal axis (x scale). Skinned by it (world x bind), the
    // bind pose's sector between the rib and the forearm maps exactly onto the current one, however the
    // angle between them opens or closes and whichever way the elbow tilts the sector out of the hand's
    // fan: the membrane stays on the forearm. Every membrane vertex on it lies in that sector, so the normal
    // axis only turns normals: it leans toward the patch's plane (kSectorPatchLean), which
    // meets the sector along its far edge (the crease between the two, stood up in a wing-walk, shared).
    public Matrix4x4 ArmSector(int w)
    {
        ref WingState st = ref State[w];
        int i = w * WingSpec.Ribs + 1;
        Vector3 c0 = RibTarget[i] - st.wrist, c1 = st.elbow - st.wrist;
        Vector3 cn = ArmRibUp[w] + kSectorPatchLean * PatchUp[w];
        Vector3 c2 = (cn.sqrMagnitude > 1e-8f ? cn.normalized : ArmRibUp[w]) * scale;
        Vector3 o = st.wrist;
        var m = Matrix4x4.identity;
        m.m00 = c0.x; m.m01 = c1.x; m.m02 = c2.x; m.m03 = o.x;
        m.m10 = c0.y; m.m11 = c1.y; m.m12 = c2.y; m.m13 = o.y;
        m.m20 = c0.z; m.m21 = c1.z; m.m22 = c2.z; m.m23 = o.z;
        return m;
    }

    static Quaternion Look(Vector3 f, Vector3 u)
    {
        Vector3 up = u - f * Vector3.Dot(u, f);
        if (up.sqrMagnitude < 1e-10f) up = Vector3.ProjectOnPlane(Vector3.up, f);
        if (up.sqrMagnitude < 1e-10f) up = Vector3.ProjectOnPlane(Vector3.right, f);
        return Quaternion.LookRotation(f, up.normalized);
    }

    // Copied from CreatureLegs (private there): the lower bone is the upper frame turned about the knee
    // hinge onto the lower direction (R13).
    internal static Quaternion LowerRot(Quaternion upper, Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-12f) return upper;
        return Quaternion.FromToRotation(upper * Vector3.forward, dir.normalized) * upper;
    }

    // Copied from CreatureLegs: +Z along the bone, +Y = pole made perpendicular to it.
    internal static Quaternion BoneRot(Vector3 dir, Vector3 pole, Quaternion attach)
    {
        if (dir.sqrMagnitude < 1e-12f) return attach;
        dir.Normalize();
        Vector3 up = Vector3.ProjectOnPlane(pole, dir);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(attach * Vector3.up, dir);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.ProjectOnPlane(Vector3.right, dir);
        return Quaternion.LookRotation(dir, up.normalized);
    }

    // v rotated about the unit axis a by rad (Rodrigues); copied from CreatureSim.
    internal static Vector3 Rotate(Vector3 v, Vector3 a, float rad)
    {
        float c = Mathf.Cos(rad), s = Mathf.Sin(rad), ad = (a.x * v.x + a.y * v.y + a.z * v.z) * (1f - c);
        return new Vector3(v.x * c + (a.y * v.z - a.z * v.y) * s + a.x * ad,
                           v.y * c + (a.z * v.x - a.x * v.z) * s + a.y * ad,
                           v.z * c + (a.x * v.y - a.y * v.x) * s + a.z * ad);
    }
}
