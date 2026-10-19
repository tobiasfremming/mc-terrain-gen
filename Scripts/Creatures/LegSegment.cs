using System;
using UnityEngine;

// One segment of an arthropod leg (design: docs/superpowers/specs/2026-10-17-multi-segment-legs-spider-design.md).
// A leg is 3..7 segments from the body outward, e.g. coxa, trochanter, femur, patella, tibia, metatarsus,
// tarsus. The last segment (the tarsus) is laid on the ground at the leg's contact angle; the others are
// posed by ArthropodLegIK. All lengths in metres at scale 1, angles in degrees.
//
// Joint bends live in the leg plane (outward x, up y; a positive bend raises the segment):
//     bend_i(s, beta) = restBendDeg_i + s * flexWeight_i + beta * liftWeight_i
// s (the reach synergy) is solved so the leg reaches its foot, beta (the lift synergy) is driven by the
// swing. Segment 0's "bend" is its absolute elevation above the horizontal.
[Serializable]
public struct LegSegment
{
    [Tooltip("Segment length, metres at scale 1.")]
    public float length;
    [Tooltip("Tube radius at the segment's start and end (per-segment taper), metres at scale 1.")]
    public float radiusStart, radiusEnd;
    [Tooltip("Rest bend at this segment's START joint, degrees (+ raises). Segment 0: elevation above horizontal.")]
    public float restBendDeg;
    [Tooltip("Hinge limits of the start joint, degrees.")]
    public float minBendDeg, maxBendDeg;
    [Tooltip("Share of the reach synergy: + raises the segment when the leg shortens, - folds it down. The knee is +-1.")]
    public float flexWeight;
    [Tooltip("Share of the swing's knee-lift synergy (+ raises).")]
    public float liftWeight;
    [Tooltip("Hinge axis rotated about the segment, degrees (0 = a planar leg).")]
    public float axisTiltDeg;
    [Tooltip("Joint bulb at the END joint: radius = knuckle x the larger adjacent radius (0 = none).")]
    public float knuckle;
    [Tooltip("Fraction of the segment drawn in the look's band colour, start..end (0, 0 = none).")]
    public float bandStart, bandEnd;
    [Tooltip("A sharp spur at the segment's distal end, metres at scale 1 (0 = none): on its lower side, raked toward the tip, claw-coloured.")]
    public float spurLength;
}
