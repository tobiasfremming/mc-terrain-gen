# Creature kit catalogue

GENERATED - do not edit. Regenerate: Unity menu *Marching Cubes > Creature Kit > Regenerate Catalogue*, or offline
`dotnet Tools~/CreatureHarness/bin/Release/net8.0/CreatureHarness.dll catalogue --write` (the `creature-kit` skill).
`Catalogue_IsFresh` fails when this file is stale.

Read this first. **Reuse before you invent**: every row says which species already use it. To make an animal, copy the
nearest species below (`CreatureHarness kit-convert <Legacy> <New>` writes a legacy one in the kit format) and edit
its parts and modules; add a new part or module type only when nothing here fits.

Units: lengths in metres at scale 1. `size` is how a value follows the individual's sizeScale s (Froude): lengths x s,
times and speeds x sqrt(s), rates / sqrt(s); the rest is size-free. Authoring sugar, expanded at load: `mirror` on any
part (<id>.L then <id>.R), `Repeat` (rows of parts), `SpineBody.profile` (keys instead of every joint), `Walk.pattern`
(gait groups of limbs left at -1).

## Part types

| part | kind | status | bones | material layers | use when | used by |
|---|---|---|---|---|---|---|
| **SoftBody** | Body | exists | 6 | 5, 6, 7 | boneless bodies: octopus, slug, jelly | TreeOctopus |
| **SpineBody** | Body | exists | = joints | 0 | anything with a backbone or a segmented trunk | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, KitMonitor, Tarantula, TestHexapod, TestSnake, WingedSerpent |
| **ArthropodLeg** | Limb | exists | = segments | 1, 2, 9 | insects, spiders, crabs, centipedes; anything with visible segments | DaddyLongLegs, DesertBeetle, Tarantula |
| **Leg2Bone** | Limb | exists | 3 | 1, 2 | lizards, mammals, birds' legs, wing arms | DesertMonitor, KitMonitor, TestHexapod, WingedSerpent |
| **Wing** | Wing | exists | 7 | 8, 1, 2 | bats, pterosaurs, dragons; the Fly module makes it fly | WingedSerpent |
| **Eyes** | Sense | exists | 2 | 3 | always (every shipped species has two eye bones) | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, KitMonitor, Tarantula, TestHexapod, TestSnake, TreeOctopus, WingedSerpent |
| **Tentacle** | Chain | exists | = spec.segments (0 = 24) | 4 | octopus arms; grabbing arms on any body (after T5.9) | TreeOctopus |
| **RigidCone** | Rigid | exists | 0 (1 with ownBone) | 2 | horns, spikes, fangs, mandibles, chelicerae | Tarantula, TreeOctopus |
| **Repeat** | Sugar | exists | = count x template |  | centipede legs, spike rows, many fins | - |

## Modules

| module | category | status | needs | used by | what |
|---|---|---|---|---|---|
| **Ambush** | Behaviour | exists | SoftBody + Tentacle parts (spine bodies: T5.9) | TreeOctopus | Rest / explore / stalk / ambush / strike / hold / retreat (OctopusBrain + prey adapters; arm roles, grooming). Hold 1.5 s, no damage. |
| **ClimbExcursion** | Behaviour | exists | Climb | DesertMonitor, KitMonitor | Approach a climbable obstacle, climb to a goal on its real surface, rest, descend (ClimbingBrain wrapping Wander). |
| **FlightExcursion** | Behaviour | exists | Fly | WingedSerpent | Rest, Face, Patrol, Soar, Landing on perches; predictive wall turn-back (FlightBrain). |
| **Wander** | Behaviour | exists |  | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, KitMonitor, Tarantula, TestHexapod, TestSnake, WingedSerpent | Safe-disc points, walk, pause, glance (WanderBrain). Containment is structural: the head is clamped to the safe radius. |
| **Climb** | Locomotion | exists | Walk | DesertMonitor, KitMonitor | Per-joint up field, real-geometry contact via ISurfaceProbe, pitch limiter (CreatureClimb, ClimbSpec). Terrain walls >= 50 deg over >= 1/2 body length (R30/R31). 0 = default for every number. |
| **Fly** | Locomotion | exists | Wing parts | WingedSerpent | Ground -> Launch -> Air <-> Glide -> Flare -> Touchdown -> Perched (CreatureFlight, FlightModel, AirChain). Froude-scaled. |
| **Slither** | Locomotion | exists | Walk | DesertMonitor, DesertSnake, KitMonitor, TestSnake, WingedSerpent | Lateral head wave the drag chain carries back (undulation in the walk). Long wave: lambda = speed / Hz >= 0.7 body length (R27). |
| **Walk** | Locomotion | exists | SpineBody | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, KitMonitor, Tarantula, TestHexapod, TestSnake, WingedSerpent | Head steering in the tangent plane, chain, grounding, gait groups, girdle sway (CreatureSim.SubStep, CreatureLegs). The default locomotion of a spine body. |
| **Chitin** | Look | exists |  | DaddyLongLegs, DesertBeetle, Tarantula | Knee/segment bands (part 9) and a velvet sheen lobe: arthropod legs. |
| **Chromatophore** | Look | exists | SoftBody | TreeOctopus | Dynamic colour cells, flash and passing-cloud phase (octopus shader). Makes the look dynamic. |
| **Cloak** | Look | exists | SoftBody (spine bodies: Phase 4) | TreeOctopus | Dissolve in Forward only: the body vanishes, shadow + SSAO stay (the user's favourite bug). Per-instance, brain-driven, with hysteresis. Octopus today. |
| **Membrane** | Look | exists |  | WingedSerpent | Translucent veined membrane (part 8, CreatureMembrane.hlsl): wings today, fins next (T5.1). |
| **Skin** | Look | exists |  | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, KitMonitor, Tarantula, TestHexapod, TestSnake, TreeOctopus, WingedSerpent | Palette dorsal/flank/belly + procedural pattern by rest arc length (CreaturePattern.hlsl); per-individual size/hue/value jitter. Layers of parts 0-2. |

## Species (examples to copy)

| species | format | plan | parts | modules | bones |
|---|---|---|---|---|---|
| DaddyLongLegs | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes | Walk (Tetrapod), Wander, Skin, Chitin | 71 |
| DesertBeetle | legacy | Spine | SpineBody, 6x ArthropodLeg, Eyes | Walk (Tripod), Wander, Skin, Chitin | 35 |
| DesertMonitor | legacy | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| DesertSnake | legacy | Spine | SpineBody, Eyes | Walk, Slither, Wander, Skin | 34 |
| KitMonitor | kit | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| Tarantula | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes, RigidCone | Walk (Tetrapod), Wander, Skin, Chitin | 75 |
| TestHexapod | legacy | Spine | SpineBody, 6x Leg2Bone, Eyes | Walk (Tripod), Wander, Skin | 28 |
| TestSnake | legacy | Spine | SpineBody, Eyes | Walk, Slither, Wander, Skin | 42 |
| TreeOctopus | legacy | Soft | 8x Tentacle, SoftBody, Eyes, RigidCone | Ambush, Skin, Cloak, Chromatophore | 201 |
| WingedSerpent | legacy | Spine | SpineBody, 4x Leg2Bone, 2x Wing, Eyes | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane | 61 |

## Parameters

### SoftBody

Shape-matched soft lattice (OctopusBody/SoftLattice: 14 particles, 5 clusters) driven by its Tentacle chains: mantle, head, web. System: OctopusBody (self-ticking ICreatureBody).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `spec` | CephalopodSpec | Spec | - | the octopus body (CephalopodSpec: web, head, mantle, siphon, lattice); its `arm` comes from the Tentacle parts - fields: armCount, arm, bodyMass, webRadius, webDepth, headRadius, mantleRadius, mantleLength, mantleTiltDeg, eyeRadius, siphonLength, siphonRadius, beta, headStiffness, tipStiffness, breathAmp, breathHz, bodyDamping, subSteps, iterations, look, cloakEnabled |  |  |

### SpineBody

Bend-limited follow-the-leader chain riding the ground or a climbed surface (CreatureSim). The body of every walker, snake and the wyvern. System: CreatureSim chain + grounding.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `spacing` | float | Metres | x s | segment length (joint spacing) |  | monitor 0.06, snake 0.03 |
| `headCapLength` | float | Metres | x s | snout cap length in front of joint 0 |  |  |
| `tailTipLength` | float | Metres | x s | pointed tail tip behind the last joint |  |  |
| `joints` | JointDef[] | Struct | - | the joints written out, head first; when empty they come from profile + jointCount |  |  |
| `joints[].halfWidth` | float | Metres | x s | cross-section half width |  |  |
| `joints[].halfHeight` | float | Metres | x s | cross-section half height |  |  |
| `joints[].clearance` | float | Metres | x s | joint centre above the ground; >= halfHeight for a walker |  |  |
| `joints[].bendLimitDeg` | float | Degrees | - | cone half-angle vs the previous segment |  | monitor 20, snake 24-30 |
| `joints[].rigid` | bool | Flag | - | bend limit 0 (neck stiffness) |  |  |
| `profile` | ProfileKey[] | Struct | - | profile keys (sugar): interpolated to jointCount joints; >= 2 keys, t = 0 and t = 1 included |  |  |
| `profile[].t` | float | Fraction | - | place along the spine, 0 = joint 0 (head) .. 1 = last joint | 0..1 |  |
| `profile[].halfWidth` | float | Metres | x s | cross-section half width |  |  |
| `profile[].halfHeight` | float | Metres | x s | cross-section half height |  |  |
| `profile[].clearance` | float | Metres | x s | joint centre above the ground |  |  |
| `profile[].bendLimitDeg` | float | Degrees | - | cone half-angle vs the previous segment |  |  |
| `jointCount` | int | Count | - | joints made from the profile (ignored when joints are written out) | 2..128 |  |
| `rigidHead` | int | Count | - | profile: joints 1..rigidHead are rigid (neck stiffness) | 0..8 |  |
| `ringSegments` | int | Count | - | mesh: vertices around each ring |  | default 16 |
| `ringsPerSegment` | int | Count | - | mesh: rings per spine segment |  | default 3 |

### ArthropodLeg

3-7 rigid segments solved by synergy IK (ArthropodLegIK): high knees, tarsus at a contact angle, knee bands. Feeler role = a pedipalp. System: CreatureLegs + ArthropodLegModel.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `role` | Walk / Feeler / WingArm / Folded | Enum | - | Walk steps in its gait group; Feeler is posed, never plants; WingArm is set by compose for a Wing's arm; Folded is planned (T5.4) |  |  |
| `gaitGroup` | int | Count | - | gait group (one group steps at a time); -1 = from the Walk module's pattern | -1..31 |  |
| `restOffset` | Vector3 | Metres | x s | foot rest point in the attach frame (x mirrored by side; y ignored: projected to the ground) |  |  |
| `stepThreshold` | float | Metres | x s | foot travel before it steps (comfort radius) |  | monitor 0.11 |
| `stepDuration` | float | Seconds | x sqrt(s) | step duration |  | monitor 0.12 |
| `stepLift` | float | Metres | x s | step apex height |  |  |
| `overshoot` | float | Metres | x s | the step lands this far ahead of the rest point |  |  |
| `footLength` | float | Metres | x s | foot (tarsus claw) length |  |  |
| `toeCount` | int | Count | - | toes (arthropod: tarsus claws) | 0..6 |  |
| `forwardSwingLimitDeg` | float | Degrees | - | upper bone (arthropod: coxa yaw) swing limit toward the head, 0 = 55 |  |  |
| `backSwingLimitDeg` | float | Degrees | - | swing limit toward the tail, 0 = 55 |  |  |
| `kneeRangeDeg` | float | Degrees | - | knee range around its rest bend, 0 = 35 (arthropod: reach synergy extension) |  |  |
| `kneeFlexDeg` | float | Degrees | - | knee flexion past rest, 0 = kneeRange (arthropod: reach synergy flexion) |  |  |
| `segments` | SegmentDef[] | Struct | - | segments body outward (coxa .. tarsus), one bone each |  |  |
| `segments[].length` | float | Metres | x s | segment length |  |  |
| `segments[].radiusStart` | float | Metres | x s | tube radius at the start |  |  |
| `segments[].radiusEnd` | float | Metres | x s | tube radius at the end |  |  |
| `segments[].restBendDeg` | float | Degrees | - | rest bend at the start joint (+ raises; segment 0: elevation) |  |  |
| `segments[].minBendDeg` | float | Degrees | - | hinge lower limit |  |  |
| `segments[].maxBendDeg` | float | Degrees | - | hinge upper limit |  |  |
| `segments[].flexWeight` | float | Fraction | - | share of the reach synergy; the knee is +-1 | -1..1 |  |
| `segments[].liftWeight` | float | Fraction | - | share of the swing's knee-lift synergy | -1..1 |  |
| `segments[].axisTiltDeg` | float | Degrees | - | hinge axis rotated about the segment (0 = planar) |  |  |
| `segments[].knuckle` | float | Fraction | - | joint bulb at the end: radius = knuckle x the larger adjacent radius (0 = none) |  |  |
| `segments[].bandStart` | float | Fraction | - | start of the band-coloured stretch (fraction of the segment) | 0..1 |  |
| `segments[].bandEnd` | float | Fraction | - | end of the band-coloured stretch | 0..1 |  |
| `contactAngleDeg` | float | Degrees | - | tarsus angle to the ground, 0 = 20 |  |  |
| `kneeLiftDeg` | float | Degrees | - | lift synergy at mid-swing (reshapes the leg; the foot stays on its path) |  |  |
| `swingRetract` | float | Fraction | - | mid-swing pull of the foot toward the coxa (flexing lifts the knee) | 0..0.5 |  |
| `elevMinDeg` | float | Degrees | - | in-plane pitch lower bound (0, 0 = +-90); outside it the planted foot steps |  |  |
| `elevMaxDeg` | float | Degrees | - | in-plane pitch upper bound |  |  |
| `liftDelay` | float | Fraction | - | lift-off delay per place behind the group's rearmost leg (metachronal wave) | 0..0.5 |  |
| `kneeJoint` | int | Count | - | joint reported as the knee (debug, tests); 0 = the largest flexWeight |  |  |
| `legSides` | int | Count | - | mesh: sides of the segment tubes, 0 = 8 |  |  |

### Leg2Bone

Vertebrate two-bone limb: TwoBoneIK with a pole, rounded knee blend, swing/knee limits (R16), keep-out (R28). Also the wyvern's wing arm. System: CreatureLegs (gait groups, throttle, sway).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `role` | Walk / Feeler / WingArm / Folded | Enum | - | Walk steps in its gait group; Feeler is posed, never plants; WingArm is set by compose for a Wing's arm; Folded is planned (T5.4) |  |  |
| `gaitGroup` | int | Count | - | gait group (one group steps at a time); -1 = from the Walk module's pattern | -1..31 |  |
| `restOffset` | Vector3 | Metres | x s | foot rest point in the attach frame (x mirrored by side; y ignored: projected to the ground) |  |  |
| `stepThreshold` | float | Metres | x s | foot travel before it steps (comfort radius) |  | monitor 0.11 |
| `stepDuration` | float | Seconds | x sqrt(s) | step duration |  | monitor 0.12 |
| `stepLift` | float | Metres | x s | step apex height |  |  |
| `overshoot` | float | Metres | x s | the step lands this far ahead of the rest point |  |  |
| `footLength` | float | Metres | x s | foot (tarsus claw) length |  |  |
| `toeCount` | int | Count | - | toes (arthropod: tarsus claws) | 0..6 |  |
| `forwardSwingLimitDeg` | float | Degrees | - | upper bone (arthropod: coxa yaw) swing limit toward the head, 0 = 55 |  |  |
| `backSwingLimitDeg` | float | Degrees | - | swing limit toward the tail, 0 = 55 |  |  |
| `kneeRangeDeg` | float | Degrees | - | knee range around its rest bend, 0 = 35 (arthropod: reach synergy extension) |  |  |
| `kneeFlexDeg` | float | Degrees | - | knee flexion past rest, 0 = kneeRange (arthropod: reach synergy flexion) |  |  |
| `upperLength` | float | Metres | x s | upper bone length | 0.005..2 | monitor 0.145 |
| `lowerLength` | float | Metres | x s | lower bone length | 0.005..2 | monitor 0.135 |
| `upperRadius` | float | Metres | x s | upper bone radius |  |  |
| `lowerRadius` | float | Metres | x s | lower bone radius |  |  |
| `pole` | Vector3 | Direction | - | knee direction hint in the attach frame (x mirrored); needs a clear fore/aft part (front z < 0, rear z > 0) or the skin folds |  |  |
| `kneeBlend` | float | Fraction | - | share of each bone blended into the rounded knee, 0 = 0.45 | 0..0.49 |  |

### Wing

3-finger membrane wing on an arm limb (a Leg2Bone it names); a wingbeat is a step with a vertical comfort band (CreatureWings, FlightModel). System: CreatureFlight + CreatureWings.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `arm` | string | PartRef | - | the Leg2Bone this wing rides on (its role becomes WingArm); a mirrored wing names the unmirrored id and gets .L / .R |  |  |
| `spec` | WingSpec | Spec | - | the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm` - fields: leg, restTip, comfortUp, comfortDown, strokePlaneDeg, pronationDeg, overshootUp, upstrokeDuration, upstrokeFold, foldReach, beatGroup, fingerLengths, fingerSpreadDeg, fingerFoldDeg, fingerMinGapDeg, fingerFoldGapDeg, groundLiftDeg, groundWristLift, groundFlankRise, fingerRadius, thumbLength, membraneRearJoint, membraneFrontJoint, trailingRib, membraneInset, area, scallop |  |  |

### Eyes

Two glossy eye spheres on their own bones (sp.EyeBone), placed at the socket (joint, offset x mirrored); cluster eyes ride them (spiders). System: CreatureSim.WriteBoneWorld.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `radius` | float | Metres | x s | eye radius (0 = no visible eyes; the bones stay) |  |  |
| `cluster` | EyeSpotDef[] | Struct | - | extra eyes rigid to the eye bones |  |  |
| `cluster[].offset` | Vector3 | Metres | x s | offset from the eye centre in the eye joint's frame (x mirrored; x = 0: one eye) |  |  |
| `cluster[].radius` | float | Metres | x s | radius |  |  |

### Tentacle

Verlet + XPBD-lite particle chain (TentacleChain) that wraps real bark (WrapMarch); driven by TentacleAgent (reach, grip, hold). Octopus arms today; socket-rooted on a spine body is T5.9. System: TentacleChain + TentacleAgent / ArmCoordinator.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `spec` | TentacleSpec | Spec | - | the arm (TentacleSpec: segments, length, radii, stretch, twist, muscle, suckers) - fields: segments, length, rootRadius, tipRadius, taper, flatten, bendLimitDeg, tipCurlDeg, minStretch, maxStretch, maxTwistDeg, twistRateDeg, wrapSqueeze, bulge, muscle, damping, massPerArm, sinusAmpDeg, sinusHz, sinusWaves, wrapPitchDeg, wrapReach, reachSpeed, strikeTime, suckerRows, suckerSize |  |  |

### RigidCone

Curved cone rigid to a spine joint: horn, spike, claw, chelicera with a fang (BodyAppendage). mirror emits the pair from this one part (no .L/.R: it has no bones). ownBone: the octopus siphon. System: mesh only (CreatureMeshBuilder).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `direction` | Vector3 | Direction | - | direction in the joint frame (x mirrored) |  |  |
| `length` | float | Metres | x s | cone length |  |  |
| `radiusStart` | float | Metres | x s | base radius |  |  |
| `radiusEnd` | float | Metres | x s | tip radius |  |  |
| `curveDeg` | float | Degrees | - | curl downward along the cone |  |  |
| `fangLength` | float | Metres | x s | a fang continuing from the tip (0 = none) |  |  |
| `ownBone` | bool | Flag | - | a bone of its own, posed by its system (the octopus siphon), instead of rigid to the joint |  |  |

### Repeat

Authoring sugar: `count` copies of `template` across joints fromJoint..toJoint (tapered by lastScale, mirrored if the template is). Expanded by compose. System: compose (CompiledSpecies).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `count` | int | Count | - | copies | 1..64 |  |
| `fromJoint` | int | Joint | - | attach joint of the first copy |  |  |
| `toJoint` | int | Joint | - | attach joint of the last copy |  |  |
| `lastScale` | float | Fraction | - | length factor at the last copy (1 = no taper) |  | default 1 |
| `template` | CreaturePart | Struct | - | the part to copy (any part type; its at.joint is replaced) |  |  |

### Ambush

Rest / explore / stalk / ambush / strike / hold / retreat (OctopusBrain + prey adapters; arm roles, grooming). Hold 1.5 s, no damage.

No parameters.

### ClimbExcursion

Approach a climbable obstacle, climb to a goal on its real surface, rest, descend (ClimbingBrain wrapping Wander).

No parameters.

### FlightExcursion

Rest, Face, Patrol, Soar, Landing on perches; predictive wall turn-back (FlightBrain).

No parameters.

### Wander

Safe-disc points, walk, pause, glance (WanderBrain). Containment is structural: the head is clamped to the safe radius.

No parameters.

### Climb

Per-joint up field, real-geometry contact via ISurfaceProbe, pitch limiter (CreatureClimb, ClimbSpec). Terrain walls >= 50 deg over >= 1/2 body length (R30/R31). 0 = default for every number.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `maxSurfaceAngleDeg` | float | Degrees | - | steepest surface, from gravity's up (0 = 140) |  |  |
| `maxHeightBodyLengths` | float | Fraction | - | highest climb in body lengths (0 = 1.5) |  |  |
| `speedFactor` | float | Fraction | - | speed factor on a vertical surface, up (0 = 0.5) |  |  |
| `descentSpeedFactor` | float | Fraction | - | speed factor going down head-first (0 = 0.4) |  |  |
| `clearanceFactor` | float | Fraction | - | body clearance on a wall vs walking (0 = 0.65) |  |  |
| `strideFactor` | float | Fraction | - | stride on a wall (0 = 0.6) |  |  |
| `stepTimeFactor` | float | Fraction | - | step time on a wall (0 = 1.2) |  |  |
| `liftFactor` | float | Fraction | - | foot lift on a wall (0 = 0.6) |  |  |
| `upRateDeg` | float | DegPerSecond | / sqrt(s) | fastest turn of a joint's up (0 = 270) |  |  |
| `upSmoothJoints` | int | Count | - | joints each side smoothed into the up field (0 = 3) |  |  |
| `pitchLimitDeg` | float | Degrees | - | largest pitch bend between segments (0 = 25) |  |  |
| `minWidthFactor` | float | Fraction | - | a climb goal needs a surface this many body widths across (0 = 1.5) |  |  |

### Fly

Ground -> Launch -> Air <-> Glide -> Flare -> Touchdown -> Perched (CreatureFlight, FlightModel, AirChain). Froude-scaled.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `spec` | FlightSpec | Spec | - | flight (FlightSpec: speeds, lift/drag, altitude PID, stroke, banking, flare, air undulation) - fields: cruiseSpeed, minSpeed, maxSpeed, kGrip, vGripMin, kLift, kDrag, upstrokeArea, featherDeg, climbGain, climbDamping, climbIntegral, climbIntegralLimit, thrustGain, demandFloor, descendCutoff, glideDemand, amplitudeMin, strokeRateMin, strokeRateMax, glideRelax, maxBankDeg, bankGain, bankDamping, maxAsymmetry, rollGain, rollDamping, glideAlphaDeg, flareAlphaDeg, launchSpeed, flareDistance, minAltitude, cruiseAltitudeFrac, airUndulationDeg, airUndulationHz, tailSag, headStabilise |  |  |

### Slither

Lateral head wave the drag chain carries back (undulation in the walk). Long wave: lambda = speed / Hz >= 0.7 body length (R27).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `undulationDeg` | float | Degrees | - | head wave amplitude |  | snake 45, monitor 6 |
| `undulationHz` | float | Hz | / sqrt(s) | wave frequency |  | snake 0.625 |

### Walk

Head steering in the tangent plane, chain, grounding, gait groups, girdle sway (CreatureSim.SubStep, CreatureLegs). The default locomotion of a spine body.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `speed` | float | MetresPerSecond | x sqrt(s) | head cruise speed |  | monitor 0.9, tarantula 0.16 |
| `turnRateDeg` | float | DegPerSecond | / sqrt(s) | turn rate |  | walkers 160, snake 240 |
| `accel` | float | Acceleration | - | acceleration |  |  |
| `turnSlowdown` | float | Fraction | - | speed lost in a full-rate turn |  |  |
| `gaitSwayDeg` | float | Degrees | - | peak yaw of the trunk between girdles toward the reaching leg (0 = none) |  |  |
| `gaitRollDeg` | float | Degrees | - | girdle roll over its planted feet |  |  |
| `subSteps` | int | Count | - | fixed sub-steps per tick (cost!) | 1..4 | default 2 |
| `pattern` | Explicit / Diagonal / Tripod / Tetrapod / Alternate / Wave | Enum | - | assigns gaitGroup to walking limbs left at -1 |  |  |
| `waveGroups` | int | Count | - | groups of a Wave pattern | 2..31 |  |
| `bob` | GaitBobDef | Struct | - | vertical body bounce kicked by each lift-off |  |  |
| `bob.amplitude` | float | Metres | x s | peak dip of the body per lift-off (0 = off) |  |  |
| `bob.hz` | float | Hz | / sqrt(s) | spring frequency |  |  |
| `bob.damping` | float | Fraction | - | damping ratio (0.2 bouncy .. 1 dead) | 0..2 |  |

### Chitin

Knee/segment bands (part 9) and a velvet sheen lobe: arthropod legs.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `bandColor` | Color | SrgbColour | - | band colour (SegmentDef.bandStart/End) |  |  |
| `sheen` | float | Fraction | - | velvet sheen strength (0 = none) | 0..1 |  |
| `sheenColor` | Color | SrgbColour | - | sheen colour |  |  |

### Chromatophore

Dynamic colour cells, flash and passing-cloud phase (octopus shader). Makes the look dynamic.

No parameters.

### Cloak

Dissolve in Forward only: the body vanishes, shadow + SSAO stay (the user's favourite bug). Per-instance, brain-driven, with hysteresis. Octopus today.

No parameters.

### Membrane

Translucent veined membrane (part 8, CreatureMembrane.hlsl): wings today, fins next (T5.1).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `colour` | Color | SrgbColour | - | membrane colour |  |  |
| `translucency` | float | Fraction | - | how much light shines through | 0..1 |  |
| `veinContrast` | float | Fraction | - | how dark the veins are | 0..1 |  |
| `rim` | float | Fraction | - | how dark the trailing rim is | 0..1 |  |

### Skin

Palette dorsal/flank/belly + procedural pattern by rest arc length (CreaturePattern.hlsl); per-individual size/hue/value jitter. Layers of parts 0-2.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `dorsal` | Color | SrgbColour | - | back colour |  |  |
| `flank` | Color | SrgbColour | - | flank colour |  |  |
| `belly` | Color | SrgbColour | - | belly colour |  |  |
| `pattern` | Color | SrgbColour | - | pattern colour |  |  |
| `patternType` | None / Bands / Spots / Stripes / BandedSpots | Enum | - | pattern |  |  |
| `patternScale` | float | Fraction | - | pattern repeats per metre of rest arc |  | monitor 9, snake 11 |
| `patternContrast` | float | Fraction | - | pattern contrast | 0..1 |  |
| `patternSoftness` | float | Fraction | - | pattern edge softness | 0..1 |  |
| `spotScale` | float | Fraction | - | spot size |  |  |
| `smoothness` | float | Fraction | - | smoothness | 0..1 |  |
| `rimStrength` | float | Fraction | - | rim light strength | 0..1 |  |
| `subsurface` | float | Fraction | - | subsurface | 0..1 |  |
| `sizeJitter` | float | Fraction | - | per-individual size jitter (+-), sizeScale = scale x (1 +- this) | 0..0.2 |  |
| `hueJitter` | float | Fraction | - | per-individual hue jitter |  |  |
| `valueJitter` | float | Fraction | - | per-individual value jitter |  |  |

## Material layers (mesh part ids; next free: 10)

- 0: body skin
- 1: limb
- 2: foot, claw, rigid cone
- 3: eye
- 4: octopus arm
- 5: octopus web
- 6: octopus mantle
- 7: octopus siphon
- 8: membrane (wings; fins next)
- 9: arthropod band

A new shading needs the next free id (and a pixel guard test); never reuse one.
