#!/usr/bin/env python3
"""Apex: species generator (creature-kit). Scaffolded from Pterodactyl (creature_kit.py new), then rebuilt as its own animal.

The apex predator of the sky: a huge flyer that is NOT a pterosaur. Its look parts are its own (SailWing, SerpentNeck,
CasqueHead, a deep-chested body with a long tail); its flight, launch, hunt and neck carriage reuse the Pterodactyl's
systems (Fly + the PteroWing chains, QuadLaunch, SkyHunt, LongNeck's runtime).

Edit the numbers, run it (python Apex.py <Assets/TerrainGen>: writes Config/Creatures/Apex.asset), then
creature_kit.py check Apex
"""
import os
import sys

sys.path.insert(0, os.environ.get('CREATURE_KIT_SCRIPTS') or 'C:\\Users\\tobia\\Documents\\unity_stuff\\MarchingCubes\\.claude\\skills\\creature-kit\\scripts')
import kityaml as K  # noqa: E402

sp = K.Species('Apex', scale=4.2)

_rid = [999]


def R():
    _rid[0] += 1
    return _rid[0]


def at(joint, x=0.0, y=0.0, z=0.0):
    return {'joint': joint, 'side': 0, 'offset': K.V(x, y, z), 'slot': 0}


# ------------------------------------------------------------------ the body: a massive deep chest rising to the neck,
# hips at joint 6, a long thick tail (24 joints, 0.07 m)
def key(t, hw, hh, cl, bend):
    return {'t': t, 'halfWidth': hw, 'halfHeight': hh, 'clearance': cl, 'bendLimitDeg': bend}


sp.add_part('SpineBody', {
    'id': 'body',
    'at': at(0),
    'mirror': 0,
    'spacing': 0.09,
    'headCapLength': 0.07,
    'tailTipLength': 0.09,
    'joints': [],
    'profile': [
        key(0, 0.12, 0.15, 0.8, 6),
        key(0.087, 0.155, 0.185, 0.785, 6),
        key(0.174, 0.15, 0.165, 0.755, 7),
        key(0.261, 0.118, 0.122, 0.72, 9),
        key(0.348, 0.088, 0.088, 0.67, 10),
        key(0.6, 0.05, 0.048, 0.56, 11),
        key(1, 0.011, 0.011, 0.47, 12),
    ],
    'jointCount': 19,
    'rigidHead': 0,
    'rigidHeadJoints': 4,
    'ringSegments': 20,
    'ringsPerSegment': 4,
}, rid=R())

# ------------------------------------------------------------------ the wing arms (heavy, long: it stands tall on its knuckles)
sp.add_part('Leg2Bone', {
    'id': 'arm',
    'at': at(1, 0.12, 0.06, 0),
    'mirror': 1,
    'role': 0,
    'gaitGroup': -1,
    'restOffset': K.V(0.24, 0, 0.14),
    'stepThreshold': 0.12,
    'stepDuration': 0.36,
    'stepLift': 0.07,
    'overshoot': 0.05,
    'footLength': 0.07,
    'toeCount': 3,
    'forwardSwingLimitDeg': 0,
    'backSwingLimitDeg': 0,
    'kneeRangeDeg': 0,
    'kneeFlexDeg': 0,
    'upperLength': 0.32,
    'lowerLength': 0.84,
    'upperRadius': 0.05,
    'lowerRadius': 0.03,
    'pole': K.V(0.4, 0.3, -1),
    'kneeBlend': 0.49,
}, rid=R())

# ------------------------------------------------------------------ strong hind legs
sp.add_part('Leg2Bone', {
    'id': 'hind',
    'at': at(5, 0.09, -0.04, 0),
    'mirror': 1,
    'role': 0,
    'gaitGroup': -1,
    'restOffset': K.V(0.16, 0, 0),
    'stepThreshold': 0.1,
    'stepDuration': 0.32,
    'stepLift': 0.06,
    'overshoot': 0.05,
    'footLength': 0.12,
    'toeCount': 4,
    'forwardSwingLimitDeg': 70,
    'backSwingLimitDeg': 50,
    'kneeRangeDeg': 45,
    'kneeFlexDeg': 20,
    'upperLength': 0.4,
    'lowerLength': 0.42,
    'upperRadius': 0.062,
    'lowerRadius': 0.032,
    'pole': K.V(0.3, 0.2, 1),
    'kneeBlend': 0,
    'ankleHeight': 0.04,
    'clawLength': 0.03,
    'clawCurveDeg': 80,
}, rid=R())

# ------------------------------------------------------------------ the sail wings (flown by the PteroWing's systems)
sp.add_part('SailWing', {
    'id': 'wing',
    'at': at(1),
    'mirror': 1,
    'arm': 'arm',
    'spec': {
        'leg': 0,
        'restTip': K.V(0.905, 0.1, 0.04),
        'comfortUp': 0.6,
        'comfortDown': 0.8,
        'strokePlaneDeg': 10,
        'pronationDeg': 18,
        'overshootUp': 0.08,
        'upstrokeDuration': 0.32,
        'upstrokeFold': 0.5,
        'foldReach': 0.3,
        'beatGroup': 0,
        'fingerLengths': [1.6, 1.12, 0.56],
        'fingerSpreadDeg': [-4, 9, 28],
        'fingerFoldDeg': [150, 158, 166],
        'fingerMinGapDeg': 4,
        'fingerFoldGapDeg': 10,
        'groundLiftDeg': 72,
        'groundTallStance': 1,
        'flankPatches': 4,
        'groundWristLift': 0.07,
        'thumbPlant': 1,
        'fanRearRollsWithFan': 1,
        'groundFlankRise': 0.6,
        'fingerRadius': 0.02,
        'thumbLength': 0.07,
        'membraneRearJoint': 5,
        'membraneFrontJoint': 2,
        'trailingRib': 0.6,
        'membraneInset': 0.8,
        'area': 3,
        'scallop': 0,
        'scallopByChord': 0,
        'membraneBillow': 0,
        'membraneDetail': 0,
        'fingerKnuckles': 0,
        'wristKnuckle': 0,
        'armMuscle': 0,
        'forearmTwin': 0,
        'thumbRadius': 0,
    },
    'phalanges': [0.32, 0.29, 0.24, 0.15],
    'fingerRoot': 0.026,
    'sweepDeg': 3,
    'flexDeg': 6,
    'flexSeconds': 0.08,
    'foldCurlDeg': 6,
    'carpusAt': 0.6,
    'pteroidLength': 0.16,
    'pteroidDeg': 25,
    'crest': 0,
    'digitLength': 0,
    'trailingCurve': 0.05,
    'leadingEdge': 0.016,
    'camber': 0.04,
    'membraneColumns': 12,
    'membraneRows': 22,
    'chain': 1,
    'spanHz': 1.6,
    'spanDamping': 0.6,
    'spanMaxDeg': 10,
    'chordHz': 1.4,
    'chordDamping': 0.5,
    'chordFeatherDeg': 16,
    'chordMaxDeg': 10,
    'billowDeg': 2,
    'bindFold': 0.65,
    'armBulk': 0.7,
    'knuckle': 1.15,
    'spurRoot': 0.6,
    'sparTip': 0.12,
    'pleats': 12,
    'pleatDepth': 0.03,
    'bladeBelly': 0.16,
}, rid=R())

sp.add_part('Eyes', {
    'id': 'eyes',
    'at': at(0, 0.03, 0.05, 0.05),
    'mirror': 0,
    'radius': 0.012,
    'cluster': [],
}, rid=R())

# ------------------------------------------------------------------ the neck: long, thick, rising steeply off the chest
sp.add_part('SerpentNeck', {
    'id': 'neck',
    'at': at(0, 0, 0.07, 0.04),
    'mirror': 0,
    'segments': 16,
    'length': 1.15,
    'rootRadius': 0.1,
    'headRadius': 0.05,
    'depth': 1.25,
    'baseDeg': 62,
    'headDeg': 12,
    'flightBaseDeg': 18,
    'flightHeadDeg': 2,
    'bindBaseDeg': 35,
    'bindHeadDeg': 8,
    'reach': 0.96,
    'stabiliseSeconds': 0.2,
    'glanceDeg': 22,
    'glanceSeconds': 2.8,
    'callDeg': 22,
    'callSeconds': 1.4,
    'callInterval': 12,
    'aimYawDeg': 70,
    'strikeReach': 0.97,
    'headClearance': 0.12,
    'maxBaseDeg': 80,
    'basePitchFollow': 0.6,
    'headPitchFollow': 0.3,
    'headLevelDeg': 22,
    'strikeConeDeg': 40,
    'maxLagDeg': 15,
    'tipReach': 0.38,
    'ringSides': 24,
    'ringsPerSegment': 6,
    'rootFlare': 0.7,
    'rootFlareLength': 0.3,
    'throatKeel': 0.25,
    'dorsalRidge': 0.1,
    'muscle': 0.18,
    'bands': 26,
    'bandDepth': 0.04,
    'spiracles': 4,
    'spiracleRadius': 0.014,
    'spiracleAt': 0.75,
    'spiracleDepth': 0.5,
}, rid=R())

# ------------------------------------------------------------------ the head: a toothed wedge, mouth open, teal casque
sp.add_part('CasqueHead', {
    'id': 'head',
    'at': at(0),
    'mirror': 0,
    'follow': 'neck',
    'skullLength': 0.17,
    'skullHeight': 0.05,
    'skullWidth': 0.056,
    'beakLength': 0.2,
    'beakDepth': 0.04,
    'beakCompress': 1.1,
    'beakDroopDeg': 4,
    'keel': 0.6,
    'gapeDeg': 40,
    'clackInterval': 7,
    'crestHeight': 0.11,
    'crestLength': 0.12,
    'crestSweepDeg': 15,
    'crestThickness': 0.008,
    'crestAt': 0.85,
    'eyeOffset': K.V(0.045, 0.04, 0.12),
    'skinLayer': 0,
    'beakLayer': 2,
    'crestLayer': 13,
    'cell': 0.0045,
    'restGapeDeg': 16,
    'teeth': 9,
    'toothLength': 0.022,
    'brow': 0.35,
    'casqueHookDeg': 70,
}, rid=R())

# ------------------------------------------------------------------ the torso's mass: a keeled chest, flight muscles, withers
sp.add_part('Swell', {
    'id': 'chest',
    'at': at(0),
    'mirror': 0,
    'toJoint': 3,
    'peakAt': 0.4,
    'keel': 0.07,
    'keelSharpness': 2.5,
    'breadth': 0.05,
    'breadthAroundDeg': 95,
    'breadthWidthDeg': 40,
    'hump': 0.04,
    'inset': 0.08,
    'ringsPerJoint': 4,
    'around': 32,
}, rid=R())

sp.add_module('Walk', {
    'speed': 0.42,
    'turnRateDeg': 55,
    'accel': 1.4,
    'turnSlowdown': 0.5,
    'gaitSwayDeg': 5,
    'gaitRollDeg': 2,
    'subSteps': 2,
    'pattern': 1,
    'waveGroups': 4,
    'bob': {'amplitude': 0, 'hz': 0, 'damping': 0},
}, rid=R())

sp.add_module('Fly', {
    'spec': {
        'cruiseSpeed': 3,
        'minSpeed': 2,
        'maxSpeed': 6,
        'kGrip': 0.4,
        'vGripMin': 2.2,
        'kLift': 0.75,
        'kDrag': 0.01,
        'upstrokeArea': 0.15,
        'featherDeg': 8.6,
        'climbGain': 0.6,
        'climbDamping': 0.5,
        'climbIntegral': 0.8,
        'climbIntegralLimit': 0.6,
        'thrustGain': 0.5,
        'demandFloor': 0.25,
        'descendCutoff': -0.6,
        'glideDemand': 0.1,
        'amplitudeMin': 0.7,
        'strokeRateMin': 2.5,
        'strokeRateMax': 6,
        'glideRelax': 4,
        'maxBankDeg': 40,
        'bankGain': 3,
        'bankDamping': 0.5,
        'maxAsymmetry': 0.4,
        'rollGain': 1,
        'rollDamping': 4,
        'glideAlphaDeg': 6,
        'flareAlphaDeg': 45,
        'launchSpeed': 2.5,
        'flareDistance': 2.2,
        'minAltitude': 1.2,
        'cruiseAltitudeFrac': 0.6,
        'airUndulationDeg': 0,
        'airUndulationHz': 0,
        'tailSag': 0,
        'headStabilise': 0.3,
        'pursuitGain': 1.6,
        'legTuckOut': 0.3,
        'legTuckDown': 0.3,
        'legTuckReach': 0.9,
        'touchdownTogether': 1,
        'airLevel': 0.8,
        'glideEnergy': 1,
        'approachBleedsSpeed': 1,
    },
    'twist': {
        'swimAmplitude': 0,
        'swimHz': 0,
        'swimVertical': 0,
        'swimRollDeg': 0,
        'corkscrewAmplitude': 0,
        'corkscrewHz': 0,
        'corkscrewTurns': 0,
        'corkscrewRolls': 0,
        'barrelRadius': 0,
        'barrelTime': 0,
        'loopRadius': 0,
        'loopSpin': 0,
        'slowRollTime': 0,
        'intervalMin': 0,
        'intervalMax': 0,
        'uprightRateDeg': 0,
        'girdleBob': 0,
        'bobHead': 0,
        'bobTail': 0,
        'bobInPhase': 0,
        'bobHighPass': 0,
    },
}, rid=R())


sp.add_module('QuadLaunch', {
    'crouchSeconds': 0.5,
    'crouchDepth': 0.14,
    'vaultSpeed': 4.2,
}, rid=R())

sp.add_module('Wander', None, rid=R())

sp.add_module('FlightExcursion', None, rid=R())

sp.add_module('SkyHunt', {
    'soarAltitude': 8,
    'soarBand': 1.2,
    'circleRadius': 0.55,
    'boutSeconds': 80,
    'restSeconds': 25,
    'stoopInterval': 20,
    'stoopDeg': 50,
    'pullUpHeight': 0.8,
    'stoopFold': 0.9,
    'practice': 1,
    'utility': 0.4,
    'handBack': 1,
}, rid=R())

# deep maroon-purple with teal bands across the back, a pale cream throat and belly
sp.add_module('Skin', {
    'dorsal': K.C(0.3, 0.07, 0.17, 1),
    'flank': K.C(0.42, 0.13, 0.25, 1),
    'belly': K.C(0.84, 0.78, 0.68, 1),
    'pattern': K.C(0.1, 0.3, 0.29, 1),
    'patternType': 1,
    'patternScale': 6,
    'patternContrast': 0.35,
    'patternSoftness': 0.6,
    'spotScale': 0,
    'smoothness': 0.45,
    'rimStrength': 0.4,
    'subsurface': 0.3,
    'sizeJitter': 0.06,
    'hueJitter': 0.015,
    'valueJitter': 0.05,
}, rid=R())

sp.add_module('Membrane', {
    'colour': K.C(0.08, 0.4, 0.36, 1),
    'translucency': 0.4,
    'veinContrast': 0.25,
    'rim': 0.3,
    'inner': 0.4,
}, rid=R())


def row(part, tint, amount, smooth):
    return {'partId': part, 'model': 4, 'noPattern': 1, 'limbPattern': 0, 'noGrain': 1, 'fineGrain': 0, 'noSheen': 0,
            'noShimmer': 0, 'tintSource': 0, 'tint': tint, 'tintAmount': amount, 'smoothnessScale': smooth}


# restyled rows (no new ids): 2 = teeth, the pale jaw, the knuckle spurs and claws (bone ivory); 13 = the teal casque
sp.add_module('Layers', {
    'rows': [
        row(2, K.C(0.86, 0.8, 0.7, 1), 0.85, 0.7),
        row(13, K.C(0.1, 0.6, 0.55, 1), 0.95, 1.1),
    ],
}, rid=R())


if __name__ == '__main__':
    tg = sys.argv[1] if len(sys.argv) > 1 else os.environ.get('CREATURE_TG')
    if not tg:
        sys.exit('usage: %s <Assets/TerrainGen> (or set CREATURE_TG)' % sys.argv[0])
    print('wrote', sp.save(os.path.join(tg, 'Config', 'Creatures', sp.name + '.asset')))
