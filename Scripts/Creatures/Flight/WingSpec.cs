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

    [Header("Fingers (wrist outward: leading .. trailing)")]
    // MinFingers..MaxFingers fingers, one entry each in the three finger arrays: 3 (the winged serpent) is the
    // pterosaur-like hand, 4-5 a bat's (a membrane zone between each pair of neighbouring fingers)
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
    [Range(0f, 75f)] public float groundLiftDeg = 0f;
    // A tall stance on near-vertical folded arms (a pterosaur's; the spine's clearances carry the shoulders
    // high and slope to the hips): the hand bone (and its thumb claw) stays on the forearm when the folded fan
    // lifts (groundLiftDeg) - only the fingers and the membrane turn up, a steep lift does not kink the wrist -
    // and the mesh's bind pose (the spread glide pose) keeps a straight spine at the girdle's clearance instead
    // of following the stance's varying clearances. Off = the low wing-walk (the hand turns with the fan).
    public bool groundTallStance = false;
    // The membrane between the humerus and the flank as this many affine patches side by side along the flank
    // (WingBoneLayout.FlankPatchCount; one bone each): the flank edge follows the body's curve instead of a
    // straight chord from the rear attach to the shoulder, and the rear attach slides along the flank (never off
    // it) to stay clear of the forearm's line. 0 or 1 = the one humerus patch (7 bones per wing).
    [Range(0, 8)] public int flankPatches = 0;
    // On the ground the wrist stands on its thumb claw: raised off the walked foot point until the thumb's tip
    // (thumbLength) is on it, at most this far (m at scale 1) - the hand bone flexes so the thumb points down onto the
    // foot point (CreatureWings.HandBoneOut; the wrist cap and knuckle ride the forearm) - the arm is drawn to the raised wrist, the gait's
    // foot point is unchanged. 0 = on the wrist.
    public float groundWristLift = 0f;
    // With groundWristLift: the thumb claw's tip is put exactly on the foot point (the wrist moved after it in 3D, not
    // only raised): the drawn claw stays planted while the arm swings over it, instead of skating a few % of the arm
    // as the hand frame turns (the kit's Gait check on the drawn feet). Off = the vertical lift only (bit-identical).
    public bool thumbPlant = false;
    // With groundTallStance: the fan-side rear rib (WingBoneLayout.FanRearRib) rolls with the fan's plane in the air
    // too, not halfway into the arm sector - for a wing whose arm zone rides the arm sector alone (a subtype), so the
    // fan-side rib serves only the sail beside it. The halfway roll turned past 90 deg off the fan in hard pull-ups
    // and flipped the narrow zone between it and the trailing rib inside out. Off = shipped (bit-identical).
    public bool fanRearRollsWithFan = false;
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

    [Header("Look (all 0 = the plain wing: the winged serpent's, bit for bit)")]
    // The scallop's depth as a fraction of the zone's own chord (the gap between its two ray tips) instead of its
    // reach: a narrow zone gets a shallow arc, not a deep spike (the dragon's "maple leaf" edge, user 2026-10-19).
    public bool scallopByChord = false;
    // The membrane bellies out to the dorsal side between its rays: camber height at the middle of a zone, a fraction
    // of the zone's width there (0 = flat sheets). The sheet meets each finger at an angle: the fingers stand out.
    [Range(0f, 0.2f)] public float membraneBillow = 0f;
    // Membrane mesh resolution: each zone's grid this many times finer both ways (0 or 1 = 6 x 8 per zone).
    [Range(0, 3)] public int membraneDetail = 0;
    // Finger bones: knuckle bulges at about a third and two thirds of the length and a flared root, this strong
    // (fraction of the local radius; 0 = a smooth taper).
    [Range(0f, 1.5f)] public float fingerKnuckles = 0f;
    // A knuckle bulb at the wrist, radius = this x the forearm radius (0 = none).
    [Range(0f, 3f)] public float wristKnuckle = 0f;
    // A dragon's / bat's arm (user 2026-10-20, "real-looking ... wing arms"; 0 = the plain tube): a thick humerus
    // out of the chest with deltoid, biceps and triceps masses (this x the upper arm's radius), a ball elbow with an
    // olecranon, a forearm thick near the elbow and lean at the wrist, carpal knobs at the wrist, flared metacarpal
    // bases on the fingers and a two-part thumb (a skin digit and a pale claw on the same curve).
    [Range(0f, 1.5f)] public float armMuscle = 0f;
    // With armMuscle: the forearm's two-bone (radius / ulna) form - a second, slimmer bone along its trailing side,
    // this x the forearm radius (0 = one bone).
    [Range(0f, 1f)] public float forearmTwin = 0f;
    // The thumb's root radius, m at scale 1 (0 = 0.45 x the forearm radius).
    public float thumbRadius = 0f;

    [Header("Landing flare (opt-in: landingFlare 0 = the shipped wing, bit for bit)")]
    [Tooltip("The landing flare's weight, 0..1 (0 = off: the flare only brakes with the flight's slow strokes, as shipped). On, as the flight's Flare mode runs (CreatureWings.Flare: blended in over the flare's first 0.15 s x sqrt s, held to the touchdown, blended out over the touchdown's first 0.25 s x sqrt s - inside the arms' handover - or 0.15 s after a go-around) the drawn wing leaves its beat for the air brake: held spread (unfolded), raised (flareRaiseDeg), the wrist swept forward ahead of the shoulder (flareSweepDeg), the fingers fanned wider (flareSpreadDeg) and the whole hand pitched nose-up (flarePitchDeg) - a bat's / a manta's broad cupped spread, not a pterosaur's single swept spar. Only the drawn pose: the flight model's strokes, areas and tilts (what lands it) are untouched. Only a DigitWing draws it: WingPart.Validate refuses it on any other wing.")]
    [Range(0f, 1f)] public float landingFlare = 0f;
    [Tooltip("Flare: the fan widened by this much (deg): the trailing finger turns this far further back, the ones between it and the leading finger in proportion (the leading finger stays). 0..45 (WingPart.Validate).")]
    public float flareSpreadDeg = 20f;
    [Tooltip("Flare: the wrist swept forward about the girdle's up by this much (deg) from the rest pose: the hand ahead of the shoulder. 0..90 (WingPart.Validate).")]
    public float flareSweepDeg = 35f;
    [Tooltip("Flare: the held stroke angle (deg, + up about the stroke axis): the wings raised high. -30..60 (WingPart.Validate).")]
    public float flareRaiseDeg = 20f;
    [Tooltip("Flare: the hand (its fingers' plane) turned toward the oncoming air until it stands this far (deg) nose-up to the flight path (the descent's: the velocity low-passed over 0.3 s x sqrt s, about one braking stroke - the hand is held, it does not rock with the strokes): the leading edge up, the trailing fingers down, the air on the ventral side - the air brake. Pitched to the path, not to the body: the braking strokes swing the path +-40 deg each beat while the body stays nearly level. The forearm is laid into the turned plane (the hand's out axis), as for any hand normal. 0..80 (WingPart.Validate).")]
    public float flarePitchDeg = 50f;

    // Fingers: the default (and the winged serpent's) finger count; a wing has FingerCount (fingerLengths.Length)
    public const int Fingers = 3, Ribs = 3;
    public const int MinFingers = 3, MaxFingers = 5;
    public int FingerCount => fingerLengths != null && fingerLengths.Length > 0 ? fingerLengths.Length : Fingers;

    // null when valid, else the first problem found.
    public string Validate(CreatureSpecies sp)
    {
        int legs = sp.legs != null ? sp.legs.Length : 0;
        if (leg < 0 || leg >= legs) return $"wing leg {leg} out of range 0..{legs - 1}";
        int nf = fingerLengths != null ? fingerLengths.Length : 0;
        if (nf < MinFingers || nf > MaxFingers) return $"fingerLengths needs {MinFingers}..{MaxFingers} entries";
        if (fingerSpreadDeg == null || fingerSpreadDeg.Length != nf) return "fingerSpreadDeg needs one entry per finger";
        if (fingerFoldDeg == null || fingerFoldDeg.Length != nf) return "fingerFoldDeg needs one entry per finger";
        for (int k = 0; k < nf; k++)
            if (!(fingerLengths[k] > 0f)) return "finger lengths must be > 0";
        for (int k = 1; k < nf; k++)
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
