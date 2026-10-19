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
| **SoftBody** | Body | exists | 6 + mouthCount x mouth.segments | 5, 6, 7 | boneless bodies: octopus, slug, jelly | Swinger, TreeOctopus |
| **SpineBody** | Body | exists | = joints | 0 | anything with a backbone or a segmented trunk | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, KitMonitor, SkyDragon, Tarantula, TestHexapod, TestSnake, TreeSpider, WingedSerpent |
| **ArthropodLeg** | Limb | exists | = segments | 1, 2, 9 | insects, spiders, crabs, centipedes; anything with visible segments | DaddyLongLegs, DesertBeetle, Tarantula, TreeSpider |
| **Leg2Bone** | Limb | exists | 3 | 1, 2 | lizards, mammals, birds' legs, wing arms | DesertMonitor, KitMonitor, SkyDragon, TestHexapod, WingedSerpent |
| **Wing** | Wing | exists | 6 + max(1, spec.flankPatches), +1 with spec.groundTallStance | 8, 1, 2 | bats, pterosaurs, dragons; the Fly module makes it fly | SkyDragon, WingedSerpent |
| **Eyes** | Sense | exists | 2 | 3 | always (every shipped species has two eye bones) | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, KitMonitor, SkyDragon, Swinger, Tarantula, TestHexapod, TestSnake, TreeOctopus, TreeSpider, WingedSerpent |
| **Antenna** | Chain | exists | = segments | 8 | whiskers, barbels, antennae, feelers, cerci, ribbon-like streamers | SkyDragon |
| **Tentacle** | Chain | exists | = spec.segments (0 = 24) | 4 | octopus arms; grabbing arms on any body (after T5.9) | Swinger, TreeOctopus |
| **FinStrip** | Fin | exists | 0 (skinned to the spine) | 8 | manes, crests, dorsal / anal fins, tail tufts, frills along a body | FinnedSandEel, SkyDragon |
| **Flapper** | Fin | exists | = segments (1-3) | 8 | pectoral fins, ear flaps, frills, small paddles that beat or flutter (not wings that fly: Wing) | FinnedSandEel |
| **LegFin** | Fin | exists | 2 (thigh fin, shin fin) | 8 | finned or feathered legs: Caraxes-like leg frills, Microraptor leg wings, any flap that rides a limb and spreads in flight | SkyDragon |
| **RigidCone** | Rigid | exists | 0 (1 with ownBone) | 2 | horns, spikes, fangs, mandibles, chelicerae | SkyDragon, Swinger, Tarantula, TreeOctopus, TreeSpider |
| **RigidLoft** | Rigid | exists | 0 (rigid to its joint) | 0, 2 | head shapes: squared snouts, jaws, brows, nostrils, bosses; any rigid lump on a joint | SkyDragon |
| **Swell** | Rigid | exists | 0 (skinned to the spine) | 0 | a deep chest with flight muscles at the wing roots, shoulders, haunches, a belly - any bulk the body's elliptical profile cannot give | SkyDragon |
| **Repeat** | Sugar | exists | = count x template |  | centipede legs, spike rows, many fins | - |

## Modules

| module | category | status | needs | used by | what |
|---|---|---|---|---|---|
| **Ambush** | Behaviour | exists | SoftBody + Tentacle parts (spine bodies: T5.9) | Swinger, TreeOctopus | Rest / explore / stalk / ambush / strike / hold / retreat (OctopusBrain + prey adapters; arm roles, grooming). Hold 1.5 s, no damage. |
| **ClimbExcursion** | Behaviour | exists | Climb | DesertMonitor, KitMonitor, TreeSpider | Approach a climbable obstacle, climb to a goal on its real surface, rest, descend (ClimbingBrain wrapping Wander). |
| **Flee** | Behaviour | exists | Walk | FinnedSandEel | Runs straight away from the nearest threat (CreatureThreats: the viewer in play mode) once it comes within the alarm radius, sliding along the enclosure wall when cornered; calms down after it leaves. Gives the species a ModularBrain (with Wander as the idle default). |
| **FlightExcursion** | Behaviour | exists | Fly | SkyDragon, WingedSerpent | Rest, Face, Patrol, Soar, Landing on perches; predictive wall turn-back (FlightBrain). |
| **Wander** | Behaviour | exists |  | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, KitMonitor, SkyDragon, Tarantula, TestHexapod, TestSnake, TreeSpider, WingedSerpent | Safe-disc points, walk, pause, glance (WanderBrain). Containment is structural: the head is clamped to the safe radius. |
| **Climb** | Locomotion | exists | Walk | DesertMonitor, KitMonitor, TreeSpider | Per-joint up field, real-geometry contact via ISurfaceProbe, pitch limiter (CreatureClimb, ClimbSpec). Terrain walls >= 50 deg over >= 1/2 body length (R30/R31). 0 = default for every number. |
| **Fly** | Locomotion | exists | Wing parts | SkyDragon, WingedSerpent | Ground -> Launch -> Air <-> Glide -> Flare -> Touchdown -> Perched (CreatureFlight, FlightModel, AirChain). Froude-scaled. twist: the twisting Chinese-dragon air chain (AirTwist). |
| **Slither** | Locomotion | exists | Walk | DesertMonitor, DesertSnake, FinnedSandEel, KitMonitor, SkyDragon, TestSnake, WingedSerpent | Lateral head wave the drag chain carries back (undulation in the walk). Long wave: lambda = speed / Hz >= 0.7 body length (R27). |
| **Walk** | Locomotion | exists | SpineBody | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, KitMonitor, SkyDragon, Tarantula, TestHexapod, TestSnake, TreeSpider, WingedSerpent | Head steering in the tangent plane, chain, grounding, gait groups, girdle sway (CreatureSim.SubStep, CreatureLegs). The default locomotion of a spine body. |
| **Chitin** | Look | exists |  | DaddyLongLegs, DesertBeetle, Tarantula, TreeSpider | Knee/segment bands (part 9) and a velvet sheen lobe: arthropod legs. |
| **Chromatophore** | Look | exists | SoftBody | Swinger, TreeOctopus | Dynamic colour cells, flash and passing-cloud phase (octopus shader). Makes the look dynamic. |
| **Cloak** | Look | exists | SoftBody (spine bodies: Phase 4) | Swinger, TreeOctopus | Dissolve in Forward only: the body vanishes, shadow + SSAO stay (the user's favourite bug). Per-instance, brain-driven, with hysteresis. Octopus today. |
| **Layers** | Look | exists |  | - | Restyles part ids (material layers) for this species: shading model (Skin, Chitin, Membrane, Eye, Claw), tint, smoothness, and which layers (pattern, scale grain, sheen, shimmer) each has. Rows not listed keep the registry's (Kit/PartLayers.cs). A new part needs a row, never a shader edit. |
| **Membrane** | Look | exists |  | FinnedSandEel, SkyDragon, WingedSerpent | Translucent veined membrane (part 8, the Membrane shading model, CreatureMembrane.hlsl): wings, FinStrip fins, antennae - any part whose layer row is Membrane. |
| **Shimmer** | Look | exists |  | SkyDragon | Iridescent scale shimmer: thin-film colour bands (colour -> colour2 with the view angle) travelling head to tail along the body's back and flanks, a faint glow in each band (Creature.shader, part 0). The SkyDragon's signature; cheap (one pow, one cos per body pixel). |
| **Skin** | Look | exists |  | DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, KitMonitor, SkyDragon, Swinger, Tarantula, TestHexapod, TestSnake, TreeOctopus, TreeSpider, WingedSerpent | Palette dorsal/flank/belly + procedural pattern by rest arc length (CreaturePattern.hlsl); per-individual size/hue/value jitter. Layers of parts 0-2. |

## Species (examples to copy)

| species | format | plan | parts | modules | bones |
|---|---|---|---|---|---|
| DaddyLongLegs | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes | Walk (Tetrapod), Wander, Skin, Chitin | 71 |
| DesertBeetle | legacy | Spine | SpineBody, 6x ArthropodLeg, Eyes | Walk (Tripod), Wander, Skin, Chitin | 35 |
| DesertMonitor | legacy | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| DesertSnake | legacy | Spine | SpineBody, Eyes | Walk, Slither, Wander, Skin | 34 |
| FinnedSandEel | kit | Spine | SpineBody, Eyes, 2x FinStrip, 2x Flapper | Walk (Diagonal), Slither, Wander, Flee, Skin, Membrane | 54 |
| KitMonitor | kit | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| SkyDragon | kit | Spine | SpineBody, 4x Leg2Bone, 2x LegFin, 2x Wing, Eyes, 4x RigidCone, 5x FinStrip, 4x Antenna, Swell, 7x RigidLoft | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane, Shimmer | 148 |
| Swinger | legacy | Soft | 2x Tentacle, SoftBody, Eyes, RigidCone | Ambush, Skin, Cloak, Chromatophore | 97 |
| Tarantula | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes, RigidCone | Walk (Tetrapod), Wander, Skin, Chitin | 75 |
| TestHexapod | legacy | Spine | SpineBody, 6x Leg2Bone, Eyes | Walk (Tripod), Wander, Skin | 28 |
| TestSnake | legacy | Spine | SpineBody, Eyes | Walk, Slither, Wander, Skin | 42 |
| TreeOctopus | legacy | Soft | 8x Tentacle, SoftBody, Eyes, RigidCone | Ambush, Skin, Cloak, Chromatophore | 201 |
| TreeSpider | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes, RigidCone | Walk (Tetrapod), Climb, Wander, ClimbExcursion, Skin, Chitin | 75 |
| WingedSerpent | legacy | Spine | SpineBody, 4x Leg2Bone, 2x Wing, Eyes | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane | 73 |

## Parameters

### SoftBody

Shape-matched soft lattice (OctopusBody/SoftLattice: 14 particles, 5 clusters) driven by its Tentacle chains: mantle, head, web. Deforms everywhere: never a rigid head. System: OctopusBody (self-ticking ICreatureBody).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `spec` | CephalopodSpec | Spec | - | the octopus body (CephalopodSpec: web, head, mantle, siphon, lattice); its `arm` comes from the Tentacle parts - fields: armCount, arm, bodyMass, webRadius, webDepth, headRadius, mantleRadius, mantleLength, mantleTiltDeg, eyeRadius, siphonLength, siphonRadius, beta, headStiffness, tipStiffness, breathAmp, breathHz, bodyDamping, subSteps, iterations, minHolds, mouthCount, mouth, mouthRing, eyeStalk, mantlePear, beakLength, look, cloakEnabled |  |  |

### SpineBody

Bend-limited follow-the-leader chain riding the ground or a climbed surface (CreatureSim). The body of every walker, snake and the wyvern. Its head is rigid by default (rigidHeadJoints: auto from the profile; 0 = off). System: CreatureSim chain + grounding.

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
| `rigidHeadJoints` | int | Count | - | rigid head: the first N joints move as one rigid body (no bend, draping or twist between them; the neck behind bends). -1 = auto: the joints up to where the body first narrows (in its front half), 0 = off (the body deforms all the way) | -1..16 | default -1 (auto); monitor 3 (auto), snake 2 |
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
| `spec` | WingSpec | Spec | - | the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm` - fields: leg, restTip, comfortUp, comfortDown, strokePlaneDeg, pronationDeg, overshootUp, upstrokeDuration, upstrokeFold, foldReach, beatGroup, fingerLengths, fingerSpreadDeg, fingerFoldDeg, fingerMinGapDeg, fingerFoldGapDeg, groundLiftDeg, groundTallStance, flankPatches, groundWristLift, groundFlankRise, fingerRadius, thumbLength, membraneRearJoint, membraneFrontJoint, trailingRib, membraneInset, area, scallop, scallopByChord, membraneBillow, membraneDetail, fingerKnuckles, wristKnuckle |  |  |

### Eyes

Two glossy eye spheres on their own bones (sp.EyeBone), placed at the socket (joint, offset x mirrored); cluster eyes ride them (spiders). System: EyesRuntime (part runtime: writes the two eye bones).

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

### Antenna

Spring chain rooted on a spine joint: whiskers, barbels, feelers, cerci, streamers. Pulled to its rest curve (stiffer at the root), damped and dragged by the air: it trails a moving body and swings out of turns and rolls. Exact lengths, bend cone. System: CreatureAntennae (part runtime, stage AfterLimbs: every sub-step) + AntennaMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `direction` | Vector3 | Direction | - | rest direction at the root in the joint frame (x mirrored by side) |  |  |
| `length` | float | Metres | x s | length |  | dragon whisker 0.55 |
| `segments` | int | Count | - | segments (one bone each) | 2..16 |  |
| `radiusStart` | float | Metres | x s | tube radius at the root |  |  |
| `radiusEnd` | float | Metres | x s | tube radius at the tip |  |  |
| `stiffnessHz` | float | Hz | / sqrt(s) | pull toward the rest curve at the root (weaker toward the tip) |  | dragon 1.6 |
| `damping` | float | Fraction | - | damping ratio of that pull | 0..2 |  |
| `airDrag` | float | Hz | / sqrt(s) | air drag rate (1/s): how much the strand trails a moving body |  | dragon 0.8 |
| `droopDeg` | float | Degrees | - | the rest curve bends down by this much toward the tip |  |  |
| `maxBendDeg` | float | Degrees | - | bend cone between segments (0 = 35) | 0..90 |  |

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

### FinStrip

Double-sided membrane strip along the spine: mane, crest, dorsal or anal fin, tail tuft. Height profile, swept-back curl, scalloped tufts; its upper rows ride the spine flowJoints further back, so it trails and flows as the body bends and twists. System: mesh only (FinStrip.EmitMesh via FinStripMesh), skinned to the spine bones.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `toJoint` | int | Joint | - | last spine joint of the base (the first is at.joint) |  |  |
| `heightStart` | float | Metres | x s | height at the first joint |  | dragon mane 0.075 |
| `heightEnd` | float | Metres | x s | height at the last joint |  |  |
| `leanDeg` | float | Degrees | - | the strip curls back by this much over its height (0 = straight up); past 40 the curl folds onto the body when it bends | 0..40 |  |
| `tufts` | int | Count | - | scallops (tufts, flames) along the free edge, 0 = a straight edge | 0..200 |  |
| `tuftDepth` | float | Fraction | - | valley depth between tufts, fraction of the height | 0..0.9 |  |
| `flowJoints` | float | Fraction | - | how many joints further back the top row rides (the trail; 0 = rigid on its joint) | 0..4 |  |
| `inset` | float | Fraction | - | base buried this fraction of the body's radius | 0..0.6 |  |
| `aroundDeg` | float | Degrees | - | around the body: 0 = on the back, 180 = under the belly (mirror: also at minus this) |  |  |
| `columnsPerJoint` | int | Count | - | mesh columns per spine segment (0 = 2) | 0..8 |  |

### Flapper

A fin or flap on 1-3 bones that beats with the body wave: frequency = the undulation x beatsPerWave (idleHz without Slither), phase locked to the head's wave (a pair beats in phase), a root-to-tip ripple (waveLag), amplitude fading with speed to idleAmplitude. Double-sided veined membrane paddle. System: FlapperRuntime (part runtime, stage Late) + Flapper.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `length` | float | Metres | x s | span, root to tip |  | sand eel pectoral 0.045 |
| `rootChord` | float | Metres | x s | chord (fore-aft width) at the root |  |  |
| `tipChord` | float | Metres | x s | chord at the tip (the tip itself is rounded) |  |  |
| `segments` | int | Count | - | bones along the span | 1..3 | default 2 |
| `sweepDeg` | float | Degrees | - | swept back from straight out sideways | 0..80 |  |
| `droopDeg` | float | Degrees | - | rest droop below the joint's horizontal (negative: raised) | -45..60 |  |
| `beatDeg` | float | Degrees | - | beat amplitude at the tip (shared over the bones) | 0..80 | sand eel 28 |
| `beatsPerWave` | float | Fraction | - | beats per undulation cycle (the beat follows the body wave) | 0.25..8 | default 1 |
| `idleHz` | float | Hz | / sqrt(s) | beat frequency when the species does not undulate (no Slither) |  | 1.5 |
| `idleAmplitude` | float | Fraction | - | amplitude at a standstill, fraction of beatDeg (the idle flutter) | 0..1 |  |
| `waveLag` | float | Fraction | - | phase lag root -> tip in cycles over the span (the ripple) | 0..0.5 |  |
| `phaseDeg` | float | Degrees | - | phase of the beat against the head's wave |  |  |
| `rays` | int | Count | - | fin rays (main veins) across the chord | 1..12 | default 5 |

### LegFin

Spiny fin frills along the back of a Leg2Bone's thigh and shin (raked spines, scalloped edge): folded flat along the leg on the ground, spread and turned level toward the side in the air - with the hind legs trailing, a rear lift surface. System: LegFinRuntime (part runtime, stage Late) + LegFin.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `leg` | string | PartRef | - | the Leg2Bone it rides on; a mirrored fin names the unmirrored id and takes .L / .R |  |  |
| `thighHeight` | float | Metres | x s | thigh fin: tallest spine |  | dragon 0.07 |
| `shinHeight` | float | Metres | x s | shin fin: tallest spine |  | dragon 0.09 |
| `rootStart` | float | Fraction | - | the fins' root along each bone: from this fraction .. | 0..0.9 | default 0.12 |
| `rootEnd` | float | Fraction | - | .. to this fraction | 0.1..1 | default 0.92 |
| `spines` | int | Count | - | spines per fin (scallops between them along the free edge) | 1..16 | default 5 |
| `spineDepth` | float | Fraction | - | valley depth between spines, fraction of the height | 0..0.9 | default 0.45 |
| `rakeDeg` | float | Degrees | - | spread: the spines rake toward the foot by this much | 0..70 | default 35 |
| `groundFold` | float | Fraction | - | on the ground the fin folds to this fraction of its height (the spines laid back along the leg) | 0.1..1 | default 0.3 |
| `airDroopDeg` | float | Degrees | - | in the air the fin turns below the level by this much (droop; negative: above) | -45..45 |  |
| `foldSeconds` | float | Seconds | x sqrt(s) | the ground fold eases in over this time | 0.02..2 | default 0.3 |

### RigidCone

Curved cone rigid to a spine joint: horn, spike, claw, chelicera with a fang (BodyAppendage). mirror emits the pair from this one part (no .L/.R: it has no bones). ownBone: the octopus siphon. System: mesh only (RigidCone.EmitMesh: a horn on the emitter).

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

### RigidLoft

Lofted solid rigid to a spine joint: superellipse cross-sections (round to squared and flat-topped, separate above and below) keyed along a direction, curled and rolled. Snouts, jaws, brow ridges, nostril bumps, cheek plates, skull shapes the spine tube cannot express. System: mesh only (RigidLoft.EmitMesh), rigid to the joint's bone.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `direction` | Vector3 | Direction | - | the loft's axis in the joint frame (x mirrored by side); its up is the joint's up made perpendicular |  |  |
| `length` | float | Metres | x s | length along the axis |  | dragon muzzle 0.15 |
| `keys` | LoftKey[] | Struct | - | cross-sections along the loft (monotone cubic between them); >= 2, t = 0 and t = 1 included |  |  |
| `keys[].t` | float | Fraction | - | place along the loft, 0 = its start .. 1 = its end | 0..1 |  |
| `keys[].halfWidth` | float | Metres | x s | half width (across, the joint frame's x) |  |  |
| `keys[].halfHeight` | float | Metres | x s | half height (the loft's up) |  |  |
| `keys[].rise` | float | Metres | x s | centre line raised (+) or lowered (-) along the loft's up |  |  |
| `keys[].squareTop` | float | Fraction | - | superellipse exponent of the upper half: 2 = round, 4 = squared, flat-topped | 1..12 |  |
| `keys[].squareBottom` | float | Fraction | - | superellipse exponent of the lower half | 1..12 |  |
| `curveDeg` | float | Degrees | - | the axis curls down (+) or up (-) by this much over the length |  |  |
| `rollDeg` | float | Degrees | - | cross-section rolled about the axis (mirrored by side; + turns the top outward) |  |  |
| `shade` | int | Count | - | shading: 0 = skin (the body's), 2 = claw / horn | 0..2 |  |
| `around` | int | Count | - | mesh: vertices around each section (0 = 16) | 0..48 |  |
| `rings` | int | Count | - | mesh: sections along the length (0 = 12) | 0..64 |  |

### Swell

A muscle mass lofted round the spine over a stretch of joints: keel below (a sternum ridge), breadth at the sides (pectorals, haunches), hump above (withers), fading into the body at both ends. Skinned to the spine like the body rings, shaded as the body. System: mesh only (Swell.EmitMesh), skinned to the spine bones.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `toJoint` | int | Joint | - | last spine joint of the stretch (the first is at.joint) |  |  |
| `peakAt` | float | Fraction | - | where along the stretch it is fullest (0 = at.joint .. 1 = toJoint) | 0.05..0.95 | dragon chest 0.35 |
| `keel` | float | Metres | x s | extra depth straight down at the peak (the keel) |  | dragon chest 0.05 |
| `keelSharpness` | float | Fraction | - | how sharply the keel is drawn to a ridge: 1 = a round belly, 4 = a sternum keel | 1..8 | default 2 |
| `breadth` | float | Metres | x s | extra breadth at the sides at the peak (pectorals, haunches) |  |  |
| `breadthAroundDeg` | float | Degrees | - | where round the body the breadth sits: 90 = the flank, 120 = the lower flank | 0..180 | default 100 |
| `breadthWidthDeg` | float | Degrees | - | angular half width of the breadth bulge | 10..90 | default 40 |
| `hump` | float | Metres | x s | extra height above at the peak (withers, a shoulder hump) |  |  |
| `inset` | float | Fraction | - | both ends sink this fraction of the body's radius inside the body | 0..0.5 | default 0.08 |
| `ringsPerJoint` | int | Count | - | mesh: rings per spine segment (0 = 4) | 0..12 |  |
| `around` | int | Count | - | mesh: vertices round each ring (0 = 32) | 0..64 |  |

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

### Flee

Runs straight away from the nearest threat (CreatureThreats: the viewer in play mode) once it comes within the alarm radius, sliding along the enclosure wall when cornered; calms down after it leaves. Gives the species a ModularBrain (with Wander as the idle default).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `radius` | float | Metres | x s | alarm radius: a threat closer than this (planar) starts the flight |  | sand eel 1.2 |
| `runDistance` | float | Metres | x s | how far ahead of the head the escape target lies |  |  |
| `calmSeconds` | float | Seconds | x sqrt(s) | keeps fleeing this long after the threat left the radius |  |  |
| `utility` | float | Fraction | - | utility with the threat at the head (Wander idles at 0.1) | 0.2..2 |  |

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

Ground -> Launch -> Air <-> Glide -> Flare -> Touchdown -> Perched (CreatureFlight, FlightModel, AirChain). Froude-scaled. twist: the twisting Chinese-dragon air chain (AirTwist).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `spec` | FlightSpec | Spec | - | flight (FlightSpec: speeds, lift/drag, altitude PID, stroke, banking, flare, air undulation) - fields: cruiseSpeed, minSpeed, maxSpeed, kGrip, vGripMin, kLift, kDrag, upstrokeArea, featherDeg, climbGain, climbDamping, climbIntegral, climbIntegralLimit, thrustGain, demandFloor, descendCutoff, glideDemand, amplitudeMin, strokeRateMin, strokeRateMax, glideRelax, maxBankDeg, bankGain, bankDamping, maxAsymmetry, rollGain, rollDamping, glideAlphaDeg, flareAlphaDeg, launchSpeed, flareDistance, minAltitude, cruiseAltitudeFrac, airUndulationDeg, airUndulationHz, tailSag, headStabilise, avoidLookahead, avoidMargin, perchGrip, perchMinRadius, pursuitGain, legTuckOut, legTuckDown, legTuckReach |  |  |
| `twist` | AirTwistSpec | Struct | - | twisting flight (AirTwist, Chinese dragon): the body follows the head's path; 3D swimming, corkscrews, barrel rolls, loops, slow rolls that travel head to tail. All 0 = the serpent's trailing air chain |  |  |
| `twist.swimAmplitude` | float | Metres | x s | always-on swimming: the head's 3D S-curve amplitude (0 = no swimming) |  | dragon 0.12 |
| `twist.swimHz` | float | Hz | / sqrt(s) | swimming wave frequency; wavelength along the body = speed / Hz |  | dragon 0.9 |
| `twist.swimVertical` | float | Fraction | - | vertical share of the swim (0 = a flat S, 1 = as tall as wide); its own slower phase turns the S-plane | 0..1 |  |
| `twist.swimRollDeg` | float | Degrees | - | roll into each swim curve (banking with the S), travels down the body |  |  |
| `twist.corkscrewAmplitude` | float | Metres | x s | corkscrew: helix radius of the head's path (0 = no corkscrews) |  | dragon 0.3 |
| `twist.corkscrewHz` | float | Hz | / sqrt(s) | corkscrew: helix turns per second |  | dragon 0.8 |
| `twist.corkscrewTurns` | int | Count | - | corkscrew: helix turns per manoeuvre (0 = 3) | 0..12 |  |
| `twist.corkscrewRolls` | int | Count | - | corkscrew: full body rolls over the manoeuvre (the twist travels head to tail) | 0..12 |  |
| `twist.barrelRadius` | float | Metres | x s | barrel roll: helix radius (0 = no barrel rolls); the body rolls once, belly out |  | dragon 0.5 |
| `twist.barrelTime` | float | Seconds | x sqrt(s) | barrel roll duration (0 = 1.6) |  |  |
| `twist.loopRadius` | float | Metres | x s | loop: radius of the vertical loop (0 = no loops) |  | dragon 0.9 |
| `twist.loopSpin` | float | Fraction | - | loop: speed round the loop / flight speed (0 = 1); the head then surges to catch the flight line up again | 0..3 |  |
| `twist.slowRollTime` | float | Seconds | x sqrt(s) | slow roll: one full roll in place along the flight line (0 = no slow rolls) |  | dragon 2.4 |
| `twist.intervalMin` | float | Seconds | x sqrt(s) | shortest pause between manoeuvres (0 = 4) |  |  |
| `twist.intervalMax` | float | Seconds | x sqrt(s) | longest pause between manoeuvres (0 = 10) |  |  |
| `twist.uprightRateDeg` | float | DegPerSecond | / sqrt(s) | how fast the body rights itself onto the banked up (0 = 120) |  |  |
| `twist.girdleBob` | float | Fraction | - | the wingbeat's shoulder bob on the twisting body: the share of the flight model's bob the girdle joint rides (0 = none: the body follows only the head's path, which never bobs; above 1 the body heaves more than the point mass, a stylised beat) | 0..4 | dragon 2.6 |
| `twist.bobHead` | float | Fraction | - | share of the girdle's bob at the head (the head is stabilised; the neck blends) | 0..1 | dragon 0.3 |
| `twist.bobTail` | float | Fraction | - | share at the tail: behind the girdle the bob travels down the body along the path (each joint bobs as the girdle did where it is now), fading to this | 0..1 | dragon 0.4 |
| `twist.bobInPhase` | float | Fraction | - | share of the bob behind the girdle that the body rides in phase with the girdle (the whole trunk heaves with the shoulders) instead of as the wave travelling down the path: a big bob at a fast beat is a short steep wave on its own (wavelength = speed / beat), which the bend cones cannot follow (0 = all travelling) | 0..1 | dragon 0.5 |
| `twist.bobHighPass` | float | Seconds | x sqrt(s) | the body rides only the beat in the shoulder bob: its slow part (a low-pass of this time constant: the stabilised path lagging a climb or a level-off) is taken out first, so a girdleBob above 1 heaves with the beat and never amplifies an altitude change (0 = the raw bob) | 0..2 | dragon 0.6 |

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

### Layers

Restyles part ids (material layers) for this species: shading model (Skin, Chitin, Membrane, Eye, Claw), tint, smoothness, and which layers (pattern, scale grain, sheen, shimmer) each has. Rows not listed keep the registry's (Kit/PartLayers.cs). A new part needs a row, never a shader edit.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `rows` | LayerDef[] | Struct | - | rows, one per restyled part id |  |  |
| `rows[].partId` | int | Count | - | the part id (material layer) this row styles: 0 body, 1 limb, 2 foot/claw/cone, 3 eye, 8 membrane, 9 band, 10+ new parts (CATALOGUE.md registry) | 0..15 |  |
| `rows[].model` | Skin / Chitin / Membrane / Eye / Claw | Enum | - | shading model: Skin, Chitin (no grain, glossy), Membrane, Eye, Claw (keratin: the tint, glossy) |  |  |
| `rows[].noPattern` | bool | Flag | - | no pattern on this part |  |  |
| `rows[].limbPattern` | bool | Flag | - | the limb pattern (mottle / spots) instead of the body's |  |  |
| `rows[].noGrain` | bool | Flag | - | no scale grain |  |  |
| `rows[].fineGrain` | bool | Flag | - | the limbs' smaller scales |  |  |
| `rows[].noSheen` | bool | Flag | - | without the velvet sheen (Chitin module) |  |  |
| `rows[].noShimmer` | bool | Flag | - | without the iridescent shimmer (Shimmer module) |  |  |
| `rows[].tintSource` | Own / Pattern / Band / Dorsal / Belly / Membrane | Enum | - | tint colour: Own (tint below) or a palette colour |  |  |
| `rows[].tint` | Color | SrgbColour | - | the row's own tint (tintSource Own) |  |  |
| `rows[].tintAmount` | float | Fraction | - | albedo -> tint after the pattern (Claw: how much of the tint) | 0..1 |  |
| `rows[].smoothnessScale` | float | Fraction | - | Chitin / Claw: x the species smoothness (0 = model default: Chitin 1.6, Claw 1.9) | 0..3 |  |

### Membrane

Translucent veined membrane (part 8, the Membrane shading model, CreatureMembrane.hlsl): wings, FinStrip fins, antennae - any part whose layer row is Membrane.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `colour` | Color | SrgbColour | - | membrane colour |  |  |
| `translucency` | float | Fraction | - | how much light shines through | 0..1 |  |
| `veinContrast` | float | Fraction | - | how dark the veins are | 0..1 |  |
| `rim` | float | Fraction | - | how dark the trailing rim is | 0..1 |  |
| `inner` | float | Fraction | - | toward its root (the wrist and the body, a fin's base) the membrane takes on the body's dorsal colour, fading to its own at the edge (0 = one colour) | 0..1 | dragon 0.6 |

### Shimmer

Iridescent scale shimmer: thin-film colour bands (colour -> colour2 with the view angle) travelling head to tail along the body's back and flanks, a faint glow in each band (Creature.shader, part 0). The SkyDragon's signature; cheap (one pow, one cos per body pixel).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `colour` | Color | SrgbColour | - | colour facing the viewer |  |  |
| `colour2` | Color | SrgbColour | - | colour at grazing angles (the thin-film hue shift) |  |  |
| `strength` | float | Fraction | - | strength (0 = none) | 0..2 |  |
| `wavelength` | float | Metres | x s | distance between bands along the body (rest arc) |  | dragon 0.22 |
| `period` | float | Seconds | x sqrt(s) | a band passes a point every this many seconds (x sqrt of the species scale) |  | dragon 2.2 |
| `glow` | float | Fraction | - | glow of the bands (emission, also in shadow) | 0..1 |  |

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

Creature.shader shades a part id by its row of the species' look-layer table (Kit/PartLayers.cs): a shading model (Skin, Chitin, Membrane, Eye, Claw) plus layers (pattern, scale grain, sheen, shimmer) and a tint. A new part takes the next free id and adds its row to PartLayers.Registry - no shader edit; a species restyles rows with the Layers module. Never reuse an id. A new shading MODEL is a shader edit: add it to CreatureLayers.hlsl + ShadingModel, with a CPU proof and the Unity pixel guard (CreaturePartLayerTests, CreatureShaderLayerTests).

| id | use | row (model, layers, tint) |
|---|---|---|
| 0 | body skin | Skin (Sheen, Shimmer) |
| 1 | limb | Skin (LimbPattern, FineGrain, Sheen, Shimmer) |
| 2 | foot, claw, rigid cone | Skin (NoPattern, FineGrain, Sheen, Shimmer, tint Pattern 0.3) |
| 3 | eye | Eye (NoPattern, NoGrain) |
| 4 | octopus arm | octopus shader family (CreatureOctopus.shader) |
| 5 | octopus web | octopus shader family (CreatureOctopus.shader) |
| 6 | octopus mantle | octopus shader family (CreatureOctopus.shader) |
| 7 | octopus siphon | octopus shader family (CreatureOctopus.shader) |
| 8 | membrane (wings, fins, antennae) | Membrane (NoPattern, NoGrain) |
| 9 | arthropod band | Skin (Sheen, Shimmer, tint Band 0.9) |
