#!/usr/bin/env python3
"""Vesper: species generator (Vesper step 1 - the skeleton only; rev 2: the humpbacked proportions).

Vesper is the Apex's animal (docs/superpowers/specs/assets/2026-10-20-apex-anatomy.jpg, -reference-hires.jpg) built
skeleton-first (docs/superpowers/specs/2026-10-21-vesper-skeleton-design.md): ONE FlowBody chain from the head's tip to
the tail's tip (the head = its first four joints, stiff but jointed), Leg2Bone wing arms (a short upper arm, a very long
forearm to the ground) carrying DigitWing wings (four long jointed finger chains + a forward thumb claw), DrakeLeg
digitigrade hind legs, Eyes. No skin: the SkeletonOnly module (F3 shows the skeleton; the per-joint radii below are the
future skin's).

Rev 2 (docs/superpowers/specs/2026-10-21-vesper-proportions-design.md): high hips, the hump the highest point, the neck
nearly level to a short head (closer joints: the profile's `spacing` channel), a very long tail reaching the ground, a long
metatarsus, the knuckles planted ahead of the chest and out wide, long strides. NOT a pterosaur (the spec's anti-goal list).

THE NUMBERS ARE FOR TUNING: every number below is at scale 1 in metres (lengths x SCALE in the game, times x sqrt SCALE).
Change one, run this file, and the asset is rewritten (editing Config/Creatures/Vesper.asset in play mode respawns
Vesper too, but this file is the source - an asset edit is lost the next time it runs).

Run it:  python Vesper.py <Assets/TerrainGen>    (writes Config/Creatures/Vesper.asset; its .meta and guid are kept)
then     creature_kit.py check Vesper            and   creature_kit.py test CreatureVesperTests
"""
import os
import sys

# kityaml (the creature-kit skill's scripts): CREATURE_KIT_SCRIPTS, else the project's .claude/skills above this tree
# (Assets/TerrainGen/Tools~/SpeciesGen -> the project root; a worktree without .claude: set CREATURE_KIT_SCRIPTS)
_here = os.path.dirname(os.path.abspath(__file__))
for _d in [os.environ.get('CREATURE_KIT_SCRIPTS'),
           os.path.join(_here, '..', '..', '..', '..', '.claude', 'skills', 'creature-kit', 'scripts'),
           os.path.join(_here, '..', '..', '..', '..', '..', '..', 'MarchingCubes', '.claude', 'skills', 'creature-kit', 'scripts')]:
    if _d and os.path.isfile(os.path.join(_d, 'kityaml.py')):
        sys.path.insert(0, _d)
        break
import kityaml as K  # noqa: E402

SCALE = 4.0                 # the whole animal x this: ~12.4 m from the snout to the tail tip

# ================================================================ the spine: ONE chain, head tip .. tail tip (FlowBody)
# Rev 2 (docs/superpowers/specs/2026-10-21-vesper-proportions-design.md; the user: "the backside sits high, there is a
# hump, and then it is quite flat to the head"): a humpbacked build - high hips, the hump the highest point just behind
# the shoulders, the neck to the head nearly level, a short head and neck by CLOSER joints (not fewer: per-joint spacing,
# the profile's `spacing` channel), a very long tail reaching the ground. NOT a pterosaur (the spec's anti-goal list):
# never high shoulders with the back sloping to low hips and the head the peak.
#
#   part                       joints   spacing   length
#   head (snout .. skull)      0..3     0.05      ~0.2 with the cap
#   neck                       4..10    0.06      ~0.42
#   trunk (shoulder..hump..hips) 11..16 0.1       ~0.6
#   tail                       17..37   0.1       ~2.2 with the tip (over half the animal)
N = 38                      # joints, head to tail
SPACING = 0.1               # metres between joints where a key gives no spacing of its own (the trunk and tail)
HEAD_CAP = 0.06             # the chain closes this far ahead of joint 0 (the snout's tip)
TAIL_TIP = 0.14             # the tail's last bit behind the last joint
HEAD_JOINTS = 4             # joints 0..3 are the head: stiff (HEAD_BEND) but jointed - not a rigid block
HEAD_BEND = 5               # deg: the head joints' bend limit (cone half-angle; the tests demand <= 6)
SHOULDER = 11               # the chest joint: the wing arms hang here (and the flight's girdle); the neck is 4..10
HIPS = 16                   # the hind legs hang here (the trunk is 11..16: shoulder, hump, hips)
TAIL_ROOT = 17              # the tail starts here: 21 joints + the tip


def at(joint=0, offset=(0, 0, 0)):
    return {'joint': joint, 'side': 0, 'offset': K.V(*offset), 'slot': 0}


def key(j, half_width, half_height, height, bend, spacing):
    """A profile key at joint j (fractional: between joints): the joint's radii (half width, half height - the future
    skin), its height above the ground when standing (metres), its bend limit (deg), and the segment length here
    (metres; each segment takes the curve's value at its midpoint, j + 0.5 for the segment j -> j + 1). Joints and
    segments between keys are interpolated smoothly (a monotone cubic: no overshoot)."""
    return {'t': round(j / (N - 1), 6), 'halfWidth': half_width, 'halfHeight': half_height, 'clearance': height,
            'bendLimitDeg': bend, 'spacing': spacing,
            'hump': 0, 'keel': 0, 'flank': 0, 'blade': 0, 'keelForward': 0}


# The stance: the head carried forward at about the shoulders' height and level (~0.86-0.88), the neck nearly flat, the
# trunk rising from the shoulders (0.85) to the hump (~0.92, joints 12-14) and falling to the high hips (0.80), the long
# tail sweeping down to the ground (its tip ~0.08). The spacing: 0.05 in the head, 0.06 in the neck, then ramped up to
# the trunk's 0.1 over the neck's base and the chest (0.062 -> 0.0765 -> 0.094 -> 0.1: neighbours at most 1.25 x apart,
# FlowBody.MaxSpacingStep - no visible kink), the keys at those segments' midpoints (j + 0.5).
PROFILE = [
    #   joint              width  height standing  bend limit spacing
    key(0,                 0.025, 0.030, 0.86 , HEAD_BEND, 0.05  ),  # the snout's tip
    key(HEAD_JOINTS - 1,   0.045, 0.055, 0.88 , HEAD_BEND, 0.05  ),  # the back of the skull (the head's last joint)
    key(HEAD_JOINTS + 0.5, 0.040, 0.048, 0.88 , 14       , 0.058 ),  # the upper neck: slim and supple
    key(SHOULDER - 1.5,    0.060, 0.075, 0.855, 12       , 0.062 ),  # the lower neck widening into the chest
    key(SHOULDER - 0.5,    0.090, 0.110, 0.85 , 11       , 0.0765),  # the neck's base (the spacing ramp)
    key(SHOULDER,          0.120, 0.140, 0.85 , 10       , 0.085 ),  # the chest / shoulders (the widest point)
    key(SHOULDER + 0.5,    0.120, 0.142, 0.87 , 10       , 0.094 ),  # (the spacing ramp's last step)
    key(SHOULDER + 2,      0.115, 0.135, 0.92 , 10       , 0.1   ),  # the hump: the highest point, just behind the shoulders
    key(HIPS,              0.100, 0.110, 0.80 , 12       , 0.1   ),  # the high hips
    key(TAIL_ROOT + 2,     0.075, 0.090, 0.64 , 18       , 0.1   ),  # the tail root: long and thick
    key(26,                0.045, 0.055, 0.30 , 19       , 0.1   ),  # the tail sweeping down
    key(N - 1,             0.012, 0.020, 0.08 , 20       , 0.1   ),  # the tail's tip on the ground
]

sp = K.Species('Vesper', scale=SCALE)

sp.add_part('FlowBody', {
    'id': 'body', 'at': at(), 'mirror': 0,
    'spacing': SPACING, 'headCapLength': HEAD_CAP, 'tailTipLength': TAIL_TIP, 'jointCount': N,
    'profile': PROFILE,
    'rigidHeadJoints': 0,   # off: the head is the chain's stiff front (HEAD_BEND), not one rigid block
    'humpSharpness': 8, 'keelSharpness': 6, 'bladeExponent': 4, 'ringSegments': 24, 'ringsPerSegment': 3,
    'tailColourFrom': 1, 'dorsalStripeTo': 0, 'dorsalStripeWidthDeg': 30, 'tailKind': 'tailfan',
    'dorsalStripeKind': 'dorsalstripe', 'ovalMaxAspect': 2,
    'restAtStance': 1,      # bound as it stands (the hump, the high hips, the tail sweeping down)
})

# ================================================================ the wing arms (walked on, folded) - Leg2Bone
# The anatomy picture: a short upper arm, a very long forearm to the ground. Rev 2: the knuckles carry all the front
# weight, planted well AHEAD of the shoulders and out to each side (the chest hangs between the arms: a heavy, broad,
# low-slung front - not a pterosaur's upright narrow stance), the elbow up and back (pole). The forearm grew with the
# shoulders' height (0.52 -> 0.85) so it reaches the ground ahead: 0.9 (the spec's "~0.8"), and the socket sits 0.12 below
# the shoulder joint on the chest's side (0.02 above it in step 1). The planted knuckles' range along the body is what the
# reach leaves ahead of the socket; with the forearm 0.8 and the socket at -0.06 the knuckles could plant at most ~0.47
# ahead (91 % of the reach), too little for knuckles ~0.45 ahead on average AND long strides around them; 0.9 and -0.12
# give ~0.70 (CreatureVesperTests: knuckles 0.46 ahead, 0.38 out; strides 0.55).
ARM_UPPER = 0.25            # upper arm (shoulder -> elbow)
ARM_FOREARM = 0.9           # forearm (elbow -> wrist, which is the foot it walks on)
sp.add_part('Leg2Bone', {
    'id': 'arm', 'at': at(SHOULDER, (0.1, -0.12, 0)), 'mirror': 1,
    'role': 0, 'gaitGroup': -1,
    'restOffset': K.V(0.38, 0, 0.6),    # where the planted knuckles rest from the shoulder joint (out, up, forward)
    # the walking step: long strides (step 1: threshold 0.14, 0.3 s, lift 0.06, strides 0.25 m) - the knuckles lift 0.32
    # behind their rest and land a little ahead of it (overshoot). Scanned with the hind legs' (Vesper_Strides_Longer,
    # m at scale 1, arms / legs): rest z 0.45 + no overshoot 0.55 / 0.28 (knuckles 0.25 ahead); z 0.55 0.58 / 0.61 (0.40
    # ahead); z 0.6 + overshoot 0.08 0.55 / 0.59 (0.46 ahead: chosen); z 0.65 0.44 / 0.52 (the reach's end throttles it)
    'stepThreshold': 0.32, 'stepDuration': 0.45, 'stepLift': 0.1, 'overshoot': 0.08,
    'footLength': 0, 'toeCount': 0,
    'forwardSwingLimitDeg': 0, 'backSwingLimitDeg': 0, 'kneeRangeDeg': 0, 'kneeFlexDeg': 0,
    'upperLength': ARM_UPPER, 'lowerLength': ARM_FOREARM,
    'upperRadius': 0.04, 'lowerRadius': 0.03,   # joint radii (the future skin)
    'pole': K.V(0.2, 1, -0.4),                  # which way the elbow points (out, up, back)
    'kneeBlend': 0,
    'ankleHeight': 0, 'clawLength': 0, 'clawCurveDeg': 0, 'toeWeb': 0, 'digitigrade': 0, 'thighMass': 0, 'scutes': 0,
    'spurLength': 0,
})

# ================================================================ the hind legs (digitigrade) - DrakeLeg
# Rev 2: the high hips (0.80) stand on long legs - a longer thigh and shin and a much longer metatarsus (the hock raised
# 0.30 off the toes); the foot plants under the hip (0.1 ahead of the hip joint: its stride runs from ~0.3 ahead to ~0.3
# behind). The socket sits 0.1 below the hip joint (0.05 in step 1): the leg's reach along the body grows with the drop
# it does not have to cover (the stride).
sp.add_part('DrakeLeg', {
    'id': 'hind', 'at': at(HIPS, (0.08, -0.1, 0)), 'mirror': 1,
    'role': 0, 'gaitGroup': -1,
    'restOffset': K.V(0.18, 0, 0.1),        # where the foot rests from the hip (out, up, forward): under the hip
    # the walking step: long strides (step 1: threshold 0.16, 0.25 s, lift 0.06, strides 0.25 m); it lands well ahead
    # (overshoot 0.2) since the stride's back end is the thigh's swing limit
    'stepThreshold': 0.4, 'stepDuration': 0.45, 'stepLift': 0.1, 'overshoot': 0.2,
    'footLength': 0.12, 'toeCount': 0,
    # the joint limits: the hump falls to the hips, so the hip joint's frame is pitched ~31 deg nose-up and the thigh's
    # rest direction measured in it is already ~39 deg off the flat reference; a 0.3 m stride behind the rest swings it
    # ~60 deg more. At the default 55 deg the swing limit lifted every hind foot ~0.16 behind its rest (strides 0.28 m;
    # back 65, the foot resting 0.05 ahead: 0.34); back 90: 0.59 (chosen; the foot lifts ~0.31 behind, the limb at 85-90 % of its reach); the
    # knee deviates up to ~43 deg there (the default 35 lifted it early too: 60)
    'forwardSwingLimitDeg': 0, 'backSwingLimitDeg': 90, 'kneeRangeDeg': 60, 'kneeFlexDeg': 0,
    'upperLength': 0.28, 'lowerLength': 0.32,    # thigh, shin
    'upperRadius': 0.034, 'lowerRadius': 0.02,
    'pole': K.V(0.3, 0.2, 0.9),                  # the knee points forward and out
    'ankleHeight': 0.30,                         # the raised hock (digitigrade: the long metatarsus)
    # the gait's limb ends on the hock (DrakeLeg.walkOnHock -> LegSpec.hockInReach): the hip socket stands 0.7 high,
    # more than thigh + shin (0.6); off, the gait (which read the limb ending on the toes) could not reach the ground -
    # the hips drooped to 0.66 and the planted feet slid (CreatureVesperTests.Red_Vesper_HockNotInReach)
    'walkOnHock': 1,
    'toes': 3, 'hallux': 1, 'talonLength': 0, 'talonCurveDeg': 110, 'haunch': 1.5,
})

# ================================================================ the wings: four finger chains + a thumb claw (DigitWing)
# The wing IS its fingers: each a jointed chain from the wrist, the four spanning the wing (the membrane comes later,
# between them). DigitWing's own proven defaults (Task 2) - the leading finger longest.
FINGER_LENGTHS = [1.5, 1.3, 1.1, 0.9]        # metres, leading finger first
FINGER_SPREAD = [20, 42, 64, 86]             # deg behind the forearm, wing spread (the fan)
# deg, folded on the ground (back along the forearm). With the raised neck the chest pitches up, and DigitWing's default
# fold (160 .. 167.5) with lift 68 leaned the folded fingers in so far that the tips crossed over the back (a left tip
# 494 mm, scale 1, right of the spine). Scanned (least tip distance on its own side / left-right tip gap, mm at scale 1;
# CreatureVesperTests.Vesper_FoldedWingsStaySeparate wants >= 0 / >= 120): fold 160 lift 68: -494 / -744; lift 82 / 88
# alone: -365 / -311; fold 140 + lift 82: -143 / 65; fold 130 + lift 82: 2 / 363; fold 120 + lift 82: 118 / 520 (chosen);
# fold 125 + lift 88: 149 / 613. Moving the elbow (pole x 0 / 0.5) or the wrist (restOffset x 0 / 0.15) only made it worse.
# Rev 2 (the knuckles 0.6 ahead and 0.38 out, the forearm 0.9, the shoulders 0.85 and a hump behind them): the step-1
# fold 120 + lift 82 crossed again (-1073 / -136: a left tip 679 mm across the spine, walking) - the fingers stood up off
# a forearm that now leans forward from the shoulder, so standing up steeply tipped them in over the back. Rescanned
# (the same measures): fold 120 / 140 / 160 + lift 70: -826 / 223, -697 / -10, -411 / -124; + lift 55: -427 / 656,
# -351 / 368, -183 / 124; + lift 40: 32 / 1082, 44 / 751, 38 / 320; lift 45 with fold 110 / 120 / 130: -125 / 1073,
# -125 / 943, -113 / 791; lift 35: 149 / 1300, 147 / 1174 (chosen: fold 120 + lift 35), 142 / 1015; lift 25: 150 / 1235,
# 148 / 1116, 145 / 968. The fingers lie back along the forearm, standing up 35 deg, their tips behind the shoulders.
FINGER_FOLD = [120, 122.5, 125, 127.5]
# The landing flare's pose (deg; CreatureVesperTests.Vesper_LandingFlare_*): in the last 0.3 s x sqrt s before the
# touchdown the fan >= 0.9 x its cruise glide width, the wrist ahead of the shoulder, the hand >= 30 deg nose-up to the path.
# Measured (land seeds 1-3, the rock perch): the fan 81-85 deg (1.23-1.29 x the glide's 66), the wrist 0.9-1.4 m ahead,
# the drawn fingers 49-50 deg nose-up to the smoothed path (the cupped finger roots read ~5 deg under the hand's 55). Pitch 50 left 36 deg on
# land seed 1 (the margin wants >= 37.5). The hand is pitched to the descent's path (the velocity low-passed over 0.3 s x
# sqrt s, about one braking stroke): pitched on the body it stood 1-25 deg to the path (the braking strokes swing the
# velocity +-40 deg a beat); pitched to the raw velocity the held hand rocked with every stroke (up to 627 deg/s x sqrt s;
# 122-213 smoothed, the bound 300). Measured against the smoothed path: 49-50 deg. The blend in (CreatureWings, 0.15 s x sqrt s): 0.2 left a 0.82 s flare (land seed 2) half blended at the window's
# start (36 deg); 0.12 moved a finger tip 1.27 x its cruise step (Vesper_LandingFlare_NoPop wants <= 1.2); 0.15: 1.16.
FLARE_SPREAD = 20           # the fan widened: the trailing finger this much further back (66 -> 86 deg leading to trailing)
FLARE_SWEEP = 35            # the wrist swept forward about the girdle's up (ahead of the shoulder)
FLARE_RAISE = 20            # the wings held raised (stroke angle up)
FLARE_PITCH = 55            # the hand pitched nose-up to the flight path (the air brake)
FLARE_CUP = 8               # each finger joint cups up toward this (deg; softly saturated at flexDeg)
sp.add_part('DigitWing', {
    'id': 'wing', 'at': at(SHOULDER), 'mirror': 1, 'arm': 'arm',
    'spec': {
        'leg': 0,
        # the wrist's rest in the spread glide pose (out, up, forward), from the shoulder joint. Rev 2 lowered the arm's
        # socket 0.14 (0.02 -> -0.12, the knuckles' reach ahead) and the wrist with it, so the wing's stroke circle is the
        # step-1 one (wrist - socket = (0.5, 0.04, 0.04)): left at 0.06 up the circle tilted up (its dihedral 4 -> 20 deg),
        # each downstroke's rise pushed harder and the cruise hung at the glide/beat boundary (one beat in 20 s x sqrt s,
        # Vesper_Bob). The longer arm (1.15 m) is folded more at the elbow in the spread pose.
        'restTip': K.V(0.6, -0.08, 0.04),
        'comfortUp': 0.5, 'comfortDown': 0.6, 'strokePlaneDeg': 10, 'pronationDeg': 18, 'overshootUp': 0.08,
        'upstrokeDuration': 0.3, 'upstrokeFold': 0.5, 'foldReach': 0.3, 'beatGroup': 0,
        'fingerLengths': FINGER_LENGTHS, 'fingerSpreadDeg': FINGER_SPREAD, 'fingerFoldDeg': FINGER_FOLD,
        'fingerMinGapDeg': 2, 'fingerFoldGapDeg': 3.5,
        'groundLiftDeg': 35,                 # folded: how steeply the fingers stand up off the forearm (step 1: the Apex's 82; see FINGER_FOLD)
        'groundTallStance': 1, 'flankPatches': 6, 'groundWristLift': 0.08,
        'thumbPlant': 1,                     # it walks on the thumb claw's tip
        'fanRearRollsWithFan': 1, 'groundFlankRise': 0.6,
        'fingerRadius': 0.014, 'thumbLength': 0.08,
        # the (future) membrane's body span: ~0.2 m behind the shoulders to ~0.3 m ahead (the neck's closer joints: 5)
        'membraneRearJoint': SHOULDER + 2, 'membraneFrontJoint': SHOULDER - 5,
        'trailingRib': 0.5, 'membraneInset': 0.8,
        'area': 3,                           # aerodynamic wing area factor (lift)
        'scallop': 0, 'scallopByChord': 0, 'membraneBillow': 0, 'membraneDetail': 0, 'fingerKnuckles': 0,
        'wristKnuckle': 0, 'armMuscle': 0, 'forearmTwin': 0, 'thumbRadius': 0,
        # the landing flare (WingSpec.landingFlare, task 3; the user: "make the wings flare when landing"): as the flight's
        # Flare mode runs the drawn wings leave the beat for an air brake - held spread, raised, the wrist swept forward
        # ahead of the shoulder, the four fingers fanned wider and the whole hand pitched nose-up, the fingers cupped
        # (flareCupDeg below) - a bat's / a manta's broad cupped spread of four graded fingers, NOT a pterosaur's single
        # swept spar. Only the drawn pose: the strokes that brake and land it are the flight's, unchanged. Blended in over
        # 0.15 s x sqrt s of the flare, out over the touchdown's first 0.25 s x sqrt s (or 0.15 after a go-around). FLARE_*
        # above.
        'landingFlare': 1,
        'flareSpreadDeg': FLARE_SPREAD, 'flareSweepDeg': FLARE_SWEEP, 'flareRaiseDeg': FLARE_RAISE, 'flarePitchDeg': FLARE_PITCH,
    },
    'fingerSegments': 4,                       # bones per finger
    'fingerRadius': [0.018, 0.016, 0.014, 0.012],   # each finger's root joint radius (the future skin)
    'fingerTaper': 0.4,                        # the tip's radius as a fraction of the root's
    'flexDeg': 10,                             # air: the most the beat bends a finger joint
    'flexHz': 2, 'flexDamping': 0.5,           # air flex / ground droop: how fast and how springy the fingers follow
    'droopDeg': 8,                             # ground: how far gravity droops a folded finger joint
    'maxBendDeg': 25,                          # no finger joint ever bends further
    'thumbLength': 0.08, 'thumbCurlDeg': 40, 'thumbSegments': 2,   # the forward thumb claw
    'flareCupDeg': FLARE_CUP,                  # the landing flare: the fingers cup up (the air under the pitched-up hand)
})

# ================================================================ the eyes (on the head's middle joint)
sp.add_part('Eyes', {'id': 'eyes', 'at': at(1, (0.035, 0.025, 0)), 'mirror': 0, 'radius': 0.011, 'cluster': []})

# ================================================================ modules
sp.add_module('Walk', {
    'speed': 0.45,                          # walking speed (m/s at scale 1)
    'turnRateDeg': 60, 'accel': 1.5, 'turnSlowdown': 0.5,
    'gaitSwayDeg': 0, 'gaitRollDeg': 0, 'subSteps': 2, 'pattern': 1, 'waveGroups': 4, 'waveTailToHead': 0,
    'bob': {'amplitude': 0, 'hz': 0, 'damping': 0},
    'burst': {'factor': 0, 'every': 0, 'seconds': 0},
    'travel': 0, 'forwardFactor': 0,
})

# The flight: the Apex's proven heavy flyer (Tools~/SpeciesGen/Apex.py, its task-10 report). It must beat to stay up
# (weak glide lift kLift 0.1), so the body heaves with every stroke - the travelling bob; it glides descending and on the
# approach. airLevel 0.8 keeps the trunk from pitching each beat (the head rides steadier than the girdle). The four
# stance-bound flags (airNeckStance .. launchTailBySegment) keep the raised neck and the tail from flipping in launch and
# touchdown.
sp.add_module('Fly', {
    'spec': {
        'cruiseSpeed': 3, 'minSpeed': 2, 'maxSpeed': 6,         # m/s at scale 1
        'kGrip': 1, 'vGripMin': 2.2,                            # how hard a beat pushes
        'kLift': 0.1,                                           # glide lift: low = it must beat (the bob)
        'kDrag': 0.01,
        'upstrokeArea': 0.15, 'featherDeg': 8.6,
        'climbGain': 0.6, 'climbDamping': 0.5, 'climbIntegral': 0.8, 'climbIntegralLimit': 0.3,
        'thrustGain': 0.5, 'demandFloor': 0.12, 'descendCutoff': -0.3, 'glideDemand': 0.14,
        # the weakest beat's amplitude. Rev 2 (0.4 -> 0.3): the neck is 0.64 m, not 1.1, and the head stabiliser keeps
        # the head within a quarter of the neck's length of the girdle (AirChain.kMaxOffset); at 0.4 the girdle bobbed
        # 1.99 m p-p (14.5 % of the body) and the head 0.47 x that (Vesper_Bob: <= 1/3; headStabilise 1.2 .. 2.5 changed
        # nothing: 0.45-0.47; a faster beat - strokeRateMin 2 / 2.5 / 3 - glided instead: 0 beats). 0.3: girdle 1.31 m
        # (9.6 %), head 0.18 x; 0.25: 9.3 %, 0.15 x; 0.2 never held its cruise altitude
        'amplitudeMin': 0.3,
        'strokeRateMin': 1.5, 'strokeRateMax': 10,              # wingbeat rate range (Hz at scale 1)
        'glideRelax': 6,
        'maxBankDeg': 35, 'bankGain': 3, 'bankDamping': 0.5, 'maxAsymmetry': 0.4, 'rollGain': 0.7, 'rollDamping': 4,
        'glideAlphaDeg': 6, 'flareAlphaDeg': 45,
        'launchSpeed': 2.5, 'flareDistance': 1.6, 'minAltitude': 1.2, 'cruiseAltitudeFrac': 0.6,
        'airUndulationDeg': 0, 'airUndulationHz': 0, 'tailSag': 0,
        # the head's steadiness in the air: the time (s) over which its target smooths out the girdle's bob. Vesper's beat
        # is slower than the Apex's (period ~1.3 s at scale 1) and its 0.6 left the head bobbing 0.46 x the girdle;
        # scanned 0.6 / 1.0 / 1.2 / 1.5: 0.46 / 0.32 / 0.29 / 0.30 (the test: <= 1/3). Higher = steadier but laggier in turns.
        # If the head lags in turns, the cheaper lever is a faster beat (raise strokeRateMin): the low-pass then filters
        # a shorter bob period, and headStabilise can come back down toward 0.6.
        'headStabilise': 1.2,
        # the stabiliser held near the girdle (FlightSpec.headStabiliseBounded): only the bob is filtered over
        # headStabilise, the turns over 0.15 s, the trunk's lead direction too. Off, the long filter trailed every turn
        # by metres (the target 14 m behind a 4.4 m neck) and the containment's cut of the outward speed swung the trunk
        # up to 62 deg in a tick: the bend-limited neck flipped and the head jumped up to 7.45 m in one tick
        # (CreatureVesperTests.Vesper_Flight_NoHeadJump).
        # (task F: it also keeps each neck joint's aim half a segment ahead - a head target swept past the head made
        # the neck's front flip side to side, 0.5-0.7 m a tick)
        'headStabiliseBounded': 1,
        # the trunk turning at most 1.5 x the fastest coordinated turn (FlightSpec.trunkTurnBounded): only the impossible
        # reversal at the wall is slowed - the head's swing there 3 m -> 0.5 m in a tick (Vesper_Aviary_NoHeadPop)
        'trunkTurnBounded': 1,
        'airNeckStance': 0.3,
        'launchHeadOnNeck': 1, 'touchdownAtStance': 1, 'launchTailBySegment': 1,
        # a touchdown's first frame is the hovering body (FlightSpec.touchdownFromHover): begun on a tick's last sub-step
        # the walking restart's body was shown for a tick - on a rock perch the girdle jumped 1.6-2.9 m there and back,
        # on open ground the head 2.1 m (CreatureVesperTests.Vesper_Touchdown_NoGirdleJump)
        # (task F: with it the touchdown's shown body and, with drapeOwnHeading, the draped run move at most 3 body
        # lengths per second x sqrt s - the tail swung down over a rock's edge 0.5-1.2 m a tick)
        'touchdownFromHover': 1,
        # a rock perch (CommandBrain's Land without an axis) keeps the avoidance's perch tube along its approach heading
        # (FlightSpec.perchTubeOnApproach); off, the tube was cleared every sub-step and the rock could turn the final away
        'perchTubeOnApproach': 1,
        # perched on a rock (task E): the tail hanging over an edge pitches down in each segment's own vertical plane
        # (FlightSpec.drapeOwnHeading; off, the whole hanging run was laid in the plane of the one segment before the
        # edge and swung about it when the edge moved: the tail tip whipped 5.7-7.3 m in a tick), and standing, the head
        # and neck held out past the edge keep the rock's ground (FlightSpec.perchFrontLevel; off, the head was grounded
        # on the floor, the neck followed and the body sank into the rock: the girdle from 4.07 to 1.72 m)
        # (CreatureVesperTests.Vesper_Touchdown_NoGirdleJump, Vesper_Perched_StaysOnTheRock)
        # (rev 2, FlightSpec.drapeCapKeepsLength: the drape's step cap keeps the hanging run's lengths - the long tail,
        # hanging from the rock's edge to the floor, whipped its tip 0.75-0.96 m in a tick as the edge moved a joint:
        # the length restore after the drape relaid the capped run; 0.36-0.53 m with it. Red_Vesper_DrapeCapLength)
        'drapeOwnHeading': 1, 'perchFrontLevel': 1, 'drapeCapKeepsLength': 1,
        # flown low past a rock's edge (a launch beside it), the air floor lifts the girdle at most 4 m/s x sqrt s
        # (FlightSpec.floorLiftEased); off, it snapped up by the rock's height in one sub-step: 1.4-1.5 m in a tick
        # (CreatureVesperTests.Vesper_LowOverRock_NoGirdleJump)
        'floorLiftEased': 1,
        # the containment turning the flight along the wall instead of cutting its outward speed (FlightSpec.containTurns)
        # is the cure for the wall's sideways flight and head swing (Vesper_Aviary_ContainTurns), but NOT ON YET: the
        # command brain's perch line-up and open-ground landing lean on the cut's instant reversal (perched 43 -> 34 of 48
        # in VesperLab's aviary; 4-10 CommandBrain tests fail with it on) - a CommandBrain perch line-up rework first
        # (follow-up). Meanwhile trunkTurnBounded mitigates the head swing
        'containTurns': 0,
        'avoidLookahead': 2.4, 'avoidMargin': 0.35, 'perchGrip': 0, 'perchMinRadius': 0.06, 'pursuitGain': 0,
        'legTuckOut': 0, 'legTuckDown': 0, 'legTuckReach': 0,
        'touchdownTogether': 1,
        'airLevel': 0.8,                                        # 0 = the trunk pitches with each beat, 1 = level
        'glideEnergy': 0, 'approachBleedsSpeed': 0,
    },
    'twist': {
        'swimAmplitude': 0, 'swimHz': 0, 'swimVertical': 0, 'swimRollDeg': 0,
        'corkscrewAmplitude': 0, 'corkscrewHz': 0, 'corkscrewTurns': 0, 'corkscrewRolls': 0,
        'barrelRadius': 0, 'barrelTime': 0, 'loopRadius': 0, 'loopSpin': 0, 'slowRollTime': 0,
        'intervalMin': 0, 'intervalMax': 0, 'uprightRateDeg': 0,
        'girdleBob': 0, 'bobHead': 0, 'bobTail': 0, 'bobInPhase': 0, 'bobHighPass': 0,
    },
})

# the launch off all fours: a crouch, then a vault off the folded wing arms
sp.add_module('QuadLaunch', {'crouchSeconds': 0.4, 'crouchDepth': 0.12, 'vaultSpeed': 4})
sp.add_module('Wander', {'arriveRadius': 0, 'pauseMin': 0, 'pauseMax': 0})
sp.add_module('FlightExcursion', None)   # its own brain (FlightBrain) until the command brain (Vesper step 1, task 4)
sp.add_module('SkeletonOnly', None)      # no skin yet: nothing drawn, F3 shows the skeleton


if __name__ == '__main__':
    tg = sys.argv[1] if len(sys.argv) > 1 else os.environ.get('CREATURE_TG')
    if not tg:
        sys.exit('usage: %s <Assets/TerrainGen> (or set CREATURE_TG)' % sys.argv[0])
    print('wrote', sp.save(os.path.join(tg, 'Config', 'Creatures', sp.name + '.asset')))
