using System;
using UnityEngine;

public enum CreaturePattern { None = 0, Bands = 1, Spots = 2, Stripes = 3, BandedSpots = 4 }

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
    public float spacing;
    public SpineJoint[] joints;
    public float headCapLength, tailTipLength;
    public LegSpec[] legs;
    public float speed, turnRateDeg, accel, turnSlowdown;
    public float undulationDeg, undulationHz;
    public int subSteps = 2;
    public int eyeJoint = 1;
    public Vector3 eyeOffset;             // x mirrored
    public float eyeRadius;
    public int ringSegments = 16, ringsPerSegment = 3;
    public CreatureLook look;

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
        int n = JointCount;
        if (n < 2) return "joints: need at least 2 spine joints";
        if (!(spacing > 0f)) return "spacing must be > 0";
        if (eyeJoint < 0 || eyeJoint >= n) return "eyeJoint out of range";
        for (int i = 0; i < LegCount; i++)
        {
            LegSpec l = legs[i];
            if (l == null) return $"legs[{i}] is null";
            if (l.attachJoint < 0 || l.attachJoint >= n) return $"legs[{i}].attachJoint {l.attachJoint} out of range 0..{n - 1}";
            if (l.side != -1 && l.side != 1) return $"legs[{i}].side must be -1 or +1";
            if (!(l.upperLength > 0f) || !(l.lowerLength > 0f)) return $"legs[{i}] limb lengths must be > 0";
        }
        return null;
    }
}
