#!/usr/bin/env python3
"""Apex: species generator (creature-kit, Apex redesign task 10).

The Apex (docs/superpowers/specs/2026-10-20-apex-redesign-design.md; the look: assets/2026-10-20-apex-reference-hires.jpg)
built from the redesign's kit parts only: one FlowBody chain from the head to the tail tip, a CrestBeakHead on joint 0,
a Dewlap throat sail and PitRow breathing holes on the neck, mirrored FleshWing blades on long wing arms (Leg2Bone) it
walks on, DrakeLeg hind legs, Eyes, and the TailFlourish overlay last. Authored at scale 1 in metres; the species scale
(SCALE) makes it the lab's largest flyer.

Run it (python Apex.py <Assets/TerrainGen>: writes Config/Creatures/Apex.asset; the .meta and its guid are kept once
written), then creature_kit.py check Apex and creature_kit.py test CreatureApexTests. The numbers and why: the task-10
report (.superpowers/sdd/2026-10-20-apex-redesign/task-10-report.md).
"""
import os
import sys

# kityaml (the creature-kit skill's scripts): CREATURE_KIT_SCRIPTS, else the project's .claude/skills above this tree
# (Assets/TerrainGen/Tools~/SpeciesGen -> the project root; a worktree without .claude: set CREATURE_KIT_SCRIPTS)
_here = os.path.dirname(os.path.abspath(__file__))
for _d in [os.environ.get('CREATURE_KIT_SCRIPTS'),
           os.path.join(_here, '..', '..', '..', '..', '.claude', 'skills', 'creature-kit', 'scripts')]:
    if _d and os.path.isfile(os.path.join(_d, 'kityaml.py')):
        sys.path.insert(0, _d)
        break
import kityaml as K  # noqa: E402

SCALE = 3.5                 # x the scale-1 numbers below: ~9.5 m from the beak to the tail tip

# ---------------------------------------------------------------- the chain (FlowBody)
# 26 joints 0.1 m apart: joint 0 the head joint, a neck of 7 fine segments rising to the chest at joint 7 (the
# shoulder: the wing arms), the trunk to the hips at joint 11 (the hind legs), the tail from joint 12 to the tip -
# the tail is 0.1 x 14 + the tip 0.14 = 1.54 m of the 2.7 m body (57 %).
N, SPACING = 26, 0.1
KEEL_FORWARD = 0.05   # the chest key's keelForward (scanned 0.03 / 0.05 / 0.08: the lowest point -0.007 / +0.009 / +0.033 m ahead)
SHOULDER, HIPS, TAIL_ROOT = 7, 11, 12


def at(joint=0, offset=(0, 0, 0)):
    return {'joint': joint, 'side': 0, 'offset': K.V(*offset), 'slot': 0}


def key(j, hw, hh, clearance, bend, hump=0, keel=0, flank=0, blade=0, keel_forward=0):
    """A profile key at joint j (t = j / (N - 1))."""
    return {'t': round(j / (N - 1), 6), 'halfWidth': hw, 'halfHeight': hh, 'clearance': clearance, 'bendLimitDeg': bend,
            'hump': hump, 'keel': keel, 'flank': flank, 'blade': blade, 'keelForward': keel_forward}


# The neck rises 0.41 m from the chest to the head joint over seven segments, spread evenly (no stance segment steeper
# than ~50 deg: a steeper one tips its walking frame over, task 8b), the head highest (the chest-to-head chord ~37 deg);
# its front keyed to the head's back (CrestBeakHead's skull starts inside it). Not steeper: the steeper neck (0.95 /
# 0.84 / 0.70) pitched the chest up so far that the folded blades leaned in and their tips crossed over the back
# (CreatureFleshWingTests.SteepProblem, task 10 fix round). The chest carries the sharp hump over the shoulders, the keel below
# and the flank masses; the trunk narrows to the hips; from joint 12.5 the tail is the full ribbon (blade 1: a tall,
# side-flattened section, height/width up to 4 at the root).
PROFILE = [
    key(0.0, 0.040, 0.052, 0.93, 8),
    key(2.5, 0.037, 0.050, 0.83, 14),
    key(4.5, 0.060, 0.075, 0.69, 12),
    # keelForward: the keel's bottom jutting forward (the sternum, spec 5.1). Without it the trunk's lowest point lay
    # 0.031 m behind the chest joint (the trunk descends to the hips faster than the keel fades behind the chest); with
    # it 0.009 m ahead (CreatureApexTests.Apex_Keel_BelowAndAheadOfTheChest).
    key(SHOULDER, 0.12, 0.14, 0.52, 10, hump=0.06, keel=0.08, flank=0.02, keel_forward=KEEL_FORWARD),
    key(10.5, 0.10, 0.11, 0.43, 12),
    key(12.5, 0.03, 0.12, 0.40, 18, blade=1),
    key(N - 1, 0.01, 0.04, 0.30, 20, blade=1),
]

sp = K.Species('Apex', scale=SCALE)

sp.add_part('FlowBody', {
    'id': 'body', 'at': at(), 'mirror': 0,
    'spacing': SPACING, 'headCapLength': 0.06, 'tailTipLength': 0.14, 'jointCount': N,
    'profile': PROFILE,
    # joints 0 and 1 rigid: the skull reaches back past joint 1 (its back half, 0.13 m), so the chain does not bend
    # under it (a touchdown bent the neck's front 0-1 through the head cap's skin: kit check 13)
    'rigidHeadJoints': 2,
    'humpSharpness': 8, 'keelSharpness': 6, 'bladeExponent': 4,
    'ringSegments': 24, 'ringsPerSegment': 3,
    # the teal: the tail from just behind the hips and a stripe along the neck's top, in the wing's own kind (one
    # kit slot for the three: the Apex's named kinds just fit the six slots)
    'tailColourFrom': 0.5, 'dorsalStripeTo': 0.25, 'dorsalStripeWidthDeg': 30,
    'tailKind': 'wingfan', 'dorsalStripeKind': 'wingfan',
    'ovalMaxAspect': 2,
    'restAtStance': 1,      # bound as it stands: the raised neck's bind is as steep as its walk
})

# ---------------------------------------------------------------- the wing arms (walked on) and the hind legs
# The wing arm (the anatomy picture): a short humerus 0.2, a very long forearm 0.5 to the ground, the wrist 0.22 m
# behind the shoulder and 0.06 m out, the elbow high and back (pole) - this, not a flag, stands the folded blades
# steep and side by side (FleshWing task 8).
sp.add_part('Leg2Bone', {
    'id': 'arm', 'at': at(SHOULDER, (0.1, 0.02, 0)), 'mirror': 1,
    'role': 0, 'gaitGroup': -1, 'restOffset': K.V(0.06, 0, -0.22),
    'stepThreshold': 0.14, 'stepDuration': 0.3, 'stepLift': 0.06, 'overshoot': 0,
    'footLength': 0, 'toeCount': 0,
    'forwardSwingLimitDeg': 0, 'backSwingLimitDeg': 0, 'kneeRangeDeg': 0, 'kneeFlexDeg': 0,
    'upperLength': 0.2, 'lowerLength': 0.5, 'upperRadius': 0.04, 'lowerRadius': 0.03,
    'pole': K.V(0.2, 1, -0.4), 'kneeBlend': 0,
    'ankleHeight': 0, 'clawLength': 0, 'clawCurveDeg': 0, 'toeWeb': 0, 'digitigrade': 0, 'thighMass': 0, 'scutes': 0,
    'spurLength': 0,
})

# Slim digitigrade hind legs at the hips: a raised hock, a long metatarsus, three toes and a hallux with dark talons;
# the feet 0.1 behind the hips, so the clubs stand clearly ahead of them (the picture; >= 0.2 m planted). The walk's
# Froude similarity (kit check 9) is sensitive to this: -0.08 and -0.12 failed it (0.989, steps 43 / 45), -0.1 passes.
sp.add_part('DrakeLeg', {
    'id': 'hind', 'at': at(HIPS, (0.08, -0.05, 0)), 'mirror': 1,
    'role': 0, 'gaitGroup': -1, 'restOffset': K.V(0.18, 0, -0.1),
    'stepThreshold': 0.16, 'stepDuration': 0.25, 'stepLift': 0.06, 'overshoot': 0,
    'footLength': 0.12, 'toeCount': 0,
    'forwardSwingLimitDeg': 0, 'backSwingLimitDeg': 0, 'kneeRangeDeg': 0, 'kneeFlexDeg': 0,
    'upperLength': 0.24, 'lowerLength': 0.26, 'upperRadius': 0.034, 'lowerRadius': 0.02,
    'pole': K.V(0.3, 0.2, 0.9), 'ankleHeight': 0.14,
    'toes': 3, 'hallux': 1, 'talonLength': 0, 'talonCurveDeg': 110, 'haunch': 1.5,
})

# ---------------------------------------------------------------- the wings
# The FleshWing defaults (task 8's passing region) but for the sail's outline and the ground lift:
#   - swept back, manta-like (spec §5.2 / the addendum): the rays start swept (36 deg behind the forearm, the default
#     20) and close up (12 deg apart, the default 16), the leading ray the longest and the others tapering (1.75 ..
#     0.85, the default 1.6 .. 0.92) - a pointed, swept wing instead of the default's round radial fan; the folded
#     blade stays a broad leaf (BroadProblem 0.168 >= 0.12);
#   - groundLiftDeg 82 (68): with the raised neck the chest is pitched up and the default lift left the folded blades
#     leaning in, tips crossing over the back; 82 stands them side by side (own side >= 0.09 x s, apart >= 0.37 x s).
sp.add_part('FleshWing', {
    'id': 'wing', 'at': at(SHOULDER), 'mirror': 1, 'arm': 'arm',
    'spec': {
        'leg': 0, 'restTip': K.V(0.6, 0.06, 0.04),
        'comfortUp': 0.5, 'comfortDown': 0.6, 'strokePlaneDeg': 10, 'pronationDeg': 18, 'overshootUp': 0.08,
        'upstrokeDuration': 0.3, 'upstrokeFold': 0.5, 'foldReach': 0.3, 'beatGroup': 0,
        'fingerLengths': [1.75, 1.45, 1.2, 1.0, 0.85],
        'fingerSpreadDeg': [36, 48, 60, 72, 84],
        'fingerFoldDeg': [160, 162, 164, 166, 168],
        'fingerMinGapDeg': 2, 'fingerFoldGapDeg': 3.5,
        'groundLiftDeg': 82, 'groundTallStance': 1, 'flankPatches': 6, 'groundWristLift': 0.08,
        'thumbPlant': 1, 'fanRearRollsWithFan': 1, 'groundFlankRise': 0.6,
        'fingerRadius': 0.014, 'thumbLength': 0.045,
        'membraneRearJoint': 9, 'membraneFrontJoint': 4,
        'trailingRib': 0.5, 'membraneInset': 0.8, 'area': 3,
        # the Wing's own look fields: a FleshWing draws its own sail (they stay 0)
        'scallop': 0, 'scallopByChord': 0, 'membraneBillow': 0, 'membraneDetail': 0, 'fingerKnuckles': 0,
        'wristKnuckle': 0, 'armMuscle': 0, 'forearmTwin': 0, 'thumbRadius': 0,
    },
    # the arm buried in the blade (task 10c): the leading edge's flesh a leaf 3 x its radius toward the sail (folded: the
    # sheet rolled round the forearm down to the club), its rest roll -55 deg off the elbow's hinge (between the spread
    # wing's sail side, ~-25 .. -40, and the folded blade's, ~-80; the scan's -55 .. -75 all pass)
    'rootThickness': 0.07, 'tipThickness': 0.012, 'edgeRound': 0.5, 'armBulk': 0.06, 'armChord': 3, 'armRollDeg': -55,
    'leadFlesh': 0.5,
    'rootSwell': 0.03, 'stripes': 9, 'stripeDepth': 0.003,
    'padRadius': 0.06, 'clubLength': 0.16, 'hookLength': 0, 'innerColour': 0.45,
    'spanSegments': 3, 'bindFold': 0.3,
    'spanHz': 1.4, 'spanDamping': 0.45, 'spanMaxDeg': 20,
    'chordHz': 1.8, 'chordDamping': 0.5, 'featherDeg': 10, 'chordMaxDeg': 6, 'billowDeg': 2,
    'columns': 6, 'rows': 15,
})

# ---------------------------------------------------------------- the head
# The eyes in the head's sockets (CrestBeakHead.eyeAt (0.15, 0.4) of a 0.26 x 0.067 skull).
sp.add_part('Eyes', {'id': 'eyes', 'at': at(0, (0.042, 0.027, 0.039)), 'mirror': 0, 'radius': 0.011, 'cluster': []})

# CrestBeakHead's defaults (the 1.5x V sickle crests, teeth, fangs, chin knob, rest gape 7, call gape 62) with a
# deeper, fleshier lower jaw than the default (the face close-ups); the teeth on the legacy "band" row (9, below the
# kit slots: no slot of their own), restyled ivory keratin by the Layers row below. FRAGILE: row 9 is the arthropod
# "band" row - any part that draws on it (an ArthropodLeg band, a Tendril's layer 9) would turn ivory too, and the
# legacy "teeth" row (13) is no way out (it is one of the six kit slots the Apex's named kinds fill). If a kit slot
# frees up (or the table grows), switch toothKind back to "ivory-teeth" and drop the row-9 Layers row.
sp.add_part('CrestBeakHead', {
    'id': 'head', 'at': at(0), 'mirror': 0,
    'skullLength': 0.26, 'skullWidth': 0.055, 'skullDepth': 0.067, 'beakLength': 0.26, 'beakHookDeg': 11,
    'browRidge': 0.3,
    'crests': [{
        'along': 0.8, 'height': 0.0825, 'length': 0.1275, 'curlDeg': 95, 'thickness': 0.0065, 'chord': 0.026,
        'flare': 2.6, 'flareAt': 0.62, 'outside': 0.75, 'hookDeg': 40, 'splayDeg': 30, 'mirror': 1,
    }],
    'grooves': 0, 'grooveDepth': 0.004,
    'jawFraction': 0.5, 'mouthCornerAt': -0.15, 'hingeBehind': 0.15,
    # throatDepth 0.7 (was 0.8): the jaw's underside runs into the throat without a step in the outline (0.0111 m at
    # 0.8 > the 0.0101 limit, 0.0078 at 0.7; CreatureApexTests.Apex_Head_FlowsIntoTheNeck)
    'jawDepth': 1.1, 'chinDepth': 0.65, 'throatDepth': 0.7, 'cheekWeb': 0.16,
    'throatRadius': 0.055, 'throatReach': 0.065,
    'gapeDeg': 62, 'callPitchDeg': 6, 'restGapeDeg': 7,
    'teeth': 16, 'toothLength': 0.014, 'toothRadius': 0.003, 'toothKind': 'band', 'toothRakeDeg': 18,
    'fangPairs': 2, 'fangLength': 0.022, 'fangLiftDeg': 35, 'chinKnob': 0.009,
    'callEverySeconds': K.Flow([('x', 6), ('y', 14)]), 'callSeconds': 0.8,
    'eyeAt': K.Flow([('x', 0.15), ('y', 0.4)]), 'pitchDeg': 0, 'cell': 0.005,
})

# ---------------------------------------------------------------- the throat
# The throat sail (Dewlap, task 10b): a deep pale pouch from just behind the jaw (joint 1) back INTO the chest (joint 7,
# SHOULDER: the girdle), deeper than the neck (§11), its lower edge bellied (edgeStraightness 0.45: between depthAt's
# curve and the straight line), sweeping up into the chest over the last 60 % (rearSweep); thick where it leaves the
# throat, bellied halfway down, puffing a little as it swings; its own chain of 5 stations x 3 links swings behind the
# neck with the part's default swing (25 deg, damping 0.35). The heavy flyer's bob heaves the girdle ~1 g every beat:
# the part's coupled stations (coupling 0.6, maxTwistDeg 8) keep the station on the girdle with its neighbours, so the
# rim never turns over (CreatureDewlapTests.Dewlap_HeavyBob_RimNeverTurnsOver; kit check 13 on six sweep seeds).
# Every value below is the part's default except the neck's joints and depths and the bellied edge.
sp.add_part('Dewlap', {
    'id': 'throat', 'at': at(), 'mirror': 0,
    'fromJoint': 1, 'toJoint': SHOULDER, 'stations': 5, 'links': 3,
    'depthAt': [0.14, 0.24, 0.28, 0.25, 0.17],
    'edgeStraightness': 0.45, 'rim': 0.012, 'thickness': 0.008,
    'swingHz': 1.6, 'swingDamping': 0.35, 'maxSwingDeg': 25, 'gravityDeg': 6,
    'rootThickness': 0.012, 'billow': 0.05, 'rearSweep': 0.6, 'coupling': 0.6, 'maxTwistDeg': 8,
})

# Five breathing holes each side in the front third of the neck (body fraction 0.08 - 0.16, just behind the skull),
# high on the throat below the side, oblique slits growing toward the head.
sp.add_part('PitRow', {
    'id': 'spiracles', 'at': at(), 'mirror': 1,
    'fromU': 0.08, 'toU': 0.16, 'angleDeg': -35, 'count': 5,
    'radius': 0.012, 'depth': 0.6, 'rim': 0.35, 'aspect': 2.2, 'slantDeg': 50, 'sizeGrowth': 0.6,
})

# ---------------------------------------------------------------- the tail overlay (last part)
# Standing, the tail leaves the hips low and rises in a loop behind the body (groundLoopDeg per joint over the 13
# joints from TAIL_ROOT + 1); in the air a slow travelling roll and curl. Decoration: the sim never sees it.
sp.add_part('TailFlourish', {
    'id': 'tail', 'at': at(), 'mirror': 0,
    'fromJoint': TAIL_ROOT,
    'airRollDeg': 70, 'airCurlDeg': 9, 'airHz': 0.35, 'waveLength': 0.6,
    'groundLoopDeg': 12, 'groundDriftDeg': 2, 'groundHz': 0.12, 'easeSeconds': 0.6,
})

# ---------------------------------------------------------------- modules
sp.add_module('Walk', {
    'speed': 0.45, 'turnRateDeg': 60, 'accel': 1.5, 'turnSlowdown': 0.5,
    'gaitSwayDeg': 0, 'gaitRollDeg': 0, 'subSteps': 2, 'pattern': 1, 'waveGroups': 4, 'waveTailToHead': 0,
    'bob': {'amplitude': 0, 'hz': 0, 'damping': 0},
    'burst': {'factor': 0, 'every': 0, 'seconds': 0},
    'travel': 0, 'forwardFactor': 0,
})

# The flight (scanned: the task-10 report). A heavy flyer that must beat to stay up - its cruise is continuous
# wingbeats, never a glide (the old Apex glided level on kLift 0.75: no bob) - so the body heaves with every stroke:
#   - weak glide lift (kLift 0.1) and a low demand floor (0.12): level flight needs beats, and the floor's beat alone
#     does not climb (a stronger floor beat climbed over the goal and the cruise turned into a glide);
#   - a firm grip of the big soft wings (kGrip 1) and a wide stroke-rate range (1.5 .. 10 Hz at scale 1, amplitude from
#     0.4): weak slow beats at the floor, strong ones to climb out;
#   - glides when it is not climbing (glideDemand 0.14 above the floor; descendCutoff -0.3): descents and the landing
#     approach glide;
#   - airLevel 0.8 (an ACCEPTED DEVIATION from the brief's "airLevel ~0", measured): the trunk does not pitch with each
#     beat's rise and fall, so the neck root stays steady and the head rides the bob at 0.20 x the girdle's (0.48 -
#     0.62 x at airLevel 0), while the girdle still heaves 12.9 % of the body per beat and the bob runs down the tail
#     as a lagging wave;
#   - avoidLookahead 2.4 (planned finals, as the winged serpent) and a short flare (1.6): it lands in the aviary
#     within a minute or so (without them it circled its point for minutes).
# The stance-bound flags (tasks 8b, 9b): the air neck keeps a share of its stance's bends, the launching head turns with
# its neck, the touchdown lays the body down at its stance, the launch hands the tail to the air chain segment by segment.
sp.add_module('Fly', {
    'spec': {
        'cruiseSpeed': 3, 'minSpeed': 2, 'maxSpeed': 6, 'kGrip': 1, 'vGripMin': 2.2, 'kLift': 0.1, 'kDrag': 0.01,
        'upstrokeArea': 0.15, 'featherDeg': 8.6,
        'climbGain': 0.6, 'climbDamping': 0.5, 'climbIntegral': 0.8, 'climbIntegralLimit': 0.3,
        'thrustGain': 0.5, 'demandFloor': 0.12, 'descendCutoff': -0.3, 'glideDemand': 0.14,
        'amplitudeMin': 0.4, 'strokeRateMin': 1.5, 'strokeRateMax': 10, 'glideRelax': 6,
        'maxBankDeg': 35, 'bankGain': 3, 'bankDamping': 0.5, 'maxAsymmetry': 0.4, 'rollGain': 0.7, 'rollDamping': 4,
        'glideAlphaDeg': 6, 'flareAlphaDeg': 45,
        'launchSpeed': 2.5, 'flareDistance': 1.6, 'minAltitude': 1.2, 'cruiseAltitudeFrac': 0.6,
        'airUndulationDeg': 0, 'airUndulationHz': 0, 'tailSag': 0,
        'headStabilise': 0.6, 'airNeckStance': 0.3,
        'launchHeadOnNeck': 1, 'touchdownAtStance': 1, 'launchTailBySegment': 1,
        'avoidLookahead': 2.4, 'avoidMargin': 0.35, 'perchGrip': 0, 'perchMinRadius': 0.06, 'pursuitGain': 0,
        'legTuckOut': 0, 'legTuckDown': 0, 'legTuckReach': 0,
        'touchdownTogether': 1, 'airLevel': 0.8, 'glideEnergy': 0, 'approachBleedsSpeed': 0,
    },
    'twist': {
        'swimAmplitude': 0, 'swimHz': 0, 'swimVertical': 0, 'swimRollDeg': 0,
        'corkscrewAmplitude': 0, 'corkscrewHz': 0, 'corkscrewTurns': 0, 'corkscrewRolls': 0,
        'barrelRadius': 0, 'barrelTime': 0, 'loopRadius': 0, 'loopSpin': 0, 'slowRollTime': 0,
        'intervalMin': 0, 'intervalMax': 0, 'uprightRateDeg': 0,
        'girdleBob': 0, 'bobHead': 0, 'bobTail': 0, 'bobInPhase': 0, 'bobHighPass': 0,
    },
})

sp.add_module('QuadLaunch', {'crouchSeconds': 0.4, 'crouchDepth': 0.12, 'vaultSpeed': 4})
sp.add_module('Wander', {'arriveRadius': 0, 'pauseMin': 0, 'pauseMax': 0})
sp.add_module('FlightExcursion', None)

# Colours (sRGB, spec §4): dorsal maroon-plum #4A1E33 with teal mottling, cream belly / throat #D9CDB8.
sp.add_module('Skin', {
    'dorsal': K.C(0.29, 0.118, 0.2, 1), 'flank': K.C(0.36, 0.16, 0.24, 1), 'belly': K.C(0.851, 0.804, 0.722, 1),
    'pattern': K.C(0.12, 0.33, 0.32, 1), 'patternType': 2, 'patternScale': 4, 'patternContrast': 0.3,
    'patternSoftness': 0.7, 'spotScale': 0.6,
    'smoothness': 0.4, 'rimStrength': 0.35, 'subsurface': 0.3,
    'sizeJitter': 0.08, 'hueJitter': 0.015, 'valueJitter': 0.05,
})
sp.add_module('Membrane', {'colour': K.C(0.122, 0.42, 0.4, 1), 'translucency': 0.3, 'veinContrast': 0.1, 'rim': 0.2, 'inner': 0})


def row(part_id, tint, model=4):
    """A Layers row by legacy part id: Claw (glossy keratin) in its own tint."""
    return {'partId': part_id, 'kind': '', 'model': model, 'noPattern': 1, 'limbPattern': 0, 'noGrain': 1,
            'fineGrain': 0, 'noSheen': 0, 'noShimmer': 0, 'tintSource': 0, 'tint': tint, 'tintAmount': 1,
            'smoothnessScale': 0}


# the talons (foot row 2) near-black; the teeth (toothKind "band", row 9) ivory #EBE0C6
sp.add_module('Layers', {'rows': [row(2, K.C(0.05, 0.035, 0.04, 1)), row(9, K.C(0.922, 0.878, 0.776, 1))]})


if __name__ == '__main__':
    tg = sys.argv[1] if len(sys.argv) > 1 else os.environ.get('CREATURE_TG')
    if not tg:
        sys.exit('usage: %s <Assets/TerrainGen> (or set CREATURE_TG)' % sys.argv[0])
    print('wrote', sp.save(os.path.join(tg, 'Config', 'Creatures', sp.name + '.asset')))
