using System;
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
    // Mesh: fraction of each limb bone blended into the rounded knee (0 = default). A larger blend
    // spreads a deep knee bend over more of the limb so the skin does not fold.
    public float kneeBlend;

    public const float DefaultSwingLimitDeg = 55f, DefaultKneeRangeDeg = 35f, DefaultKneeBlend = 0.45f;
    public float ForwardSwingLimit => forwardSwingLimitDeg > 0f ? forwardSwingLimitDeg : DefaultSwingLimitDeg;
    public float BackSwingLimit => backSwingLimitDeg > 0f ? backSwingLimitDeg : DefaultSwingLimitDeg;
    public float KneeRange => kneeRangeDeg > 0f ? kneeRangeDeg : DefaultKneeRangeDeg;
    public float KneeFlex => kneeFlexDeg > 0f ? kneeFlexDeg : KneeRange;
    public float KneeBlend => kneeBlend > 0f ? Mathf.Min(kneeBlend, 0.49f) : DefaultKneeBlend;
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

    // Raised when the asset is edited (Inspector, play mode included): the creature lab respawns the
    // species at its new scale.
    public static event Action<CreatureSpecies> Changed;
    void OnValidate() => Changed?.Invoke(this);

    public int JointCount => joints != null ? joints.Length : 0;
    int LegCount => legs != null ? legs.Length : 0;
    public int BoneCount => JointCount + 3 * LegCount + 2;
    public int LegBone(int leg, int part) => JointCount + 3 * leg + part;
    public int EyeBone(int eye) => JointCount + 3 * LegCount + eye;
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
                r = Mathf.Max(r, Mathf.Abs(l.shoulderOffset.x) + l.upperLength + l.lowerLength);
            }
            return r;
        }
    }

    // null when valid, else the first problem found.
    public string Validate()
    {
        if (bodyPlan == CreatureBodyPlan.Cephalopod) return cephalopod.Validate();
        int n = JointCount;
        if (n < 2) return "joints: need at least 2 spine joints";
        if (!(spacing > 0f)) return "spacing must be > 0";
        if (!(scale > 0f)) return "scale must be > 0";
        if (eyeJoint < 0 || eyeJoint >= n) return "eyeJoint out of range";
        for (int i = 0; i < LegCount; i++)
        {
            LegSpec l = legs[i];
            if (l == null) return $"legs[{i}] is null";
            if (l.attachJoint < 0 || l.attachJoint >= n) return $"legs[{i}].attachJoint {l.attachJoint} out of range 0..{n - 1}";
            if (l.side != -1 && l.side != 1) return $"legs[{i}].side must be -1 or +1";
            if (l.gaitGroup < 0 || l.gaitGroup > 31) return $"legs[{i}].gaitGroup must be in 0..31";
            if (!(l.upperLength > 0f) || !(l.lowerLength > 0f)) return $"legs[{i}] limb lengths must be > 0";
            if (!(l.ForwardSwingLimit < 180f) || !(l.BackSwingLimit < 180f) || !(l.KneeRange < 180f) || !(l.KneeFlex < 180f))
                return $"legs[{i}] joint-range limits must be < 180 deg";
        }
        return null;
    }
}
