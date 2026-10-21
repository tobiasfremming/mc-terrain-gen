#!/usr/bin/env python3
"""Vesper: species generator (Vesper step 1 - the skeleton only).

Vesper is the Apex's animal (docs/superpowers/specs/assets/2026-10-20-apex-anatomy.jpg, -reference-hires.jpg) built
skeleton-first (docs/superpowers/specs/2026-10-21-vesper-skeleton-design.md): ONE FlowBody chain from the head's tip to
the tail's tip (the head = its first four joints, stiff but jointed), Leg2Bone wing arms (a short upper arm, a very long
forearm to the ground) carrying DigitWing wings (four long jointed finger chains + a forward thumb claw), DrakeLeg
digitigrade hind legs, Eyes. No skin: the SkeletonOnly module (F3 shows the skeleton; the per-joint radii below are the
future skin's).

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
N = 30                      # joints, head to tail
SPACING = 0.1               # metres between joints (the same everywhere along the chain)
HEAD_CAP = 0.06             # the chain closes this far ahead of joint 0 (the snout's tip)
TAIL_TIP = 0.14             # the tail's last bit behind the last joint
HEAD_JOINTS = 4             # joints 0..3 are the head: stiff (HEAD_BEND) but jointed - not a rigid block
HEAD_BEND = 5               # deg: the head joints' bend limit (cone half-angle; the tests demand <= 6)
SHOULDER = 11               # the chest joint: the wing arms hang here (and the flight's girdle)
HIPS = 15                   # the hind legs hang here
TAIL_ROOT = 16              # the tail starts here: 14 joints + the tip = 1.54 m of the 3.1 m body (~half)


def at(joint=0, offset=(0, 0, 0)):
    return {'joint': joint, 'side': 0, 'offset': K.V(*offset), 'slot': 0}


def key(j, half_width, half_height, height, bend):
    """A profile key at joint j: the joint's radii (half width, half height - the future skin), its height above the
    ground when standing (metres), its bend limit (deg). Joints between keys are interpolated smoothly."""
    return {'t': round(j / (N - 1), 6), 'halfWidth': half_width, 'halfHeight': half_height, 'clearance': height,
            'bendLimitDeg': bend, 'hump': 0, 'keel': 0, 'flank': 0, 'blade': 0, 'keelForward': 0}


# The stance (the anatomy picture): the head held high and level, the neck rising steeply from the chest (as the Apex's:
# 0.43 m over 7 segments, no segment steeper than ~45 deg), the trunk falling gently to the hips, the long tail low.
PROFILE = [
    #     joint          width  height  standing height  bend limit
    key(0,              0.025, 0.030, 0.93, HEAD_BEND),   # the snout's tip
    key(HEAD_JOINTS - 1, 0.045, 0.055, 0.95, HEAD_BEND),  # the back of the skull (the head's last joint)
    key(HEAD_JOINTS + 1.5, 0.040, 0.050, 0.83, 14),       # the upper neck: slim and supple
    key(SHOULDER - 2.5, 0.060, 0.075, 0.69, 12),          # the lower neck widening into the chest
    key(SHOULDER,       0.120, 0.140, 0.52, 10),          # the chest (the widest point)
    key(HIPS - 0.5,     0.100, 0.110, 0.43, 12),          # the trunk narrowing to the hips
    key(TAIL_ROOT + 0.5, 0.070, 0.090, 0.40, 18),         # the tail root: long and thick
    key(N - 1,          0.012, 0.020, 0.30, 20),          # the tail's tip
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
    'restAtStance': 1,      # bound as it stands (the raised neck)
})

# ================================================================ the wing arms (walked on, folded) - Leg2Bone
# The anatomy picture: a short upper arm, a very long forearm to the ground (upper = 0.4 x forearm). The Apex's proven
# placement: the wrist rests 0.22 m behind the shoulder and 0.06 m out, the elbow high and back (pole).
ARM_UPPER = 0.2             # upper arm (shoulder -> elbow)
ARM_FOREARM = 0.5           # forearm (elbow -> wrist, which is the foot it walks on)
sp.add_part('Leg2Bone', {
    'id': 'arm', 'at': at(SHOULDER, (0.1, 0.02, 0)), 'mirror': 1,
    'role': 0, 'gaitGroup': -1,
    'restOffset': K.V(0.06, 0, -0.22),   # where the planted wrist rests from the shoulder (out, up, forward)
    'stepThreshold': 0.14, 'stepDuration': 0.3, 'stepLift': 0.06, 'overshoot': 0,   # the walking step
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
# The Apex's: a raised hock, a long metatarsus, three toes and a hallux; the feet 0.1 m behind the hips.
sp.add_part('DrakeLeg', {
    'id': 'hind', 'at': at(HIPS, (0.08, -0.05, 0)), 'mirror': 1,
    'role': 0, 'gaitGroup': -1,
    'restOffset': K.V(0.18, 0, -0.1),     # where the foot rests from the hip (out, up, forward)
    'stepThreshold': 0.16, 'stepDuration': 0.25, 'stepLift': 0.06, 'overshoot': 0,
    'footLength': 0.12, 'toeCount': 0,
    'forwardSwingLimitDeg': 0, 'backSwingLimitDeg': 0, 'kneeRangeDeg': 0, 'kneeFlexDeg': 0,
    'upperLength': 0.24, 'lowerLength': 0.26,    # thigh, shin
    'upperRadius': 0.034, 'lowerRadius': 0.02,
    'pole': K.V(0.3, 0.2, 0.9),                  # the knee points forward and out
    'ankleHeight': 0.14,                         # the raised hock (digitigrade: the metatarsus)
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
FINGER_FOLD = [120, 122.5, 125, 127.5]
sp.add_part('DigitWing', {
    'id': 'wing', 'at': at(SHOULDER), 'mirror': 1, 'arm': 'arm',
    'spec': {
        'leg': 0,
        'restTip': K.V(0.6, 0.06, 0.04),     # the wrist's rest in the spread glide pose (out, up, forward)
        'comfortUp': 0.5, 'comfortDown': 0.6, 'strokePlaneDeg': 10, 'pronationDeg': 18, 'overshootUp': 0.08,
        'upstrokeDuration': 0.3, 'upstrokeFold': 0.5, 'foldReach': 0.3, 'beatGroup': 0,
        'fingerLengths': FINGER_LENGTHS, 'fingerSpreadDeg': FINGER_SPREAD, 'fingerFoldDeg': FINGER_FOLD,
        'fingerMinGapDeg': 2, 'fingerFoldGapDeg': 3.5,
        'groundLiftDeg': 82,                 # folded: how steeply the fingers stand up off the forearm (the Apex's 82; see FINGER_FOLD)
        'groundTallStance': 1, 'flankPatches': 6, 'groundWristLift': 0.08,
        'thumbPlant': 1,                     # it walks on the thumb claw's tip
        'fanRearRollsWithFan': 1, 'groundFlankRise': 0.6,
        'fingerRadius': 0.014, 'thumbLength': 0.08,
        'membraneRearJoint': SHOULDER + 2, 'membraneFrontJoint': SHOULDER - 3,   # the (future) membrane's body span
        'trailingRib': 0.5, 'membraneInset': 0.8,
        'area': 3,                           # aerodynamic wing area factor (lift)
        'scallop': 0, 'scallopByChord': 0, 'membraneBillow': 0, 'membraneDetail': 0, 'fingerKnuckles': 0,
        'wristKnuckle': 0, 'armMuscle': 0, 'forearmTwin': 0, 'thumbRadius': 0,
    },
    'fingerSegments': 4,                       # bones per finger
    'fingerRadius': [0.018, 0.016, 0.014, 0.012],   # each finger's root joint radius (the future skin)
    'fingerTaper': 0.4,                        # the tip's radius as a fraction of the root's
    'flexDeg': 10,                             # air: the most the beat bends a finger joint
    'flexHz': 2, 'flexDamping': 0.5,           # air flex / ground droop: how fast and how springy the fingers follow
    'droopDeg': 8,                             # ground: how far gravity droops a folded finger joint
    'maxBendDeg': 25,                          # no finger joint ever bends further
    'thumbLength': 0.08, 'thumbCurlDeg': 40, 'thumbSegments': 2,   # the forward thumb claw
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
        'amplitudeMin': 0.4,                                    # the weakest beat's amplitude
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
        'headStabiliseBounded': 1,
        # the trunk (and the head target's direction) turn at most 2 rad/s / sqrt s (FlightSpec.trunkTurnBounded): at the
        # aviary's wall the outward speed is cut and the turn reverses what is left in a few tenths of a second; the trunk
        # followed that reversal in a few ticks and the head swung 1.8-3 m in one tick (task E)
        'trunkTurnBounded': 1,
        'airNeckStance': 0.3,
        'launchHeadOnNeck': 1, 'touchdownAtStance': 1, 'launchTailBySegment': 1,
        # a touchdown's first frame is the hovering body (FlightSpec.touchdownFromHover): begun on a tick's last sub-step
        # the walking restart's body was shown for a tick - on a rock perch the girdle jumped 1.6-2.9 m there and back,
        # on open ground the head 2.1 m (CreatureVesperTests.Vesper_Touchdown_NoGirdleJump)
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
        'drapeOwnHeading': 1, 'perchFrontLevel': 1,
        # flown low past a rock's edge (a launch beside it), the air floor lifts the girdle at most 4 m/s x sqrt s
        # (FlightSpec.floorLiftEased); off, it snapped up by the rock's height in one sub-step: 1.4-1.5 m in a tick
        # (CreatureVesperTests.Vesper_LowOverRock_NoGirdleJump)
        'floorLiftEased': 1,
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
