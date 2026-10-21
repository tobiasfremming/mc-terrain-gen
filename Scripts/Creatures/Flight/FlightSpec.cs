using System;
using UnityEngine;

// Flight data of a winged species (design 2026-10-17-winged-serpent-design.md, section 2). Standalone in
// Phase A; Phase B adds `public FlightSpec flight;` to CreatureSpecies (null = cannot fly).
//
// Every value is authored at scale 1 and follows Froude similarity (FlightModel): lengths x scale, times
// and speeds x sqrt(scale), accelerations unchanged. The defaults are the designer's numpy prototype
// (wyvern_proto/proto.py: m = 6 kg, kW = 2.2, kL = 0.12, kD = 0.06), divided by the mass so the air plant
// is mass-independent: kGrip = 2 kW / m, kLift = kL / m, kDrag = kD / m.
[Serializable]
public class FlightSpec
{
    [Header("Speed")]
    public float cruiseSpeed = 5f;          // m/s at scale 1
    public float minSpeed = 3f, maxSpeed = 8f;

    [Header("Air plant")]
    [Tooltip("Per-wing grip rate kappa = kGrip x area x max(|v|, vGripMin) / scale (1/s). Grip fraction per sub-step = kappa h / (1 + kappa h); 1 = a planted foot.")]
    public float kGrip = 0.73333f;
    public float vGripMin = 3f;             // m/s at scale 1
    public float kLift = 0.02f;             // glide lift accel = kLift (1 - fold) v_fwd^2 / scale
    public float kDrag = 0.01f;             // parasitic drag accel = kDrag |v| v / scale
    public float upstrokeArea = 0.15f;      // feathered upstroke: area fraction (x (1 - fold))
    public float featherDeg = 8.6f;         // upstroke normal tilted back by this

    [Header("Controller")]
    public float climbGain = 0.6f;          // per metre of altitude error (/ scale)
    public float climbDamping = 0.5f;       // per m/s of vertical speed (/ sqrt(scale))
    public float climbIntegral = 0.8f;      // per metre-second of altitude error (Froude-scaled); 0 = PD only (the prototype)
    public float climbIntegralLimit = 0.3f; // cap on the integral's share of the climb demand
    public float thrustGain = 0.5f;         // per m/s of speed error (/ sqrt(scale))
    public float demandFloor = 0.25f;       // minimum stroke demand unless descending hard
    public float descendCutoff = -0.6f;     // climb demand below this drops the floor (glide down)
    public float glideDemand = 0.05f;       // below this the wings are held spread (glide)
    public float amplitudeMin = 0.3f;       // stroke amplitude (fraction of the comfort band) at zero demand
    public float strokeRateMin = 2f, strokeRateMax = 8f; // downstroke angular rate, rad/s at scale 1
    public float glideRelax = 6f;           // 1/s: held wings return to the rest angle

    [Header("Banking")]
    public float maxBankDeg = 35f;
    public float bankGain = 3f;           // stroke asymmetry per radian of bank error
    public float bankDamping = 0.5f;       // stroke asymmetry per rad/s of roll rate (x sqrt(scale))
    public float maxAsymmetry = 0.4f;       // left/right amplitude asymmetry cap
    public float rollGain = 0.7f;          // roll accel (rad/s^2) per m/s^2 of left-right push difference (x scale)
    public float rollDamping = 4f;          // 1/s: dihedral + aerodynamic roll damping (/ sqrt(scale))

    [Header("Phase B: launch, glide, landing, altitude")]
    public float glideAlphaDeg = 6f, flareAlphaDeg = 45f;
    public float launchSpeed = 3.5f;
    public float flareDistance = 3f;
    public float minAltitude = 1.5f;
    [Range(0f, 1f)] public float cruiseAltitudeFrac = 0.6f;

    [Header("Air chain")]
    public float airUndulationDeg = 14.3f;  // leader-direction yaw wave behind the girdle (flying snake)
    public float airUndulationHz = 1.2f;
    public float tailSag = 0.3f;            // m/s at the tail end (x sqrt(scale)), falls off toward the girdle
    public float headStabilise = 0.15f;     // s: time constant of the head target's 2nd-order low-pass (x sqrt(scale))
    [Tooltip("Share of the stance's neck shape the air chain keeps (for a species bound at its stance, CreatureSpecies.restAtSpacing - FlowBody.restAtStance): each neck joint's bend limit is taken around this share of its bend in the stance (CreatureSim.RestRun's layout), so a steeply raised neck lowers toward the flight line (the head target stays on it) by at most its bend limits and its skin never bends further from the bind than they allow. 0 = the shipped chain (the limits around a straight neck). The twisting chain (FlightSpec.twist, AirTwist) ignores it.")]
    [Range(0f, 1f)] public float airNeckStance;
    [Tooltip("Launching, the head turns with its rising neck (blended in by the head's air weight) instead of keeping the walking heading until the air takes over: a raised neck's front folded over the level head. Off = the shipped launch.")]
    public bool launchHeadOnNeck;
    [Tooltip("A touchdown on the ground starts the walking body at its stance (the girdle where it is, the other joints laid along the heading at their clearances, each segment spacing long: CreatureSim.RestRun's layout) instead of at the hovering pose, which the walking chain then hauled up to a steeply raised neck's clearances in a few ticks (the chest kinked, a neck segment tipped over). The eased touchdown carries the hovering body there. Off = the shipped touchdown.")]
    public bool touchdownAtStance;
    [Tooltip("Launching, the trunk and tail behind the girdle are handed to the air chain segment by segment: each segment's direction is turned from the walking body's toward the air chain's by its air weight, from the blended girdle back (exact lengths), instead of lerping the two bodies' joint positions. A tail curled from a circling walk (or standing after one) lies far from the air chain's tail trailing the vault; the position blend between them shortened segments to nothing and re-lengthening folded the tail back on itself (bends up to 178 deg). Off = the shipped launch.")]
    public bool launchTailBySegment;

    [Header("Obstacle avoidance (FlightAvoidance)")]
    [Tooltip("s at scale 1: predicted flight paths this far ahead are swept by rays against real geometry (bark, rocks) and the turn is changed early when one is blocked. 0 = off.")]
    public float avoidLookahead = 0f;
    [Tooltip("m at scale 1: clearance kept beyond the wing tips.")]
    public float avoidMargin = 0.35f;

    [Header("Trees (perching, climbing)")]
    [Tooltip("Perched on a branch or rock top, the feet close round it (CreatureLegs.Grip): rest points that find no support move in toward the spine until they are on the perch. Off = the walking rest points.")]
    public bool perchGrip = false;
    [Tooltip("m at scale 1: the thinnest branch chosen as a perch (its curvature radius; obstacle avoidance on).")]
    public float perchMinRadius = 0.06f;

    [Header("Steering")]
    [Tooltip("Turn rate per radian of heading error when steering to a point, / sqrt(scale) (0 = 0.9). A long, slow-rolling flyer needs more to line up a landing in its aviary.")]
    public float pursuitGain;
    public float PursuitGain => pursuitGain > 0f ? pursuitGain : 0.9f;

    [Header("Hind legs in the air")]
    [Tooltip("Tucked hind legs: the foot's direction from the hip in the girdle frame is (out x side, -down, -1); out 0 = 0.25. A larger out spreads the trailing legs into a rear lift surface (Microraptor, the dragon's finned legs).")]
    public float legTuckOut;
    [Tooltip("... its downward share (0 = 0.35): small = the legs trail level behind the body.")]
    public float legTuckDown;
    [Tooltip("... the foot at this fraction of the leg's reach (0 = 0.8): near 1 = the legs stretched straight back.")]
    public float legTuckReach;
    [Header("Short bodies")]
    [Tooltip("Touchdown eases the whole body onto the ground at once instead of hips and tail first: a short deep trunk (a pterosaur's), where the per-joint lag bent it 90 deg between neighbouring joints. Off = the shipped ease.")]
    public bool touchdownTogether;
    [Tooltip("Share of the flight path's climb or descent the trunk ignores in the air (0 = it trails the path, the shipped chain; 1 = the trunk stays level): a short-bodied flyer glides and flares with its body level instead of hanging along the path.")]
    [Range(0f, 1f)] public float airLevel;
    [Tooltip("Gliding, the descent's energy goes into the forward speed (the lift tilts forward down a sinking path): a soarer keeps its speed and lift down a long glide. Off = the shipped model (a glide only loses speed to drag).")]
    public bool glideEnergy;
    [Tooltip("With glideEnergy: on a landing approach a glide gains speed from its descent only up to the approach's speed goal, so its turns stay tight enough for a final (a fast glider circled its point inside the flare distance for minutes). Off = the shipped model.")]
    public bool approachBleedsSpeed;
    public float LegTuckOut => legTuckOut > 0f ? legTuckOut : 0.25f;
    public float LegTuckDown => legTuckDown > 0f ? legTuckDown : 0.35f;
    public float LegTuckReach => legTuckReach > 0f ? legTuckReach : 0.8f;

    // Twisting air chain (AirTwist, Chinese-dragon flight): the body follows the head's own path through the
    // air, which swims in 3D S-curves and corkscrews; the roll travels head to tail. Off (all zero) for every
    // species that does not set it (the kit's Fly.twist fills it at compose): the air chain above is unchanged.
    [NonSerialized] public AirTwistSpec twist;
    // Filled at compose by the kit's modules (0 = off; never serialized here): QuadLaunch's crouch (s at scale 1) and
    // its depth (m), the vault's upward speed (m/s at scale 1; 0 = the shipped 2.4), and SkyHunt's soaring height (m at
    // scale 1 over the ground: the aviary is made at least this tall, AviarySizing).
    [NonSerialized] public float launchCrouch, launchCrouchDepth, vaultSpeed, soarCeiling;
}
