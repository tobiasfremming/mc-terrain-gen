// Creature kit T5.6: Antenna - a thin spring chain rooted on a spine joint (whiskers, barbels, feelers, cerci,
// streamers). One bone per segment (layout block Chain, after the eyes). Each point is pulled toward the chain's
// rest curve in the root joint's frame (stiffer at the root), damped, and dragged by the air, so it trails behind
// a moving body and swings out of a turn or a roll; exact segment lengths and a bend cone keep it a clean strand.
// Runtime: CreatureAntennae (stepped by CreatureSim every sub-step, ground and air). Mesh: AntennaMesh (a tapered
// tube, membrane shading part 8 like the fins and wings: translucent).
using System;
using UnityEngine;

// The runtime antenna (CreatureSpecies.antennae, filled by the kit's Antenna part): authored at scale 1.
[Serializable]
public struct AntennaSpec
{
    public int joint;                 // spine joint it is rooted on
    public int side;                  // -1 left, +1 right (offset.x, direction.x mirrored), 0 midline
    public Vector3 offset;            // root in the joint frame (m)
    public Vector3 direction;         // rest direction at the root, joint frame
    public float length;              // m
    public int segments;              // bones (2..16)
    public float radiusStart, radiusEnd;
    public float stiffnessHz;         // pull toward the rest curve at the root (Froude-scaled), weaker toward the tip
    public float damping;             // 0..1: damping ratio of that pull
    public float airDrag;             // 1/s: drag of the air on the strand (Froude-scaled): how much it trails
    public float droopDeg;            // the rest curve bends down by this much toward the tip
    public float maxBendDeg;          // bend cone between segments (0 = 35)

    public int Segments => Mathf.Clamp(segments, 2, 16);
    public float MaxBend => maxBendDeg > 0f ? maxBendDeg : 35f;
}

namespace CreatureKit
{
    [Serializable]
    [Part("Antenna", PartKind.Chain, Bones = "= segments", Summary = "Spring chain rooted on a spine joint: whiskers, barbels, feelers, cerci, streamers. Pulled to its rest curve (stiffer at the root), damped and dragged by the air: it trails a moving body and swings out of turns and rolls. Exact lengths, bend cone.",
          System = "CreatureAntennae (part runtime, stage AfterLimbs: every sub-step) + AntennaMesh", MaterialLayers = new[] { 8 },
          UseWhen = "whiskers, barbels, antennae, feelers, cerci, ribbon-like streamers")]
    public sealed partial class Antenna : CreaturePart
    {
        [Param(ParamUnit.Direction, "rest direction at the root in the joint frame (x mirrored by side)")] public Vector3 direction = new Vector3(0f, 0f, 1f);
        [Param(ParamUnit.Metres, "length", Example = "dragon whisker 0.55")] public float length;
        [Param(ParamUnit.Count, "segments (one bone each)", Min = 2, Max = 16)] public int segments = 8;
        [Param(ParamUnit.Metres, "tube radius at the root")] public float radiusStart;
        [Param(ParamUnit.Metres, "tube radius at the tip")] public float radiusEnd;
        [Param(ParamUnit.Hz, "pull toward the rest curve at the root (weaker toward the tip)", Example = "dragon 1.6")] public float stiffnessHz = 1.5f;
        [Param(ParamUnit.Fraction, "damping ratio of that pull", Min = 0, Max = 2)] public float damping = 0.4f;
        [Param(ParamUnit.Hz, "air drag rate (1/s): how much the strand trails a moving body", Example = "dragon 0.8")] public float airDrag = 0.8f;
        [Param(ParamUnit.Degrees, "the rest curve bends down by this much toward the tip")] public float droopDeg;
        [Param(ParamUnit.Degrees, "bend cone between segments (0 = 35)", Min = 0, Max = 90)] public float maxBendDeg;

        public override int BoneCount(in SpeciesCtx c) => Mathf.Clamp(segments, 2, 16);

        public AntennaSpec ToRuntime() => new AntennaSpec
        {
            joint = at.joint, side = at.side, offset = at.offset, direction = direction, length = length, segments = segments,
            radiusStart = radiusStart, radiusEnd = radiusEnd, stiffnessHz = stiffnessHz, damping = damping, airDrag = airDrag,
            droopDeg = droopDeg, maxBendDeg = maxBendDeg,
        };

        public static Antenna From(in AntennaSpec a, string id) => new Antenna
        {
            id = id, at = new PartSocket(a.joint, a.side, a.offset), direction = a.direction, length = a.length, segments = a.segments,
            radiusStart = a.radiusStart, radiusEnd = a.radiusEnd, stiffnessHz = a.stiffnessHz, damping = a.damping, airDrag = a.airDrag,
            droopDeg = a.droopDeg, maxBendDeg = a.maxBendDeg,
        };

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return $"{id}: an Antenna needs a SpineBody";
            if (at.joint < 0 || at.joint >= c.Joints) return $"{id}: joint {at.joint} outside 0..{c.Joints - 1}";
            if (segments < 2 || segments > 16) return $"{id}: segments must be 2..16";
            if (!(length > 0f)) return $"{id}: length must be > 0";
            if (!(stiffnessHz > 0f)) return $"{id}: stiffnessHz must be > 0";
            if (damping < 0f || airDrag < 0f) return $"{id}: damping and airDrag must be >= 0";
            if (direction.sqrMagnitude < 1e-8f) return $"{id}: direction must not be zero";
            return null;
        }
    }
}
