// Creature kit T2.2: the part types for everything that exists today (design §2.4). Data only: CompiledSpecies
// turns them into the runtime fields the systems read (LegSpec, SpineJoint, WingSpec, ...); Phase 3 moves the
// systems onto the parts. Every field carries [Param] (unit, size rule, meaning) - the catalogue is generated
// from them. Authoring sugar: `mirror` on any part, Repeat for rows of parts, SpineBody.profile for the body.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CreatureKit
{
    // ------------------------------------------------------------------ bodies

    // One spine joint, written out (the legacy SpineJoint, documented).
    [Serializable]
    public struct JointDef
    {
        [Param(ParamUnit.Metres, "cross-section half width")] public float halfWidth;
        [Param(ParamUnit.Metres, "cross-section half height")] public float halfHeight;
        [Param(ParamUnit.Metres, "joint centre above the ground; >= halfHeight for a walker")] public float clearance;
        [Param(ParamUnit.Degrees, "cone half-angle vs the previous segment", Example = "monitor 20, snake 24-30")] public float bendLimitDeg;
        [Param(ParamUnit.Flag, "bend limit 0 (neck stiffness)")] public bool rigid;
    }

    // A profile key: the body's cross-section at a place along it, interpolated (monotone cubic) to every joint.
    [Serializable]
    public struct ProfileKey
    {
        [Param(ParamUnit.Fraction, "place along the spine, 0 = joint 0 (head) .. 1 = last joint", Min = 0, Max = 1)] public float t;
        [Param(ParamUnit.Metres, "cross-section half width")] public float halfWidth;
        [Param(ParamUnit.Metres, "cross-section half height")] public float halfHeight;
        [Param(ParamUnit.Metres, "joint centre above the ground")] public float clearance;
        [Param(ParamUnit.Degrees, "cone half-angle vs the previous segment")] public float bendLimitDeg;
    }

    [Serializable]
    [Part("SpineBody", PartKind.Body, Bones = "= joints", Summary = "Bend-limited follow-the-leader chain riding the ground or a climbed surface (CreatureSim). The body of every walker, snake and the wyvern.",
          System = "CreatureSim chain + grounding", MaterialLayers = new[] { 0 }, UseWhen = "anything with a backbone or a segmented trunk")]
    public sealed class SpineBody : CreaturePart
    {
        [Param(ParamUnit.Metres, "segment length (joint spacing)", Example = "monitor 0.06, snake 0.03")] public float spacing = 0.06f;
        [Param(ParamUnit.Metres, "snout cap length in front of joint 0")] public float headCapLength = 0.05f;
        [Param(ParamUnit.Metres, "pointed tail tip behind the last joint")] public float tailTipLength = 0.04f;
        [Param(ParamUnit.Struct, "the joints written out, head first; when empty they come from profile + jointCount")] public JointDef[] joints = new JointDef[0];
        [Param(ParamUnit.Struct, "profile keys (sugar): interpolated to jointCount joints; >= 2 keys, t = 0 and t = 1 included")] public ProfileKey[] profile = new ProfileKey[0];
        [Param(ParamUnit.Count, "joints made from the profile (ignored when joints are written out)", Min = 2, Max = 128)] public int jointCount;
        [Param(ParamUnit.Count, "profile: joints 1..rigidHead are rigid (neck stiffness)", Min = 0, Max = 8)] public int rigidHead;
        [Param(ParamUnit.Count, "mesh: vertices around each ring", Default = "16")] public int ringSegments = 16;
        [Param(ParamUnit.Count, "mesh: rings per spine segment", Default = "3")] public int ringsPerSegment = 3;

        public int JointCount => joints != null && joints.Length > 0 ? joints.Length : jointCount;
        public float BodyLength => headCapLength + spacing * (JointCount - 1) + tailTipLength;
        public override int BoneCount(in SpeciesCtx c) => JointCount;
        protected override void CopyArrays() { joints = (JointDef[])joints?.Clone(); profile = (ProfileKey[])profile?.Clone(); }

        public override string Validate(in SpeciesCtx c)
        {
            if (JointCount < 2) return $"{id}: needs at least 2 joints (joints, or profile + jointCount)";
            if (!(spacing > 0f)) return $"{id}: spacing must be > 0";
            if ((joints == null || joints.Length == 0))
            {
                if (profile == null || profile.Length < 2) return $"{id}: profile needs >= 2 keys";
                for (int i = 1; i < profile.Length; i++) if (!(profile[i].t > profile[i - 1].t)) return $"{id}: profile keys must be in increasing t";
                if (profile[0].t != 0f || profile[profile.Length - 1].t != 1f) return $"{id}: profile must start at t = 0 and end at t = 1";
            }
            return null;
        }

        // The joints the runtime gets: written out, or the profile interpolated.
        public SpineJoint[] BuildJoints()
        {
            if (joints != null && joints.Length > 0)
            {
                var a = new SpineJoint[joints.Length];
                for (int i = 0; i < a.Length; i++)
                    a[i] = new SpineJoint { halfWidth = joints[i].halfWidth, halfHeight = joints[i].halfHeight, clearance = joints[i].clearance, bendLimitDeg = joints[i].bendLimitDeg, rigid = joints[i].rigid };
                return a;
            }
            int n = Mathf.Max(jointCount, 2);
            var r = new SpineJoint[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (n - 1f);
                r[i] = new SpineJoint
                {
                    halfWidth = Profile(t, 0), halfHeight = Profile(t, 1), clearance = Profile(t, 2), bendLimitDeg = Profile(t, 3),
                    rigid = i >= 1 && i <= rigidHead,
                };
            }
            return r;
        }

        static float Channel(in ProfileKey k, int ch) => ch == 0 ? k.halfWidth : ch == 1 ? k.halfHeight : ch == 2 ? k.clearance : k.bendLimitDeg;

        // Monotone cubic Hermite (Fritsch-Carlson) through the keys: smooth, no overshoot between keys (a body
        // never bulges past its widest key), exact at every key.
        public float Profile(float t, int ch)
        {
            ProfileKey[] k = profile;
            int n = k.Length;
            if (t <= k[0].t) return Channel(k[0], ch);
            if (t >= k[n - 1].t) return Channel(k[n - 1], ch);
            int i = 0;
            while (i < n - 2 && t >= k[i + 1].t) i++;
            if (t == k[i].t) return Channel(k[i], ch);
            float h = k[i + 1].t - k[i].t, y0 = Channel(k[i], ch), y1 = Channel(k[i + 1], ch);
            float m0 = Tangent(i, ch), m1 = Tangent(i + 1, ch);
            float s = (t - k[i].t) / h, s2 = s * s, s3 = s2 * s;
            return (2f * s3 - 3f * s2 + 1f) * y0 + (s3 - 2f * s2 + s) * h * m0 + (-2f * s3 + 3f * s2) * y1 + (s3 - s2) * h * m1;
        }

        float Tangent(int i, int ch)
        {
            ProfileKey[] k = profile;
            int n = k.Length;
            float dPrev = i > 0 ? (Channel(k[i], ch) - Channel(k[i - 1], ch)) / (k[i].t - k[i - 1].t) : 0f;
            float dNext = i < n - 1 ? (Channel(k[i + 1], ch) - Channel(k[i], ch)) / (k[i + 1].t - k[i].t) : 0f;
            if (i == 0) return dNext;
            if (i == n - 1) return dPrev;
            if (dPrev * dNext <= 0f) return 0f;                     // a local extremum: flat, no overshoot
            float m = 0.5f * (dPrev + dNext);
            // Fritsch-Carlson limit: |m| <= 3 min(|dPrev|, |dNext|)
            float lim = 3f * Mathf.Min(Mathf.Abs(dPrev), Mathf.Abs(dNext));
            return Mathf.Clamp(m, -lim, lim);
        }
    }

    [Serializable]
    [Part("SoftBody", PartKind.Body, Bones = "6", Summary = "Shape-matched soft lattice (OctopusBody/SoftLattice: 14 particles, 5 clusters) driven by its Tentacle chains: mantle, head, web.",
          System = "OctopusBody (self-ticking ICreatureBody)", MaterialLayers = new[] { 5, 6, 7 }, UseWhen = "boneless bodies: octopus, slug, jelly")]
    public sealed class SoftBody : CreaturePart
    {
        public const int SoftBones = 6;
        [Param(ParamUnit.Spec, "the octopus body (CephalopodSpec: web, head, mantle, siphon, lattice); its `arm` comes from the Tentacle parts")]
        public CephalopodSpec spec;
        public override int BoneCount(in SpeciesCtx c) => SoftBones;
    }

    // ------------------------------------------------------------------ limbs

    // Walk: steps in its gait group. Feeler: posed at its rest point, never plants (a pedipalp). WingArm: walks on
    // the ground, a Wing drives it in the air (set by compose for the arm a Wing names). Folded: held folded at
    // rest (a wing arm on a six-limbed flyer; needs the T5.4 flag).
    public enum LimbRole { Walk = 0, Feeler = 1, WingArm = 2, Folded = 3 }

    [Serializable]
    public abstract class LimbPart : CreaturePart
    {
        [Param(ParamUnit.Enum, "Walk steps in its gait group; Feeler is posed, never plants; WingArm is set by compose for a Wing's arm; Folded is planned (T5.4)")]
        public LimbRole role;
        [Param(ParamUnit.Count, "gait group (one group steps at a time); -1 = from the Walk module's pattern", Min = -1, Max = 31)]
        public int gaitGroup = -1;
        [Param(ParamUnit.Metres, "foot rest point in the attach frame (x mirrored by side; y ignored: projected to the ground)")]
        public Vector3 restOffset;
        [Param(ParamUnit.Metres, "foot travel before it steps (comfort radius)", Example = "monitor 0.11")] public float stepThreshold;
        [Param(ParamUnit.Seconds, "step duration", Example = "monitor 0.12")] public float stepDuration;
        [Param(ParamUnit.Metres, "step apex height")] public float stepLift;
        [Param(ParamUnit.Metres, "the step lands this far ahead of the rest point")] public float overshoot;
        [Param(ParamUnit.Metres, "foot (tarsus claw) length")] public float footLength;
        [Param(ParamUnit.Count, "toes (arthropod: tarsus claws)", Min = 0, Max = 6)] public int toeCount;
        [Param(ParamUnit.Degrees, "upper bone (arthropod: coxa yaw) swing limit toward the head, 0 = 55")] public float forwardSwingLimitDeg;
        [Param(ParamUnit.Degrees, "swing limit toward the tail, 0 = 55")] public float backSwingLimitDeg;
        [Param(ParamUnit.Degrees, "knee range around its rest bend, 0 = 35 (arthropod: reach synergy extension)")] public float kneeRangeDeg;
        [Param(ParamUnit.Degrees, "knee flexion past rest, 0 = kneeRange (arthropod: reach synergy flexion)")] public float kneeFlexDeg;

        public bool Walks => role == LimbRole.Walk || role == LimbRole.WingArm;

        // the runtime leg: the fields every limb shares
        protected LegSpec BaseSpec() => new LegSpec
        {
            attachJoint = at.joint, side = at.side, shoulderOffset = at.offset, restOffset = restOffset,
            stepThreshold = stepThreshold, stepDuration = stepDuration, stepLift = stepLift, overshoot = overshoot,
            gaitGroup = gaitGroup < 0 ? 0 : gaitGroup, footLength = footLength, toeCount = toeCount,
            forwardSwingLimitDeg = forwardSwingLimitDeg, backSwingLimitDeg = backSwingLimitDeg, kneeRangeDeg = kneeRangeDeg, kneeFlexDeg = kneeFlexDeg,
            feeler = role == LimbRole.Feeler,
        };

        protected void CopyBase(LegSpec l)
        {
            at = new PartSocket(l.attachJoint, l.side, l.shoulderOffset);
            restOffset = l.restOffset; stepThreshold = l.stepThreshold; stepDuration = l.stepDuration; stepLift = l.stepLift; overshoot = l.overshoot;
            gaitGroup = l.gaitGroup; footLength = l.footLength; toeCount = l.toeCount;
            forwardSwingLimitDeg = l.forwardSwingLimitDeg; backSwingLimitDeg = l.backSwingLimitDeg; kneeRangeDeg = l.kneeRangeDeg; kneeFlexDeg = l.kneeFlexDeg;
            role = l.feeler ? LimbRole.Feeler : LimbRole.Walk;
        }

        public abstract LegSpec ToLegSpec();

        public override string Validate(in SpeciesCtx c)
        {
            if (!c.Soft && (at.joint < 0 || at.joint >= c.Joints)) return $"{id}: attach joint {at.joint} outside 0..{c.Joints - 1}";
            if (at.side != -1 && at.side != 1) return $"{id}: side must be -1 or +1 (or mirror)";
            if (role == LimbRole.Folded) return $"{id}: role Folded needs the folded-wing flag (T5.4, not available yet)";
            return null;
        }
    }

    [Serializable]
    [Part("Leg2Bone", PartKind.Limb, Bones = "3", Summary = "Vertebrate two-bone limb: TwoBoneIK with a pole, rounded knee blend, swing/knee limits (R16), keep-out (R28). Also the wyvern's wing arm.",
          System = "CreatureLegs (gait groups, throttle, sway)", MaterialLayers = new[] { 1, 2 }, UseWhen = "lizards, mammals, birds' legs, wing arms")]
    public sealed class Leg2Bone : LimbPart
    {
        public const int Bones = 3; // upper, lower, foot
        [Param(ParamUnit.Metres, "upper bone length", Min = 0.005f, Max = 2f, Example = "monitor 0.145")] public float upperLength;
        [Param(ParamUnit.Metres, "lower bone length", Min = 0.005f, Max = 2f, Example = "monitor 0.135")] public float lowerLength;
        [Param(ParamUnit.Metres, "upper bone radius")] public float upperRadius;
        [Param(ParamUnit.Metres, "lower bone radius")] public float lowerRadius;
        [Param(ParamUnit.Direction, "knee direction hint in the attach frame (x mirrored); needs a clear fore/aft part (front z < 0, rear z > 0) or the skin folds")] public Vector3 pole;
        [Param(ParamUnit.Fraction, "share of each bone blended into the rounded knee, 0 = 0.45", Min = 0f, Max = 0.49f)] public float kneeBlend;

        public override int BoneCount(in SpeciesCtx c) => Bones;

        public override LegSpec ToLegSpec()
        {
            LegSpec l = BaseSpec();
            l.upperLength = upperLength; l.lowerLength = lowerLength; l.upperRadius = upperRadius; l.lowerRadius = lowerRadius;
            l.pole = pole; l.kneeBlend = kneeBlend;
            return l;
        }

        public static Leg2Bone From(LegSpec l, string id)
        {
            var p = new Leg2Bone { id = id };
            p.CopyBase(l);
            p.upperLength = l.upperLength; p.lowerLength = l.lowerLength; p.upperRadius = l.upperRadius; p.lowerRadius = l.lowerRadius;
            p.pole = l.pole; p.kneeBlend = l.kneeBlend;
            return p;
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (!(upperLength > 0f && lowerLength > 0f)) return $"{id}: limb lengths must be > 0";
            return base.Validate(c);
        }
    }

    // One arthropod leg segment (the legacy LegSegment, documented).
    [Serializable]
    public struct SegmentDef
    {
        [Param(ParamUnit.Metres, "segment length")] public float length;
        [Param(ParamUnit.Metres, "tube radius at the start")] public float radiusStart;
        [Param(ParamUnit.Metres, "tube radius at the end")] public float radiusEnd;
        [Param(ParamUnit.Degrees, "rest bend at the start joint (+ raises; segment 0: elevation)")] public float restBendDeg;
        [Param(ParamUnit.Degrees, "hinge lower limit")] public float minBendDeg;
        [Param(ParamUnit.Degrees, "hinge upper limit")] public float maxBendDeg;
        [Param(ParamUnit.Fraction, "share of the reach synergy; the knee is +-1", Min = -1, Max = 1)] public float flexWeight;
        [Param(ParamUnit.Fraction, "share of the swing's knee-lift synergy", Min = -1, Max = 1)] public float liftWeight;
        [Param(ParamUnit.Degrees, "hinge axis rotated about the segment (0 = planar)")] public float axisTiltDeg;
        [Param(ParamUnit.Fraction, "joint bulb at the end: radius = knuckle x the larger adjacent radius (0 = none)")] public float knuckle;
        [Param(ParamUnit.Fraction, "start of the band-coloured stretch (fraction of the segment)", Min = 0, Max = 1)] public float bandStart;
        [Param(ParamUnit.Fraction, "end of the band-coloured stretch", Min = 0, Max = 1)] public float bandEnd;

        public LegSegment ToRuntime() => new LegSegment
        {
            length = length, radiusStart = radiusStart, radiusEnd = radiusEnd, restBendDeg = restBendDeg, minBendDeg = minBendDeg, maxBendDeg = maxBendDeg,
            flexWeight = flexWeight, liftWeight = liftWeight, axisTiltDeg = axisTiltDeg, knuckle = knuckle, bandStart = bandStart, bandEnd = bandEnd,
        };

        public static SegmentDef From(in LegSegment s) => new SegmentDef
        {
            length = s.length, radiusStart = s.radiusStart, radiusEnd = s.radiusEnd, restBendDeg = s.restBendDeg, minBendDeg = s.minBendDeg, maxBendDeg = s.maxBendDeg,
            flexWeight = s.flexWeight, liftWeight = s.liftWeight, axisTiltDeg = s.axisTiltDeg, knuckle = s.knuckle, bandStart = s.bandStart, bandEnd = s.bandEnd,
        };
    }

    [Serializable]
    [Part("ArthropodLeg", PartKind.Limb, Bones = "= segments", Summary = "3-7 rigid segments solved by synergy IK (ArthropodLegIK): high knees, tarsus at a contact angle, knee bands. Feeler role = a pedipalp.",
          System = "CreatureLegs + ArthropodLegModel", MaterialLayers = new[] { 1, 2, 9 }, UseWhen = "insects, spiders, crabs, centipedes; anything with visible segments")]
    public sealed class ArthropodLeg : LimbPart
    {
        [Param(ParamUnit.Struct, "segments body outward (coxa .. tarsus), one bone each")] public SegmentDef[] segments = new SegmentDef[0];
        [Param(ParamUnit.Degrees, "tarsus angle to the ground, 0 = 20")] public float contactAngleDeg;
        [Param(ParamUnit.Degrees, "lift synergy at mid-swing (reshapes the leg; the foot stays on its path)")] public float kneeLiftDeg;
        [Param(ParamUnit.Fraction, "mid-swing pull of the foot toward the coxa (flexing lifts the knee)", Min = 0, Max = 0.5f)] public float swingRetract;
        [Param(ParamUnit.Degrees, "in-plane pitch lower bound (0, 0 = +-90); outside it the planted foot steps")] public float elevMinDeg;
        [Param(ParamUnit.Degrees, "in-plane pitch upper bound")] public float elevMaxDeg;
        [Param(ParamUnit.Fraction, "lift-off delay per place behind the group's rearmost leg (metachronal wave)", Min = 0, Max = 0.5f)] public float liftDelay;
        [Param(ParamUnit.Count, "joint reported as the knee (debug, tests); 0 = the largest flexWeight")] public int kneeJoint;
        [Param(ParamUnit.Count, "mesh: sides of the segment tubes, 0 = 8")] public int legSides;

        public override int BoneCount(in SpeciesCtx c) => segments != null ? segments.Length : 0;
        protected override void CopyArrays() { segments = (SegmentDef[])segments?.Clone(); }

        public override LegSpec ToLegSpec()
        {
            LegSpec l = BaseSpec();
            l.segments = new LegSegment[segments.Length];
            for (int i = 0; i < segments.Length; i++) l.segments[i] = segments[i].ToRuntime();
            l.contactAngleDeg = contactAngleDeg; l.kneeLiftDeg = kneeLiftDeg; l.swingRetract = swingRetract;
            l.elevMinDeg = elevMinDeg; l.elevMaxDeg = elevMaxDeg; l.liftDelay = liftDelay; l.kneeJoint = kneeJoint; l.legSides = legSides;
            return l;
        }

        public static ArthropodLeg From(LegSpec l, string id)
        {
            var p = new ArthropodLeg { id = id };
            p.CopyBase(l);
            p.segments = new SegmentDef[l.segments.Length];
            for (int i = 0; i < p.segments.Length; i++) p.segments[i] = SegmentDef.From(l.segments[i]);
            p.contactAngleDeg = l.contactAngleDeg; p.kneeLiftDeg = l.kneeLiftDeg; p.swingRetract = l.swingRetract;
            p.elevMinDeg = l.elevMinDeg; p.elevMaxDeg = l.elevMaxDeg; p.liftDelay = l.liftDelay; p.kneeJoint = l.kneeJoint; p.legSides = l.legSides;
            return p;
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (segments == null || segments.Length < 3 || segments.Length > 7) return $"{id}: an ArthropodLeg needs 3..7 segments";
            bool flex = false;
            for (int i = 0; i < segments.Length; i++) flex |= segments[i].flexWeight != 0f;
            if (!flex) return $"{id}: some segment needs flexWeight != 0 (the reach synergy)";
            if (swingRetract < 0f || swingRetract > 0.5f) return $"{id}: swingRetract must be in 0..0.5";
            if (liftDelay < 0f || liftDelay > 0.5f) return $"{id}: liftDelay must be in 0..0.5";
            return base.Validate(c);
        }
    }

    // ------------------------------------------------------------------ wings, chains

    [Serializable]
    [Part("Wing", PartKind.Wing, Bones = "7", Summary = "3-finger membrane wing on an arm limb (a Leg2Bone it names); a wingbeat is a step with a vertical comfort band (CreatureWings, FlightModel).",
          System = "CreatureFlight + CreatureWings", MaterialLayers = new[] { 8, 1, 2 }, UseWhen = "bats, pterosaurs, dragons; the Fly module makes it fly")]
    public sealed class Wing : CreaturePart
    {
        public const int Bones = WingBoneLayout.BonesPerWing; // 3 fingers, 2 ribs, arm sector, humerus patch
        [Param(ParamUnit.PartRef, "the Leg2Bone this wing rides on (its role becomes WingArm); a mirrored wing names the unmirrored id and gets .L / .R")]
        public string arm = "";
        [Param(ParamUnit.Spec, "the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm`")]
        public WingSpec spec = new WingSpec();

        public override int BoneCount(in SpeciesCtx c) => Bones;
        public override void CollectReferences(List<string> into) => into.Add(arm);
        protected override void CopyArrays()
        {
            if (spec == null) return;
            WingSpec s = KitTypes.Memberwise(spec);
            s.fingerLengths = (float[])spec.fingerLengths?.Clone(); s.fingerSpreadDeg = (float[])spec.fingerSpreadDeg?.Clone(); s.fingerFoldDeg = (float[])spec.fingerFoldDeg?.Clone();
            spec = s;
        }

        public override string Validate(in SpeciesCtx c)
        {
            if (spec == null) return $"{id}: no spec";
            if (!(c.Find(arm) is Leg2Bone)) return $"{id}: arm '{arm}' must name a Leg2Bone";
            return null;
        }
    }

    [Serializable]
    [Part("Tentacle", PartKind.Chain, Bones = "= spec.segments (0 = 24)", Summary = "Verlet + XPBD-lite particle chain (TentacleChain) that wraps real bark (WrapMarch); driven by TentacleAgent (reach, grip, hold). Octopus arms today; socket-rooted on a spine body is T5.9.",
          System = "TentacleChain + TentacleAgent / ArmCoordinator", MaterialLayers = new[] { 4 }, UseWhen = "octopus arms; grabbing arms on any body (after T5.9)")]
    public sealed class Tentacle : CreaturePart
    {
        [Param(ParamUnit.Spec, "the arm (TentacleSpec: segments, length, radii, stretch, twist, muscle, suckers)")]
        public TentacleSpec spec;
        public override int BoneCount(in SpeciesCtx c) => spec.Segments;
        public override string Validate(in SpeciesCtx c) => c.Soft ? null : $"{id}: a Tentacle on a spine body needs the socket-rooted chain (T5.9, not available yet)";
    }

    // ------------------------------------------------------------------ rigid cones, eyes

    [Serializable]
    [Part("RigidCone", PartKind.Rigid, Bones = "0 (1 with ownBone)", Summary = "Curved cone rigid to a spine joint: horn, spike, claw, chelicera with a fang (BodyAppendage). mirror emits the pair from this one part (no .L/.R: it has no bones). ownBone: the octopus siphon.",
          System = "mesh only (CreatureMeshBuilder)", MaterialLayers = new[] { 2 }, UseWhen = "horns, spikes, fangs, mandibles, chelicerae")]
    public sealed class RigidCone : CreaturePart
    {
        [Param(ParamUnit.Direction, "direction in the joint frame (x mirrored)")] public Vector3 direction = new Vector3(0f, 1f, 0f);
        [Param(ParamUnit.Metres, "cone length")] public float length;
        [Param(ParamUnit.Metres, "base radius")] public float radiusStart;
        [Param(ParamUnit.Metres, "tip radius")] public float radiusEnd;
        [Param(ParamUnit.Degrees, "curl downward along the cone")] public float curveDeg;
        [Param(ParamUnit.Metres, "a fang continuing from the tip (0 = none)")] public float fangLength;
        [Param(ParamUnit.Flag, "a bone of its own, posed by its system (the octopus siphon), instead of rigid to the joint")] public bool ownBone;

        public override int BoneCount(in SpeciesCtx c) => ownBone ? 1 : 0;

        public BodyAppendage ToRuntime() => new BodyAppendage
        {
            joint = at.joint, offset = at.offset, direction = direction, length = length, radiusStart = radiusStart, radiusEnd = radiusEnd,
            curveDeg = curveDeg, fangLength = fangLength, mirror = mirror,
        };

        public static RigidCone From(in BodyAppendage a, string id) => new RigidCone
        {
            id = id, at = new PartSocket(a.joint, 0, a.offset), mirror = a.mirror, direction = a.direction, length = a.length,
            radiusStart = a.radiusStart, radiusEnd = a.radiusEnd, curveDeg = a.curveDeg, fangLength = a.fangLength,
        };

        public override string Validate(in SpeciesCtx c)
            => !c.Soft && (at.joint < 0 || at.joint >= c.Joints) ? $"{id}: joint {at.joint} outside 0..{c.Joints - 1}" : null;
    }

    [Serializable]
    public struct EyeSpotDef
    {
        [Param(ParamUnit.Metres, "offset from the eye centre in the eye joint's frame (x mirrored; x = 0: one eye)")] public Vector3 offset;
        [Param(ParamUnit.Metres, "radius")] public float radius;
    }

    [Serializable]
    [Part("Eyes", PartKind.Sense, Bones = "2", Summary = "Two glossy eye spheres on their own bones (sp.EyeBone), placed at the socket (joint, offset x mirrored); cluster eyes ride them (spiders).",
          System = "CreatureSim.WriteBoneWorld", MaterialLayers = new[] { 3 }, UseWhen = "always (every shipped species has two eye bones)")]
    public sealed class Eyes : CreaturePart
    {
        [Param(ParamUnit.Metres, "eye radius (0 = no visible eyes; the bones stay)")] public float radius;
        [Param(ParamUnit.Struct, "extra eyes rigid to the eye bones")] public EyeSpotDef[] cluster = new EyeSpotDef[0];

        public override int BoneCount(in SpeciesCtx c) => 2;
        protected override void CopyArrays() { cluster = (EyeSpotDef[])cluster?.Clone(); }
        public override string Validate(in SpeciesCtx c)
            => !c.Soft && (at.joint < 0 || at.joint >= c.Joints) ? $"{id}: eye joint {at.joint} outside 0..{c.Joints - 1}" : null;
    }

    // ------------------------------------------------------------------ authoring sugar

    // Repeat: `count` copies of `template` spread over the attach joints fromJoint..toJoint (rounded), lengths
    // tapered linearly to lastScale at the last copy; ids <id>0, <id>1, ...; a mirrored template mirrors each copy.
    // Expanded by compose: never reaches the runtime.
    [Serializable]
    [Part("Repeat", PartKind.Sugar, Bones = "= count x template", Summary = "Authoring sugar: `count` copies of `template` across joints fromJoint..toJoint (tapered by lastScale, mirrored if the template is). Expanded by compose.",
          System = "compose (CompiledSpecies)", UseWhen = "centipede legs, spike rows, many fins")]
    public sealed class Repeat : CreaturePart
    {
        [Param(ParamUnit.Count, "copies", Min = 1, Max = 64)] public int count = 2;
        [Param(ParamUnit.Joint, "attach joint of the first copy")] public int fromJoint;
        [Param(ParamUnit.Joint, "attach joint of the last copy")] public int toJoint;
        [Param(ParamUnit.Fraction, "length factor at the last copy (1 = no taper)", Default = "1")] public float lastScale = 1f;
        [Param(ParamUnit.Struct, "the part to copy (any part type; its at.joint is replaced)")]
        [SerializeReference] public CreaturePart template;

        protected override void CopyArrays() { template = template?.Clone(); }
    }
}
