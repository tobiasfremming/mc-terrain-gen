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
// by strokePlaneDeg), pulls in with the fold, and TwoBoneIK places the elbow. Fingers fan out from the
// wrist in the wing plane; ribs run from the wrist to the trailing edge (rib 0, midway from finger 3's tip to
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
    public readonly Vector3[] FingerDir, FingerTip, RibDir, RibUp, RibTarget;
    public readonly float[] RibLength;     // current, metres
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
        FingerDir = new Vector3[c * WingSpec.Fingers]; FingerTip = new Vector3[c * WingSpec.Fingers];
        RibDir = new Vector3[c * WingSpec.Ribs]; RibUp = new Vector3[c * WingSpec.Ribs];
        RibTarget = new Vector3[c * WingSpec.Ribs]; RibLength = new float[c * WingSpec.Ribs];
        ribRestLength = new float[c * WingSpec.Ribs];

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
        var restR = new Quaternion[sp.JointCount];
        int g0 = c > 0 ? attach[0] : 0;
        for (int i = 0; i < restJ.Length; i++)
        {
            restJ[i] = new Vector3(0f, sp.joints[i].clearance, -sp.spacing * (i - g0));
            restR[i] = Quaternion.identity;
        }
        for (int w = 0; w < c; w++)
        {
            PoseWing(w, restJ, restR, 1f);
            for (int k = 0; k < WingSpec.Ribs; k++) ribRestLength[w * WingSpec.Ribs + k] = RibLength[w * WingSpec.Ribs + k];
        }
        Pose();
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
        PoseHand(w, js, rs, sc, nw);
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
        PoseHand(w, joints, rotations, scale, nw);
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

        // Flank ribs (1: rear attach, 2: front attach) from the wrist to the flank. When the wrist comes in
        // line with the body (an arm reaching far forward in a wing-walk step, the body curled in a turn)
        // the two flank ribs close up and the membrane between them would flip; the front attach then
        // slides forward along the flank (up to kFrontSlide joints, continuously: one secant step on the
        // angle) to keep them kFlankGapDeg apart.
        Vector3 rearT = FlankPoint(w, js, rs, sc, ws.membraneRearJoint);
        SetRib(w * WingSpec.Ribs + 1, wrist, rearT, -hOut);
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
        st.handOut = hOut; st.handNormal = nw;

        // Fingers fan out in the wing plane. Order invariant: finger k stays at least (4 - k) x gap ahead
        // of the rear flank rib, the gap growing from fingerMinGapDeg to fingerFoldGapDeg with the fold
        // (folding swings the fingers back toward it; past it the membrane behind finger 3 would turn
        // inside out). The trailing rib lies between the two.
        float ribAngle = Mathf.Atan2(Vector3.Dot(rr, hBack), Vector3.Dot(rr, hOut)) * Mathf.Rad2Deg;
        if (ribAngle < 0f) ribAngle += 360f;
        for (int k = 0; k < WingSpec.Fingers; k++)
        {
            float deg = Mathf.Lerp(ws.fingerSpreadDeg[k], ws.fingerFoldDeg[k], st.fold);
            deg = Mathf.Min(deg, ribAngle - (WingSpec.Fingers + 1 - k) * Mathf.Lerp(ws.fingerMinGapDeg, ws.fingerFoldGapDeg, st.fold));
            float a = deg * Mathf.Deg2Rad;
            Vector3 d = Mathf.Cos(a) * hOut + Mathf.Sin(a) * hBack;
            int i = w * WingSpec.Fingers + k;
            FingerDir[i] = d;
            FingerTip[i] = wrist + d * (ws.fingerLengths[k] * sc);
        }
        // Trailing rib (0): between finger 3 and the rear flank rib, by angle (slerp) and by length, so
        // both of its zones keep a moderate angle and similar ray lengths. Two-ray skinning of a zone whose
        // rays differ much in length folds over when the wing folds (the short ray turns the far points).
        int f3i = w * WingSpec.Fingers + WingSpec.Fingers - 1, rr1 = w * WingSpec.Ribs + 1;
        Vector3 tdir = Vector3.Slerp(FingerDir[f3i], RibDir[rr1], ws.trailingRib);
        // Its length scales with the square root of the rear flank rib's stretch (rest: the length lerp),
        // halfway between the rigid finger and the stretching rib, so neither of its zones has to absorb
        // the whole length change.
        float tlen = Mathf.Lerp(ws.fingerLengths[WingSpec.Fingers - 1] * sc, RibLength[rr1], ws.trailingRib);
        float rest0 = ribRestLength[w * WingSpec.Ribs], rest1 = ribRestLength[rr1];
        if (rest0 > 0f && rest1 > 0f) tlen = rest0 * sc * Mathf.Sqrt(RibLength[rr1] / (rest1 * sc));
        SetRib(w * WingSpec.Ribs, wrist, wrist + tdir * tlen, -hOut);
        // rib ups: the wing-plane normal made perpendicular to the rib. Continuous whatever the fan does
        // (a fan normal from neighbouring rays flips its roll when two rays line up).
        for (int k = 0; k < WingSpec.Ribs; k++)
        {
            int i = w * WingSpec.Ribs + k;
            Vector3 u = nw - RibDir[i] * Vector3.Dot(nw, RibDir[i]);
            if (u.sqrMagnitude < 1e-10f) u = Vector3.Cross(RibDir[i], hOut);
            RibUp[i] = u.sqrMagnitude > 1e-12f ? u.normalized : Vector3.up;
        }
    }

    const float kFlankGapDeg = 15f, kFrontSlide = 3f;

    // The membrane's attach point on the flank at a fractional joint index (interpolated between joints).
    Vector3 FlankPoint(int w, Vector3[] js, Quaternion[] rs, float sc, float joint)
    {
        int j0 = Mathf.Clamp((int)joint, 0, js.Length - 1), j1 = Mathf.Min(j0 + 1, js.Length - 1);
        float f = Mathf.Clamp01(joint - j0);
        float inset = specs[w].membraneInset * sc * side[w];
        Vector3 p0 = js[j0] + rs[j0] * new Vector3(inset * sp.joints[j0].halfWidth, 0f, 0f);
        if (f <= 0f) return p0;
        Vector3 p1 = js[j1] + rs[j1] * new Vector3(inset * sp.joints[j1].halfWidth, 0f, 0f);
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
        int legs = sp.legs != null ? sp.legs.Length : 0;
        int n = sp.JointCount;
        Vector3 sc = Vector3.one * scale;
        for (int w = 0; w < specs.Length; w++)
        {
            ref WingState st = ref State[w];
            int leg = specs[w].leg;
            Quaternion ar = rotations[attach[w]];
            Vector3 pole = ar * poleLocal[w];
            Quaternion upper = BoneRot(st.elbow - st.shoulder, pole, ar);
            dst[offset + WingBoneLayout.LegBone(n, leg, 0)] = Matrix4x4.TRS(st.shoulder, upper, sc);
            dst[offset + WingBoneLayout.LegBone(n, leg, 1)] = Matrix4x4.TRS(st.elbow, LowerRot(upper, st.wrist - st.elbow), sc);
            dst[offset + WingBoneLayout.LegBone(n, leg, 2)] = Matrix4x4.TRS(st.wrist, Look(st.handOut, st.handNormal), sc);
            for (int k = 0; k < WingSpec.Fingers; k++)
                dst[offset + WingBoneLayout.WingBone(n, legs, w, k)] =
                    Matrix4x4.TRS(st.wrist, Look(FingerDir[w * WingSpec.Fingers + k], st.handNormal), sc);
            for (int k = 0; k < WingSpec.Ribs; k++)
            {
                int i = w * WingSpec.Ribs + k;
                float stretch = RibLength[i] / Mathf.Max(1e-6f, ribRestLength[i] * scale);
                dst[offset + WingBoneLayout.WingBone(n, legs, w, WingBoneLayout.FirstRib + k)] =
                    Matrix4x4.TRS(st.wrist, Look(RibDir[i], RibUp[i]), Vector3.one * (scale * stretch));
            }
        }
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
