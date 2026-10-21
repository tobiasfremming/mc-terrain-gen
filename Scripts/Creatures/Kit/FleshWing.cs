// Creature kit: FleshWing - a soft, thick, folding wing (Apex redesign §5.2). It flies, folds and is walked on exactly
// as a Wing is (a WingPart: CreatureFlight + CreatureWings beat it, fold it, stand it on the wrist in a wing-walk), but
// it is drawn as flesh, never as a sheet:
//   sail       the flight system's fan from the wrist (its fingers - the hidden rays of the sail -, the trailing rib, the
//              rear flank rib, the forearm) and its flank patches to the body, drawn as two skins (dorsal, ventral) offset
//              +- half the thickness law (ThicknessAt: thick at the root and along the arm, thin toward the tip and the
//              trailing edge) and joined by a soft rounded rim along the leading edge and the smooth trailing edge;
//   arm        the leading edge's flesh round the humerus and forearm (the body's dorsal colour), no bone tube nor ball
//              elbow: one loft from the shoulder through a soft bend at the elbow to the club, rounded round the bones and
//              running on into the sheet as a leaf (armChord x its radius toward the sail; folded, the blade's lower half is
//              the sheet rolled round the forearm down to the club), on three flesh frames of its own;
//   pad        the wrist's blunt soft club - what it walks on (WingSpec.thumbPlant: CreatureWings.ThumbTip, the walked
//              contact, is the pad's lowest point);
//   colour     the inner (arm) half in the body's dorsal colour, the outer half the `wingfan` kind with dark `wingstripe`
//              stripes (and shallow ridges) running along the rays, i.e. along the folded blade.
// Bones: the flight's (WingBoneLayout.Bones(spec)), then spanSegments - 1 per finger: each finger is a span chain (segment
// 0 is the flight's finger bone, the rest follow it with a lag: the outer wing whips at the stroke's turn), and the rays
// behind the leading finger are a chord chain (the trailing edge feathers on the beat and billows in a glide). The sail
// between two rays is skinned to the two rays' bones at the same span segment (the shared membrane's rule); then the fan
// sector and the arm's three flesh frames (ArmFleshBone). The bind pose
// is the spread wing, optionally part-folded (bindFold). Off by default: a species without a FleshWing is unchanged.
// Allocation-free after construction; main thread.
using System;
using UnityEngine;

namespace CreatureKit
{
    [Serializable]
    [Part("FleshWing", PartKind.Wing, Bones = "as Wing + fingers x (spanSegments - 1) + 1 (the fan sector) + 3 (the arm's flesh: humerus, elbow, forearm)",
          Summary = "A soft, thick, folding wing on an arm limb (a Leg2Bone it names), flown, folded and walked on like a Wing (CreatureFlight + CreatureWings): two skins offset by a thickness law (thick root and arm, thin tip and trailing edge) joined by a rounded rim, the arm buried in the leading edge's flesh (one soft loft bending at the elbow, a leaf running on into the sheet: folded, the sheet rolled round the forearm), a blunt soft pad at the wrist that it walks on, lengthwise stripes along the rays; span chains along the fingers and a chord chain across the rays (the outer wing lags and whips, the trailing edge feathers and billows).",
          System = "CreatureFlight + CreatureWings (beat, fold, wing-walk) + FleshWingRuntime (span and chord chains; stage Late) + FleshWingMesh",
          MaterialLayers = new[] { 0 }, MaterialKinds = new[] { "wingfan", "wingstripe" }, KitOnly = true,
          UseWhen = "meaty manta-like wings that fold into tall soft blades and are walked on (the Apex); not a membrane between bony fingers. The steep folded blades come from the arm's proportions: a short humerus and a long forearm (upper << lower, e.g. 0.2 / 0.5 on a 0.5 m chest), the wrist's restOffset behind the shoulder (z ~ -0.22) and close in (x ~ 0.06), pole (0.2, 1, -0.4): the forearm stands near vertical and the folded fingers run up along it, side by side over the back")]
    public sealed partial class FleshWing : WingPart
    {
        [Param(ParamUnit.Metres, "thickness at the root (the body and the arm), dorsal to ventral skin", Example = "apex 0.07")]
        public float rootThickness = 0.07f;
        [Param(ParamUnit.Metres, "thickness toward the tip (the law never goes below half of it)", Example = "apex 0.012")]
        public float tipThickness = 0.012f;
        [Param(ParamUnit.Fraction, "the rim's roundness: the free edge rolls into a bead at least this x tipThickness in radius", Min = 0.4f, Max = 2f)]
        public float edgeRound = 0.5f;
        [Param(ParamUnit.Metres, "the fleshy arm's radius at the shoulder (it tapers to the wrist)", Example = "apex 0.06")]
        public float armBulk = 0.06f;
        [Param(ParamUnit.Fraction, "the leading edge's flesh round the forearm runs on into the sheet as a leaf: its section reaches this x its radius from the bone toward the sail (1 = a round tube; folded, the blade's lower half is this broad down to the club)", Min = 1, Max = 4, Example = "apex 3")]
        public float armChord = 3f;
        [Param(ParamUnit.Degrees, "the leaf's rest roll about the forearm, from the elbow's hinge axis toward the sail (the elbow and the leaf's upper part keep it; down the forearm the leaf turns on to face the sail, at most a radian either way): between the spread wing's sail side and the folded blade's", Min = -180, Max = 180, Example = "apex -55")]
        public float armRollDeg = -55f;
        [Param(ParamUnit.Fraction, "the leading edge's flesh, part of the sail's thickness: the arm's flesh runs on along the leading finger, this x armBulk extra half thickness at the root's leading edge, tapering with the span (0 = the plain thickness law)", Min = 0, Max = 1.5f)]
        public float leadFlesh = 0.5f;
        [Param(ParamUnit.Metres, "extra thickness swelling the root next to the body (the wing muscles), fading out by a quarter of the span", Example = "apex 0.03")]
        public float rootSwell = 0.03f;
        [Param(ParamUnit.Count, "dark stripes across the sail, running along the rays (0 = none)", Min = 0, Max = 24)]
        public int stripes = 9;
        [Param(ParamUnit.Metres, "the stripes' shallow ridges: each stripe stands this much proud of the skin", Example = "apex 0.003")]
        public float stripeDepth = 0.003f;
        [Param(ParamUnit.Metres, "the wrist pad's radius: the blunt soft club the folded wing stands on", Example = "apex 0.06")]
        public float padRadius = 0.06f;
        [Param(ParamUnit.Metres, "the club's length along the stance's up from the walked contact (the sail rolled round the arm's end; at least 2.2 x padRadius); the forearm swells into it over as much", Example = "apex 0.16")]
        public float clubLength = 0.16f;
        [Param(ParamUnit.Metres, "a hook curling forward and down off the club's front (the hand's claw showing through; 0 = none)", Example = "apex 0.05")]
        public float hookLength = 0f;
        [Param(ParamUnit.Fraction, "the inner part of the sail in the body's dorsal colour, as a fraction of the rays' length from the wrist (the rest is wingfan with stripes)", Min = 0, Max = 1)]
        public float innerColour = 0.45f;
        [Param(ParamUnit.Count, "segments per finger (span chain links); >= 2 to whip", Min = 1, Max = 4)]
        public int spanSegments = 3;
        [Param(ParamUnit.Fraction, "the bind pose's fold (0 = the spread glide pose): the mesh is built part-folded so neither the spread nor the ground fold asks two-ray skinning for too large a ratio", Min = 0, Max = 0.8f)]
        public float bindFold = 0.3f;
        [Param(ParamUnit.Hz, "span chain: natural frequency of a finger segment following the one before (x 1/sqrt size)", Example = "apex 1.4")]
        public float spanHz = 1.4f;
        [Param(ParamUnit.Fraction, "span chain: damping ratio (< 0.7: the tip overshoots - the whip)", Min = 0.1f, Max = 2)]
        public float spanDamping = 0.45f;
        [Param(ParamUnit.Degrees, "span chain: the most a segment bends over the one before, either way", Min = 1, Max = 30)]
        public float spanMaxDeg = 20f;
        [Param(ParamUnit.Hz, "chord chain: natural frequency of a ray following the ray ahead", Example = "apex 1.8")]
        public float chordHz = 1.8f;
        [Param(ParamUnit.Fraction, "chord chain: damping ratio", Min = 0.1f, Max = 2)]
        public float chordDamping = 0.5f;
        [Param(ParamUnit.Degrees, "chord chain: the feathering the beat asks of the first ray behind the leading finger at the fastest stroke (+ trailing edge up on the downstroke)", Min = 0, Max = 30)]
        public float featherDeg = 10f;
        [Param(ParamUnit.Degrees, "chord chain: the most a ray turns over the ray ahead", Min = 1, Max = 25)]
        public float chordMaxDeg = 6f;
        [Param(ParamUnit.Degrees, "gliding, the sail billows: each ray lifts this much over the ray ahead (and each finger segment over the one before)", Min = 0, Max = 10)]
        public float billowDeg = 2f;
        [Param(ParamUnit.Count, "mesh: columns per sail zone (across the rays)", Min = 2, Max = 24)]
        public int columns = 6;
        [Param(ParamUnit.Count, "mesh: rows along the rays (rounded up to a multiple of spanSegments)", Min = 4, Max = 48)]
        public int rows = 15;

        public int Segments => Mathf.Max(1, spanSegments);

        // the default wing: five fingers, a manta's swept sail spread, tall soft blades folded on the ground (the Apex
        // redesign's numbers, the middle of the scanned passing region - task 8 report); steep, side-by-side blades also
        // need the arm's proportions (see UseWhen)
        public FleshWing() { spec = DefaultSpec(); }

        public static WingSpec DefaultSpec() => new WingSpec
        {
            restTip = new Vector3(0.6f, 0.06f, 0.04f), comfortUp = 0.5f, comfortDown = 0.6f, strokePlaneDeg = 10f,
            pronationDeg = 18f, overshootUp = 0.08f, upstrokeDuration = 0.3f, upstrokeFold = 0.5f, foldReach = 0.3f,
            fingerLengths = new[] { 1.6f, 1.42f, 1.25f, 1.08f, 0.92f },
            fingerSpreadDeg = new[] { 20f, 36f, 52f, 68f, 84f },
            fingerFoldDeg = new[] { 160f, 162f, 164f, 166f, 168f },
            fingerMinGapDeg = 2f, fingerFoldGapDeg = 3.5f,
            groundLiftDeg = 68f, groundTallStance = true, fanRearRollsWithFan = true, thumbPlant = true, groundWristLift = 0.08f,
            // membraneRearJoint / membraneFrontJoint are the fixture body's joints (rear attach half way from the shoulder at
            // joint 7 to the hips at 11): a species sets them for its own body
            groundFlankRise = 0.6f, flankPatches = 6, thumbLength = 0.045f, membraneRearJoint = 9, membraneFrontJoint = 4,
            trailingRib = 0.5f, area = 3f, scallop = 0f,
        };
        // the span segments (spanSegments - 1 per finger), then the fan sector (FanSector), then the arm's flesh (ArmFleshBone)
        public int ExtraBones => spec.FingerCount * (Segments - 1) + 1 + ArmFleshBones;
        internal const int ArmFleshBones = 3;
        public override int BoneCount(in SpeciesCtx c) => FlightBones + ExtraBones;
        public override int MeshOrder => 100;   // a new type: after every shipped vertex

        // The shared thickness law (the mesh and the tests): span01 0 root .. 1 tip, chord01 0 leading .. 1 trailing edge.
        public float ThicknessAt(float span01, float chord01)
        {
            float s = Mathf.Pow(Mathf.Clamp01(span01), 0.8f), c = Mathf.Clamp01(chord01);
            return Mathf.Max(tipThickness * 0.5f, Mathf.Lerp(rootThickness, tipThickness, s) * (1f - c * c * 0.7f));
        }

        // the rim's radius at the free edge (the bead the skins roll into)
        public float RimRadius(float halfThickness) => Mathf.Max(halfThickness, edgeRound * tipThickness);

        public override string Validate(in SpeciesCtx c)
        {
            string e = base.Validate(c);
            if (e != null) return e;
            if (!(rootThickness > 0f) || !(tipThickness > 0f)) return $"{id}: rootThickness and tipThickness > 0";
            if (!(armBulk > 0f) || !(padRadius > 0f) || rootSwell < 0f || stripeDepth < 0f || leadFlesh < 0f) return $"{id}: armBulk, padRadius > 0; rootSwell, stripeDepth, leadFlesh >= 0";
            if (!(clubLength > 0f) || hookLength < 0f) return $"{id}: clubLength > 0, hookLength >= 0";
            if (!(armChord >= 1f) || armChord > 4f) return $"{id}: armChord in 1..4 (x the arm's radius)";
            if (!(armRollDeg >= -180f) || armRollDeg > 180f) return $"{id}: armRollDeg in -180..180";
            if (edgeRound < 0.4f) return $"{id}: edgeRound >= 0.4 (a rounded rim)";
            if (stripes < 0) return $"{id}: stripes >= 0";
            if (innerColour < 0f || innerColour > 1f) return $"{id}: innerColour in 0..1";
            if (spanSegments < 1 || spanSegments > 4) return $"{id}: spanSegments in 1..4";
            if (bindFold < 0f || bindFold > 0.8f) return $"{id}: bindFold in 0..0.8";
            if (!(spanHz > 0f) || !(chordHz > 0f) || !(spanDamping > 0f) || !(chordDamping > 0f) || !(spanMaxDeg > 0f) || !(chordMaxDeg > 0f))
                return $"{id}: spanHz, chordHz, the dampings and the max bends > 0";
            if (columns < 2 || rows < 4) return $"{id}: columns >= 2, rows >= 4";
            if (spec.FingerCount < 4) return $"{id}: a FleshWing needs 4-5 fingers (rays of the sail)";
            // the spec is the flight's; a FleshWing draws its own wing - the Wing's look fields would silently do nothing
            WingSpec w = spec;
            if (w.scallop != 0f || w.scallopByChord || w.membraneBillow != 0f || w.membraneDetail != 0 || w.fingerKnuckles != 0f
                || w.wristKnuckle != 0f || w.armMuscle != 0f || w.forearmTwin != 0f || w.thumbRadius != 0f)
                return $"{id}: spec.scallop / scallopByChord / membraneBillow / membraneDetail / fingerKnuckles / wristKnuckle / armMuscle / forearmTwin / thumbRadius are the Wing's look - a FleshWing draws its own: set them to 0";
            return null;
        }

        public override IPartRuntime CreateRuntime(in PartCtx c)
        {
            if (c.Sim == null || c.Sim.Flight == null) return null;
            int w = PteroWing.WingIndex(KitView.Parts(c.Species), c.Index);
            return new FleshWingRuntime(this, c.Sim, w, c.Species.Layout.Start(c.Index) + FlightBones);
        }

        // ------------------------------------------------------------ shared geometry (runtime and mesh)

        // finger k's segment s bone (s = 0: the flight's finger bone)
        internal int SegmentBone(CreatureSpecies sp, int w, int extra, int k, int s) => s == 0 ? sp.WingBone(w, k) : extra + k * (Segments - 1) + s - 1;
        // The fan sector: an affine frame at the wrist on the trailing rib and the rear flank rib (to its flank end), as the
        // flight's arm sector is on the rear rib and the forearm. The sail zone between those two rays rides it alone: the
        // rear rib stretches to stay on the flank (a launch reaches the wrist a metre from it), and two-ray skinning of a zone
        // whose one ray stretches while the other does not turns its trailing edge over; an affine bone maps its triangle
        // exactly whatever the stretch.
        internal int FanSectorBone(int extra) => extra + spec.FingerCount * (Segments - 1);
        // The arm's flesh frames (the leading edge's loft rides them, FleshWingMesh.LeafArm), k: 0 the humerus (at the
        // shoulder, +Z to the elbow), 1 the elbow (at the elbow, +Z down the forearm), both +X the elbow's hinge axis turned
        // armRollDeg about their own +Z (one roll both sides of the joint: their blend is a pure hinge - the soft bend never
        // twists); 2 the forearm (at the elbow, +Z down the forearm, +X the elbow's turned on toward the sail: the fingers'
        // side square to the forearm, by sin(that angle off armRollDeg) radians - the sail's side within ~10 deg over +-50
        // deg, never more than a radian off the elbow's, continuous whatever the sail does). Rigid, uniformly scaled; their
        // rolls never flip with the arm's leg bones' (whose pole rule turns them over in a beat). Mirrored wings mirror the
        // angles.
        internal int ArmFleshBone(int extra, int k) => FanSectorBone(extra) + 1 + k;

        // The rays a chord chain turns about the wrist, behind the leading finger: fingers 1.., then the trailing rib.
        internal int ChordRays => spec.FingerCount;
        internal int ChordRayBone(int r) => r < spec.FingerCount - 1 ? r + 1 : WingBoneLayout.FirstRibOf(spec);

        // The bind pose's wings: the spread rest pose with this wing part-folded (bindFold).
        internal CreatureWings BindWings(CreatureSim rest, int w)
        {
            CreatureSpecies sp = rest.Species;
            var cw = new CreatureWings(sp, sp.wings, rest.Joints, rest.Rotations, 1f);
            if (bindFold > 0f) { cw.SetPose(w, 0f, bindFold); cw.Pose(); }
            return cw;
        }
    }

    // One flesh wing of one individual: the span chains (one per finger) and the chord chain, written on top of the
    // flight's bones (stage Late; WriteBones runs after the flight's).
    public sealed class FleshWingRuntime : IPartRuntime, ISpreadWingsRest
    {
        readonly FleshWing p;
        readonly CreatureSim sim;
        readonly int w, extra, leg, fingers, segs;
        readonly float T;
        readonly AngleChain[] span;
        readonly AngleChain chord;
        readonly float[] bends;          // per finger x segment: the bend written (deg, + = dorsal)
        readonly float[] chordAngle;     // per chord ray: the angle written (deg)
        readonly float[] lengths;
        Matrix4x4[] bindScratch;
        internal static bool IgnoreSizeForTest;   // RED hook (CreatureFleshWingTests.Red_FleshWing_NotFroude): chains in plain seconds

        public int Wing => w;
        public int FirstExtraBone => extra;
        public int Fingers => fingers;
        public int Segments => segs;
        public int ChordRays => chordAngle.Length;

        public FleshWingRuntime(FleshWing part, CreatureSim sim, int wing, int firstExtra)
        {
            p = part; this.sim = sim; w = wing; extra = firstExtra;
            CreatureSpecies sp = sim.Species;
            leg = sp.wings[w].leg;
            fingers = sp.wings[w].FingerCount; segs = part.Segments;
            T = sim.TimeScale;
            span = new AngleChain[fingers];
            for (int k = 0; k < fingers; k++) span[k] = new AngleChain(segs);
            chord = new AngleChain(1 + part.ChordRays);
            bends = new float[fingers * segs];
            chordAngle = new float[part.ChordRays];
            lengths = (float[])sp.wings[w].fingerLengths.Clone();
        }

        public PartStage Stage => PartStage.Late;

        // the bend of finger k's segment s over segment s - 1 (deg, + = dorsal; s = 0 is always 0)
        public float SpanBend(int finger, int seg) => seg <= 0 ? 0f : bends[finger * segs + seg];
        // the chord chain's angle of ray r (1 = the first ray behind the leading finger .. ChordRays; deg, + = trailing edge up)
        public float ChordAngle(int ray) => ray <= 0 || ray > chordAngle.Length ? 0f : chordAngle[ray - 1];

        public void Step(in StepCtx c, float dt)
        {
            CreatureFlight fl = sim.Flight;
            if (fl == null || !(dt > 0f)) return;
            float T = IgnoreSizeForTest ? 1f : this.T;
            ref WingState st = ref fl.Wings.State[w];
            bool air = fl.Mode != FlightMode.Ground;
            bool glide = air && fl.Model.Gliding;
            // folding, the chains fade out fast (a closing fan's zones are narrow): fully off on the ground
            float spread = air ? (1f - st.fold) * (1f - st.fold) : 0f;
            float strokeDeg = st.angle * Mathf.Rad2Deg;
            float L0 = Mathf.Max(1e-4f, lengths[0]);
            for (int k = 0; k < fingers; k++)
            {
                AngleChain a = span[k];
                for (int s = 1; s < segs; s++) a.SetOffset(s, glide ? p.billowDeg * spread : 0f);
                // a shorter finger is a stiffer chain (a pendulum's frequency ~ 1 / sqrt(length))
                float hz = p.spanHz * Mathf.Sqrt(L0 / Mathf.Max(1e-4f, lengths[k]));
                a.Step(strokeDeg, dt, 2f * Mathf.PI * hz / T, p.spanDamping, p.spanMaxDeg);
                for (int s = 1; s < segs; s++) bends[k * segs + s] = a.Bend(s) * spread;
            }
            float rateMax = Mathf.Max(1e-3f, sim.Species.flight.strokeRateMax / T);
            bool beating = fl.Mode == FlightMode.Air && !glide;   // a launch's, a flare's and a landing's swings drive no feathering
            float feather = beating ? Mathf.Clamp(p.featherDeg * -st.angleRate / rateMax, -p.chordMaxDeg, p.chordMaxDeg) : 0f;
            for (int r = 1; r < chord.Count; r++) chord.SetOffset(r, glide ? p.billowDeg * spread : 0f);
            chord.Step(feather, dt, 2f * Mathf.PI * p.chordHz / T, p.chordDamping, p.chordMaxDeg);
            for (int r = 1; r < chord.Count; r++) chordAngle[r - 1] = chord.Angle(r) * spread;
        }

        public void WriteBones(Matrix4x4[] dst, int offset) => Write(dst, offset, true);

        // the arm's flesh frames: the IK plane's share in the hinge axis, the flesh's largest elbow bend (deg)
        const float kPoleHinge = 0.25f, kFleshBendDeg = 160f;
        // RED hook (CreatureFleshWingTests.Red_FleshWing_LowBendCap_ShoulderWanders): the flesh's bend cap (deg;
        // 0 = kFleshBendDeg)
        internal static float FleshBendForTest;

        // the bind: the spread wing (part-folded by bindFold: its arm and flight bones rewritten), no chains
        public void AfterSpreadWings(Matrix4x4[] bones, int offset)
        {
            if (p.bindFold > 0f)
            {
                CreatureSpecies sp = sim.Species;
                if (bindScratch == null) bindScratch = new Matrix4x4[sp.BoneCount];
                p.BindWings(sim, w).WriteBones(bindScratch, 0);
                for (int k = 0; k < 3; k++) bones[offset + sp.LegBone(leg, k)] = bindScratch[sp.LegBone(leg, k)];
                for (int k = 0; k < p.FlightBones; k++) bones[offset + sp.WingBone(w, k)] = bindScratch[sp.WingBone(w, k)];
            }
            Write(bones, offset, false);
        }

        // the segment bones on the flight bones in dst as they stand, no chains (tests: the spread wing of a part-folded bind)
        internal void WriteRestSegments(Matrix4x4[] dst, int offset) => Write(dst, offset, false);

        void Write(Matrix4x4[] dst, int o, bool chains)
        {
            CreatureSpecies sp = sim.Species;
            float s = sim.SizeScale;
            // the trailing rib: a ray of the sail like a finger - at its own length (the flight scales it with the square root
            // of the rear rib's stretch), in the fingers' plane (turned about their normal from the last finger toward the rear
            // rib's direction in that plane, spec.trailingRib of the way) and rolled as they are: the zone between it and the
            // last finger is a flat fan zone that never twists as it closes (the rear rib leaves the plane: the fan sector
            // behind takes that)
            int trail = o + sp.WingBone(w, WingBoneLayout.FirstRibOf(sp.wings[w]));
            {
                Matrix4x4 B = dst[trail], L = dst[o + sp.WingBone(w, fingers - 1)];
                Matrix4x4 Rr = dst[o + sp.WingBone(w, WingBoneLayout.FirstRibOf(sp.wings[w]) + 1)];
                Vector3 n = PartMeshEmitter.Unit(L.GetColumn(1), Vector3.up), d4 = PartMeshEmitter.Unit(L.GetColumn(2), Vector3.forward);
                Vector3 rr = (Vector3)Rr.GetColumn(2);
                rr -= n * Vector3.Dot(rr, n);
                Vector3 f = d4;
                if (rr.sqrMagnitude > 1e-10f)
                {
                    float a = Vector3.SignedAngle(d4, rr, n);
                    f = Quaternion.AngleAxis(a * sp.wings[w].trailingRib, n) * d4;
                }
                dst[trail] = Matrix4x4.TRS(B.GetColumn(3), Quaternion.LookRotation(f, n), new Vector3(s, s, s));
            }
            // the arm's flesh frames (FleshWing.ArmFleshBone), from the arm's joints as its leg bones carry them and the fingers'
            // directions before the chains
            {
                Vector3 sh = dst[o + sp.LegBone(leg, 0)].GetColumn(3), el = dst[o + sp.LegBone(leg, 1)].GetColumn(3), wr = dst[o + sp.LegBone(leg, 2)].GetColumn(3);
                Vector3 uz = PartMeshEmitter.Unit(el - sh, Vector3.forward), fz = PartMeshEmitter.Unit(wr - el, uz);
                Vector3 sum = Vector3.zero;
                for (int k = 0; k < fingers; k++) sum += PartMeshEmitter.Unit(dst[o + sp.WingBone(w, k)].GetColumn(2), fz);
                Vector3 q = sum - fz * Vector3.Dot(sum, fz);
                float side = sim.Flight.Wings.Side(w) < 0 ? 1f : -1f;   // (the angles' sense: the right wing's mirrored)
                // the hinge axis: the bones' cross, steadied by the arm's IK plane (shoulder -> wrist x the pole: the same
                // direction wherever the elbow stands on the pole's side) - an arm folded flat or straight keeps a hinge
                Vector3 reach = wr - sh;
                Vector3 plane = PartMeshEmitter.Unit(Vector3.Cross(reach, sim.Flight.Wings.ArmPole(w)), Vector3.zero);
                Vector3 hinge = (Vector3.Cross(fz, uz) + kPoleHinge * plane) * side;
                // (the fingers never lie along the forearm - spread they sweep out from it, folded they stand 10-25 deg off it)
                q = PartMeshEmitter.Unit(q, PartMeshEmitter.Unit(hinge, Vector3.right));
                hinge = PartMeshEmitter.Unit(hinge, PartMeshEmitter.Unit(Vector3.Cross(q, fz), Vector3.right));
                // the flesh's elbow bends at most kFleshBendDeg (an arm folded flat in a touchdown's blend: the flesh's humerus
                // turns about the elbow instead, its shoulder end inside the chest)
                float bend = Vector3.Angle(uz, fz), cap = FleshBendForTest > 0f ? FleshBendForTest : kFleshBendDeg;
                if (bend > cap)
                {
                    float lu = (el - sh).magnitude;
                    uz = Quaternion.AngleAxis(bend - cap, -side * hinge) * uz;
                    sh = el - uz * lu;
                }
                float roll = p.armRollDeg, tau = side * Vector3.SignedAngle(hinge, q, fz);
                float turn = roll + Mathf.Rad2Deg * Mathf.Sin((tau - roll) * Mathf.Deg2Rad);
                Vector3 xu = Quaternion.AngleAxis(side * roll, uz) * hinge, xe = Quaternion.AngleAxis(side * roll, fz) * hinge, xf = Quaternion.AngleAxis(side * turn, fz) * hinge;
                Vector3 sc = new Vector3(s, s, s);
                dst[o + p.ArmFleshBone(extra, 0)] = Matrix4x4.TRS(sh, Quaternion.LookRotation(uz, Vector3.Cross(uz, xu)), sc);
                dst[o + p.ArmFleshBone(extra, 1)] = Matrix4x4.TRS(el, Quaternion.LookRotation(fz, Vector3.Cross(fz, xe)), sc);
                dst[o + p.ArmFleshBone(extra, 2)] = Matrix4x4.TRS(el, Quaternion.LookRotation(fz, Vector3.Cross(fz, xf)), sc);
            }
            // the chord chain: each ray behind the leading finger pitched about the wrist across the sail (+ lifts its far
            // end - the trailing edge - toward the dorsal side)
            if (chains)
            {
                Matrix4x4 F0 = dst[o + sp.WingBone(w, 0)];
                Vector3 wrist = F0.GetColumn(3);
                Vector3 dorsal = sim.Flight.Wings.State[w].handNormal;
                for (int r = 0; r < chordAngle.Length; r++)
                {
                    float ang = chordAngle[r];
                    if (ang == 0f) continue;
                    int b = o + sp.WingBone(w, p.ChordRayBone(r));
                    Matrix4x4 B = dst[b];
                    Vector3 d = PartMeshEmitter.Unit(B.GetColumn(2), F0.GetColumn(2));
                    Vector3 axis = Vector3.Cross(d, dorsal);
                    if (axis.sqrMagnitude < 1e-10f) continue;
                    Quaternion q = Quaternion.AngleAxis(ang, axis.normalized);
                    dst[b] = Matrix4x4.TRS(wrist, q, Vector3.one) * Matrix4x4.TRS(-wrist, Quaternion.identity, Vector3.one) * B;
                }
            }
            // the span chains: segment s rides segment s - 1, bent about its +X (+ = dorsal)
            for (int k = 0; k < fingers; k++)
            {
                Matrix4x4 m = dst[o + sp.WingBone(w, k)];
                float seg = lengths[k] / segs;
                for (int sg = 1; sg < segs; sg++)
                {
                    float bend = chains ? bends[k * segs + sg] : 0f;
                    m = m * Matrix4x4.TRS(new Vector3(0f, 0f, seg), Quaternion.AngleAxis(-bend, Vector3.right), Vector3.one);
                    dst[o + extra + k * (segs - 1) + sg - 1] = m;
                }
            }
            // the fan sector: on the trailing rib (a fixed length along it) and the rear flank rib to its flank end
            {
                CreatureWings wg = sim.Flight.Wings;
                Matrix4x4 T = dst[trail], R = dst[o + sp.WingBone(w, WingBoneLayout.FirstRibOf(sp.wings[w]) + 1)];
                Vector3 wrist = T.GetColumn(3);
                Vector3 c0 = T.MultiplyVector(new Vector3(0f, 0f, wg.RibRestLength(w, 0)));
                Vector3 c1 = R.MultiplyVector(new Vector3(0f, 0f, wg.RibRestLength(w, 1)));
                Vector3 c2 = PartMeshEmitter.Unit(Vector3.Cross(c0, c1), T.GetColumn(1)) * s;
                var m = Matrix4x4.identity;
                m.m00 = c0.x; m.m01 = c1.x; m.m02 = c2.x; m.m03 = wrist.x;
                m.m10 = c0.y; m.m11 = c1.y; m.m12 = c2.y; m.m13 = wrist.y;
                m.m20 = c0.z; m.m21 = c1.z; m.m22 = c2.z; m.m23 = wrist.z;
                dst[o + p.FanSectorBone(extra)] = m;
            }
        }

        public void EncapsulateBounds(ref Vector3 lo, ref Vector3 hi) { }   // within the wing's reach (the renderer's)

        public void DrawDebug(ICreatureDebugDraw d) { }
    }
}
