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
    [Tooltip("The air chain's head target and trunk lead held steady and near the girdle: the head stabiliser low-passes only the vertical (the beat's bob) over headStabilise and the horizontal (turns) over at most 0.15 s, its smoothed girdle stays within a quarter of the neck's length of the real one, the target's direction at most 40 deg off the trunk's line; the trunk leads along the girdle's velocity low-passed over 0.15 s. Without it a long headStabilise trailed a turn by metres (Vesper: the target 14 m behind a 4.4 m neck) and the containment's cut of the outward speed swung the trunk up to 62 deg in one tick: the bend-limited neck flipped and the head jumped up to 7.45 m in one tick (the Apex 4.3 m, Hexadrake and WingedSerpent 0.7 m on a 0.5 m neck). Each neck joint's aim is also kept at least half a segment ahead of it (its sideways part faded out as it falls behind): a head target swept past the head, inside the neck's reach, left the bend cones to pick a side from a near-backward aim and the neck's front flipped side to side tick by tick (Vesper 0.5-0.7 m a tick). The twisting chain (AirTwist) ignores it. Off = the shipped air chain.")]
    public bool headStabiliseBounded;
    [Tooltip("The air chain's trunk (and its head target's direction) turns toward the flight direction at most 1.5 x the fastest coordinated turn (g tan maxBankDeg / minSpeed; Vesper: 2.6 rad/s at scale 4), above any turn the flight model flies. Only an impossible reversal is slowed: the containment's cut of the outward speed and the turn after it reversed the flight within ~0.3 s, the trunk followed in a few ticks and the head swung up to 3 m in one tick. A mitigation of that symptom; FlightSpec.containTurns removes the cause. The twisting chain (AirTwist) ignores it. Off = the shipped air chain.")]
    public bool trunkTurnBounded;
    [Tooltip("Share of the stance's neck shape the air chain keeps (for a species bound at its stance, CreatureSpecies.restAtSpacing - FlowBody.restAtStance): each neck joint's bend limit is taken around this share of its bend in the stance (CreatureSim.RestRun's layout), so a steeply raised neck lowers toward the flight line (the head target stays on it) by at most its bend limits and its skin never bends further from the bind than they allow. 0 = the shipped chain (the limits around a straight neck). The twisting chain (FlightSpec.twist, AirTwist) ignores it.")]
    [Range(0f, 1f)] public float airNeckStance;
    [Tooltip("Launching, the head turns with its rising neck (blended in by the head's air weight) instead of keeping the walking heading until the air takes over: a raised neck's front folded over the level head. Off = the shipped launch.")]
    public bool launchHeadOnNeck;
    [Tooltip("A touchdown on the ground starts the walking body at its stance (the girdle where it is, the other joints laid along the heading at their clearances, each segment spacing long: CreatureSim.RestRun's layout) instead of at the hovering pose, which the walking chain then hauled up to a steeply raised neck's clearances in a few ticks (the chest kinked, a neck segment tipped over). The eased touchdown carries the hovering body there. Off = the shipped touchdown.")]
    public bool touchdownAtStance;
    [Tooltip("Launching, the trunk and tail behind the girdle are handed to the air chain segment by segment: each segment's direction is turned from the walking body's toward the air chain's by its air weight, from the blended girdle back (exact lengths), instead of lerping the two bodies' joint positions. A tail curled from a circling walk (or standing after one) lies far from the air chain's tail trailing the vault; the position blend between them shortened segments to nothing and re-lengthening folded the tail back on itself (bends up to 178 deg). WARNING: it also changes the hips' and the hind limbs' frames at launch (the tail bones get transported frames) - on WingedSerpent the limb inversions in launch rose from 46 to 116 with it on; check a species' limbs (PoseSweep) before turning it on. Off = the shipped launch.")]
    public bool launchTailBySegment;
    [Tooltip("A touchdown's first frame shows the hovering body (where its eased blend starts), not the body the walking state restarts from. A touchdown begun on a tick's last sub-step showed that body for one tick - laid along a perch with the girdle on its point, or (touchdownAtStance) at the stance - and the blend carried it back the next tick: on a rock perch the girdle jumped 1.6-4.3 m there and back, on open ground Vesper's head 2.1 m. Through the touchdown the shown body also moves at most 3 body lengths per second x sqrt(scale) joint by joint (the girdle free): the together blend swung a tail held up behind the descending flyer down over a rock's edge through 120-180 deg in 0.6 of the touchdown, its tip 0.5-0.6 m a tick (Vesper; 0.05 L = 0.62 m). That cap is a mitigation - a velocity clamp on the shown joints (lengths restored from the girdle), not the cause's fix: the blend's slerp axis still flips where a segment's hover and walking directions are nearly opposite, the clamp only turns it into a fast sweep. Off = the shipped touchdown.")]
    public bool touchdownFromHover;
    [Tooltip("A perch landing asked for without an axis (CreatureFlight.Land(point, source): a rock's top) keeps the obstacle avoidance's perch tube (FlightAvoidance.SetPerch) along the heading it was approached on, as a branch perch keeps it along its axis. Without it the tube was cleared every sub-step (the axis is zero) and the rock's top beyond 0.9 m x s of the landing point could turn the final away. Off = the shipped avoidance.")]
    public bool perchTubeOnApproach;
    [Tooltip("Perched, a run of joints hanging past the edge of the support (CreatureFlight.Drape: a tail over a rock's edge) pitches down in each segment's own vertical plane (its walked heading) instead of all in the plane of the one segment before the edge. That plane was taken afresh every sub-step: when the edge moved to another joint, or that segment stood nearly vertical over the edge (its heading flipping), the whole hanging run swung about the edge - on a rock perch Vesper's tail tip whipped 5.7-7.3 m in one tick. The joints past the edge (hanging, lying on the ground again past the hanging run, or supported again after hanging) also move at most 3 body lengths per second x sqrt(scale) a sub-step until they are settled: the edge or the run's end moving to another joint relaid the whole run behind it at once (over a rock's corner the tail 0.5-1.4 m in a tick). That cap is a mitigation - a velocity clamp on the draped joints, not the cause's fix: the drape is still not continuous in where its edge falls, the clamp only turns an edge change into a fast sweep. Off = the shipped drape.")]
    public bool drapeOwnHeading;
    [Tooltip("Perched and standing (a perch touchdown, or on the perch with Halt), the head and neck held out past the support's edge keep the support's ground (each joint ahead of the girdle whose ground falls away is raised to the ground of the joint behind it) instead of being grounded on the floor below. Without it the head came down to the floor, the length restore dragged the neck after it, the neck's probes then took the floor rather than the rock's top, and within about a second the whole body sank into the rock (Vesper's girdle from 4.07 to 1.72 m on a 2 m rock). Off = the shipped perch.")]
    public bool perchFrontLevel;
    [Tooltip("In the air a rise of the ground floor itself (the girdle is kept its clearance above the surface below it; that surface jumped up) lifts the girdle at most 4 m/s x sqrt(scale) instead of at once; the girdle's own descent onto the floor is still stopped at once. Flown low past a rock's edge (a launch beside it, a low pass), the surface below jumps by the rock's height and the girdle was snapped up by it in one sub-step: Vesper's whole body 1.4-1.5 m in one tick. Off = the shipped floor.")]
    public bool floorLiftEased;
    [Tooltip("Near the containment's safe radius an outward flight is turned along the wall at the coordinated turn rate (g tan maxBankDeg / v), begun when the radial room left is what that turn needs to come round to the tangent; the speed is kept, only its direction turns. Without it the containment cut the outward speed (up to 90 deg in one sub-step) and the wall turn reversed what was left within ~0.3 s, far tighter than the flyer's turn: the body, following the velocity, flew sideways or tail first for seconds (Vesper: +z 5 m/s to -z 3.4 m/s). Off = the shipped containment.")]
    public bool containTurns;

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
