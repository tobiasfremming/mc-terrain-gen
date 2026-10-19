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
| **SpineBody** | Body | exists | = joints | 0 | anything with a backbone or a segmented trunk | Crocodile, DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, RockCrab, Scolopendra, SkyDragon, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeSpider, WingedSerpent |
| **ArthropodLeg** | Limb | exists | = segments | 1, 2, 9 | insects, spiders, crabs, centipedes; anything with visible segments | DaddyLongLegs, DesertBeetle, GiantCentipede, RockCrab, Scolopendra, Tarantula, TentacleCrab, TreeSpider |
| **BendyLeg** | Limb | exists | = segmentCount | 1 | harvestmen, stilt-legged and wobbly animals, any limb that should look flexible: non-uniform, kinked, bouncing legs | GolfballStrider |
| **Leg2Bone** | Limb | exists | 3 | 1, 2 | lizards, mammals, birds' legs, wing arms | DesertMonitor, Hexadrake, KitMonitor, SkyDragon, TestHexapod, WingedSerpent |
| **SprawlLeg** | Limb | exists | 3 | 1, 2 | crocodilians, monitors, tegus, salamanders, any heavy-bodied sprawling or semi-erect walker | Crocodile, KomodoDragon |
| **Wing** | Wing | exists | 6 + max(1, spec.flankPatches), +1 with spec.groundTallStance | 8, 1, 2 | bats, pterosaurs, dragons; the Fly module makes it fly | Hexadrake, SkyDragon, WingedSerpent |
| **Eyes** | Sense | exists | 2 | 3 | always (every shipped species has two eye bones) | Crocodile, DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, RockCrab, Scolopendra, SkyDragon, Swinger, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeOctopus, TreeSpider, WingedSerpent |
| **Antenna** | Chain | exists | = segments | 8 | whiskers, barbels, antennae, feelers, cerci, ribbon-like streamers | GiantCentipede, Scolopendra, SkyDragon |
| **Tendril** | Chain | exists | = segments | 1, 9 | tentacles, feeding arms, feelers that touch the ground; on a spine body (soft bodies: Tentacle) | TentacleCrab |
| **Tentacle** | Chain | exists | = spec.segments (0 = 24) | 4 | octopus arms; grabbing arms on any body (after T5.9) | Swinger, TreeOctopus |
| **Tongue** | Chain | exists | = segments | 14 | monitor lizards, snakes: a tongue that flicks in and out; any quick extensible feeler that hides in the head | KomodoDragon |
| **FinStrip** | Fin | exists | 0 (skinned to the spine) | 8 | manes, crests, dorsal / anal fins, tail tufts, frills along a body | FinnedSandEel, Hexadrake, SkyDragon |
| **Flapper** | Fin | exists | = segments (1-3) | 8 | pectoral fins, ear flaps, frills, small paddles that beat or flutter (not wings that fly: Wing) | FinnedSandEel |
| **LegFin** | Fin | exists | 2 (thigh wing, shin wing) | 1, 8 | winged or finned legs: Microraptor hind wings, Caraxes-like leg membranes, any ribbed flap that rides a limb and spreads in flight | SkyDragon |
| **ArmourPlates** | Rigid | exists | 0 (each plate rigid to its joint) | 10 | segmented armour: centipede / millipede tergites, woodlice, trilobites, armadillo bands, ankylosaur back plates; ventral = true: sternites, belly plates | Scolopendra |
| **Chela** | Rigid | exists | 0 (rigid to its ArthropodLeg's last bone) | 1, 2 | crab and lobster claws (crusher and cutter), scorpion pincers: any heavy chela on an arthropod limb | RockCrab, TentacleCrab |
| **DimpledOrb** | Rigid | exists | 1 | 0 | a round abdomen (spiders, ticks), puffballs, seed heads, golf-ball or pitted shells; any heavy round mass that should wobble on its body | GolfballStrider |
| **EyeStalk** | Rigid | exists | 1 | 1, 2, 9 | crab and snail eye stalks, periscopes, anything on a stalk that folds away | RockCrab |
| **Jaw** | Rigid | exists | 1 | 0, 2, 13 | a mouth that opens: a crocodile basking agape (opensUp), a yawn, a threat gape, a snapping beak | Crocodile |
| **LimbSpines** | Rigid | exists | 0 (rigid to the leg's segment bones) | 1, 2, 9 | spiny legs (king crab), leg bristles and setae fringes (stone crab, tarantula), raptorial spines | RockCrab, TentacleCrab |
| **Pincer** | Rigid | exists | = segments (1-4) | 11, 9 | forcipules, mandibles, chelicera fangs, pincers, earwig forceps, centipede ultimate legs - any jointed appendage that opens and closes | Scolopendra |
| **RigidCone** | Rigid | exists | 0 (1 with ownBone) | 2 | horns, spikes, fangs, mandibles, chelicerae | GiantCentipede, Hexadrake, SkyDragon, Swinger, Tarantula, TentacleCrab, TreeOctopus, TreeSpider |
| **RigidLoft** | Rigid | exists | 0 (rigid to its joint) | 0, 2 | head shapes: squared snouts, jaws, brows, nostrils, bosses; any rigid lump on a joint | GiantCentipede, Hexadrake, RockCrab, Scolopendra, SkyDragon, TentacleCrab |
| **Sculpt** | Rigid | exists | 0 (rigid to its joint, or rides a Jaw's bone) | 0, 2, 13 | realistic heads and skulls: brows over recessed eye sockets, jowls, lip lines, nostrils, skull tables, a lower jaw on a Jaw; any rigid organic solid a loft cannot express | Crocodile, KomodoDragon |
| **ScuteRow** | Rigid | exists | 0 (skinned to the spine) | 12, 0 | crocodile and alligator armour, a tail's double crest, a stegosaur-like row of plates, spiny lizard backs | Crocodile |
| **SkinFolds** | Rigid | exists | 0 (skinned to the spine) | 0 | neck and throat folds of monitor lizards, a dewlap, wrinkled elbows of skin, the gular folds of iguanas | KomodoDragon |
| **SpiderFace** | Rigid | exists | 0 (rigid to its joint, or rides `on`'s bone) | 1, 3, 2 | spiders, harvestmen, mites, any small-headed arthropod whose face sits low on a round body | GolfballStrider |
| **SpineField** | Rigid | exists | 0 (rigid to its loft's joint) | 0, 2 | a spiny carapace (king crab), marginal teeth (stone crab), bosses, a horned crown: anything studding a shell or a head | RockCrab, TentacleCrab |
| **Stumble** | Rigid | exists | 0 (posture: the sim's JointLift and Halt) |  | goofy, top-heavy or stilt-legged walkers, a tired or injured animal, anything that should look uncoordinated | GolfballStrider |
| **Swell** | Rigid | exists | 0 (skinned to the spine) | 0 | a deep chest with flight muscles at the wing roots, shoulders, haunches, a belly - any bulk the body's elliptical profile cannot give | KomodoDragon, SkyDragon |
| **ThreatDisplay** | Rigid | exists | 0 (sets the sim's posture inputs) |  | a predator that threatens when it stops: centipedes, scorpions, mantises, cobras rearing, a crab raising its claws | Scolopendra |
| **Tuck** | Rigid | exists | 0 (poses the others' bones) |  | a creature that hides by folding up: rock crabs, box crabs, armadillos, pill bugs, tortoises | RockCrab |
| **Repeat** | Sugar | exists | = count x template |  | centipede legs, spike rows, many fins | GiantCentipede, Scolopendra |

## Modules

| module | category | status | needs | used by | what |
|---|---|---|---|---|---|
| **Ambush** | Behaviour | exists | SoftBody + Tentacle parts (spine bodies: T5.9) | Swinger, TreeOctopus | Rest / explore / stalk / ambush / strike / hold / retreat (OctopusBrain + prey adapters; arm roles, grooming). Hold 1.5 s, no damage. |
| **ClimbExcursion** | Behaviour | exists | Climb | DesertMonitor, KitMonitor, TreeSpider | Approach a climbable obstacle, climb to a goal on its real surface, rest, descend (ClimbingBrain wrapping Wander). |
| **Flee** | Behaviour | exists | Walk | FinnedSandEel | Runs straight away from the nearest threat (CreatureThreats: the viewer in play mode) once it comes within the alarm radius, sliding along the enclosure wall when cornered; calms down after it leaves. Gives the species a ModularBrain (with Wander as the idle default). |
| **FlightExcursion** | Behaviour | exists | Fly | Hexadrake, SkyDragon, WingedSerpent | Rest, Face, Patrol, Soar, Landing on perches; predictive wall turn-back (FlightBrain). |
| **Wander** | Behaviour | exists |  | Crocodile, DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, RockCrab, Scolopendra, SkyDragon, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeSpider, WingedSerpent | Safe-disc points, walk, pause, glance (WanderBrain). Containment is structural: the head is clamped to the safe radius. |
| **Climb** | Locomotion | exists | Walk | DesertMonitor, KitMonitor, TreeSpider | Per-joint up field, real-geometry contact via ISurfaceProbe, pitch limiter (CreatureClimb, ClimbSpec). Terrain walls >= 50 deg over >= 1/2 body length (R30/R31). 0 = default for every number. |
| **Fly** | Locomotion | exists | Wing parts | Hexadrake, SkyDragon, WingedSerpent | Ground -> Launch -> Air <-> Glide -> Flare -> Touchdown -> Perched (CreatureFlight, FlightModel, AirChain). Froude-scaled. twist: the twisting Chinese-dragon air chain (AirTwist). |
| **Slither** | Locomotion | exists | Walk | Crocodile, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, Hexadrake, KitMonitor, KomodoDragon, Scolopendra, SkyDragon, TestSnake, WingedSerpent | Lateral head wave the drag chain carries back (undulation in the walk). Long wave: lambda = speed / Hz >= 0.7 body length (R27). |
| **Walk** | Locomotion | exists | SpineBody | Crocodile, DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, RockCrab, Scolopendra, SkyDragon, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeSpider, WingedSerpent | Head steering in the tangent plane, chain, grounding, gait groups, girdle sway (CreatureSim.SubStep, CreatureLegs). The default locomotion of a spine body. |
| **Chitin** | Look | exists |  | DaddyLongLegs, DesertBeetle, GiantCentipede, GolfballStrider, RockCrab, Scolopendra, Tarantula, TentacleCrab, TreeSpider | Knee/segment bands (part 9) and a velvet sheen lobe: arthropod legs. |
| **Chromatophore** | Look | exists | SoftBody | Swinger, TreeOctopus | Dynamic colour cells, flash and passing-cloud phase (octopus shader). Makes the look dynamic. |
| **Cloak** | Look | exists | SoftBody (spine bodies: Phase 4) | Swinger, TreeOctopus | Dissolve in Forward only: the body vanishes, shadow + SSAO stay (the user's favourite bug). Per-instance, brain-driven, with hysteresis. Octopus today. |
| **Layers** | Look | exists |  | Crocodile, GiantCentipede, GolfballStrider, Hexadrake, KomodoDragon, RockCrab, Scolopendra, TentacleCrab | Restyles part ids (material layers) for this species: shading model (Skin, Chitin, Membrane, Eye, Claw), tint, smoothness, and which layers (pattern, scale grain, sheen, shimmer) each has. Rows not listed keep the registry's (Kit/PartLayers.cs). A new part needs a row, never a shader edit. |
| **Membrane** | Look | exists |  | FinnedSandEel, Hexadrake, SkyDragon, WingedSerpent | Translucent veined membrane (part 8, the Membrane shading model, CreatureMembrane.hlsl): wings, FinStrip fins, antennae - any part whose layer row is Membrane. |
| **Shimmer** | Look | exists |  | SkyDragon | Iridescent scale shimmer: thin-film colour bands (colour -> colour2 with the view angle) travelling head to tail along the body's back and flanks, a faint glow in each band (Creature.shader, part 0). The SkyDragon's signature; cheap (one pow, one cos per body pixel). |
| **Skin** | Look | exists |  | Crocodile, DaddyLongLegs, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, RockCrab, Scolopendra, SkyDragon, Swinger, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeOctopus, TreeSpider, WingedSerpent | Palette dorsal/flank/belly + procedural pattern by rest arc length (CreaturePattern.hlsl); per-individual size/hue/value jitter. Layers of parts 0-2. |

## Species (examples to copy)

| species | format | plan | parts | modules | bones |
|---|---|---|---|---|---|
| Crocodile | kit | Spine | SpineBody, 4x SprawlLeg, Eyes, Jaw, 2x Sculpt, 21x ScuteRow | Walk (Diagonal), Slither, Wander, Skin, Layers | 51 |
| DaddyLongLegs | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes | Walk (Tetrapod), Wander, Skin, Chitin | 71 |
| DesertBeetle | legacy | Spine | SpineBody, 6x ArthropodLeg, Eyes | Walk (Tripod), Wander, Skin, Chitin | 35 |
| DesertMonitor | legacy | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| DesertSnake | legacy | Spine | SpineBody, Eyes | Walk, Slither, Wander, Skin | 34 |
| FinnedSandEel | kit | Spine | SpineBody, Eyes, 2x FinStrip, 2x Flapper | Walk (Diagonal), Slither, Wander, Flee, Skin, Membrane | 54 |
| GiantCentipede | kit | Spine | SpineBody, Eyes, RigidLoft, RigidCone, 4x Antenna, 42x ArthropodLeg | Walk (Wave), Slither, Wander, Skin, Chitin, Layers | 252 |
| GolfballStrider | kit | Spine | SpineBody, Eyes, 10x BendyLeg, DimpledOrb, SpiderFace, Stumble | Walk (Tetrapod), Wander, Skin, Chitin, Layers | 63 |
| Hexadrake | kit | Spine | SpineBody, 6x Leg2Bone, 2x Wing, Eyes, 5x RigidLoft, 3x RigidCone, 2x FinStrip | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane, Layers | 78 |
| KitMonitor | kit | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| KomodoDragon | kit | Spine | SpineBody, 4x SprawlLeg, Eyes, 2x Sculpt, Tongue, 3x SkinFolds, 3x Swell | Walk (Diagonal), Slither, Wander, Skin, Layers | 53 |
| RockCrab | kit | Spine | SpineBody, Eyes, 10x ArthropodLeg, RigidLoft, 6x SpineField, 2x EyeStalk, 2x Chela, 8x LimbSpines, Tuck | Walk (Tetrapod), Wander, Skin, Chitin, Layers | 57 |
| Scolopendra | kit | Spine | SpineBody, Eyes, RigidLoft, 2x Antenna, 40x ArthropodLeg, 2x ArmourPlates, 4x Pincer, ThreatDisplay | Walk (Wave), Slither, Wander, Skin, Chitin, Layers | 245 |
| SkyDragon | kit | Spine | SpineBody, 4x Leg2Bone, 2x LegFin, 2x Wing, Eyes, 4x RigidCone, 5x FinStrip, 4x Antenna, Swell, 7x RigidLoft | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane, Shimmer | 148 |
| Swinger | legacy | Soft | 2x Tentacle, SoftBody, Eyes, RigidCone | Ambush, Skin, Cloak, Chromatophore | 97 |
| Tarantula | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes, RigidCone | Walk (Tetrapod), Wander, Skin, Chitin | 75 |
| TentacleCrab | kit | Spine | SpineBody, Eyes, 8x ArthropodLeg, RigidLoft, 3x RigidCone, 4x Tendril, 6x SpineField, 2x Chela, 8x LimbSpines | Walk (Tripod), Wander, Skin, Chitin, Layers | 86 |
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
| `spec` | CephalopodSpec | Spec | - | the octopus body (CephalopodSpec: web, head, mantle, siphon, lattice); its `arm` comes from the Tentacle parts - fields: armCount, arm, bodyMass, webRadius, webDepth, headRadius, mantleRadius, mantleLength, mantleTiltDeg, eyeRadius, siphonLength, siphonRadius, beta, headStiffness, tipStiffness, breathAmp, breathHz, bodyDamping, subSteps, iterations, minHolds, mouthCount, mouth, mouthRing, eyeStalk, mantlePear, beakLength, locomotion, look, cloakEnabled |  |  |

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
| `role` | Walk / Feeler / WingArm / Folded | Enum | - | Walk steps in its gait group; Feeler is posed, never plants; WingArm is set by compose for a Wing's arm; Folded: a Wing's arm held folded on the ground (wrist at restOffset, never plants), flown by the Wing in the air |  |  |
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
| `segments[].spurLength` | float | Metres | x s | a sharp spur at the segment's distal end, on its lower side, raked toward the tip (claw layer); 0 = none |  | scolopendra 0.0018 |
| `contactAngleDeg` | float | Degrees | - | tarsus angle to the ground, 0 = 20 |  |  |
| `kneeLiftDeg` | float | Degrees | - | lift synergy at mid-swing (reshapes the leg; the foot stays on its path) |  |  |
| `swingRetract` | float | Fraction | - | mid-swing pull of the foot toward the coxa (flexing lifts the knee) | 0..0.5 |  |
| `elevMinDeg` | float | Degrees | - | in-plane pitch lower bound (0, 0 = +-90); outside it the planted foot steps |  |  |
| `elevMaxDeg` | float | Degrees | - | in-plane pitch upper bound |  |  |
| `liftDelay` | float | Fraction | - | lift-off delay per place behind the group's rearmost leg (metachronal wave) | 0..0.5 |  |
| `kneeJoint` | int | Count | - | joint reported as the knee (debug, tests); 0 = the largest flexWeight |  |  |
| `legSides` | int | Count | - | mesh: sides of the segment tubes, 0 = 8 |  |  |

### BendyLeg

Long thin springy limb generated from a few numbers (length, segment count, rise, arch, knee spread, seeded kinks and length jitter): walks as an arthropod leg (synergy IK), drawn as one continuous tapered tube blended through its joints, and flexes on its own - every joint lags the IK pose on a damped spring (inertia), sags, the lifted tip whips; a planted tip stays exactly on its foot. System: CreatureLegs (arthropod synergy IK, gait groups) + BendyLegSystem (part runtime, stage AfterLimbs: re-poses the leg's bones) + BendyLeg.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `role` | Walk / Feeler / WingArm / Folded | Enum | - | Walk steps in its gait group; Feeler is posed, never plants; WingArm is set by compose for a Wing's arm; Folded: a Wing's arm held folded on the ground (wrist at restOffset, never plants), flown by the Wing in the air |  |  |
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
| `segmentCount` | int | Count | - | segments, coxa to tarsus (one bone each; 3..7 like an ArthropodLeg) | 3..7 | default 7 |
| `length` | float | Metres | x s | whole leg length, root to tip | 0.01..5 | golfball strider 1.1 .. 1.6 |
| `tarsusShare` | float | Fraction | - | the tarsus (last segment, laid toward the ground) as a share of the length | 0.03..0.5 | default 0.14 |
| `lengthJitter` | float | Fraction | - | uneven segment lengths: each solved segment x (1 +- this), seeded | 0..0.6 |  |
| `riseDeg` | float | Degrees | - | elevation of the first segment above the horizontal | -30..89 | default 60 |
| `archDeg` | float | Degrees | - | total downward turn of the solved segments (the knee arch) | 0..200 | default 120 |
| `kneeAt` | float | Fraction | - | where along the solved leg the turn is concentrated (0 root .. 1 ankle) | 0..1 | default 0.35 |
| `kneeSpread` | float | Fraction | - | how widely the turn spreads (1 = an even curve, small = one sharp knee) | 0..1 | default 0.3 |
| `kinkDeg` | float | Degrees | - | irregular extra bend at each joint (+-, seeded, zero-sum) | 0..45 |  |
| `seed` | int | Count | - | seed of the kinks and the length jitter |  |  |
| `rangeDeg` | float | Degrees | - | hinge range around each rest bend | 5..120 | default 60 |
| `contactAngleDeg` | float | Degrees | - | tarsus angle to the ground, 0 = 20 |  |  |
| `swingLiftDeg` | float | Degrees | - | lift synergy at mid-swing: the root half curls, the distal half opens |  | default 20 |
| `swingRetract` | float | Fraction | - | mid-swing pull of the foot toward the coxa | 0..0.5 | default 0.1 |
| `rootRadius` | float | Metres | x s | radius at the root |  | golfball strider 0.007 |
| `tipRadius` | float | Metres | x s | radius at the tip |  | golfball strider 0.0018 |
| `smooth` | float | Fraction | - | rounding of each joint: the blend zone's half width as a share of the shorter neighbouring segment (0 = rigid segments meeting at the joint) | 0..0.9 | default 0.5 |
| `beads` | float | Fraction | - | bead at each joint, x the leg's radius there (0 = none) | 0..3 |  |
| `around` | int | Count | - | mesh: vertices round the tube, 0 = 8 | 0..16 |  |
| `wobbleHz` | float | Hz | / sqrt(s) | wobble frequency at mid-leg (the root is stiffer, the tip floppier) | 0.2..20 | default 2.2 |
| `wobbleDamping` | float | Fraction | - | wobble damping ratio (0.1 springy .. 1 dead) | 0..2 | default 0.2 |
| `inertia` | float | Fraction | - | how much the joints follow their own acceleration (inertia; 0 = a rigid leg) | 0..3 | default 1 |
| `sag` | float | Metres | x s | sag of mid-leg under its own weight |  | default 0.01 |
| `whip` | float | Fraction | - | lifted tip whip: how loosely the swinging tip follows its foot (0 = exactly) | 0..1 | default 0.5 |
| `coupling` | float | Fraction | - | neighbour coupling of the wobble (a continuous rod), x the spring | 0..2 | default 0.6 |
| `maxDeflect` | float | Fraction | - | largest deflection of any joint, x the leg length | 0..0.5 | default 0.12 |

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
| `role` | Walk / Feeler / WingArm / Folded | Enum | - | Walk steps in its gait group; Feeler is posed, never plants; WingArm is set by compose for a Wing's arm; Folded: a Wing's arm held folded on the ground (wrist at restOffset, never plants), flown by the Wing in the air |  |  |
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
| `kneeBlend` | float | Fraction | - | 0 = ball knee (rigid tubes meeting in one sphere, never creases); > 0 = the older skinned knee curve blending this share of each bone | 0..0.49 |  |
| `ankleHeight` | float | Metres | x s | ankle height above the foot's contact point (the foot is built from it down to the toes), 0 = 1.2 x lowerRadius, < 0 = none; a high ankle is a digitigrade hock |  |  |
| `clawLength` | float | Metres | x s | claw length, 0 = 0.16 x footLength |  |  |
| `clawCurveDeg` | float | Degrees | - | claw curl over its length, 0 = 75 |  |  |
| `toeWeb` | float | Fraction | - | webbing between the toes, share of the toe length (0 = none) | 0..1 |  |
| `digitigrade` | bool | Flag | - | digitigrade dragon / raptor leg mesh: heavy muscled thigh, lean shin, knobbly raised hock (ankleHeight = the metatarsus' height), long metatarsus, three forward toes (+ a hallux when toeCount >= 4) with knuckles and big talons |  |  |
| `thighMass` | float | Fraction | - | digitigrade: the thigh's muscle masses, x upperRadius (0 = 0.5) | 0..1.5 |  |
| `scutes` | bool | Flag | - | digitigrade: overlapping scutes on the front of the shin and metatarsus and on the toes |  |  |
| `spurLength` | float | Metres | x s | digitigrade: a spur on the back of the hock (0 = none) |  |  |

### SprawlLeg

Heavy sprawling vertebrate limb: walks as a Leg2Bone (TwoBoneIK, pole, limits, ankle), drawn with a flank mass blended into the trunk, a muscled upper limb, a ball joint in loose skin folds, a thick forearm/shin (optional rows of large scales), a heavy wrist and a foot of individually described digits with thick curved claws and webbing. System: CreatureLegs (gait groups, throttle, sway) + SprawlLeg.EmitMesh (LimbLoft, DigitFoot).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `role` | Walk / Feeler / WingArm / Folded | Enum | - | Walk steps in its gait group; Feeler is posed, never plants; WingArm is set by compose for a Wing's arm; Folded: a Wing's arm held folded on the ground (wrist at restOffset, never plants), flown by the Wing in the air |  |  |
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
| `upperLength` | float | Metres | x s | upper bone length | 0.005..2 | komodo fore 0.13 |
| `lowerLength` | float | Metres | x s | lower bone length | 0.005..2 | komodo fore 0.12 |
| `upperRadius` | float | Metres | x s | upper bone radius (the limb's thickness by the root) |  |  |
| `lowerRadius` | float | Metres | x s | lower bone radius |  |  |
| `pole` | Vector3 | Direction | - | knee direction hint in the attach frame (x mirrored): front z < 0 (elbow back), rear z > 0 (knee forward) |  |  |
| `ankleHeight` | float | Metres | x s | ankle height above the foot's contact point, 0 = 1.2 x lowerRadius, < 0 = none |  |  |
| `rootMass` | float | Fraction | - | flank mass radius, x upperRadius | 1..3 | default 1.5 |
| `rootStretch` | float | Fraction | - | flank mass stretched along the spine, x its radius | 1..2.5 | default 1.4 |
| `rootDepth` | float | Fraction | - | how deep the flank mass reaches into the body, x its radius | 0.5..2 | default 1.1 |
| `muscle` | float | Fraction | - | muscle belly on top of the upper arm / thigh, x upperRadius | 0..1 | default 0.25 |
| `muscleAt` | float | Fraction | - | where along the upper bone the belly is fullest | 0.1..0.9 | default 0.35 |
| `kneeSize` | float | Fraction | - | elbow / knee ball radius, x upperRadius (at least 1.05 x lowerRadius) | 0.4..1.5 | default 0.8 |
| `forearm` | float | Fraction | - | forearm / calf belly near the joint, x lowerRadius | 0..1 | default 0.2 |
| `wrist` | float | Fraction | - | wrist / ankle radius, x lowerRadius | 0.4..1.2 | default 0.75 |
| `folds` | int | Count | - | loose skin folds on each side of the elbow / knee (0 = smooth) | 0..6 |  |
| `foldHeight` | float | Fraction | - | fold height, x the joint radius | 0..0.3 | default 0.08 |
| `wristFolds` | int | Count | - | folds round the wrist / ankle (0 = none) | 0..4 |  |
| `scaleRows` | int | Count | - | rows of large scales along the forearm / shin (0 = none) | 0..12 |  |
| `scaleAround` | int | Count | - | large scales round the forearm / shin | 3..12 | default 6 |
| `scaleHeight` | float | Fraction | - | large-scale relief, x lowerRadius | 0..0.3 | default 0.07 |
| `around` | int | Count | - | mesh: vertices round the limb, 0 = 16 | 0..32 |  |
| `digitLengths` | float[] | Fraction | - | each digit's length, x footLength, inner (I) to outer; empty = a lizard's (front 0.45 0.65 0.8 0.85 0.55, hind with the 4th longest) |  |  |
| `phalanges` | int[] | Count | - | phalanges per digit, inner to outer; empty = the reptile formula 2 3 4 5 3 (hind 2 3 4 5 4) |  |  |
| `digitSpreadDeg` | float | Degrees | - | fan of the digits from the innermost to the outermost | 0..160 | default 70 |
| `footYawDeg` | float | Degrees | - | the whole foot turned outward from the heading | -30..60 | default 12 |
| `digitRadius` | float | Metres | x s | digit radius at the root, 0 = 0.075 x footLength |  |  |
| `palmWidth` | float | Fraction | - | sole width at the knuckles, x the digits' spread (0 = auto) | 0..3 |  |
| `clawedDigits` | int | Count | - | digits with a claw, counted from the inner one (-1 = all) | -1..6 | default -1 |
| `clawLength` | float | Metres | x s | claw length, 0 = 0.18 x footLength |  | komodo 0.03 |
| `clawCurveDeg` | float | Degrees | - | claw curl over its length, 0 = 60 |  |  |
| `clawThickness` | float | Fraction | - | claw base radius, x the digit's tip radius | 0.2..1.5 | default 0.8 |
| `toeWeb` | float | Fraction | - | webbing between the digits, share of their length (0 = none) | 0..1 |  |

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
| `spec` | WingSpec | Spec | - | the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm` - fields: leg, restTip, comfortUp, comfortDown, strokePlaneDeg, pronationDeg, overshootUp, upstrokeDuration, upstrokeFold, foldReach, beatGroup, fingerLengths, fingerSpreadDeg, fingerFoldDeg, fingerMinGapDeg, fingerFoldGapDeg, groundLiftDeg, groundTallStance, flankPatches, groundWristLift, groundFlankRise, fingerRadius, thumbLength, membraneRearJoint, membraneFrontJoint, trailingRib, membraneInset, area, scallop, scallopByChord, membraneBillow, membraneDetail, fingerKnuckles, wristKnuckle, armMuscle, forearmTwin, thumbRadius |  |  |

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
| `probeDeg` | float | Degrees | - | probing: the rest curve sweeps side to side by this much about the root (up and down by 0.6 x it, the pair out of step), all the time - a centipede feeling its way; 0 = still | 0..60 | scolopendra 14 |
| `probeHz` | float | Hz | / sqrt(s) | probing sweeps per second |  | scolopendra 1.3 |

### Tendril

Muscular tentacle rooted on a spine joint (TentacleChain physics: gravity, muscle tone toward a rest curl, a travelling writhe, exact length and bend limits). Hangs and swings while the body moves; when it stops, reaches out and holds its tip on the ground ahead (an eased pin: no tremble), feeling about, and lets go when the body walks on. System: TendrilRuntime (part runtime, stage Late) + TentacleChain.

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
| `length` | float | Metres | x s | length, root to tip |  | tentacle crab 0.22 |
| `segments` | int | Count | - | particles (= bones) along it | 4..24 | default 12 |
| `rootRadius` | float | Metres | x s | radius at the root |  |  |
| `tipRadius` | float | Metres | x s | radius at the tip |  |  |
| `curlDeg` | float | Degrees | - | rest curl toward the belly side at the tip (rises toward it) | 0..120 |  |
| `bendLimitDeg` | float | Degrees | - | bend cone per joint | 5..60 | default 28 |
| `muscle` | float | Fraction | - | muscle tone: how firmly it holds its curl against gravity | 0.05..1 |  |
| `damping` | float | Hz | / sqrt(s) | velocity damping rate relative to still air (1/s) |  | 1.5 |
| `mass` | float | Kilograms | x s^3 | mass of the whole tendril |  | 0.02 |
| `writheDeg` | float | Degrees | - | writhe: amplitude of the wave travelling down it | 0..30 |  |
| `writheHz` | float | Hz | / sqrt(s) | writhe frequency |  | 0.8 |
| `reach` | float | Fraction | - | reach: how far ahead it lays its tip, fraction of its length (0 = never touches down) | 0..0.95 |  |
| `holdSeconds` | float | Seconds | x sqrt(s) | how long a touch holds before it feels for another spot |  | 2 |
| `releaseSpeed` | float | Fraction | - | the body moves faster than this fraction of its walking speed: let go, hang | 0..1 |  |
| `layer` | int | Count | - | material layer (part id): 1 the limbs', 9 the band colour's | 1..9 | default 1 |

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

### Tongue

A forked tongue on a physics chain (TentacleChain) rooted in the mouth: shoots out, flicks (a fast travelling wave plus the chain's follow-through as the head sways), draws back until hidden in the head, waits, flicks again. System: TongueRuntime (part runtime, stage Late) + TentacleChain.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `direction` | Vector3 | Direction | - | the direction it shoots out, in the joint frame (along the mouth) |  |  |
| `length` | float | Metres | x s | length out of the root, extended |  | komodo 0.14 |
| `segments` | int | Count | - | particles (= bones) along it | 4..16 | default 8 |
| `rootRadius` | float | Metres | x s | radius at the root |  |  |
| `tipRadius` | float | Metres | x s | radius where it forks |  |  |
| `forkLength` | float | Fraction | - | fork tines: length, fraction of the tongue's | 0..0.5 |  |
| `forkSpreadDeg` | float | Degrees | - | fork tines: spread between them | 0..90 |  |
| `waveDeg` | float | Degrees | - | flick: wave amplitude per joint (bounded by bendLimitDeg) | 0..40 | komodo 16 |
| `waveHz` | float | Hz | / sqrt(s) | flick: wave frequency |  | komodo 7 |
| `waves` | float | Fraction | - | flick: waves along the tongue | 0.25..4 |  |
| `planeDeg` | float | Degrees | - | flick: plane of the wave rolled from side-to-side (0) toward up-and-down (90) | 0..90 |  |
| `bendLimitDeg` | float | Degrees | - | bend cone per joint | 5..60 | default 35 |
| `muscle` | float | Fraction | - | muscle tone: how straight it holds against gravity | 0.05..1 |  |
| `damping` | float | Hz | / sqrt(s) | velocity damping rate relative to the head (1/s) |  | 6 |
| `mass` | float | Kilograms | x s^3 | mass of the tongue |  | 0.004 |
| `outSeconds` | float | Seconds | x sqrt(s) | shooting out (eased) |  | 0.08 |
| `flickSeconds` | float | Seconds | x sqrt(s) | flicking, fully out |  | 0.5 |
| `inSeconds` | float | Seconds | x sqrt(s) | drawing back in (eased) |  | 0.12 |
| `intervalMin` | float | Seconds | x sqrt(s) | shortest wait between flicks |  |  |
| `intervalMax` | float | Seconds | x sqrt(s) | longest wait between flicks |  |  |
| `layer` | int | Count | - | material layer (part id): 14 tongue (restyle it with Layers), 1 limb, 0 body | 0..15 | default 14 |

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

Leg wings along the back of a Leg2Bone's thigh and shin: a fan of finger-like ribs raked toward the foot with a scalloped bat-wing membrane between them - folded flat along the leg on the ground, spread toward the side in the air (with the hind legs trailing, a rear wing pair), flexing with the wingbeat and tilting in glides and turns. System: LegFinRuntime (part runtime, stage Late) + LegFin.EmitMesh.

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
| `thighHeight` | float | Metres | x s | thigh wing: its longest rib |  | dragon 0.3 |
| `shinHeight` | float | Metres | x s | shin wing: its longest rib |  | dragon 0.42 |
| `rootStart` | float | Fraction | - | the ribs' roots along each bone: from this fraction .. | 0..0.9 | default 0.12 |
| `rootEnd` | float | Fraction | - | .. to this fraction | 0.1..1 | default 0.92 |
| `spines` | int | Count | - | ribs per wing (a membrane zone between each pair) | 2..8 | default 5 |
| `scallop` | float | Fraction | - | the trailing edge arcs in between rib tips by this fraction of their spacing (the bat-wing scallop) | 0..0.6 | default 0.3 |
| `firstRib` | float | Fraction | - | the first rib's length, fraction of the longest (the ribs lengthen toward the foot) | 0.2..1 | default 0.55 |
| `rakeDeg` | float | Degrees | - | the ribs' mean rake toward the foot | 0..70 | default 35 |
| `fanDeg` | float | Degrees | - | the fan: the first rib rakes this much less than the last | 0..60 | default 30 |
| `ribCurveDeg` | float | Degrees | - | each rib curves toward the foot by this much from root to tip | 0..40 | default 12 |
| `ribRadius` | float | Metres | x s | rib radius at the root (finger-like, tapering, knuckled); 0 = no rib tubes | 0.. | dragon 0.006 |
| `billow` | float | Fraction | - | the membrane bellies between the ribs by this fraction of their spacing | 0..0.2 | default 0 |
| `groundFold` | float | Fraction | - | on the ground the spread presses to this fraction toward the leg | 0.05..1 | default 0.3 |
| `foldShear` | float | Fraction | - | folded: the ribs lay back up the leg by this much per unit of spread | 0..3 | default 1.2 |
| `foldSpan` | float | Fraction | - | folded: the whole wing fits inside this fraction of the bone's span (centred on the roots) | 0.2..1.5 | default 1 |
| `airDroopDeg` | float | Degrees | - | powered flight: the wings turn below the level by this much (droop; negative: above) | -45..45 |  |
| `glideDroopDeg` | float | Degrees | - | gliding: the same, eased in while the flight glides | -45..45 |  |
| `flapDeg` | float | Degrees | - | flex with the wingbeat: the shin wing follows the main wings' stroke (up with it, lagging) by up to this much (the thigh 0.6x) | 0..30 |  |
| `flutterHz` | float | Hz | / sqrt(s) | the flex's spring: its natural frequency (x 1/sqrt s), lightly damped - the lag and the flutter | 0.5..20 | default 4 |
| `bankDeg` | float | Degrees | - | in a full bank the two sides tilt against each other by this much (the inner wing down) | 0..30 |  |
| `foldSeconds` | float | Seconds | x sqrt(s) | the ground fold and the glide / bank angles ease over this time | 0.02..2 | default 0.3 |

### ArmourPlates

A row of hard shingled dorsal plates over a spine body, one every `step` joints: sized from the body's cross-section (width x half width, heights x half height), domed, rounded corners, a raised rim along the back and side margins, longitudinal sutures; each tucks under the plate before it, so the seams show as steps; they slide over each other and part on a bend. System: mesh only (ArmourPlates.EmitMesh), each plate rigid to its joint's bone.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `toJoint` | int | Joint | - | the last plate's joint (the first is at.joint) |  |  |
| `step` | int | Count | - | joints from one plate to the next (1 = a plate on every joint) | 1..8 | default 1 |
| `width` | float | Fraction | - | plate half width x the body's half width at its joint (> 1: the plate overhangs the flanks) | 0.5..2 | default 1.1 |
| `edgeHeight` | float | Fraction | - | the plate's side margin above the joint centre, x the body's half height | -1..1.5 | default 0.2 |
| `crown` | float | Fraction | - | dome: the midline above the margin, x the body's half height | 0..2 | default 1 |
| `flatness` | float | Fraction | - | dome flatness: 2 = round, 4 = flat-topped with rounded shoulders | 1.2..8 | default 3 |
| `slope` | float | Fraction | - | shingling: the back margin is this much higher than the front (x half height); the front tucks under the plate before | 0..1 | default 0.25 |
| `overlap` | float | Fraction | - | how far a plate reaches past half way to its neighbours, x the plate pitch | 0..0.5 | default 0.15 |
| `rim` | float | Fraction | - | raised bead along the back and side margins, x half height (0 = none) | 0..0.5 | default 0.1 |
| `grooves` | int | Count | - | longitudinal sutures (grooves) per side of the midline | 0..3 | default 1 |
| `grooveDepth` | float | Fraction | - | suture depth, x half height | 0..0.3 | default 0.06 |
| `thickness` | float | Fraction | - | the plate's edge wall (its thickness at the margin), x half height | 0.02..1 | default 0.25 |
| `cornerRound` | float | Fraction | - | planform: rounding of the four corners (0 = square, 0.9 = nearly oval) | 0..0.9 | default 0.35 |
| `keel` | float | Fraction | - | a sharp ridge along each plate's midline, x half height (0 = none) | 0..1 |  |
| `cornerSpike` | float | Fraction | - | the back corners drawn out sideways (x the plate's half width) and backward into upturned points (0 = none) | 0..0.6 |  |
| `firstReach` | float | Fraction | - | front plate: x the plate pitch it reaches forward (a head shield's collar); 0 = like the others | 0..2 |  |
| `ventral` | bool | Flag | - | under the body instead of over it (sternites, belly plates): the row mirrored top to bottom, every height measured downward, the dome facing the ground |  |  |
| `layer` | int | Count | - | material layer (part id): 10 = armour plate; 0 body, 2 claw | 0..15 | default 10 |
| `across` | int | Count | - | mesh: vertices across a plate | 6..40 | default 18 |
| `along` | int | Count | - | mesh: rows along a plate | 3..24 | default 8 |

### Chela

A heavy crab claw on an ArthropodLeg's last segment: bulbous flattened palm, fixed finger and movable finger (held open by openDeg) curling together, blunt teeth (a big molar on a crusher), dark tips, optional palm spines. Rigid to the leg's last bone: it walks, poses and folds (Tuck) with the limb. System: mesh only (Chela.EmitMesh), rigid to the leg's last bone.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `leg` | string | PartRef | - | the ArthropodLeg whose last segment carries the claw; a mirrored chela names the unmirrored id and takes .L / .R |  |  |
| `palmLength` | float | Metres | x s | palm length along the propodus (from its joint) |  | stone crab crusher 0.11 |
| `palmHeight` | float | Metres | x s | palm half height at its widest (the tall axis) |  | stone crab crusher 0.05 |
| `palmWidth` | float | Metres | x s | palm half thickness at its widest |  | stone crab crusher 0.034 |
| `wrist` | float | Fraction | - | the wrist (palm start) half sizes, fraction of the widest | 0.2..1 | default 0.55 |
| `square` | float | Fraction | - | superellipse exponent of the palm's section: 2 = oval, 3-4 = boxy | 1.5..6 | default 2.2 |
| `fingerLength` | float | Metres | x s | finger length past the palm's end |  | stone crab crusher 0.07 |
| `fingerHeight` | float | Metres | x s | finger half height at its root |  | stone crab crusher 0.02 |
| `fingerWidth` | float | Fraction | - | finger half thickness, fraction of its half height | 0.3..1.5 | default 0.8 |
| `dactylScale` | float | Fraction | - | the movable finger's length, x the fixed finger's | 0.6..1.5 | default 1.08 |
| `openDeg` | float | Degrees | - | the movable finger held open by this much | 0..60 | default 10 |
| `curveDeg` | float | Degrees | - | each finger curls toward the other by this much over its length | 0..80 | default 22 |
| `pitchDeg` | float | Degrees | - | the fingers pitched down from the palm's axis | -40..40 |  |
| `rollDeg` | float | Degrees | - | the claw rolled about the propodus (mirrored by side; + turns the top outward) | -180..180 |  |
| `yawDeg` | float | Degrees | - | the claw turned about the up axis at its wrist (mirrored by side; + points the fingers toward the midline): claws held across the face | -90..90 |  |
| `tipRound` | float | Fraction | - | finger tips: 0 = sharp points, 0.5 = blunt rounded ends | 0..0.7 |  |
| `teeth` | int | Count | - | teeth along each finger's biting edge | 0..8 | default 3 |
| `toothSize` | float | Metres | x s | tooth size (height) |  |  |
| `molar` | float | Fraction | - | the first (innermost) tooth x this: a crusher's molar | 0.5..3 | default 1 |
| `tipFraction` | float | Fraction | - | dark tip: this share of each finger's length, in tipLayer | 0..1 | default 0.3 |
| `spines` | int | Count | - | spines on the palm's outer and upper faces | 0..40 |  |
| `spineLength` | float | Metres | x s | palm spine length |  |  |
| `seed` | int | Count | - | seed of the spines' scatter |  |  |
| `layer` | int | Count | - | material layer of the palm and fingers (1 = the limbs') | 0..15 | default 1 |
| `tipLayer` | int | Count | - | material layer of the finger tips and teeth (2 = the claw row) | 0..15 | default 2 |
| `spineLayer` | int | Count | - | material layer of the palm spines | 0..15 | default 1 |
| `around` | int | Count | - | mesh: vertices around each section (0 = 14) | 0..32 |  |

### DimpledOrb

A big round body on a bone of its own covered in golf-ball pits (spherical Fibonacci lattice, analytic pit normals, pits darkened through the dorsal channel); it rocks on a damped spring about a pivot when the body accelerates (bobs, starts, stops, turns). System: DimpledOrbRuntime (part runtime, stage AfterLimbs: its bone) + DimpledOrb.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `radius` | float | Metres | x s | radius | 0.005..3 | golfball strider 0.16 |
| `squash` | float | Fraction | - | height x the radius (1 = round) | 0.3..2 | default 1 |
| `stretch` | float | Fraction | - | length x the radius (1 = round) | 0.3..2.5 | default 1 |
| `pitCount` | int | Count | - | pits over the whole surface (0 = smooth) | 0..3000 | golf ball 336 |
| `pitRadius` | float | Fraction | - | pit radius x the lattice spacing (0.5 = pits touching) | 0.1..0.6 | default 0.42 |
| `pitDepth` | float | Metres | x s | pit depth |  | golfball strider 0.005 |
| `pitShade` | float | Fraction | - | how much darker a pit's floor is (dorsal channel drop) | 0..1 | default 0.5 |
| `detail` | int | Count | - | mesh: icosphere subdivisions (4 = 2562 vertices, 5 = 10242) | 2..6 | default 5 |
| `pivot` | Vector3 | Metres | x s | the point it rocks about, in the joint's frame (where it sits on the body) |  |  |
| `rockHz` | float | Hz | / sqrt(s) | rocking spring frequency | 0.2..20 | default 2 |
| `rockDamping` | float | Fraction | - | rocking damping ratio | 0..2 | default 0.3 |
| `rockGain` | float | Fraction | - | how strongly the pivot's acceleration rocks it (1 = a free pendulum's) | 0..3 | default 1 |
| `maxRockDeg` | float | Degrees | - | largest rock | 0..45 | default 12 |
| `layer` | int | Count | - | material layer (part id) | 0..15 | default 0 |

### EyeStalk

A rigid stalk on a bone of its own rooted on a spine joint (a crab's eye stalk): rigid to its joint at rest; a posture part (Tuck) swings it about its root to tuckDirection, and the eye of its side rides its tip (carriesEye). Tapered tube, rounded tip. System: EyeStalkRuntime (part runtime, bones only) + EyeStalk.EmitMesh; folded by TuckRuntime.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `direction` | Vector3 | Direction | - | rest direction in the joint frame (x mirrored by side) |  |  |
| `tuckDirection` | Vector3 | Direction | - | direction when folded by a Tuck part (x mirrored by side); zero = it never folds |  |  |
| `length` | float | Metres | x s | length, root to tip |  | rock crab 0.045 |
| `radiusStart` | float | Metres | x s | radius at the root |  |  |
| `radiusEnd` | float | Metres | x s | radius at the tip |  |  |
| `carriesEye` | bool | Flag | - | the eye of this side (Eyes part: eye 0 left, 1 right) rides the tip: place the Eyes' offset at the tip |  |  |
| `layer` | int | Count | - | material layer (part id): 1 the limbs', 2 the claw/cone row, 9 the band colour's | 1..9 | default 1 |

### Jaw

A hinged jaw on its own bone: a lofted solid (RigidLoft sections) with a tooth row along each lip, hinged at the socket about the joint's x axis. Idle gape: after the body stands still a while it opens, holds and closes (eased), and closes at once when the body walks on. System: JawRuntime (part runtime, stage Late) + Jaw.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `direction` | Vector3 | Direction | - | the jaw's axis from the hinge in the joint frame (along the snout) |  |  |
| `back` | float | Metres | x s | the loft starts this far behind the hinge (along -direction) |  |  |
| `length` | float | Metres | x s | loft length along the axis (from its start) |  | crocodile snout 0.26 |
| `keys` | LoftKey[] | Struct | - | cross-sections along the loft (RigidLoft's LoftKey); >= 2, t = 0 and t = 1 included |  |  |
| `keys[].t` | float | Fraction | - | place along the loft, 0 = its start .. 1 = its end | 0..1 |  |
| `keys[].halfWidth` | float | Metres | x s | half width (across, the joint frame's x) |  |  |
| `keys[].halfHeight` | float | Metres | x s | half height (the loft's up) |  |  |
| `keys[].rise` | float | Metres | x s | centre line raised (+) or lowered (-) along the loft's up |  |  |
| `keys[].squareTop` | float | Fraction | - | superellipse exponent of the upper half: 2 = round, 4 = squared, flat-topped | 1..12 |  |
| `keys[].squareBottom` | float | Fraction | - | superellipse exponent of the lower half | 1..12 |  |
| `curveDeg` | float | Degrees | - | the axis curls down (+) or up (-) over the length |  |  |
| `opensUp` | bool | Flag | - | opens upward (an upper jaw / snout that lifts); off: a lower jaw that drops |  |  |
| `gapeDeg` | float | Degrees | - | widest gape (hinge angle) | 0..80 | crocodile 38 |
| `restSeconds` | float | Seconds | x sqrt(s) | the body stands still this long before a gape |  |  |
| `intervalSeconds` | float | Seconds | x sqrt(s) | mean pause between gapes (jittered x 0.5 .. 1.5) |  |  |
| `openSeconds` | float | Seconds | x sqrt(s) | opening time (eased) |  |  |
| `holdSeconds` | float | Seconds | x sqrt(s) | how long it holds the jaw open (jittered x 0.6 .. 1.4) |  |  |
| `closeSeconds` | float | Seconds | x sqrt(s) | closing time (eased) |  |  |
| `releaseSpeed` | float | Fraction | - | the body moves faster than this fraction of its walking speed: close | 0..1 |  |
| `teethPerSide` | int | Count | - | teeth per side (0 = none) | 0..24 |  |
| `toothLength` | float | Metres | x s | tooth length (the first and last teeth 0.6 x) |  |  |
| `toothRadius` | float | Metres | x s | tooth base radius |  |  |
| `teethFrom` | float | Fraction | - | teeth from this place along the loft .. | 0..1 |  |
| `teethTo` | float | Fraction | - | .. to this place | 0..1 |  |
| `toothInset` | float | Fraction | - | teeth sit this fraction of the half width in from the lip edge | 0..0.5 |  |
| `toothSplayDeg` | float | Degrees | - | teeth splay outward by this much | -30..45 |  |
| `layer` | int | Count | - | material layer of the jaw: 0 the body's skin, 2 claw / horn (a beak) | 0..15 |  |
| `toothLayer` | int | Count | - | material layer of the teeth: 2 claw (default), 13 teeth (restyle it with Layers) | 0..15 | default 2 |
| `around` | int | Count | - | mesh: vertices around each section (0 = 20) | 0..48 |  |
| `rings` | int | Count | - | mesh: sections along the length (0 = 16) | 0..64 |  |
| `hideLoft` | bool | Flag | - | draws no loft (its keys still bound it): a Sculpt that follows this jaw draws it |  |  |
| `headLiftDeg` | float | Degrees | - | as it gapes the head lifts too: the rigid head's bones and the eyes turn up about the hinge by this much at full gape, the jaw opening gapeDeg from the lifted head (a crocodile: the skull rises as the lower jaw drops). Put the Jaw after the Eyes | 0..60 | crocodile 24 |

### LimbSpines

Spines or bristles along an ArthropodLeg's segments: perMetre per segment, scattered along and round the tube (aroundDeg +- spreadDeg from the top), raked toward the tip. Long thin ones read as hairs. Deterministic; mirror names the limb's .L / .R. System: mesh only (LimbSpines.EmitMesh), rigid to the leg's bones.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `leg` | string | PartRef | - | the ArthropodLeg; a mirrored part names the unmirrored id and takes .L / .R |  |  |
| `fromSegment` | int | Count | - | first segment with spines | 0..6 |  |
| `toSegment` | int | Count | - | last segment with spines | 0..6 | default 6 |
| `perMetre` | float | Count | - | spines per metre of segment (x 1 / s: the same count at every size) | 0..2000 | king crab 90 |
| `aroundDeg` | float | Degrees | - | round the tube: the band's centre (0 = top, 90 = toward the head, 180 = below) | -180..180 |  |
| `spreadDeg` | float | Degrees | - | round the tube: +- this about the centre | 0..180 | default 70 |
| `length` | float | Metres | x s | spine length |  |  |
| `lengthJitter` | float | Fraction | - | length varies by +- this share | 0..0.9 | default 0.3 |
| `baseRatio` | float | Fraction | - | base radius, x the length | 0.03..1 | default 0.3 |
| `tipRatio` | float | Fraction | - | tip radius, x the base radius (0 = sharp) | 0..1 |  |
| `leanDeg` | float | Degrees | - | raked toward the leg's tip | -80..85 | default 45 |
| `curveDeg` | float | Degrees | - | each spine curls toward the tip by this much | -90..90 |  |
| `seed` | int | Count | - | seed of the scatter |  |  |
| `layer` | int | Count | - | material layer (1 = the limbs', 2 = the claw row, 9 = the band colour) | 0..15 | default 1 |
| `sides` | int | Count | - | mesh: sides of each cone (0 = 5) | 0..12 |  |

### Pincer

A hinged, jointed, curved appendage on 1-4 bones that opens and closes: curls toward its closed side about one hinge axis, tapers to a sharp tip in the band colour, joint bulbs and inner spines. Closed at rest, walkOpen while walking; at a standstill it flexes slowly (open, hold, snap shut, rest) every flexPeriod. mirror makes the pair. System: PincerRuntime (part runtime, stage Late) + Pincer.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `direction` | Vector3 | Direction | - | rest direction of the first segment in the joint frame (x mirrored by side) |  |  |
| `hingeAxis` | Vector3 | Direction | - | hinge axis in the joint frame (x mirrored): opening turns the chain about it, away from the midline for (0, 1, 0) on a forward-pointing part |  | default (0, 1, 0) |
| `length` | float | Metres | x s | length along the chain, root to tip |  | scolopendra forcipule 0.011 |
| `segments` | int | Count | - | segments (one bone each) | 1..4 | default 2 |
| `baseFraction` | float | Fraction | - | the first segment's share of the length (0 = equal segments) | 0..0.9 |  |
| `radiusStart` | float | Metres | x s | radius at the root |  |  |
| `radiusEnd` | float | Metres | x s | radius where the tip starts |  |  |
| `tipLength` | float | Metres | x s | the pointed tip at the end (tapers to a point; band colour); <= the last segment |  |  |
| `curveDeg` | float | Degrees | - | curl toward the closed side over the whole length | -180..270 |  |
| `knuckle` | float | Fraction | - | bulb at each joint between segments, x the radius (0 = none) | 0..1 |  |
| `spines` | int | Count | - | spines along the first segment's inner edge (one: a tooth at its end) | 0..8 |  |
| `spineLength` | float | Metres | x s | spine length |  |  |
| `openDeg` | float | Degrees | - | opening at the peak of a flex (about the hinge) | 0..90 | forcipule 32 |
| `distalShare` | float | Fraction | - | share of the opening taken by the joints past the first (the tip unfolding) | 0..1 |  |
| `flexPeriod` | float | Seconds | x sqrt(s) | one idle flex: open slowly, hold, snap shut, rest |  | default 3 |
| `idleDelay` | float | Seconds | x sqrt(s) | standstill before the first flex |  | default 0.6 |
| `walkOpen` | float | Fraction | - | opening held while walking, x openDeg | 0..1 |  |
| `phase` | float | Fraction | - | place in the flex cycle at its start (a second pair can lag the first) | 0..1 |  |
| `displayOpenDeg` | float | Degrees | - | threat display (CreatureSim.Display, a ThreatDisplay part): the opening held at the full display; 0 = no display pose | 0..90 |  |
| `displayRaiseDeg` | float | Degrees | - | threat display: the whole appendage lifts about its root toward the joint's up by this much | -60..90 |  |
| `spineSegments` | int | Count | - | segments carrying the inner spines (the first N; 0 or 1 = the first only) | 0..4 |  |
| `layer` | int | Count | - | material layer (part id) of the segments: 11 = pincer; 1 limb, 2 claw, 10 plate | 0..15 | default 11 |
| `sides` | int | Count | - | mesh: sides of the tube (0 = 10) | 0..24 |  |

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
| `lumpHeight` | float | Metres | x s | lumps: the surface pushed in and out by up to this much (smooth noise; 0 = smooth). A boulder, a knobbly shell |  | rock crab 0.012 |
| `lumpSize` | float | Metres | x s | lumps: their size (noise feature size) |  | rock crab 0.06 |
| `lumpSeed` | int | Count | - | lumps: noise seed (another seed, other lumps) |  |  |
| `lumpSymmetric` | bool | Flag | - | lumps: mirror-symmetric about the loft's midline (a carapace's regions; the left side's lumps the mirror of the right's) |  |  |
| `lumpRidge` | float | Fraction | - | lumps: 0 = smooth bumps .. 1 = ridged (creases between angular facets: weathered rock, a grooved shell) | 0..1 |  |

### Sculpt

A solid modelled as a signed-distance field: ellipsoids, round cones, rounded boxes and planes in the joint frame, smooth-unioned, carved (smooth subtraction) and trimmed (smooth intersection), mirrored across the midline; polygonised once per species by surface nets with normals from the field's gradient, plus tooth rows placed on its lip line. Rigid to its joint or riding a Jaw's bone. System: mesh only (Sculpt.EmitMesh -> SculptMesher, cached per shape set), rigid to one bone.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `shapes` | SculptShape[] | Struct | - | the field's shapes, combined in order (the first is usually a Union) |  |  |
| `shapes[].kind` | Ellipsoid / RoundCone / Box / Plane | Enum | - | Ellipsoid \| RoundCone (a tapered capsule from centre to end) \| Box (rounded) \| Plane (solid below it; for Intersect / Subtract) |  |  |
| `shapes[].op` | Union / Subtract / Intersect | Enum | - | Union (add) \| Subtract (carve: a socket, a nostril, a groove) \| Intersect (trim) |  |  |
| `shapes[].blend` | float | Metres | x s | smooth blend radius with the shapes before it (0 = a sharp crease) |  |  |
| `shapes[].centre` | Vector3 | Metres | x s | centre in the joint frame (RoundCone: its start; Plane: a point on it) |  |  |
| `shapes[].size` | Vector3 | Metres | x s | Ellipsoid radii / Box half extents; RoundCone: x = radius at the start, y = radius at the end |  |  |
| `shapes[].end` | Vector3 | Metres | x s | RoundCone: its end in the joint frame |  |  |
| `shapes[].rotationDeg` | Vector3 | Degrees | - | rotation of an Ellipsoid, Box or Plane: x pitch (+ nose down), y yaw (+ toward +x), z roll; a Plane's normal is its rotated +y |  |  |
| `shapes[].round` | float | Metres | x s | Box: corner rounding |  |  |
| `shapes[].squash` | float | Fraction | - | RoundCone: its cross-section's height / width (0 or 1 = round, 0.5 = flattened) | 0..4 |  |
| `shapes[].mirror` | bool | Flag | - | also its mirror image across the midline (x -> -x): eye sockets, nostrils, jowls (an Intersect trims by both) |  |  |
| `follow` | string | PartRef | - | a Jaw part whose bone it rides (a lower jaw that opens); empty = rigid to its joint |  |  |
| `cell` | float | Metres | x s | grid cell of the polygoniser (smaller: finer and slower; ~1/60 of the head's length) |  | crocodile 0.0045 |
| `layer` | int | Count | - | material layer (part id): 0 the body's skin, 2 claw / horn | 0..15 |  |
| `liningBelow` | float | Metres | x s | the surface's upward-facing parts (normal within 60 deg of up) below this height (joint frame) are shaded as belly (a mouth's floor and palate show the pale lining, not the back); 0 = off | -1..1 |  |
| `dorsalShift` | float | Fraction | - | shading: added to the dorsal value (-1 belly .. +1 back); -0.5 turns a lower jaw's sides pale like the throat | -2..2 |  |
| `teeth` | SculptTeeth[] | Struct | - | tooth rows on the surface |  |  |
| `teeth[].from` | Vector3 | Metres | x s | guide line start in the joint frame (just outside the lip, on the side the teeth come out) |  |  |
| `teeth[].to` | Vector3 | Metres | x s | guide line end |  |  |
| `teeth[].bow` | Vector3 | Metres | x s | the guide line bows by this much at its middle (a curved tooth row) |  |  |
| `teeth[].count` | int | Count | - | teeth in the row | 1..32 |  |
| `teeth[].length` | float | Metres | x s | tooth length (the end teeth 0.6 x) |  |  |
| `teeth[].radius` | float | Metres | x s | tooth base radius |  |  |
| `teeth[].direction` | Vector3 | Direction | - | the way the teeth point (an upper row: down), in the joint frame; each tooth sits where this line leaves the solid |  |  |
| `teeth[].splayDeg` | float | Degrees | - | teeth splay outward (away from the midline) by this much | -45..45 |  |
| `teeth[].curveDeg` | float | Degrees | - | each tooth curls back by this much | -60..60 |  |
| `teeth[].jitter` | float | Fraction | - | tooth lengths vary by up to this fraction (deterministic) | 0..0.9 |  |
| `teeth[].sink` | float | Fraction | - | sunk into the gum by this fraction of its length | 0..0.9 |  |
| `teeth[].mirror` | bool | Flag | - | also the mirror row (x -> -x) |  |  |
| `toothLayer` | int | Count | - | material layer of the teeth: 2 claw, 13 teeth | 0..15 | default 13 |

### ScuteRow

A row of raised keeled scutes (osteoderms) along the spine at an angle round the body that may drift along the row, tapering; mirror for the paired rows. Each scute a flat-shaded keeled pyramid riding the spine as one piece. System: mesh only (ScuteRow.EmitMesh), skinned to the spine bones.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `toJoint` | int | Joint | - | last spine joint of the row (the first is at.joint) |  |  |
| `perJoint` | float | Fraction | - | scutes per spine segment | 0.25..4 | default 1 |
| `aroundStartDeg` | float | Degrees | - | angle round the body from the back at the first scute (mirror: also at minus this) | 0..150 |  |
| `aroundEndDeg` | float | Degrees | - | angle round the body at the last scute | 0..150 |  |
| `heightStart` | float | Metres | x s | ridge height at the first scute |  | crocodile back 0.012 |
| `heightEnd` | float | Metres | x s | ridge height at the last scute |  |  |
| `widthStart` | float | Metres | x s | scute width (across) at the first scute |  |  |
| `widthEnd` | float | Metres | x s | scute width at the last scute |  |  |
| `lengthFraction` | float | Fraction | - | scute length, fraction of the gap between scutes | 0.2..1.2 | default 0.85 |
| `keelWidth` | float | Fraction | - | keel: the ridge's width, fraction of the scute's (0 = a sharp keel) | 0..0.9 |  |
| `frontHeight` | float | Fraction | - | the ridge's front end height, fraction of its back end's (a saw-tooth profile when low) | 0..1 | default 0.45 |
| `inset` | float | Fraction | - | the base sinks this fraction of the height into the skin | 0..1 | default 0.35 |
| `layer` | int | Count | - | material layer: 12 armour (restyle it with Layers), 0 the body's skin | 0..15 | default 12 |

### SkinFolds

Loose skin over a stretch of the spine: transverse folds round the body on an angular window (throat, flanks) and a sagging dewlap below, fading into the body at both ends. Skinned to the spine like the body rings, shaded as the body. System: mesh only (SkinFolds.EmitMesh), skinned to the spine bones.

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
| `folds` | int | Count | - | folds (ridges round the body) over the stretch | 1..40 |  |
| `height` | float | Metres | x s | fold height at the middle of the stretch |  | komodo throat 0.006 |
| `sharpness` | float | Fraction | - | how sharp each fold is: 1 = a gentle wave, 4 = narrow creases between flat skin | 1..8 | default 2 |
| `aroundDeg` | float | Degrees | - | centre of the window round the body from the back: 180 the throat, 90 the flank (mirror: also at minus this) | 0..180 | default 180 |
| `widthDeg` | float | Degrees | - | angular half width of the window | 10..180 | default 70 |
| `sag` | float | Metres | x s | loose skin sagging straight down at the middle of the stretch (a dewlap) |  |  |
| `sagAt` | float | Fraction | - | where along the stretch the sag is deepest (0 = at.joint .. 1 = toJoint) | 0.05..0.95 | default 0.5 |
| `inset` | float | Fraction | - | both ends sink this fraction of the body's radius inside the body | 0..0.5 | default 0.06 |
| `ringsPerJoint` | int | Count | - | mesh: rings per spine segment (0 = 8) | 0..16 |  |
| `around` | int | Count | - | mesh: vertices round each ring (0 = 32) | 0..64 |  |

### SpiderFace

A small spider face set on a body: a rounded cap, a cluster of glossy eyes (a big pair, then smaller pairs further out and up), and two chelicera fangs hanging from its lower front, curling back. Rigid to its joint or riding a DimpledOrb's bone. System: mesh only (SpiderFace.EmitMesh).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `on` | string | PartRef | - | a DimpledOrb whose bone the face rides (empty = rigid to its joint) |  |  |
| `direction` | Vector3 | Direction | - | where the face looks, in the joint frame (+z forward, -y down) |  |  |
| `width` | float | Metres | x s | cap width |  | golfball strider 0.075 |
| `height` | float | Metres | x s | cap height |  |  |
| `depth` | float | Metres | x s | cap depth (how far it bulges along the facing direction) |  |  |
| `eyeCount` | int | Count | - | eyes (pairs from the middle out; an odd count puts one on top) | 0..8 | default 6 |
| `eyeRadius` | float | Metres | x s | radius of the big front pair (the others are smaller) |  |  |
| `fangLength` | float | Metres | x s | fang length (0 = none) |  |  |
| `fangRadius` | float | Metres | x s | fang radius at its root |  |  |
| `fangCurveDeg` | float | Degrees | - | fang curl back toward the body over its length |  | default 45 |
| `fangSpread` | float | Fraction | - | fangs apart, x the cap width | 0..1 | default 0.32 |
| `layer` | int | Count | - | material layer of the cap (part id) | 0..15 | default 1 |

### SpineField

Spines, knobs or teeth on a RigidLoft's surface: scattered over a patch (t along, phi round; min spacing) or an even row (marginal teeth); curved cones along the surface normal raked back, sizes graded along the loft. Deterministic; mirror makes the symmetric pair. System: mesh only (SpineField.EmitMesh), rigid to the loft's joint.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `loft` | string | PartRef | - | the RigidLoft it grows from |  |  |
| `count` | int | Count | - | spines (a row: per side) | 1..200 |  |
| `row` | bool | Flag | - | an even row along the loft at phiMin (marginal teeth) instead of a scatter |  |  |
| `tMin` | float | Fraction | - | patch along the loft: from .. | 0..1 |  |
| `tMax` | float | Fraction | - | .. to | 0..1 | default 0.9 |
| `phiMinDeg` | float | Degrees | - | patch round the loft: from (0 = right margin, 90 = top, 180 = left, -90 = belly; mirrored by side) | -180..270 |  |
| `phiMaxDeg` | float | Degrees | - | .. to | -180..270 | default 90 |
| `lengthMin` | float | Metres | x s | shortest spine |  |  |
| `lengthMax` | float | Metres | x s | longest spine |  |  |
| `baseRatio` | float | Fraction | - | base radius, x the length | 0.05..2 | default 0.35 |
| `tipRatio` | float | Fraction | - | tip radius, x the base radius (0 = sharp, 0.6 = a blunt knob) | 0..1 |  |
| `startScale` | float | Fraction | - | size x this at the loft's start .. | 0..3 | default 1 |
| `endScale` | float | Fraction | - | .. and x this at its end | 0..3 | default 1 |
| `leanDeg` | float | Degrees | - | raked back toward the loft's start (negative: toward its end) | -80..80 |  |
| `curveDeg` | float | Degrees | - | each spine curls (toward the rake) by this much | -90..90 |  |
| `spacing` | float | Metres | x s | scatter: no two bases closer than this (0 = no limit) |  |  |
| `seed` | int | Count | - | seed of the scatter |  |  |
| `layer` | int | Count | - | material layer (0 = the body skin, 2 = the claw row) | 0..15 |  |
| `sides` | int | Count | - | mesh: sides of each cone (0 = 6) | 0..16 |  |

### Stumble

A clumsy walker: every so often (seeded per individual) the body lurches down front first and springs back, stopping for part of it to readjust; adds to CreatureSim.JointLift and sets Halt. Froude-timed. System: StumbleRuntime (part runtime, stage Late) + CreatureSim JointLift / Halt.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `every` | float | Seconds | x sqrt(s) | mean time between stumbles (each gap 0.5..1.5 x this, per individual) |  | golfball strider 6 |
| `seconds` | float | Seconds | x sqrt(s) | one stumble: the quick dip and the springy recovery |  | golfball strider 0.7 |
| `depth` | float | Metres | x s | how far the front of the body drops |  | golfball strider 0.07 |
| `lurch` | float | Fraction | - | lurch: the share of the dip the tail end does not take (0 = level, 1 = only the front) | 0..1 | default 0.6 |
| `haltShare` | float | Fraction | - | the share of a stumble it stops walking to readjust (0 = walks through it) | 0..1 | default 0.5 |

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

### ThreatDisplay

Posture: after standing still for idleDelay the creature rears into a threat display - head and forebody lifted (headLift fading over liftJoints), CreatureSim.Display ramped to 1 so parts with a display pose follow (Pincer.displayOpenDeg / displayRaiseDeg: forcipules spread, ultimate legs raised). Holds while it stands, drops when it walks. System: ThreatDisplayRuntime (part runtime, stage Late) + CreatureSim JointLift / Display.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `idleDelay` | float | Seconds | x sqrt(s) | standstill before the display starts |  | default 0.6 |
| `riseSeconds` | float | Seconds | x sqrt(s) | time to rear fully into the display |  | default 0.5 |
| `dropSeconds` | float | Seconds | x sqrt(s) | time to drop out of it when the creature walks off |  | default 0.25 |
| `headLift` | float | Metres | x s | lift of the head (joint 0) at the full display |  | scolopendra 0.006 |
| `liftJoints` | int | Count | - | joints the lift fades over (joint 0 full .. this joint none) | 1..32 | default 6 |

### Tuck

Posture: when scared (CreatureSim.Scare, or a random timer per individual) it stops, crouches, folds its ArthropodLegs (LegFold rows: a pitch per segment in a fold plane) and EyeStalks away, sleeps exactly still, then peeks (stalks first), unfolds and walks on. Rock mimicry, playing dead, a pill bug's roll. System: TuckRuntime (part runtime, stage Late; the LAST part: blends the other parts' bones) + CreatureSim Halt / Crouch / Asleep.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `curlSeconds` | float | Seconds | x sqrt(s) | time to reach the tucked pose |  | rock crab 0.45 |
| `holdSeconds` | float | Seconds | x sqrt(s) | time held tucked after a full scare (x 0.5..1 with the intensity, x 0.7..1.3 per tuck) |  | 4 |
| `peekSeconds` | float | Seconds | x sqrt(s) | the peek: the stalks rise and look about while the body stays down |  |  |
| `uncurlSeconds` | float | Seconds | x sqrt(s) | time to unfold |  |  |
| `crouch` | float | Metres | x s | how far the body crouches (every joint's clearance lowered) |  |  |
| `randomScareSeconds` | float | Seconds | x sqrt(s) | random scares: mean time up and about between them (uniform 0.25..1.75 x this, per individual); 0 = only real scares (CreatureSim.Scare) |  | rock crab 14 |
| `minScare` | float | Fraction | - | a scare weaker than this is ignored | 0..1 |  |
| `legs` | LegFold[] | Struct | - | how each folded leg folds (legs not listed keep their IK pose) |  |  |
| `legs[].leg` | string | PartRef | - | the ArthropodLeg it folds; a mirrored leg's id (without .L / .R) folds both |  |  |
| `legs[].yawDeg` | float | Degrees | - | the fold plane: 0 = straight out sideways, + swept toward the tail, - toward the head (mirrored by side) | -90..90 |  |
| `legs[].pitchDeg` | float[] | Degrees | - | each segment's pitch in the fold plane (body out): 0 = out level, 90 = up, 180 = back in level, -90 = down; one per segment |  |  |
| `legs[].lag` | float | Fraction | - | starts folding this fraction of the curl later (and unfolds that much earlier) | 0..0.6 |  |
| `legs[].segmentYawDeg` | float[] | Degrees | - | optional: a fold plane per segment (its yaw, as yawDeg; one per segment), so a limb can fold flat - the merus out, the rest swept back along the body's side; empty = every segment in yawDeg's plane |  |  |

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

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `arriveRadius` | float | Metres | x s | a goal counts as reached within this distance (0 = 0.4) |  | scolopendra 0.12 |
| `pauseMin` | float | Seconds | x sqrt(s) | shortest pause at a goal (with pauseMax > 0; Froude-timed) |  |  |
| `pauseMax` | float | Seconds | x sqrt(s) | longest pause at a goal (0 = the default 0.5..3 s, not size-scaled) |  | scolopendra 0.5..1.2 |

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
| `waveGroups` | int | Count | - | groups of a Wave pattern: legs per side in one wavelength (each foot is in the air 1/waveGroups of the time) | 2..15 | centipede 5 |
| `waveTailToHead` | bool | Flag | - | Wave: the lift-offs run tail to head (a millipede's direct wave) instead of head to tail (a centipede's retrograde wave) |  |  |
| `bob` | GaitBobDef | Struct | - | vertical body bounce kicked by each lift-off |  |  |
| `bob.amplitude` | float | Metres | x s | peak dip of the body per lift-off (0 = off) |  |  |
| `bob.hz` | float | Hz | / sqrt(s) | spring frequency |  |  |
| `bob.damping` | float | Fraction | - | damping ratio (0.2 bouncy .. 1 dead) | 0..2 |  |
| `burst` | GaitBurstDef | Struct | - | speed bursts: every so often the cruise speed surges (a predator's dash), seeded per individual; factor 0 = off |  |  |
| `burst.factor` | float | Fraction | - | cruise speed x this at the peak of a burst (<= 1 = off); the gait's stride limit still caps it | 0..4 | scolopendra 2.2 |
| `burst.every` | float | Seconds | x sqrt(s) | mean time between bursts (each gap 0.5..1.5 x this, per individual) |  |  |
| `burst.seconds` | float | Seconds | x sqrt(s) | one burst: a quick surge, a hold, an easing back |  |  |

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

## Material layers (mesh part ids; next free: 15)

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
| 10 | armour plate (ArmourPlates) | Chitin (NoPattern, NoGrain) |
| 11 | pincer, jaw (Pincer) | Claw (NoPattern, NoGrain, tint Pattern 0.3) |
| 12 | armour scutes (ScuteRow) | Skin (Sheen, Shimmer) |
| 13 | teeth (Jaw) | Skin (Sheen, Shimmer) |
| 14 | tongue (Tongue) | Skin (Sheen, Shimmer) |
