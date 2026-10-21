// Creature kit: DigitWing - a wing whose wing IS its fingers (Vesper step 1, skeleton only). The arm, the beat, the fold,
// launch / landing and the wing-walk are the flight system's (a WingPart: CreatureFlight + CreatureWings; the planted
// wrist is CreatureLegs' foot and always wins on the ground). On top of the flight's bones this part adds:
//   fingers   one jointed chain per finger (spec.FingerCount, 4 by default), rooted at the wrist and aimed by the flight's
//             finger direction (spread in the air, folded along the forearm on the ground), fingerSegments segments each.
//             Every joint bends in 3D over the segment before it (the frame is carried down the chain):
//               air     a lag / flex about the finger's cross axis driven by the beat (AngleChain on the stroke angle:
//                       each segment follows the one before with a damped spring, flexHz / flexDamping; the bend softly
//                       saturates at flexDeg per joint) and, gliding, a small billow up (kBillow x flexDeg per joint);
//                       in the landing flare (WingSpec.landingFlare) the drawn stroke is held raised and every joint cups
//                       up toward flareCupDeg (the air under the pitched-up hand), blended by the flare's weight;
//               ground  a droop toward world down (a damped spring per joint toward droopDeg x the sine of the segment's
//                       angle off the vertical);
//             every joint's bend clamped to maxBendDeg. The ground (walking: the nearest planted foot's plane; flaring and
//             touching down: the flight's ground below) keeps every joint its radius above it: a sinking segment turns up
//             within the bend limit, and if the chain still sinks the whole finger is lifted at the root, off the flight's
//             finger direction (RootLiftDeg: the least lift that clears it, a bounded bisection).
//   thumb     a small claw of thumbSegments short bones: an arc from the wrist ending on the flight's ThumbTip (forward along
//             the arm's leading side in the air; on the ground the walked contact), curling down by thumbCurlDeg. One
//             thumb: the part's thumbLength is the flight's spec.thumbLength (ConfigureRuntimeSpec).
// Joint radii are authored (fingerRadius per finger at its root, tapering to fingerTaper x it at the tip): they become the
// skin later; F3 draws them (DrawDebug). No mesh in this step: the species needs SkeletonOnly (a step 1 requirement that goes
// away with the skin in step 3).
// Bones: the flight's (WingBoneLayout.Bones(spec)), then fingers x fingerSegments (finger k's segment j at k x fingerSegments
// + j: at the segment's root joint, +Z along it, +Y its dorsal side), then thumbSegments (the claw, root first).
// Allocation-free after construction; main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("DigitWing", PartKind.Wing, Bones = "as Wing + fingers x fingerSegments + thumbSegments",
          Summary = "A wing that is its fingers: on an arm limb (a Leg2Bone it names), flown, folded and walked on like a Wing (CreatureFlight + CreatureWings), with one jointed 3D chain per finger rooted at the wrist and aimed by the flight's finger directions - the fingers flex and lag with the beat and billow gliding in the air, droop under gravity (above the ground) when folded on the ground, bend-limited - and a small forward thumb claw curling down. Skeleton only for now (no mesh: needs SkeletonOnly until the skin comes in Vesper step 3); joint radii authored, drawn by F3.",
          System = "CreatureFlight + CreatureWings (beat, fold, wing-walk) + DigitWingRuntime (finger chains, thumb; stage Late)",
          MaterialLayers = new int[0], KitOnly = true,
          UseWhen = "skeleton-first winged creatures (Vesper): long bony fingers that will carry a membrane between them later; the arm's proportions as FleshWing's (a short humerus, a long forearm)")]
    public sealed class DigitWing : WingPart
    {
        [Param(ParamUnit.Count, "segments (bones) per finger chain", Min = 1, Max = 8, Default = "4")]
        public int fingerSegments = 4;
        [Param(ParamUnit.Metres, "each finger's joint radius at its root (one per finger, leading first; the skin later)", Example = "vesper 0.018 0.016 0.014 0.012")]
        public float[] fingerRadius = { 0.018f, 0.016f, 0.014f, 0.012f };
        [Param(ParamUnit.Fraction, "the finger tip's radius as a fraction of its root's (linear along the chain)", Min = 0.05f, Max = 1f, Default = "0.4")]
        public float fingerTaper = 0.4f;
        [Param(ParamUnit.Degrees, "air: the most the beat bends a joint over the segment before it (0 = stiff fingers)", Min = 0, Max = 45, Default = "10")]
        public float flexDeg = 10f;
        [Param(ParamUnit.Hz, "air flex and ground droop: natural frequency of a segment following the one before (the leading finger's; a shorter finger is stiffer)", Example = "vesper 2")]
        public float flexHz = 2f;
        [Param(ParamUnit.Fraction, "air flex and ground droop: damping ratio (< 0.7: the tips overshoot - a whip)", Min = 0.05f, Max = 2f, Default = "0.5")]
        public float flexDamping = 0.5f;
        [Param(ParamUnit.Degrees, "ground: the most gravity droops a joint toward world down (a segment lying level; less as it hangs)", Min = 0, Max = 45, Default = "8")]
        public float droopDeg = 8f;
        [Param(ParamUnit.Degrees, "the most any joint bends over the segment before it (air and ground)", Min = 1, Max = 90, Default = "25")]
        public float maxBendDeg = 25f;
        [Param(ParamUnit.Degrees, "the landing flare (WingSpec.landingFlare on): each finger joint cups up (dorsal: the air under the pitched-up hand billows the span) toward this, blended by the flare's weight - the air flex's billow, harder (softly saturated at flexDeg like it)", Min = 0, Max = 45, Default = "8")]
        public float flareCupDeg = 8f;
        [Param(ParamUnit.Metres, "the thumb claw's length along its curve", Example = "vesper 0.08")]
        public float thumbLength = 0.08f;
        [Param(ParamUnit.Degrees, "the thumb claw's curl down from its root to its tip", Min = 0, Max = 120, Default = "40")]
        public float thumbCurlDeg = 40f;
        [Param(ParamUnit.Count, "segments (bones) of the thumb claw", Min = 1, Max = 4, Default = "2")]
        public int thumbSegments = 2;

        public DigitWing() { spec = DefaultSpec(); }

        // The default wing: four long fingers fanned from the leading edge back, folded steep along the forearm on the
        // ground (the FleshWing's proven wing-walk: a tall stance standing on the thumb claw, thumbPlant).
        public static WingSpec DefaultSpec() => new WingSpec
        {
            restTip = new Vector3(0.6f, 0.06f, 0.04f), comfortUp = 0.5f, comfortDown = 0.6f, strokePlaneDeg = 10f,
            pronationDeg = 18f, overshootUp = 0.08f, upstrokeDuration = 0.3f, upstrokeFold = 0.5f, foldReach = 0.3f,
            fingerLengths = new[] { 1.5f, 1.3f, 1.1f, 0.9f },
            fingerSpreadDeg = new[] { 20f, 42f, 64f, 86f },
            fingerFoldDeg = new[] { 160f, 162.5f, 165f, 167.5f },
            fingerMinGapDeg = 2f, fingerFoldGapDeg = 3.5f,
            groundLiftDeg = 68f, groundTallStance = true, fanRearRollsWithFan = true, thumbPlant = true, groundWristLift = 0.08f,
            groundFlankRise = 0.6f, flankPatches = 6, thumbLength = 0.08f, membraneRearJoint = 9, membraneFrontJoint = 4,
            trailingRib = 0.5f, area = 3f, scallop = 0f,
        };

        public int Fingers => spec.FingerCount;
        public int Segments => Mathf.Max(1, fingerSegments);
        public int ThumbSegments => Mathf.Max(1, thumbSegments);
        public int ExtraBones => Fingers * Segments + ThumbSegments;
        public override int BoneCount(in SpeciesCtx c) => FlightBones + ExtraBones;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex (it emits none)

        // finger k's segment j (0 = at the wrist), the thumb's segment j: bone offsets from the part's first own bone
        public int FingerBone(int k, int j) => k * Segments + j;
        public int ThumbBone(int j) => Fingers * Segments + j;

        // finger k's joint j radius (0 = the root at the wrist .. Segments = the tip), at scale 1
        public float JointRadius(int k, int j)
        {
            float r0 = fingerRadius != null && k < fingerRadius.Length ? fingerRadius[k] : 0f;
            return r0 * Mathf.Lerp(1f, fingerTaper, j / (float)Segments);
        }

        // one thumb: the drawn claw's length is the walked contact's (CreatureWings.ThumbTip reads spec.thumbLength)
        protected internal override void ConfigureRuntimeSpec(WingSpec runtime) => runtime.thumbLength = thumbLength;

        protected override void CopyArrays()
        {
            base.CopyArrays();
            fingerRadius = (float[])fingerRadius?.Clone();
        }

        protected override bool DrawsLandingFlare => true;   // its drawn pose reads WingSpec.landingFlare (the flare's pose)

        public override string Validate(in SpeciesCtx c)
        {
            string e = base.Validate(c);
            if (e != null) return e;
            if (c.Module<SkeletonOnly>() == null) return $"{id}: a DigitWing has no skin yet (Vesper step 1): the species needs the SkeletonOnly module";
            if (fingerSegments < 1 || fingerSegments > 8) return $"{id}: fingerSegments in 1..8";
            if (thumbSegments < 1 || thumbSegments > 4) return $"{id}: thumbSegments in 1..4";
            if (fingerRadius == null || fingerRadius.Length != spec.FingerCount) return $"{id}: fingerRadius needs one radius per finger ({spec.FingerCount})";
            for (int k = 0; k < fingerRadius.Length; k++) if (!(fingerRadius[k] > 0f)) return $"{id}: fingerRadius[{k}] > 0";
            if (!(fingerTaper > 0f) || fingerTaper > 1f) return $"{id}: fingerTaper in (0, 1]";
            if (!(flexDeg >= 0f) || !(droopDeg >= 0f)) return $"{id}: flexDeg, droopDeg >= 0";
            if (!(flexHz > 0f) || !(flexDamping > 0f) || !(maxBendDeg > 0f)) return $"{id}: flexHz, flexDamping, maxBendDeg > 0";
            if (!(thumbLength > 0f) || !(thumbCurlDeg >= 0f) || thumbCurlDeg > 120f) return $"{id}: thumbLength > 0, thumbCurlDeg in 0..120";
            if (!(flareCupDeg >= 0f) || flareCupDeg > 45f) return $"{id}: flareCupDeg in 0..45";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
        {
            if (c.Sim == null || c.Sim.Flight == null) return null;
            int w = PteroWing.WingIndex(KitView.Parts(c.Species), c.Index);
            return new DigitWingRuntime(this, c.Sim, w, c.Species.Layout.Start(c.Index) + FlightBones);
        }
    }

    // One digit wing of one individual. Step (stage Late, once per tick) runs the dynamics: the flex chains on the beat,
    // the droop springs toward the targets of the last pose. WriteBones builds the pose on the flight's bones as they
    // stand in dst (the wrist, the finger directions, the fan's dorsal side): the chains, the ground plane, the thumb -
    // and keeps it for Joint / BendDeg / DrawDebug.
    public sealed class DigitWingRuntime : IPartRuntime
    {
        const float kBillow = 0.3f;          // gliding: each joint billows up this x flexDeg
        const float kFlareDamping = 1f;      // the landing flare: the flex chains' damping ratio when it is held
        const float kAirBlendSeconds = 0.25f; // the air <-> ground weight's time (x sqrt size)
        const float kThumbRadius = 0.5f;     // the thumb's root radius, x the leading finger's (it tapers to a point x 0.3)
        const int kLiftIterations = 10;      // ground: bisection steps for the root lift when the bend-limited chain sinks in

        readonly DigitWing p;
        readonly CreatureSim sim;
        readonly int w, extra, leg, F, S, TS;
        readonly float T;
        readonly float[] lengths, hz;
        readonly AngleChain[] flex;
        readonly Vector2[] droop, droopV, droopTarget;   // per finger x joint: deg (x dorsal, y across the finger)
        readonly Vector3[] joints;                        // per finger: S + 1 (root .. tip), world
        readonly float[] bends;                           // per finger x joint: the drawn bend (deg, sign = dorsal)
        readonly Vector3[] segDir, segUp;                 // per finger x segment: the posed direction, dorsal side
        readonly float[] rootLift;                        // per finger: the ground's lift at the root (deg)
        readonly Vector3[] thumb;                         // TS + 1 joints
        float airW = -1f;
        float heldBend, drawFlare;   // the landing flare: each joint's held (cupped) bend, the flare's weight as drawn
        bool posed;

        // RED hooks (CreatureDigitWingTests): the ground plane ignored; the chains in plain seconds
        internal static bool IgnoreGroundForTest, IgnoreSizeForTest;

        public int Wing => w;
        public int FirstExtraBone => extra;
        public int Fingers => F;
        public int Segments => S;
        public int ThumbSegments => TS;
        public bool Posed => posed;

        // finger's joint k (0 = the root at the wrist .. Segments = the tip), world, as last posed
        public Vector3 Joint(int finger, int k) => joints[finger * (S + 1) + k];
        // the bend of finger's segment k over the one before it (k = 0: over the flight's finger direction turned up by
        // RootLiftDeg), deg; + dorsal. Always within maxBendDeg.
        public float BendDeg(int finger, int k) => bends[finger * S + k];
        // the ground's lift of the whole finger at the root, off the flight's finger direction (deg; 0 off the ground)
        public float RootLiftDeg(int finger) => rootLift[finger];
        // finger's joint k radius, world
        public float Radius(int finger, int k) => p.JointRadius(finger, k) * sim.SizeScale;
        // the thumb claw's joint k (0 = the wrist .. ThumbSegments = the tip), world
        public Vector3 ThumbJoint(int k) => thumb[k];

        public DigitWingRuntime(DigitWing part, CreatureSim sim, int wing, int firstExtra)
        {
            p = part; this.sim = sim; w = wing; extra = firstExtra;
            CreatureSpecies sp = sim.Species;
            leg = sp.wings[w].leg;
            F = sp.wings[w].FingerCount; S = part.Segments; TS = part.ThumbSegments;
            T = sim.TimeScale;
            lengths = (float[])sp.wings[w].fingerLengths.Clone();
            hz = new float[F];
            float L0 = Mathf.Max(1e-4f, lengths[0]);
            // a shorter finger is a stiffer chain (a pendulum's frequency ~ 1 / sqrt(length))
            for (int k = 0; k < F; k++) hz[k] = part.flexHz * Mathf.Sqrt(L0 / Mathf.Max(1e-4f, lengths[k]));
            flex = new AngleChain[F];
            for (int k = 0; k < F; k++) flex[k] = new AngleChain(S + 1);
            droop = new Vector2[F * S]; droopV = new Vector2[F * S]; droopTarget = new Vector2[F * S];
            joints = new Vector3[F * (S + 1)];
            bends = new float[F * S];
            segDir = new Vector3[F * S]; segUp = new Vector3[F * S];
            rootLift = new float[F];
            thumb = new Vector3[TS + 1];
        }

        public PartStage Stage => PartStage.Late;

        public void Step(in StepCtx c, float dt)
        {
            CreatureFlight fl = sim.Flight;
            if (fl == null || !(dt > 0f)) return;
            float Te = IgnoreSizeForTest ? 1f : T;
            bool ground = fl.Mode == FlightMode.Ground;
            float want = ground ? 0f : 1f;
            if (airW < 0f) airW = want;
            else airW = Mathf.MoveTowards(airW, want, dt / (kAirBlendSeconds * Te));
            bool glide = !ground && fl.Model.Gliding;
            // the drawn stroke (the landing flare holds it raised: CreatureWings.PosedAngle) drives the flex; the flare cups
            // every joint toward flareCupDeg (WingSpec.landingFlare; 0 = the shipped billow)
            float strokeDeg = fl.Wings.PosedAngle(w) * Mathf.Rad2Deg;
            float flare = fl.Wings.FlareWeight(w);
            float billow = glide ? kBillow * p.flexDeg : 0f;
            if (flare > 0f) billow = Mathf.Lerp(billow, p.flareCupDeg, flare);
            heldBend = billow;
            float zeta = p.flexDamping;
            // (held, the flare's chains settle without ringing: damped toward critical by its weight - underdamped, the drive's
            // swing from the beat onto the held raise rang the fingers 4-5 deg a tick through the hold)
            if (flare > 0f) zeta = Mathf.Lerp(zeta, Mathf.Max(zeta, kFlareDamping), flare);
            for (int k = 0; k < F; k++)
            {
                float omega = 2f * Mathf.PI * hz[k] / Te;
                // air: the beat drives the chain's root, every segment follows the one before (the lag); gliding, a billow
                AngleChain a = flex[k];
                for (int j = 1; j <= S; j++) a.SetOffset(j, billow);
                a.Step(strokeDeg, dt, omega, zeta, p.maxBendDeg);
                // ground: each joint's droop is a damped spring toward its gravity target (the last pose's)
                int sub = Mathf.Clamp(Mathf.CeilToInt(omega * dt / 0.5f), 1, 64);
                float h = dt / sub, g = 1f - airW;
                for (int j = 0; j < S; j++)
                {
                    int i = k * S + j;
                    Vector2 target = droopTarget[i] * g, x = droop[i], v = droopV[i];
                    for (int it = 0; it < sub; it++)
                    {
                        v += (omega * omega * (target - x) - 2f * zeta * omega * v) * h;
                        x += v * h;
                    }
                    droop[i] = x; droopV[i] = v;
                }
            }
        }

        // the air flex of a joint: the chain's bend softly saturated at flexDeg (no hard stop at the stroke's turn)
        float Flex(float raw) => p.flexDeg > 0f ? p.flexDeg * (float)Math.Tanh(raw / p.flexDeg) : 0f;

        // n segments each turned curl / n further, centred on the chord: the sum of their projections on the chord per
        // unit segment length
        static float SumCos(float curlDeg, int n)
        {
            float sum = 0f;
            for (int j = 0; j < n; j++) sum += Mathf.Cos((-0.5f * curlDeg + curlDeg * (j + 0.5f) / n) * Mathf.Deg2Rad);
            return sum;
        }

        // Finger k posed from the wrist: the flight's direction d (dorsal n) turned up by lift (deg, toward the plane's
        // normal), then every joint's bend (air flex + droop, at most maxBendDeg), then the ground: a segment whose end
        // would sink below the plane + its radius is turned up toward it - never past maxBendDeg over the segment before.
        // Writes joints, segDir, segUp, bends, droopTarget; returns the deepest remaining penetration (m).
        float PoseFinger(int k, Vector3 wrist, Vector3 d, Vector3 n, float lift, float flexW, bool clamp, Vector3 g0, Vector3 gn, float sc)
        {
            if (lift > 0f)
            {
                Vector3 ax = Vector3.Cross(d, gn);
                if (ax.sqrMagnitude > 1e-10f)
                {
                    Quaternion q = Quaternion.AngleAxis(Mathf.Min(lift, Vector3.Angle(d, gn)), ax.normalized);
                    d = (q * d).normalized; n = q * n;
                }
            }
            n = PartMeshEmitter.Unit(n - d * Vector3.Dot(n, d), Vector3.up);
            Vector3 b = Vector3.Cross(n, d);
            float seg = lengths[k] * sc / S, maxB = p.maxBendDeg, worst = 0f;
            Vector3 pos = wrist;
            joints[k * (S + 1)] = pos;
            AngleChain a = flex[k];
            for (int j = 0; j < S; j++)
            {
                int i = k * S + j;
                // the gravity target in this joint's frame (the segment before it): toward world down, x the sine of the
                // angle off the vertical (a hanging segment no longer droops)
                Vector3 gp = Vector3.down - d * Vector3.Dot(Vector3.down, d);
                droopTarget[i] = new Vector2(Vector3.Dot(gp, n), Vector3.Dot(gp, b)) * p.droopDeg;
                // the bend: the air flex (dorsal) + the droop, clamped to maxBendDeg
                Vector2 e = droop[i];
                // (the landing flare: the drawn bend goes to the held cup by the flare's weight - the chains' own lag
                // settling after the beat rolled the held fan 1 deg a tick, 210-240 deg/s x sqrt s on long perch flares)
                float bend = a.Bend(j + 1);
                if (drawFlare > 0f) bend = Mathf.Lerp(bend, heldBend, drawFlare);
                e.x += flexW * Flex(bend);
                float mag = e.magnitude;
                if (mag > maxB) { e *= maxB / mag; mag = maxB; }
                Vector3 dPrev = d, nPrev = n;
                if (mag > 1e-5f)
                {
                    Vector3 toward = (n * e.x + b * e.y) / mag;
                    Quaternion q = Quaternion.AngleAxis(mag, Vector3.Cross(d, toward).normalized);
                    d = (q * d).normalized; n = q * n; b = q * b;
                }
                Vector3 next = pos + d * seg;
                if (clamp)
                {
                    float r = p.JointRadius(k, j + 1) * sc;
                    float hk = Vector3.Dot(pos - g0, gn);
                    if (Vector3.Dot(next - g0, gn) < r)
                    {
                        // the direction that puts the end on the plane + r, within maxBendDeg of the segment before
                        float want = Mathf.Clamp((r - hk) / seg, -1f, 1f);
                        Vector3 flat = d - gn * Vector3.Dot(d, gn);
                        flat = PartMeshEmitter.Unit(flat, PartMeshEmitter.Unit(b - gn * Vector3.Dot(b, gn), Vector3.forward));
                        Vector3 dn = flat * Mathf.Sqrt(1f - want * want) + gn * want;
                        if (Vector3.Angle(dPrev, dn) > maxB)
                            dn = Vector3.RotateTowards(dPrev, dn, maxB * Mathf.Deg2Rad * 0.9999f, 0f);
                        if (Vector3.Dot(dn, gn) > Vector3.Dot(d, gn))
                        {
                            Quaternion fix = Quaternion.FromToRotation(d, dn);
                            d = dn.normalized; n = fix * n; b = fix * b;
                            next = pos + d * seg;
                        }
                    }
                    float pen = r - Vector3.Dot(next - g0, gn);
                    if (pen > worst) worst = pen;
                }
                // re-square the carried frame (rounding)
                n = PartMeshEmitter.Unit(n - d * Vector3.Dot(n, d), nPrev);
                b = Vector3.Cross(n, d);
                float ang = Vector3.Angle(dPrev, d);
                bends[i] = Vector3.Dot(d, nPrev) < 0f ? -ang : ang;
                segDir[i] = d; segUp[i] = n;
                pos = next;
                joints[k * (S + 1) + j + 1] = pos;
            }
            return worst;
        }

        public void WriteBones(Matrix4x4[] dst, int o)
        {
            CreatureSim s = sim;
            CreatureFlight fl = s.Flight;
            if (fl == null) return;
            CreatureSpecies sp = s.Species;
            float sc = s.SizeScale;
            var scale = new Vector3(sc, sc, sc);
            ref WingState st = ref fl.Wings.State[w];
            Vector3 wrist = dst[o + sp.LegBone(leg, 2)].GetColumn(3);
            float flexW = airW < 0f ? 0f : airW * (1f - Mathf.Clamp01(fl.Wings.DrawnFold(w)));
            drawFlare = fl.Wings.FlareWeight(w);

            // the ground plane: on the ground, the nearest planted foot's (point and normal); flaring or touching down,
            // the flight's ground below the girdle (level)
            bool clamp = !IgnoreGroundForTest;
            Vector3 g0 = Vector3.zero, gn = Vector3.up;
            if (clamp && fl.Mode == FlightMode.Ground)
            {
                float best = float.PositiveInfinity;
                LegState[] legs = s.Legs;
                for (int l = 0; l < legs.Length; l++)
                {
                    if (legs[l].stepping || sp.legs[l].feeler) continue;
                    float dd = (legs[l].foot - wrist).sqrMagnitude;
                    if (dd < best) { best = dd; g0 = legs[l].foot; gn = legs[l].footNormal.sqrMagnitude > 0.5f ? legs[l].footNormal.normalized : Vector3.up; }
                }
                clamp = best < float.PositiveInfinity;
            }
            else if (clamp && (fl.Mode == FlightMode.Flare || fl.Mode == FlightMode.Touchdown) && !float.IsInfinity(fl.HeightAboveGround))
                g0 = fl.GroundBelow;
            else clamp = false;

            for (int k = 0; k < F; k++)
            {
                Matrix4x4 fb = dst[o + sp.WingBone(w, k)];
                Vector3 d0 = PartMeshEmitter.Unit(fb.GetColumn(2), st.handOut);
                Vector3 n0 = (Vector3)fb.GetColumn(1);
                n0 -= d0 * Vector3.Dot(n0, d0);
                n0 = PartMeshEmitter.Unit(n0, PartMeshEmitter.Unit(Vector3.Cross(d0, Vector3.right), Vector3.up));
                // the chain within its bend limits; when the ground still has a joint, the whole finger is lifted at the
                // root (turned off the flight's finger direction toward the plane's normal) and posed again (bounded)
                float eps = 1e-5f * sc;
                float lift = 0f;
                if (PoseFinger(k, wrist, d0, n0, 0f, flexW, clamp, g0, gn, sc) > eps)
                {
                    // the least lift that clears the ground: bisection between none and the finger turned onto the normal
                    float lo = 0f, hi = Vector3.Angle(d0, gn);
                    if (PoseFinger(k, wrist, d0, n0, hi, flexW, clamp, g0, gn, sc) <= eps)
                        for (int it = 0; it < kLiftIterations; it++)
                        {
                            float mid = 0.5f * (lo + hi);
                            if (PoseFinger(k, wrist, d0, n0, mid, flexW, clamp, g0, gn, sc) <= eps) hi = mid; else lo = mid;
                        }
                    lift = hi;
                    PoseFinger(k, wrist, d0, n0, lift, flexW, clamp, g0, gn, sc);
                }
                rootLift[k] = lift;
                for (int j = 0; j < S; j++)
                    dst[o + extra + p.FingerBone(k, j)] = Matrix4x4.TRS(joints[k * (S + 1) + j], Quaternion.LookRotation(segDir[k * S + j], segUp[k * S + j]), scale);
            }

            // the thumb claw: an arc from the wrist ending on the flight's thumb tip (forward along the leading side in
            // the air, the walked contact on the ground: one thumb - DigitWing.thumbLength drives spec.thumbLength),
            // curling down (ventral) by thumbCurlDeg
            {
                Vector3 hn = PartMeshEmitter.Unit(st.handNormal, Vector3.up);
                Vector3 fwd = -fl.Wings.Side(w) * Vector3.Cross(hn, PartMeshEmitter.Unit(fl.Wings.HandBoneOut[w], st.handOut));
                Vector3 tip = fl.Wings.ThumbTip(w);
                float c = (tip - wrist).magnitude;
                Vector3 chord = PartMeshEmitter.Unit(tip - wrist, PartMeshEmitter.Unit(fwd, Vector3.forward));
                Vector3 down = -hn - chord * Vector3.Dot(-hn, chord);
                down = PartMeshEmitter.Unit(down, PartMeshEmitter.Unit(Vector3.Cross(chord, Vector3.right), Vector3.down));
                Vector3 axis = Vector3.Cross(chord, down);   // turning chord toward down
                float curl = p.thumbCurlDeg;
                // each segment's length: the polyline's chord is exactly the wrist -> thumb tip distance
                float segLen = c / Mathf.Max(1e-6f, SumCos(curl, TS));
                Vector3 thumbUp = -down;
                Vector3 pos = wrist;
                thumb[0] = pos;
                for (int j = 0; j < TS; j++)
                {
                    float ang = -0.5f * curl + curl * (j + 0.5f) / TS;
                    Vector3 d = Quaternion.AngleAxis(ang, axis) * chord;
                    Vector3 up = Quaternion.AngleAxis(ang, axis) * thumbUp;
                    dst[o + extra + p.ThumbBone(j)] = Matrix4x4.TRS(pos, Quaternion.LookRotation(d, up), scale);
                    pos += d * segLen;
                    thumb[j + 1] = pos;
                }
                thumb[TS] = tip;   // (rounding)
            }
            posed = true;
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi)
        {
            if (!posed) return;
            for (int i = 0; i < joints.Length; i++) { lo = Vector3.Min(lo, joints[i]); hi = Vector3.Max(hi, joints[i]); }
            for (int i = 0; i < thumb.Length; i++) { lo = Vector3.Min(lo, thumb[i]); hi = Vector3.Max(hi, thumb[i]); }
        }

        static readonly Color kFinger = new Color(0.25f, 0.75f, 0.69f), kThumb = new Color(0.95f, 0.9f, 0.8f);

        // F3: every finger's segments and joints (a small circle at each joint's radius), the thumb claw.
        public void DrawDebug(ICreatureDebugDraw dd)
        {
            if (!posed) return;
            float sc = sim.SizeScale;
            for (int k = 0; k < F; k++)
            {
                int b = k * (S + 1);
                for (int j = 0; j < S; j++) dd.Line(joints[b + j], joints[b + j + 1], kFinger);
                for (int j = 0; j <= S; j++) dd.Point(joints[b + j], p.JointRadius(k, j) * sc, kFinger);
            }
            float r0 = kThumbRadius * p.JointRadius(0, 0) * sc;
            for (int j = 0; j < TS; j++) dd.Line(thumb[j], thumb[j + 1], kThumb);
            for (int j = 0; j <= TS; j++) dd.Point(thumb[j], r0 * Mathf.Lerp(1f, 0.3f, j / (float)TS), kThumb);
        }
    }
}
