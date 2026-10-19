using System;
using System.Collections.Generic;
using UnityEngine;

public enum CreaturePattern { None = 0, Bands = 1, Spots = 2, Stripes = 3, BandedSpots = 4 }

// Spine: a CreatureSim (spine, legs). Cephalopod: an OctopusCreature built from `cephalopod`; the spine
// fields are then unused.
public enum CreatureBodyPlan { Spine = 0, Cephalopod = 1 }

[Serializable]
public struct SpineJoint
{
    public float halfWidth, halfHeight;   // ellipse semi-axes, metres
    public float clearance;               // joint centre height above ground, metres
    public float bendLimitDeg;            // cone half-angle vs previous segment
    public bool rigid;                    // bend limit 0
}

[Serializable]
public class LegSpec
{
    public int attachJoint;
    public int side;                      // -1 left, +1 right
    public Vector3 shoulderOffset;        // attach-joint local frame; x is mirrored by side
    public Vector3 restOffset;            // foot rest point, attach-joint local frame, x mirrored by side (y ignored: projected to ground)
    public float upperLength, lowerLength, upperRadius, lowerRadius;
    public Vector3 pole;                  // attach-joint local, x mirrored by side
    public float stepThreshold, stepDuration, stepLift, overshoot;
    public int gaitGroup;
    public float footLength;
    public int toeCount;
    // Joint-range limits, degrees (0 = default): how far the upper bone may swing from its rest direction
    // toward the head (forward) and toward the tail (back), and how far the knee bend may deviate from its
    // rest bend (kneeRangeDeg; kneeFlexDeg, if set, replaces it for bending further than rest: a limb with
    // a strongly bent rest knee has little flexion left before the skinned knee folds). Past a limit the
    // foot steps; a little further the body slows and stops.
    public float forwardSwingLimitDeg, backSwingLimitDeg, kneeRangeDeg, kneeFlexDeg;
    // Mesh: 0 = a ball knee (two rigid tubes meeting in one sphere: never creases at any bend, the default since
    // 2026-10-19); > 0 = the older skinned knee curve, blending this fraction of each limb bone.
    public float kneeBlend;

    // Two-bone limb: height of the ankle (the lower bone's end) above the foot's contact point, metres at
    // scale 1; the foot (pad, toes) is built from the ankle down to the ground. 0 = auto (DefaultAnkleHeight x
    // lowerRadius), < 0 = none (the lower bone ends on the ground, the pose before 2026-10-19).
    [Tooltip("Two-bone limb: ankle height above the foot's contact point, metres (0 = auto 1.2 x lowerRadius, < 0 = none).")]
    public float ankleHeight;
    [Tooltip("Two-bone limb: claw length, metres (0 = auto 0.16 x footLength).")]
    public float clawLength;
    [Tooltip("Two-bone limb: how far a claw curls down over its length, degrees (0 = 75).")]
    public float clawCurveDeg;
    [Tooltip("Two-bone limb: webbing between the toes, fraction of the toe length (0 = none, 1 = to the tips).")]
    [Range(0f, 1f)] public float toeWeb;
    // Two-bone limb mesh: a digitigrade dragon / raptor hind leg (Leg2Bone.Digitigrade) - a heavy thigh with muscle
    // masses, a lean shin, a knobbly raised hock (ankleHeight: the metatarsus' height), a long metatarsus down to
    // the toes, three forward toes (a fourth as a hallux behind) with knuckles and big curved talons. Off = the
    // vertebrate limb above.
    [Tooltip("Two-bone limb: digitigrade dragon / raptor leg mesh (heavy thigh, lean shin, raised hock, long metatarsus, talons).")]
    public bool digitigrade;
    [Tooltip("Digitigrade: the thigh's muscle masses, x upperRadius (0 = 0.5).")]
    public float thighMass;
    [Tooltip("Digitigrade: overlapping scutes on the front of the shin and metatarsus and on the toes.")]
    public bool scutes;
    [Tooltip("Digitigrade: a spur on the back of the hock, metres (0 = none).")]
    public float spurLength;

    public const float DefaultSwingLimitDeg = 55f, DefaultKneeRangeDeg = 35f, DefaultKneeBlend = 0.45f, DefaultAnkleHeight = 1.2f;
    // the ankle height in use, metres at scale 1 (0 for an arthropod leg or a feeler)
    public float AnkleHeight => IsArthropod || feeler ? 0f : ankleHeight > 0f ? ankleHeight : ankleHeight < 0f ? 0f : DefaultAnkleHeight * lowerRadius;
    public float ForwardSwingLimit => forwardSwingLimitDeg > 0f ? forwardSwingLimitDeg : DefaultSwingLimitDeg;
    public float BackSwingLimit => backSwingLimitDeg > 0f ? backSwingLimitDeg : DefaultSwingLimitDeg;
    public float KneeRange => kneeRangeDeg > 0f ? kneeRangeDeg : DefaultKneeRangeDeg;
    public float KneeFlex => kneeFlexDeg > 0f ? kneeFlexDeg : KneeRange;
    public float KneeBlend => kneeBlend > 0f ? Mathf.Min(kneeBlend, 0.49f) : DefaultKneeBlend;

    // ---- arthropod limb (design docs/superpowers/specs/2026-10-17-multi-segment-legs-spider-design.md) ----
    // Non-empty segments replace the two-bone limb: N segments from the body outward (coxa .. tarsus), one
    // bone each, solved by ArthropodLegIK. upperLength/lowerLength/radii/pole/kneeBlend are then unused;
    // forward/backSwingLimitDeg limit the coxa's yaw, kneeRangeDeg/kneeFlexDeg the reach synergy s
    // (extension/flexion, 0 = no extra limit), toeCount/footLength are the tarsus claws.
    [Tooltip("Arthropod leg segments, body outward (coxa .. tarsus). Empty: the two-bone limb above.")]
    public LegSegment[] segments;
    [Tooltip("Arthropod: tarsus angle to the ground, degrees (0 = 20).")]
    public float contactAngleDeg;
    [Tooltip("Arthropod: the lift synergy at mid-swing, degrees (reshapes the leg; the foot stays on its path).")]
    public float kneeLiftDeg;
    [Tooltip("Arthropod: mid-swing pull of the foot toward the coxa, fraction of its distance - flexing lifts the knee.")]
    public float swingRetract;
    [Tooltip("Arthropod: in-plane pitch range, degrees (0, 0 = +-90); outside it the planted foot steps.")]
    public float elevMinDeg, elevMaxDeg;
    [Tooltip("Arthropod: lift-off delay per place behind the group's rearmost leg, fraction of a step (metachronal wave).")]
    public float liftDelay;
    [Tooltip("Arthropod: joint reported as the knee (debug, tests); 0 = the joint with the largest flexWeight.")]
    public int kneeJoint;
    [Tooltip("Arthropod mesh: sides of the segment tubes (0 = 8).")]
    public int legSides;
    [Tooltip("A feeler (pedipalp): posed in the attach frame at restOffset, never plants or steps.")]
    public bool feeler;

    public bool IsArthropod => segments != null && segments.Length > 0;
    // bones of this leg: one per segment, or upper, lower, foot
    public int SegmentCount => IsArthropod ? segments.Length : 3;
    public float TotalLength
    {
        get
        {
            if (!IsArthropod) return upperLength + lowerLength;
            float t = 0f;
            for (int i = 0; i < segments.Length; i++) t += segments[i].length;
            return t;
        }
    }
    public ArthropodLegParams ModelParams => new ArthropodLegParams
    {
        contactAngleDeg = contactAngleDeg, kneeLiftDeg = kneeLiftDeg,
        sMinDeg = kneeRangeDeg > 0f ? -kneeRangeDeg : float.NegativeInfinity,
        sMaxDeg = kneeFlexDeg > 0f ? kneeFlexDeg : float.PositiveInfinity,
        elevMinDeg = elevMinDeg, elevMaxDeg = elevMaxDeg, kneeJoint = kneeJoint,
    };
}

// Bone order of the legs: each leg's SegmentCount bones in leg order (3 for a two-bone leg: upper, lower,
// foot), after the spine. With two-bone legs only this is exactly the original 3-per-leg layout.
// WingBoneLayout (feat/creature-wyvern) must use LegBones/LegStart here instead of 3 * legs.
public static class LegBoneLayout
{
    public static int LegBones(LegSpec[] legs)
    {
        int c = 0;
        if (legs != null) for (int i = 0; i < legs.Length; i++) c += legs[i] != null ? legs[i].SegmentCount : 3;
        return c;
    }

    public static int LegStart(LegSpec[] legs, int leg)
    {
        int c = 0;
        for (int i = 0; i < leg; i++) c += legs[i] != null ? legs[i].SegmentCount : 3;
        return c;
    }
}

// Vertical body bounce driven by the gait: each gait group's lift-off kicks a damped spring on the body's
// height (0 amplitude = off).
[Serializable]
public struct GaitBob
{
    [Tooltip("Peak dip of the body per lift-off, metres at scale 1 (0 = off).")]
    public float amplitude;
    [Tooltip("Spring frequency, Hz at scale 1 (Froude-scaled).")]
    public float hz;
    [Tooltip("Damping ratio (0.2 bouncy .. 1 dead).")]
    public float damping;
}

// Speed bursts (the kit's Walk.burst): every so often the cruise speed surges by `factor` for `seconds` - a predator's
// sudden dash - then falls back. Irregular, seeded per individual; Froude-timed. factor <= 1 = off (bit-identical).
[Serializable]
public struct GaitBurst
{
    [Tooltip("Cruise speed x this at the peak of a burst (<= 1 = off). The gait's stride limit still caps it.")]
    public float factor;
    [Tooltip("Mean time between bursts, seconds at scale 1 (each gap 0.5..1.5 x this, per individual).")]
    public float every;
    [Tooltip("Length of one burst, seconds at scale 1 (a quick surge, a hold, an easing back).")]
    public float seconds;
    public bool On => factor > 1f && every > 0f && seconds > 0f;
}

// An extra eye, rigid to the eye bone of its side: offset in the eye joint's frame (x mirrored; x = 0: one eye).
[Serializable]
public struct EyeSpot
{
    public Vector3 offset;
    public float radius;
}

// A rigid curved cone on a spine joint (chelicerae, fangs, horns): base at offset (joint frame, x mirrored
// when mirror), along direction (joint frame, x mirrored), curling down by curveDeg; a fang of fangLength
// continues from its tip.
[Serializable]
public struct BodyAppendage
{
    public int joint;
    public Vector3 offset, direction;
    public float length, radiusStart, radiusEnd, curveDeg, fangLength;
    public bool mirror;
}

// Climbing (0 = default for every number). A climbing species follows the surface it stands on - steep
// terrain (dug-hole walls, cliffs) and real collider geometry such as plant bark - with a per-joint up field
// instead of gravity's up; see CreatureClimb. Without canClimb the creature walks as before.
[Serializable]
public struct ClimbSpec
{
    public bool canClimb;
    [Tooltip("Steepest surface the creature walks on, degrees from gravity's up (90 = vertical, more = overhang). Past it the creature stops (Blocked).")]
    public float maxSurfaceAngleDeg;
    [Tooltip("Highest climb above the enclosure ground, in body lengths (the lab caps it further by the plant's height).")]
    public float maxHeightBodyLengths;
    [Tooltip("Speed factor on a vertical surface, going up / going down head-first.")]
    public float speedFactor, descentSpeedFactor;
    [Tooltip("Gait on a vertical surface, relative to walking: body clearance, stride (step threshold and overshoot), step time, foot lift.")]
    public float clearanceFactor, strideFactor, stepTimeFactor, liftFactor;
    [Tooltip("Fastest turn of a joint's up, degrees per second at scale 1 (Froude-scaled like the turn rate).")]
    public float upRateDeg;
    [Tooltip("Joints each side over which the surface normals are smoothed into the body's up field.")]
    public int upSmoothJoints;
    [Tooltip("Largest pitch bend between two body segments, degrees: a sharper fold is lifted away from the surface.")]
    public float pitchLimitDeg;
    [Tooltip("A climb goal needs a surface at least this many body widths across.")]
    public float minWidthFactor;

    // 140: a bulbous trunk's base (the lab Baobab's bark faces 103-137 deg near the ground) is climbed; a
    // branch's or a slab's underside (150-180) is not
    public float MaxSurfaceAngle => maxSurfaceAngleDeg > 0f ? maxSurfaceAngleDeg : 140f;
    public float MaxHeightBodyLengths => maxHeightBodyLengths > 0f ? maxHeightBodyLengths : 1.5f;
    public float SpeedFactor => speedFactor > 0f ? speedFactor : 0.5f;
    public float DescentSpeedFactor => descentSpeedFactor > 0f ? descentSpeedFactor : 0.4f;
    public float ClearanceFactor => clearanceFactor > 0f ? clearanceFactor : 0.65f;
    public float StrideFactor => strideFactor > 0f ? strideFactor : 0.6f;
    public float StepTimeFactor => stepTimeFactor > 0f ? stepTimeFactor : 1.2f;
    public float LiftFactor => liftFactor > 0f ? liftFactor : 0.6f;
    public float UpRateDeg => upRateDeg > 0f ? upRateDeg : 270f;
    public int UpSmoothJoints => upSmoothJoints > 0 ? upSmoothJoints : 3;
    public float PitchLimitDeg => pitchLimitDeg > 0f ? pitchLimitDeg : 25f;
    public float MinWidthFactor => minWidthFactor > 0f ? minWidthFactor : 1.5f;
}

[Serializable]
public struct CreatureLook
{
    public Color dorsal, flank, belly, pattern;
    public CreaturePattern patternType;
    public float patternScale, patternContrast, patternSoftness, spotScale;
    public float smoothness, rimStrength, subsurface;
    public float sizeJitter, hueJitter, valueJitter;   // per-individual ranges
    // Winged species: the wing membrane (part 8): colour, how much light shines through it (0..1), how dark
    // its veins are (0..1), how dark its trailing rim is (0..1).
    public Color membrane;
    public float membraneTranslucency, veinContrast, membraneRim;
    // ... and how far it takes on the body's dorsal colour toward its root (the wrist, the body, a fin's base), fading
    // to its own colour at the edge (0..1; 0 = one colour)
    public float membraneInner;
    // arthropod knee bands (mesh part 9) and a velvet sheen lobe (0 = none)
    public Color bandColor;
    public float sheen;
    public Color sheenColor;
    // Iridescent scale shimmer (the kit's Shimmer look, the dragon's signature; 0 = none): bands of thin-film
    // colour (shimmerColor -> shimmerColor2 with the view angle) travelling head -> tail along the body every
    // shimmerWavelength metres (rest arc, scale 1), one band passing a point every shimmerPeriod s (x sqrt of the
    // species scale), at strength shimmer, glowing faintly by shimmerGlow.
    public Color shimmerColor, shimmerColor2;
    public float shimmer, shimmerWavelength, shimmerPeriod, shimmerGlow;
}

[CreateAssetMenu(menuName = "Marching Cubes/Creature Species")]
public class CreatureSpecies : ScriptableObject
{
    // Size of the species relative to the authored dimensions below (lengths in metres at scale 1). Every
    // individual's sizeScale = scale x (1 +- look.sizeJitter); the gait's timing follows sqrt(sizeScale).
    [Tooltip("Overall size multiplier; everything - body, legs, gait timing (sqrt(scale)), enclosure - follows it.")]
    [Min(0.05f)] public float scale = 1f;
    public float spacing;
    public SpineJoint[] joints;
    public float headCapLength, tailTipLength;
    // Rigid head (user 2026-10-19): the first rigidHeadJoints spine joints move as one rigid body - their layout and
    // frames stay the rest shape's whatever the ground, a climb or a flight does (no bend, per-joint draping or
    // twist between them); the bend happens in the neck behind. 0 = off (every joint grounded on its own: the
    // test fixtures, a worm); 2..JointCount-1 = on. A kit SpineBody defaults to its profile's head (SpineBody.rigidHeadJoints).
    [Tooltip("Head joints that move as one rigid body (0 = off, every joint drapes on its own; else 2..joints-1).")]
    [Min(0)] public int rigidHeadJoints;
    // The rigid head's joint count the sim uses: 0 when off or meaningless (fewer than 2 joints, no neck behind).
    public int RigidHead => rigidHeadJoints >= 2 && rigidHeadJoints < JointCount ? rigidHeadJoints : 0;
    public LegSpec[] legs;
    public float speed, turnRateDeg, accel, turnSlowdown;
    public float undulationDeg, undulationHz;
    // Gait-coupled spine motion (0 = none). Each girdle (a joint with legs on both sides) swings toward
    // the leg that reaches forward, so diagonal gait groups make a standing S-wave: gaitSwayDeg is the
    // peak yaw of the trunk between girdles at full sway. gaitRollDeg rolls a girdle over its planted
    // feet while the other side steps.
    public float gaitSwayDeg, gaitRollDeg;
    public int subSteps = 2;
    public int eyeJoint = 1;
    public Vector3 eyeOffset;             // x mirrored
    public float eyeRadius;
    public int ringSegments = 16, ringsPerSegment = 3;
    public CreatureLook look;
    public CreatureBodyPlan bodyPlan = CreatureBodyPlan.Spine;
    public CephalopodSpec cephalopod;
    public ClimbSpec climb;
    // Winged species (Flight/, design 2026-10-17-winged-serpent): each wing's arm is one of the legs (walked
    // on the ground: wing-walk), driven by its WingSpec in the air. Empty = no wings; flight data is used
    // only with wings (CanFly).
    public WingSpec[] wings;
    public FlightSpec flight;
    public GaitBob gaitBob;
    public GaitBurst gaitBurst;
    // Wave gait (the kit's Walk.pattern Wave, T5.8): the gait groups step strictly in turn, in ascending group id,
    // the next as soon as the last has landed while any foot wants to step - a metachronal wave down the body
    // (CreatureLegs.StartWave). false = the event-driven gait every shipped species walks with.
    [Tooltip("Gait groups step strictly in turn (ascending id): a metachronal wave. Off = the event-driven gait.")]
    public bool waveGait;
    public EyeSpot[] eyeCluster;
    public BodyAppendage[] appendages;
    // Membrane strips along the spine (the kit's FinStrip: manes, crests, fins), skinned to the spine; empty = none.
    public FinStripSpec[] fins;
    // Spring chains rooted on spine joints (the kit's Antenna: whiskers, barbels, feelers), one bone per segment
    // after the eyes; empty = none (every shipped layout unchanged).
    public AntennaSpec[] antennae;

    // ---- creature kit (design docs/superpowers/specs/2026-10-18-creature-kit-design.md, Config/Creatures/CATALOGUE.md) ----
    // kitVersion 0: the legacy fields above are the species. 1: the species is `parts` + `modules` (with their
    // authoring sugar: mirror, Repeat, profile keys, gait patterns), expanded at load into Compiled; the legacy
    // fields above stay empty and the systems read Compiled.Runtime. `scale` is used by both.
    [Tooltip("0 = the legacy fields; 1 = creature kit parts + modules (see Config/Creatures/CATALOGUE.md).")]
    public int kitVersion;
    [SerializeReference] public List<CreatureKit.CreaturePart> parts = new List<CreatureKit.CreaturePart>();
    [SerializeReference] public List<CreatureKit.CreatureModule> modules = new List<CreatureKit.CreatureModule>();

    // Raised when the asset is edited (Inspector, play mode included): the creature lab respawns the
    // species at its new scale.
    public static event Action<CreatureSpecies> Changed;
    void OnValidate() { legModels = null; InvalidateLayout(); compiled = null; Changed?.Invoke(this); }

    // The creature kit's view of this species (Kit/CompiledSpecies): parts, modules, bone layout, and Runtime -
    // the species whose fields the systems read. Every runtime entry point (CreatureSim, the mesh builder, the
    // renderer, CreatureWorld.Spawn) starts from sp.Compiled.Runtime. A legacy asset is its own runtime view
    // (LegacySpeciesAdapter: the same objects, bit-identical). Built on first use, dropped by OnValidate.
    [NonSerialized] CreatureKit.CompiledSpecies compiled;
    public CreatureKit.CompiledSpecies Compiled => compiled ?? (compiled = kitVersion > 0 ? CompileKit() : CreatureKit.LegacySpeciesAdapter.Compile(this));
    public void InvalidateCompiled() { compiled = null; InvalidateLayout(); }

    // kitVersion 1: the runtime view is a hidden species holding the expanded legacy fields. It is reused on
    // every recompile (OnValidate may not destroy objects), so its identity is stable for the renderer.
    [NonSerialized] CreatureSpecies runtimeView;
    [NonSerialized] CreatureKit.CompiledSpecies source;   // a runtime view: the compiled kit species it was written from
    CreatureKit.CompiledSpecies CompileKit()
    {
        CreatureKit.CompiledSpecies c = CreatureKit.CompiledSpecies.Compose(name, parts, modules);
        if (c.Error != null) { c.Runtime = this; return c; }   // Validate reports it; the empty fields never run
        if (runtimeView == null)
        {
            runtimeView = CreateInstance<CreatureSpecies>();
            runtimeView.hideFlags = HideFlags.DontSave;
        }
        runtimeView.name = name;
        runtimeView.scale = scale;
        c.WriteLegacyFields(runtimeView);
        runtimeView.legModels = null;
        runtimeView.InvalidateCompiled();
        c.Runtime = runtimeView;
        // the runtime view's kit description is the authored one (its composed parts and modules, with any part
        // type the legacy fields cannot hold), and so is its bone layout
        runtimeView.compiled = c;
        runtimeView.source = c;
        return c;
    }

    // The arthropod legs' solver models, built once per species leg (null for a two-bone leg) and shared by
    // every individual: the solver's scratch makes them main-thread only, like the sims.
    [NonSerialized] ArthropodLegModel[] legModels;
    internal ArthropodLegModel LegModel(int leg)
    {
        LegSpec l = legs[leg];
        if (l == null || !l.IsArthropod) return null;
        if (legModels == null || legModels.Length != legs.Length) legModels = new ArthropodLegModel[legs.Length];
        return legModels[leg] ?? (legModels[leg] = new ArthropodLegModel(l.segments, l.ModelParams));
    }

    // A leg's foot reach, metres at scale 1: upper + lower, or the arthropod's longest ankle reach plus its
    // tarsus laid at the contact angle (step triggers, stride, throttle and sway measure against it).
    public float LegReach(int leg)
    {
        LegSpec l = legs[leg];
        if (!l.IsArthropod) return l.upperLength + l.lowerLength;
        ArthropodLegModel m = LegModel(leg);
        return m.Error != null ? l.TotalLength : m.ReachLong + m.TarsusLength * m.CosC;
    }

    public int JointCount => joints != null ? joints.Length : 0;
    int LegCount => legs != null ? legs.Length : 0;
    public int WingCount => wings != null ? wings.Length : 0;
    public int AntennaCount => antennae != null ? antennae.Length : 0;
    public bool CanFly => WingCount > 0 && flight != null && flight.cruiseSpeed > 0f;
    // Bone order: spine, the leg bones (3 per legacy leg, one per arthropod segment), 6 per wing, 2 eyes -
    // the creature kit's generic registry (Kit/CreatureBoneLayout) built from these fields, cached. The cache
    // follows the joints/legs/wings arrays (a new array or length rebuilds it) and OnValidate; code that
    // changes a leg's segment count in place calls InvalidateLayout.
    [NonSerialized] CreatureKit.CreatureBoneLayout layout;
    [NonSerialized] SpineJoint[] layoutJoints;
    [NonSerialized] LegSpec[] layoutLegs;
    [NonSerialized] WingSpec[] layoutWings;
    [NonSerialized] AntennaSpec[] layoutAntennae;
    [NonSerialized] int layoutJointCount, layoutLegCount, layoutWingCount;
    public CreatureKit.CreatureBoneLayout Layout
    {
        get
        {
            if (kitVersion > 0 && !ReferenceEquals(Compiled.Runtime, this)) return Compiled.Runtime.Layout;
            if (source != null && source.Layout != null) return source.Layout;   // a kit runtime view: the parts' layout
            if (layout == null || !ReferenceEquals(layoutJoints, joints) || !ReferenceEquals(layoutLegs, legs) || !ReferenceEquals(layoutWings, wings) || !ReferenceEquals(layoutAntennae, antennae)
                || layoutJointCount != JointCount || layoutLegCount != LegCount || layoutWingCount != WingCount)
            {
                layout = CreatureKit.CreatureBoneLayout.FromLegacy(this);
                layoutJoints = joints; layoutLegs = legs; layoutWings = wings; layoutAntennae = antennae;
                layoutJointCount = JointCount; layoutLegCount = LegCount; layoutWingCount = WingCount;
            }
            return layout;
        }
    }
    public void InvalidateLayout() => layout = null;
    public int BoneCount => Layout.BoneCount;
    public int LegBone(int leg, int part) => Layout.LegBone(leg, part);
    public int WingBone(int wing, int k) => Layout.WingBone(wing, k);
    public int EyeBone(int eye) => Layout.EyeBone(eye);
    public int AntennaBone(int antenna, int k) => Layout.ChainBone(antenna, k);
    public float BodyLength => headCapLength + spacing * (JointCount - 1) + tailTipLength;

    public float MaxLateralReach
    {
        get
        {
            float r = 0f;
            for (int i = 0; i < JointCount; i++) r = Mathf.Max(r, joints[i].halfWidth);
            for (int i = 0; i < LegCount; i++)
            {
                LegSpec l = legs[i];
                if (l == null) continue;
                r = Mathf.Max(r, Mathf.Abs(l.shoulderOffset.x) + (l.IsArthropod ? l.TotalLength : l.upperLength + l.lowerLength));
            }
            // a spread wing: the wrist's rest point plus its longest finger
            for (int w = 0; w < WingCount; w++)
            {
                WingSpec ws = wings[w];
                if (ws == null) continue;
                float finger = 0f;
                if (ws.fingerLengths != null) for (int k = 0; k < ws.fingerLengths.Length; k++) finger = Mathf.Max(finger, ws.fingerLengths[k]);
                r = Mathf.Max(r, Mathf.Abs(ws.restTip.x) + finger);
            }
            // an antenna reaches out by its root offset and its length
            for (int a = 0; a < AntennaCount; a++) r = Mathf.Max(r, Mathf.Abs(antennae[a].offset.x) + antennae[a].length);
            // a fin strip stands out of the body by its height
            if (fins != null)
                foreach (FinStripSpec f in fins)
                {
                    int a = Mathf.Clamp(f.fromJoint, 0, Mathf.Max(0, JointCount - 1));
                    float hh = JointCount > 0 ? joints[a].halfHeight : 0f;
                    r = Mathf.Max(r, hh + Mathf.Max(f.heightStart, f.heightEnd));
                }
            return r;
        }
    }

    // null when valid, else the first problem found.
    public string Validate()
    {
        if (kitVersion > 0) return Compiled.Error ?? (ReferenceEquals(Compiled.Runtime, this) ? "kit species did not compile" : Compiled.Runtime.Validate());
        if (bodyPlan == CreatureBodyPlan.Cephalopod) return cephalopod.Validate();
        int n = JointCount;
        if (n < 2) return "joints: need at least 2 spine joints";
        if (!(spacing > 0f)) return "spacing must be > 0";
        if (!(scale > 0f)) return "scale must be > 0";
        if (eyeJoint < 0 || eyeJoint >= n) return "eyeJoint out of range";
        if (rigidHeadJoints < 0 || rigidHeadJoints > n - 1) return $"rigidHeadJoints {rigidHeadJoints} out of range 0..{n - 1}";
        for (int i = 0; i < LegCount; i++)
        {
            LegSpec l = legs[i];
            if (l == null) return $"legs[{i}] is null";
            if (l.attachJoint < 0 || l.attachJoint >= n) return $"legs[{i}].attachJoint {l.attachJoint} out of range 0..{n - 1}";
            if (l.side != -1 && l.side != 1) return $"legs[{i}].side must be -1 or +1";
            if (l.gaitGroup < 0 || l.gaitGroup > 31) return $"legs[{i}].gaitGroup must be in 0..31";
            if (l.IsArthropod)
            {
                string e = LegModel(i).Error;
                if (e != null) return $"legs[{i}]: {e}";
                if (l.swingRetract < 0f || l.swingRetract > 0.5f) return $"legs[{i}].swingRetract must be in 0..0.5";
                if (l.liftDelay < 0f || l.liftDelay > 0.5f) return $"legs[{i}].liftDelay must be in 0..0.5";
            }
            else if (!(l.upperLength > 0f) || !(l.lowerLength > 0f)) return $"legs[{i}] limb lengths must be > 0";
            if (!(l.ForwardSwingLimit < 180f) || !(l.BackSwingLimit < 180f) || !(l.KneeRange < 180f) || !(l.KneeFlex < 180f))
                return $"legs[{i}] joint-range limits must be < 180 deg";
        }
        for (int w = 0; w < WingCount; w++)
        {
            if (wings[w] == null) return $"wings[{w}] is null";
            string e = wings[w].Validate(this);
            if (e != null) return $"wings[{w}]: {e}";
            for (int o = 0; o < w; o++) if (wings[o] != null && wings[o].leg == wings[w].leg) return $"wings[{w}] shares leg {wings[w].leg}";
        }
        return null;
    }
}
