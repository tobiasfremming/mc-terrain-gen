using System;
using UnityEngine;

// One wing of a winged species (design section 1-2). The arm is a LegSpec (species.legs[leg]: upper =
// humerus, lower = forearm, foot = wrist/hand): on the ground CreatureLegs walks it (wing-walk), in the
// air CreatureWings drives it. Phase B adds `public WingSpec[] wings;` to CreatureSpecies (null = none).
//
// A wingbeat is a step: the wrist is "planted" in the air during the downstroke and the shoulder rises
// over it; when the wrist leaves the vertical comfort band at the bottom the wing steps (upstroke) to
// the band top + overshoot, part-folded and feathered.
//
// Authored at scale 1, metres, in the attach (girdle) joint frame with x mirrored by the arm's side.
[Serializable]
public class WingSpec
{
    public int leg;                                        // the arm, index into species.legs
    public Vector3 restTip = new Vector3(1.06f, 0.08f, 0.05f); // wrist rest point (spread glide pose)
    public float comfortUp = 0.55f, comfortDown = 0.55f;  // vertical band around the rest wrist, along girdle up
    public float strokePlaneDeg = 10f;   // stroke axis pitched up from girdle forward: the downstroke sweeps forward
    public float pronationDeg = 18f;     // downstroke normal tilt per unit thrust demand (leading edge down)
    public float overshootUp = 0.08f;    // upstroke target = band-top angle x (1 + overshootUp)
    public float upstrokeDuration = 0.22f;
    [Range(0f, 1f)] public float upstrokeFold = 0.6f; // peak fold mid-upstroke
    [Range(0f, 0.8f)] public float foldReach = 0.3f;  // at full fold the wrist pulls in by this fraction of its reach
    public int beatGroup;                // wings of one group beat together (both wings: 0)

    [Header("Fingers (wrist outward: leading, middle, trailing)")]
    public float[] fingerLengths = { 0.75f, 0.65f, 0.55f };
    // In the wing plane, from the forearm direction toward the tail. Spread = glide pose, fold = on the ground
    // (folded back along the body: each membrane zone keeps about half its spread angle, since two-ray
    // skinning cannot compress a zone much further without the columns crossing; 120/140/160 already fails).
    public float[] fingerSpreadDeg = { -12f, 30f, 78f };
    public float[] fingerFoldDeg = { 100f, 122f, 146f };
    public float fingerMinGapDeg = 4f;
    public float fingerFoldGapDeg = 10f;  // folded: rays at least this far apart (no membrane sliver flips)
    // On the ground (wing-walk, perch) the folded hand's fan is turned up about the rear flank rib until the
    // fan's outer edge rises this far above the girdle's plane: the folded fingers sweep back and up, clear
    // of the ground, the membrane tucked between them and the flank (user feedback 2026-10-18). 0 = flat.
    [Range(0f, 60f)] public float groundLiftDeg = 0f;
    // On the ground the wrist stands on its thumb claw: raised off the walked foot point until the thumb's tip
    // (thumbLength) is on it, at most this far (m at scale 1); the arm is drawn to the raised wrist, the gait's
    // foot point is unchanged. 0 = on the wrist.
    public float groundWristLift = 0f;
    // On the ground the membrane's flank attach rises this far up the flank (x the joint's halfHeight): the
    // folded wing tucks up against the body instead of hanging below it. 0 = at mid-flank as in flight.
    [Range(0f, 1.5f)] public float groundFlankRise = 0f;
    public float fingerRadius = 0.014f;
    public float thumbLength = 0.07f;

    [Header("Membrane")]
    // Membrane: finger 1 -> 2 -> 3 -> trailing rib -> rear flank rib -> front flank rib, one zone between
    // each pair of rays. The flank ribs run from the wrist to membraneRearJoint / membraneFrontJoint (the
    // front attach slides forward along the flank when the wrist comes in line with the body) and are
    // scaled so they always end on the flank; the trailing rib lies at trailingRib of the way from
    // finger 3 to the rear flank rib, by angle and by length.
    public int membraneRearJoint = 13, membraneFrontJoint = 5;
    [Range(0.2f, 0.8f)] public float trailingRib = 0.5f;
    [Range(0f, 1.2f)] public float membraneInset = 0.8f; // flank attach at this fraction of the joint's halfWidth
    public float area = 1f;              // aerodynamic area factor
    [Range(0f, 0.4f)] public float scallop = 0.12f;     // trailing-edge scallop depth between rays (fraction)

    public const int Fingers = 3, Ribs = 3;

    // null when valid, else the first problem found.
    public string Validate(CreatureSpecies sp)
    {
        int legs = sp.legs != null ? sp.legs.Length : 0;
        if (leg < 0 || leg >= legs) return $"wing leg {leg} out of range 0..{legs - 1}";
        if (fingerLengths == null || fingerLengths.Length != Fingers) return "fingerLengths needs 3 entries";
        if (fingerSpreadDeg == null || fingerSpreadDeg.Length != Fingers) return "fingerSpreadDeg needs 3 entries";
        if (fingerFoldDeg == null || fingerFoldDeg.Length != Fingers) return "fingerFoldDeg needs 3 entries";
        for (int k = 0; k < Fingers; k++)
            if (!(fingerLengths[k] > 0f)) return "finger lengths must be > 0";
        for (int k = 1; k < Fingers; k++)
        {
            if (fingerSpreadDeg[k] - fingerSpreadDeg[k - 1] < fingerMinGapDeg) return "fingerSpreadDeg must increase by >= fingerMinGapDeg";
            if (fingerFoldDeg[k] - fingerFoldDeg[k - 1] < fingerMinGapDeg) return "fingerFoldDeg must increase by >= fingerMinGapDeg";
        }
        if (membraneRearJoint < 0 || membraneRearJoint >= sp.JointCount) return "membraneRearJoint out of range";
        if (membraneFrontJoint < 0 || membraneFrontJoint >= sp.JointCount) return "membraneFrontJoint out of range";
        if (membraneFrontJoint >= membraneRearJoint) return "membraneFrontJoint must be ahead of membraneRearJoint";
        if (!(comfortUp > 0f) || !(comfortDown > 0f)) return "comfort band must be > 0";
        if (!(upstrokeDuration > 0f)) return "upstrokeDuration must be > 0";
        return null;
    }
}
