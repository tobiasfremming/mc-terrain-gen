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
| **FlowBody** | Body | exists | = jointCount | 0 | a body whose neck, trunk and tail flow into each other with a section an ellipse cannot draw: humped backs, keeled chests, ribbon or paddle tails | Apex, Vesper |
| **RayBody** | Body | exists | = jointCount | 15 | manta and eagle rays, skates, stingrays, flatfish-like gliders: a disc body whose sides are its wings | SkyManta |
| **SoftBody** | Body | exists | 6 + mouthCount x mouth.segments | 5, 6, 7 | boneless bodies: octopus, slug, jelly | Swinger, TreeOctopus |
| **SpineBody** | Body | exists | = joints | 0 | anything with a backbone or a segmented trunk | Crocodile, DaddyLongLegs, Delver, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, Pterodactyl, RockCrab, Scolopendra, ShaggyMonitor, SkyDragon, StriderMatriarch, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeSpider, WingedSerpent |
| **ArthropodLeg** | Limb | exists | = segments | 1, 2, 9 | insects, spiders, crabs, centipedes; anything with visible segments | DaddyLongLegs, DesertBeetle, GiantCentipede, RockCrab, Scolopendra, Tarantula, TentacleCrab, TreeSpider |
| **BendyLeg** | Limb | exists | = segmentCount | 1 | harvestmen, stilt-legged and wobbly animals, any limb that should look flexible: non-uniform, kinked, bouncing legs | GolfballStrider, StriderMatriarch |
| **DigLeg** | Limb | exists | 3 | 1, 11 | burrowers and diggers: moles, armadillos, pangolins, badgers, aardvarks, any heavy limb with spade claws | Delver |
| **DrakeLeg** | Limb | exists | 3 | 1, 2 | dragons, wyverns, raptors, birds: any digitigrade hind leg standing on toes with talons | Apex, Vesper |
| **Leg2Bone** | Limb | exists | 3 | 1, 2 | lizards, mammals, birds' legs, wing arms | Apex, DesertMonitor, Hexadrake, KitMonitor, Pterodactyl, ShaggyMonitor, SkyDragon, TestHexapod, Vesper, WingedSerpent |
| **SprawlLeg** | Limb | exists | 3 | 1, 2 | crocodilians, monitors, tegus, salamanders, any heavy-bodied sprawling or semi-erect walker | Crocodile, KomodoDragon |
| **DigitWing** | Wing | exists | as Wing + fingers x fingerSegments + thumbSegments |  | skeleton-first winged creatures (Vesper): long bony fingers that will carry a membrane between them later; the arm's proportions as FleshWing's (a short humerus, a long forearm) | Vesper |
| **FleshWing** | Wing | exists | as Wing + fingers x (spanSegments - 1) + 1 (the fan sector) + 3 (the arm's flesh: humerus, elbow, forearm) | 0 | meaty manta-like wings that fold into tall soft blades and are walked on (the Apex); not a membrane between bony fingers. The steep folded blades come from the arm's proportions: a short humerus and a long forearm (upper << lower, e.g. 0.2 / 0.5 on a 0.5 m chest), the wrist's restOffset behind the shoulder (z ~ -0.22) and close in (x ~ 0.06), pole (0.2, 1, -0.4): the forearm stands near vertical and the folded fingers run up along it, side by side over the back | Apex |
| **PteroWing** | Wing | exists | as Wing + 6 (wing-finger phalanges 2-4, the pteroid, 2 propatagium patches) | 8, 1, 2 | pterosaurs (azhdarchids, pteranodons): a long single wing finger, a pteroid and propatagium, walking on the folded wings' knuckles | Pterodactyl |
| **Wing** | Wing | exists | 6 + max(1, spec.flankPatches), +1 with spec.groundTallStance | 8, 1, 2 | bats, pterosaurs, dragons; the Fly module makes it fly | Hexadrake, SkyDragon, WingedSerpent |
| **Eyes** | Sense | exists | 2 | 3 | always (every shipped species has two eye bones) | Apex, Crocodile, DaddyLongLegs, Delver, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, Pterodactyl, RockCrab, Scolopendra, ShaggyMonitor, SkyDragon, SkyManta, StriderMatriarch, Swinger, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeOctopus, TreeSpider, Vesper, WingedSerpent |
| **Antenna** | Chain | exists | = segments | 8 | whiskers, barbels, antennae, feelers, cerci, ribbon-like streamers | GiantCentipede, Scolopendra, SkyDragon |
| **AttachedChain** | Chain | exists | = segments + 1 |  | extra feature chains on a spine body that must follow it: skin pouches and flaps hung from jaw/neck joints to the chest (two-ended), wattles, streamers, lappets and free flaps (one-ended); the skeleton the skin is lofted on later | - |
| **Dewlap** | Chain | exists | = stations x links |  | a dewlap, throat fan, pelican pouch or throat sail hanging under a FlowBody's neck | Apex |
| **LongNeck** | Chain | exists | = segments (the last is the head's bone) | 0 | long necks that are carried, not dragged: pterosaurs, giraffes, sauropods, swans, plesiosaurs; any head held up off the body | Pterodactyl |
| **TailFlourish** | Chain | exists | 0 (re-poses the body's tail bones) |  | a long decorative tail that should look alive without steering anything: ribbon tails, plumes, a dragon's tail in flight | Apex |
| **Tendril** | Chain | exists | = segments | 1, 9 | tentacles, feeding arms, feelers that touch the ground; on a spine body (soft bodies: Tentacle) | StriderMatriarch, TentacleCrab |
| **Tentacle** | Chain | exists | = spec.segments (0 = 24) | 4 | octopus arms; grabbing arms on any body (after T5.9) | Swinger, TreeOctopus |
| **Tongue** | Chain | exists | = segments | 14 | monitor lizards, snakes: a tongue that flicks in and out; any quick extensible feeler that hides in the head | KomodoDragon |
| **WhipTail** | Chain | exists | = segments | 15 | manta and stingray whips, rat and opossum tails, whip lizards: any long thin tail with no muscle of its own that lags the body | SkyManta |
| **CephalicLobe** | Fin | exists | 2 (base, tip) | 15 | manta and devil ray head fins, rolled ear flaps, curled horn-like frills, a squid's rolled arms at rest | SkyManta |
| **FinStrip** | Fin | exists | 0 (skinned to the spine) | 8 | manes, crests, dorsal / anal fins, tail tufts, frills along a body | FinnedSandEel, Hexadrake, SkyDragon |
| **Flapper** | Fin | exists | = segments (1-3) | 8 | pectoral fins, ear flaps, frills, small paddles that beat or flutter (not wings that fly: Wing) | FinnedSandEel |
| **LegFin** | Fin | exists | 2 (thigh wing, shin wing) | 1, 8 | winged or finned legs: Microraptor hind wings, Caraxes-like leg membranes, any ribbed flap that rides a limb and spreads in flight | SkyDragon |
| **RayWing** | Fin | exists | = stations x chordRows | 15 | manta / eagle ray / skate pectoral fins; any thick wing or fin that bends along its span AND its chord (a whale fluke, a cuttlefish fin, a soft-bodied flyer) | SkyManta |
| **ArmourPlates** | Rigid | exists | 0 (each plate rigid to its joint) | 10 | segmented armour: centipede / millipede tergites, woodlice, trilobites, armadillo bands, ankylosaur back plates; ventral = true: sternites, belly plates | Scolopendra |
| **BandShell** | Rigid | exists | 0 (each band rigid to its joint) | 10 | armadillos, pangolins, glyptodonts, trilobites, woodlice, armoured diggers: any banded shell over the back, with a head shield | Delver |
| **BeakHead** | Rigid | exists | 1 (the lower jaw) | 0, 2 | pterosaurs, birds, herons and storks, beaked dinosaurs, turtles: any head that is mostly beak; with a crest (casque, sail) or without | Pterodactyl |
| **Burrow** | Rigid | exists | 0 (poses the others' bones) |  | burrowers: moles, armadillos, sandworms, burrowing lizards and amphisbaenians, anything that digs in and bursts out | Delver |
| **Chela** | Rigid | exists | 0 (rigid to its ArthropodLeg's last bone) | 1, 2 | crab and lobster claws (crusher and cutter), scorpion pincers: any heavy chela on an arthropod limb | RockCrab, TentacleCrab |
| **CrestBeakHead** | Rigid | exists | 2 (the jaw, the upper head) | 0 | a serpent- or dragon-like head ending in a beak, on a FlowBody (or SpineBody) head joint: crested, toothed beaks; the Apex | Apex |
| **DimpledOrb** | Rigid | exists | 1 | 0 | a round abdomen (spiders, ticks), puffballs, seed heads, golf-ball or pitted shells; any heavy round mass that should wobble on its body | GolfballStrider, StriderMatriarch |
| **EyeStalk** | Rigid | exists | 1 | 1, 2, 9 | crab and snail eye stalks, periscopes, anything on a stalk that folds away | RockCrab, TentacleCrab |
| **Jaw** | Rigid | exists | 1 | 0, 2, 13 | a mouth that opens: a crocodile basking agape (opensUp), a yawn, a threat gape, a snapping beak | Crocodile |
| **LimbSpines** | Rigid | exists | 0 (rigid to the leg's segment bones) | 1, 2, 9 | spiny legs (king crab), leg bristles and setae fringes (stone crab, tarantula), raptorial spines | RockCrab, TentacleCrab |
| **Maw** | Rigid | exists | 1 (the lower jaw) | 0, 13, 14 | reptilian and monstrous heads with a huge gape: burrowers bursting out jaws first, crocodilian or theropod-like heads, deep-sea gapers | Delver |
| **Pincer** | Rigid | exists | = segments (1-4) | 11, 9 | forcipules, mandibles, chelicera fangs, pincers, earwig forceps, centipede ultimate legs - any jointed appendage that opens and closes | Scolopendra |
| **PitRow** | Rigid | exists | 0 (skinned like the body's rings) | 0 | breathing holes along a throat or flank, nostrils, sensory pits on a FlowBody | Apex |
| **RigidCone** | Rigid | exists | 0 (1 with ownBone) | 2 | horns, spikes, fangs, mandibles, chelicerae | GiantCentipede, Hexadrake, SkyDragon, Swinger, Tarantula, TentacleCrab, TreeOctopus, TreeSpider |
| **RigidLoft** | Rigid | exists | 0 (rigid to its joint) | 0, 2 | head shapes: squared snouts, jaws, brows, nostrils, bosses; any rigid lump on a joint | GiantCentipede, Hexadrake, RockCrab, Scolopendra, SkyDragon, TentacleCrab |
| **Sculpt** | Rigid | exists | 0 (rigid to its joint, or rides a Jaw's bone) | 0, 2, 13 | realistic heads and skulls: brows over recessed eye sockets, jowls, lip lines, nostrils, skull tables, a lower jaw on a Jaw; any rigid organic solid a loft cannot express | Crocodile, KomodoDragon |
| **ScuteRow** | Rigid | exists | 0 (skinned to the spine) | 12, 0 | crocodile and alligator armour, a tail's double crest, a stegosaur-like row of plates, spiny lizard backs | Crocodile |
| **SerratedRings** | Rigid | exists | 0 (each ring rigid to its joint) | 10 | armoured tails: glyptodont and ankylosaur tail rings, trilobite pygidia, girdled lizards, serrated or spiked tails, segmented sea-creature tails | Delver |
| **Shag** | Rigid | exists | 0 (each lock rigid to its joint or limb bone) | 0 | furry or shaggy underbodies, manes, beards, chest ruffs, hairy thighs; any coat of locks on a spine body | Delver |
| **SkinFolds** | Rigid | exists | 0 (skinned to the spine) | 0 | neck and throat folds of monitor lizards, a dewlap, wrinkled elbows of skin, the gular folds of iguanas | KomodoDragon |
| **SpiderFace** | Rigid | exists | 0 (rigid to its joint, or rides `on`'s bone) | 1, 3, 2 | spiders, harvestmen, mites, any small-headed arthropod whose face sits low on a round body | GolfballStrider, StriderMatriarch |
| **SpineField** | Rigid | exists | 0 (rigid to its loft's joint) | 0, 2 | a spiny carapace (king crab), marginal teeth (stone crab), bosses, a horned crown: anything studding a shell or a head | RockCrab, TentacleCrab |
| **Stumble** | Rigid | exists | 0 (posture: the sim's JointLift and Halt) |  | goofy, top-heavy or stilt-legged walkers, a tired or injured animal, anything that should look uncoordinated | GolfballStrider, StriderMatriarch |
| **Swell** | Rigid | exists | 0 (skinned to the spine) | 0 | a deep chest with flight muscles at the wing roots, shoulders, haunches, a belly - any bulk the body's elliptical profile cannot give | KomodoDragon, SkyDragon |
| **ThreatDisplay** | Rigid | exists | 0 (sets the sim's posture inputs) |  | a predator that threatens when it stops: centipedes, scorpions, mantises, cobras rearing, a crab raising its claws | Scolopendra |
| **Tuck** | Rigid | exists | 0 (poses the others' bones) |  | a creature that hides by folding up: rock crabs, box crabs, armadillos, pill bugs, tortoises | RockCrab |
| **ArthropodPlan** | Sugar | exists | = the generated parts |  | spiders, crabs: start here, then add or override parts | RockCrab, TentacleCrab |
| **Repeat** | Sugar | exists | = count x template |  | centipede legs, spike rows, many fins | GiantCentipede, Scolopendra |

## Modules

| module | category | status | needs | used by | what |
|---|---|---|---|---|---|
| **Ambush** | Behaviour | exists | SoftBody + Tentacle parts (spine bodies: T5.9) | Swinger, TreeOctopus | Rest / explore / stalk / ambush / strike / hold / retreat (OctopusBrain + prey adapters; arm roles, grooming). Hold 1.5 s, no damage. |
| **Bask** | Behaviour | exists | a Burrow part | Delver | Lies down where it is for a while: curled, the head tucked under its hood, or flat in the sun (the Burrow part's Rest posture); comes due every so often per individual. |
| **ClimbExcursion** | Behaviour | exists | Climb | DesertMonitor, KitMonitor, ShaggyMonitor, TreeSpider | Approach a climbable obstacle, climb to a goal on its real surface, rest, descend (ClimbingBrain wrapping Wander). |
| **Flee** | Behaviour | exists | Walk | FinnedSandEel | Runs straight away from the nearest threat (CreatureThreats: the viewer in play mode) once it comes within the alarm radius, sliding along the enclosure wall when cornered; calms down after it leaves. Gives the species a ModularBrain (with Wander as the idle default). |
| **FlightExcursion** | Behaviour | exists | Fly | Apex, Hexadrake, Pterodactyl, SkyDragon, Vesper, WingedSerpent | Rest, Face, Patrol, Soar, Landing on perches; predictive wall turn-back (FlightBrain). |
| **Follow** | Behaviour | exists | Walk | Delver | Trails a visible member of its group (same species, same enclosure: CreatureGroup) at a distance for a while, every so often per individual. |
| **Forage** | Behaviour | exists | Walk + a Burrow part | Delver | Walks a little way, then sniffs and scratches at the ground (the Burrow part's Scratch posture: head down, digging limbs scooping) for a while; comes due every so often per individual. |
| **School** | Behaviour | exists | an airborne locomotion (Soar) | SkyManta | Flying in a loose group: separation (3D, strongest), alignment, cohesion, a shared waypoint that moves round the enclosure at a height in the band, the wall and the aviary's obstacles; vetoes the locomotion's manoeuvres while a neighbour is near. Members of a lab group find each other by species and enclosure. |
| **SkyHunt** | Behaviour | exists | Fly (+ a LongNeck: the strike) | Pterodactyl | A soaring predator's hunt: climb to soarAltitude, circle in long glides and bursts of slow deep beats, stoop on prey below (HuntPrey; with none, a practice mark on the ground) with the wings folded and the neck reaching, pull out over it with a strike, climb away. In bouts; the species' FlightExcursion flies between them. Gives the species a ModularBrain. |
| **Tunnel** | Behaviour | exists | Walk + a Burrow part | Delver | Every so often (per individual) goes underground to a goal across the enclosure: the Burrow part plans the trip through the enclosure's shared tunnels (walk to a hole, ride, dig only the rest), digs in, travels hidden and bursts out. Holds until the trip ends; never while more than maxHiddenShare of its group is under. Gives the species a ModularBrain. |
| **Wander** | Behaviour | exists |  | Apex, Crocodile, DaddyLongLegs, Delver, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, Pterodactyl, RockCrab, Scolopendra, ShaggyMonitor, SkyDragon, StriderMatriarch, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeSpider, Vesper, WingedSerpent | Safe-disc points, walk, pause, glance (WanderBrain). Containment is structural: the head is clamped to the safe radius. |
| **Climb** | Locomotion | exists | Walk | DesertMonitor, KitMonitor, ShaggyMonitor, TreeSpider | Per-joint up field, real-geometry contact via ISurfaceProbe, pitch limiter (CreatureClimb, ClimbSpec). Terrain walls >= 50 deg over >= 1/2 body length (R30/R31). 0 = default for every number. |
| **Fly** | Locomotion | exists | Wing parts | Apex, Hexadrake, Pterodactyl, SkyDragon, Vesper, WingedSerpent | Ground -> Launch -> Air <-> Glide -> Flare -> Touchdown -> Perched (CreatureFlight, FlightModel, AirChain). Froude-scaled. twist: the twisting Chinese-dragon air chain (AirTwist). |
| **QuadLaunch** | Locomotion | exists | Fly | Apex, Pterodactyl, Vesper | A quadrupedal launch: a crouch on the four limbs (the body lowered by crouchDepth over crouchSeconds), then a vault at vaultSpeed off the folded wing arms; the wings open at the top (CreatureFlight.Launch). Without it a flyer hops off at the shipped 2.4 m/s. |
| **Slither** | Locomotion | exists | Walk | Crocodile, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, Hexadrake, KitMonitor, KomodoDragon, Scolopendra, ShaggyMonitor, SkyDragon, TestSnake, WingedSerpent | Lateral head wave the drag chain carries back (undulation in the walk). Long wave: lambda = speed / Hz >= 0.7 body length (R27). |
| **Soar** | Locomotion | exists | a spine chain body (RayBody); flapping parts (RayWing) read its stroke | SkyManta | Always-airborne flight without legs or legacy wings (manta rays, gliding fish): flap bouts alternating with long glides, coordinated banked turns, an altitude band over the ground and under the roof, barrel rolls and loops; the body lies on the head's 3D path and heaves opposite the stroke. Drives flapping parts (RayWing) through IFlapDriver; sizes its own aviary (IAirborne). |
| **Walk** | Locomotion | exists | SpineBody | Apex, Crocodile, DaddyLongLegs, Delver, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, Pterodactyl, RockCrab, Scolopendra, ShaggyMonitor, SkyDragon, StriderMatriarch, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeSpider, Vesper, WingedSerpent | Head steering in the tangent plane, chain, grounding, gait groups, girdle sway (CreatureSim.SubStep, CreatureLegs). The default locomotion of a spine body. |
| **Chitin** | Look | exists |  | DaddyLongLegs, DesertBeetle, GiantCentipede, GolfballStrider, RockCrab, Scolopendra, StriderMatriarch, Tarantula, TentacleCrab, TreeSpider | Knee/segment bands (part 9) and a velvet sheen lobe: arthropod legs. |
| **Chromatophore** | Look | exists | SoftBody | Swinger, TreeOctopus | Dynamic colour cells, flash and passing-cloud phase (octopus shader). Makes the look dynamic. |
| **Cloak** | Look | exists | SoftBody (spine bodies: Phase 4) | Swinger, TreeOctopus | Dissolve in Forward only: the body vanishes, shadow + SSAO stay (the user's favourite bug). Per-instance, brain-driven, with hysteresis. Octopus today. |
| **Fur** | Look | exists | SpineBody | ShaggyMonitor | Hair in regions: an Undercoat of shells (short dense fur) and Strands (long fur, manes, tufts) as GPU blades skinned with the body's bone matrices and simulated as springs that trail the motion and droop (Scripts/Creatures/Fur, Resources/CreatureFur). Off = no extra draw. |
| **Layers** | Look | exists |  | Apex, Crocodile, Delver, GiantCentipede, GolfballStrider, Hexadrake, KomodoDragon, Pterodactyl, RockCrab, Scolopendra, SkyManta, StriderMatriarch, TentacleCrab | Restyles part ids (material layers) for this species: shading model (Skin, Chitin, Membrane, Eye, Claw), tint, smoothness, and which layers (pattern, scale grain, sheen, shimmer) each has. Rows not listed keep the registry's (Kit/PartLayers.cs). A new part needs a row, never a shader edit. |
| **Membrane** | Look | exists |  | Apex, FinnedSandEel, Hexadrake, Pterodactyl, SkyDragon, WingedSerpent | Translucent veined membrane (part 8, the Membrane shading model, CreatureMembrane.hlsl): wings, FinStrip fins, antennae - any part whose layer row is Membrane. |
| **Shimmer** | Look | exists |  | SkyDragon | Iridescent scale shimmer: thin-film colour bands (colour -> colour2 with the view angle) travelling head to tail along the body's back and flanks, a faint glow in each band (Creature.shader, part 0). The SkyDragon's signature; cheap (one pow, one cos per body pixel). |
| **SkeletonOnly** | Look | exists |  | Vesper | Draw nothing: the species is simulated and F3 shows its skeleton, but the renderer builds no mesh and submits no draw (CreatureSpecies.skeletonOnly). For a species built skeleton-first, before its skin. |
| **Skin** | Look | exists |  | Apex, Crocodile, DaddyLongLegs, Delver, DesertBeetle, DesertMonitor, DesertSnake, FinnedSandEel, GiantCentipede, GolfballStrider, Hexadrake, KitMonitor, KomodoDragon, Pterodactyl, RockCrab, Scolopendra, ShaggyMonitor, SkyDragon, SkyManta, StriderMatriarch, Swinger, Tarantula, TentacleCrab, TestHexapod, TestSnake, TreeOctopus, TreeSpider, WingedSerpent | Palette dorsal/flank/belly + procedural pattern by rest arc length (CreaturePattern.hlsl); per-individual size/hue/value jitter. Layers of parts 0-2. |

## Species (examples to copy)

| species | format | plan | parts | modules | bones |
|---|---|---|---|---|---|
| Apex | kit | Spine | FlowBody, 2x Leg2Bone, 2x DrakeLeg, 2x FleshWing, Eyes, CrestBeakHead, Dewlap, 2x PitRow, TailFlourish | Walk (Diagonal), Fly, QuadLaunch, Wander, FlightExcursion, Skin, Membrane, Layers | 115 |
| Crocodile | kit | Spine | SpineBody, 4x SprawlLeg, Eyes, Jaw, 2x Sculpt, 21x ScuteRow | Walk (Diagonal), Slither, Wander, Skin, Layers | 51 |
| DaddyLongLegs | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes | Walk (Tetrapod), Wander, Skin, Chitin | 71 |
| Delver | kit | Spine | SpineBody, 4x DigLeg, Eyes, BandShell, SerratedRings, Maw, Shag, Burrow | Walk (Diagonal), Wander, Skin, Tunnel, Forage, Bask, Follow, Layers | 67 |
| DesertBeetle | legacy | Spine | SpineBody, 6x ArthropodLeg, Eyes | Walk (Tripod), Wander, Skin, Chitin | 35 |
| DesertMonitor | legacy | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| DesertSnake | legacy | Spine | SpineBody, Eyes | Walk, Slither, Wander, Skin | 34 |
| FinnedSandEel | kit | Spine | SpineBody, Eyes, 2x FinStrip, 2x Flapper | Walk (Diagonal), Slither, Wander, Flee, Skin, Membrane | 54 |
| GiantCentipede | kit | Spine | SpineBody, Eyes, RigidLoft, RigidCone, 4x Antenna, 42x ArthropodLeg | Walk (Wave), Slither, Wander, Skin, Chitin, Layers | 252 |
| GolfballStrider | kit | Spine | SpineBody, Eyes, 10x BendyLeg, DimpledOrb, SpiderFace, Stumble | Walk (Tetrapod), Wander, Skin, Chitin, Layers | 63 |
| Hexadrake | kit | Spine | SpineBody, 6x Leg2Bone, 2x Wing, Eyes, 5x RigidLoft, 3x RigidCone, 2x FinStrip | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane, Layers | 78 |
| KitMonitor | kit | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin | 40 |
| KomodoDragon | kit | Spine | SpineBody, 4x SprawlLeg, Eyes, 2x Sculpt, Tongue, 3x SkinFolds, 3x Swell | Walk (Diagonal), Slither, Wander, Skin, Layers | 53 |
| Pterodactyl | kit | Spine | SpineBody, 4x Leg2Bone, 2x PteroWing, Eyes, LongNeck, BeakHead | Walk (Diagonal), Fly, QuadLaunch, Wander, FlightExcursion, SkyHunt, Skin, Membrane, Layers | 79 |
| RockCrab | kit | Spine | SpineBody, RigidLoft, 10x ArthropodLeg, 2x Chela, 2x EyeStalk, Eyes, 6x SpineField, 8x LimbSpines, Tuck | Wander, Skin, Chitin, Layers, Walk (Tetrapod) | 65 |
| Scolopendra | kit | Spine | SpineBody, Eyes, RigidLoft, 2x Antenna, 40x ArthropodLeg, 2x ArmourPlates, 4x Pincer, ThreatDisplay | Walk (Wave), Slither, Wander, Skin, Chitin, Layers | 245 |
| ShaggyMonitor | kit | Spine | SpineBody, 4x Leg2Bone, Eyes | Walk (Diagonal), Slither, Climb, Wander, ClimbExcursion, Skin, Fur | 40 |
| SkyDragon | kit | Spine | SpineBody, 4x Leg2Bone, 2x LegFin, 2x Wing, Eyes, 4x RigidCone, 5x FinStrip, 4x Antenna, Swell, 7x RigidLoft | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane, Shimmer | 148 |
| SkyManta | kit | Spine | RayBody, Eyes, 2x RayWing, 2x CephalicLobe, WhipTail | Soar, School, Skin, Layers | 112 |
| StriderMatriarch | kit | Spine | SpineBody, Eyes, 8x BendyLeg, DimpledOrb, SpiderFace, Stumble, 8x Tendril | Walk (Tetrapod), Wander, Skin, Chitin, Layers | 151 |
| Swinger | legacy | Soft | 2x Tentacle, SoftBody, Eyes, RigidCone | Ambush, Skin, Cloak, Chromatophore | 97 |
| Tarantula | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes, RigidCone | Walk (Tetrapod), Wander, Skin, Chitin | 75 |
| TentacleCrab | kit | Spine | SpineBody, RigidLoft, 8x ArthropodLeg, 2x Chela, 2x EyeStalk, Eyes, 2x RigidCone, 4x Tendril, 6x SpineField, 8x LimbSpines | Wander, Skin, Chitin, Layers, Walk (Tetrapod) | 93 |
| TestHexapod | legacy | Spine | SpineBody, 6x Leg2Bone, Eyes | Walk (Tripod), Wander, Skin | 28 |
| TestSnake | legacy | Spine | SpineBody, Eyes | Walk, Slither, Wander, Skin | 42 |
| TreeOctopus | legacy | Soft | 8x Tentacle, SoftBody, Eyes, RigidCone | Ambush, Skin, Cloak, Chromatophore | 201 |
| TreeSpider | legacy | Spine | SpineBody, 10x ArthropodLeg, Eyes, RigidCone | Walk (Tetrapod), Climb, Wander, ClimbExcursion, Skin, Chitin | 75 |
| Vesper | kit | Spine | FlowBody, 2x Leg2Bone, 2x DrakeLeg, 2x DigitWing, Eyes | Walk (Diagonal), Fly, QuadLaunch, Wander, FlightExcursion, SkeletonOnly | 108 |
| WingedSerpent | legacy | Spine | SpineBody, 4x Leg2Bone, 2x Wing, Eyes | Walk (Diagonal), Slither, Fly, Wander, FlightExcursion, Skin, Membrane | 73 |

## Parameters

### FlowBody

One chain from the head to the tail tip (the sim runs it like a SpineBody's) drawn as one flowing surface with a shaped section: superellipse from oval to tall ribbon (blade), a sharp dorsal hump, a keel, flank masses. The neck and the tail are the same chain as the trunk; a head part sits on joint 0. System: CreatureSim chain (ISpineChainBody) + FlowBody.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `spacing` | float | Metres | x s | joint spacing (fine in the neck for a smooth S) |  | fixture 0.1 |
| `headCapLength` | float | Metres | x s | the surface closes with a small pole this far ahead of joint 0 (inside the head part) |  |  |
| `tailTipLength` | float | Metres | x s | fine tail tip behind the last joint |  |  |
| `jointCount` | int | Count | - | joints, head to tail | 3..128 | fixture 24 |
| `profile` | FlowKey[] | Struct | - | profile keys (>= 2, t = 0 and t = 1 included, increasing t): monotone cubic to every joint and ring |  |  |
| `profile[].t` | float | Fraction | - | place along the spine, 0 = joint 0 (head) .. 1 = last joint | 0..1 |  |
| `profile[].halfWidth` | float | Metres | x s | section half width |  |  |
| `profile[].halfHeight` | float | Metres | x s | section half height (an oval is at most 2x as tall as wide; blade stretches it to this) |  |  |
| `profile[].clearance` | float | Metres | x s | joint centre above the ground |  |  |
| `profile[].bendLimitDeg` | float | Degrees | - | cone half-angle vs the previous segment |  |  |
| `profile[].hump` | float | Metres | x s | dorsal hump: a sharp ridge rising this far above the section's top |  |  |
| `profile[].keel` | float | Metres | x s | keel: the sternum jutting this far below the section's bottom |  |  |
| `profile[].flank` | float | Metres | x s | flank masses: this much extra width at the sides |  |  |
| `profile[].blade` | float | Fraction | - | 0 = the oval .. 1 = the full ribbon (superellipse bladeExponent, the full halfHeight) | 0..1 |  |
| `profile[].keelForward` | float | Metres | x s | keel forward: the keel's lowest point moved this far toward the head, level in the bind (the sternum jutting forward and down; the keel term's share of it, 0 at the flanks); 0 = straight down | 0.. | default 0 |
| `rigidHeadJoints` | int | Count | - | rigid head: the first N joints move as one rigid body; -1 = auto from the profile (FlowBody.AutoHead: the joints before the neck narrows, if it narrows before the body widens), 0 = off | -1..16 | default -1 (auto) |
| `humpSharpness` | float | Fraction | - | hump exponent on max(0, sin phi): higher = a sharper ridge | 1..64 | default 8 |
| `keelSharpness` | float | Fraction | - | keel exponent on max(0, -sin phi): higher = a narrower keel | 1..64 | default 6 |
| `bladeExponent` | float | Fraction | - | superellipse exponent at blade 1 (2 = ellipse; 4 = a rounded ribbon) | 2..12 | default 4 |
| `ringSegments` | int | Count | - | mesh: vertices around each ring | 8..64 | default 24 |
| `ringsPerSegment` | int | Count | - | mesh: rings per spine segment | 1..8 | default 3 |
| `tailColourFrom` | float | Fraction | - | body fraction (0 front tip .. 1 tail tip) from which the surface is tailKind (default `tailfan`); >= 1 = off | 0..1 | default 1 (off) |
| `dorsalStripeTo` | float | Fraction | - | body fraction up to which a dorsalStripeKind band (default `dorsalstripe`) runs along the top from the head; 0 = off | 0..1 | default 0 (off) |
| `dorsalStripeWidthDeg` | float | Degrees | - | dorsal stripe width around the section (centred on the top) | 0..180 | default 30 |
| `tailKind` | string | Text | - | the material kind of the tail behind tailColourFrom: tailfan (a kit slot of its own, styled by a species Layers row naming it), or a kind the species already draws (a FleshWing's wingfan: the tail in the wing's colour, one slot for both) |  | default tailfan |
| `dorsalStripeKind` | string | Text | - | the material kind of the dorsal stripe: dorsalstripe (a kit slot of its own), or a kind the species already draws (one slot for both) |  | default dorsalstripe |
| `ovalMaxAspect` | float | Fraction | - | an oval (blade 0) is at most this many times as tall as wide; blade 1 stretches it to the key's full halfHeight | 1..8 | default 2 |
| `restAtStance` | bool | Flag | - | bind the body as it stands: each rest segment spacing long between the joints' clearances (a raised neck bound as steep as it walks); off = joints one spacing apart along the ground (SpineBody's rest) |  | default on |

### RayBody

The thick flat body of a ray: a short stiff spine chain (the sim runs it like a SpineBody's) under a broad lens-shaped trunk - domed back, flatter belly, blunt rear at the tail root - whose edges run on into RayWing pectoral fins without a seam (one shared surface, RayShape). System: CreatureSim chain (ISpineChainBody) + RayBody.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `jointCount` | int | Count | - | spine joints head to tail root | 3..24 | manta 8 |
| `spacing` | float | Metres | x s | joint spacing |  | manta 0.14 |
| `headCapLength` | float | Metres | x s | disc front ahead of joint 0 (the head and mouth) |  |  |
| `tailTipLength` | float | Metres | x s | disc rear behind the last joint (the tail root) |  |  |
| `halfWidth` | float | Metres | x s | half width of the body; the wings start here |  | manta 0.24 |
| `thickness` | float | Metres | x s | full thickness at the centre |  | manta 0.2 |
| `edgeThickness` | float | Metres | x s | full thickness at the body's edge (the wing root) |  |  |
| `trailingThickness` | float | Metres | x s | minimum thickness of the trailing edge (never a knife edge) |  |  |
| `headRound` | float | Metres | x s | the front edge recedes this much at the body's edge (a rounded head) |  |  |
| `rearInset` | float | Metres | x s | the rear edge comes forward this much at the body's edge |  |  |
| `topShare` | float | Fraction | - | share of the thickness above the chord line (domed back, flatter belly) | 0.3..0.8 |  |
| `fullness` | float | Fraction | - | section fullness at the centre (exponent on the thickness form: < 1 fuller) | 0.3..1 |  |
| `bluntRear` | float | Fraction | - | blunt rear at the centre: the section ends this far before its fine trailing edge (the tail root) | 0..0.4 |  |
| `bendLimitDeg` | float | Degrees | - | bend limit per joint (a ray's trunk is stiff) |  | manta 5 |
| `columns` | int | Count | - | mesh: columns across the body | 4..24 | default 8 |
| `dorsalFinHeight` | float | Metres | x s | dorsal fin height (0 = none) |  | manta 0.07 |
| `dorsalFinLength` | float | Metres | x s | dorsal fin base length |  |  |
| `dorsalFinAt` | float | Fraction | - | dorsal fin place along the spine: 0 head .. 1 last joint | 0..1 |  |
| `dorsalFinThickness` | float | Metres | x s | dorsal fin thickness at its base |  |  |

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
| `segments[].flatten` | float | Fraction | - | cross-section flattened in the leg's plane: height = (1 - flatten) x width (0 = round): a crab's broad flat leg segments | 0..0.8 | stone crab merus 0.35 |
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
| `femurLiftDeg` | float | Degrees | - | upward turn at the first joint (a low coxa that clears a big body, then a steep femur up to a high knee); added to the arch's turns | 0..100 |  |
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
| `hipStiffness` | float | Fraction | - | knees first: how stiff the hip's elevation is against the knees (0 = off, the IK pitches the whole leg; 4..12 = the knees and distal joints take the motion, a harvestman's stride) | 0..50 |  |
| `swingHipDeg` | float | Degrees | - | mid-swing: the hip lifts the leg by this much (knees first only) | 0..60 |  |
| `swingKneeDeg` | float | Degrees | - | mid-swing: the arch folds by this much more, spread over the knee joints (knees first only) | 0..120 |  |
| `bodyMargin` | float | Metres | x s | kept this far outside any body obstacle (a DimpledOrb), plus the leg's radius; 0 = no collision with the body | 0..1 |  |

### DigLeg

Heavy digging limb: walks as a Leg2Bone (TwoBoneIK, pole, limits, ankle), drawn with a buried shoulder mass and deltoid hump, a short thick upper limb, a ball joint, a forearm swelling to the wrist, a broad spade hand turned out and big flat scimitar claws (middle longest) whose tips meet the ground. `digs` lets a Burrow part pose it in a scoop stroke. System: CreatureLegs (gait groups, throttle, sway) + DigLeg.EmitMesh (LimbLoft).

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
| `upperLength` | float | Metres | x s | upper bone length | 0.005..2 | delver fore 0.12 |
| `lowerLength` | float | Metres | x s | lower bone length | 0.005..2 | delver fore 0.115 |
| `upperRadius` | float | Metres | x s | upper bone radius |  |  |
| `lowerRadius` | float | Metres | x s | lower bone radius |  |  |
| `pole` | Vector3 | Direction | - | knee direction hint in the attach frame (x mirrored): front z < 0 (elbow back), rear z > 0 (knee forward) |  |  |
| `ankleHeight` | float | Metres | x s | wrist / ankle height above the foot's contact point, 0 = 1.2 x lowerRadius, < 0 = none |  |  |
| `shoulderMass` | float | Fraction | - | shoulder / hip mass radius, x upperRadius | 1..3 | default 1.6 |
| `rootDepth` | float | Fraction | - | how deep the mass reaches into the body, x its radius | 0.3..2 | default 1.2 |
| `forearmSwell` | float | Fraction | - | forearm swelling toward the wrist, x lowerRadius (0 = a plain taper) | 0..1 | default 0.3 |
| `handWidth` | float | Fraction | - | hand half-width at the knuckles, x lowerRadius | 0.5..3 | default 1.5 |
| `handThick` | float | Fraction | - | hand half-thickness, x lowerRadius | 0.2..1.5 | default 0.55 |
| `handYawDeg` | float | Degrees | - | the hand turned outward from the heading | -30..60 | default 15 |
| `clawLength` | float | Metres | x s | the longest claw's length (0 = 0.9 x footLength) |  | delver fore 0.11 |
| `clawCurveDeg` | float | Degrees | - | claw curl over its length | 10..140 | default 70 |
| `clawWidth` | float | Fraction | - | claw width / height at the base (flat side to side) | 0.2..1 | default 0.55 |
| `clawSpreadDeg` | float | Degrees | - | fan of the claws from the inner to the outer | 0..120 | default 35 |
| `digs` | bool | Flag | - | a digging limb: a Burrow part poses it in its scoop stroke |  |  |
| `around` | int | Count | - | mesh: vertices round the limb, 0 = 16 | 0..32 |  |

### DrakeLeg

Slim dragon hind leg: walks as a Leg2Bone (TwoBoneIK, pole, limits, a raised hock as the ankle), drawn with a muscular haunch blended into the trunk (into a FlowBody's drawn section), a lean thigh, a ball knee, a slim shin, a knobbly raised hock, a long metatarsus, knuckled toes on the ground with dark curved talons touching down at the foot point, an optional hallux. System: CreatureLegs (gait groups, throttle, sway) + DrakeLeg.EmitMesh (LimbLoft).

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
| `upperLength` | float | Metres | x s | thigh (upper bone) length | 0.005..2 | apex 0.24 |
| `lowerLength` | float | Metres | x s | shin (lower bone) length, to the hock | 0.005..2 | apex 0.26 |
| `upperRadius` | float | Metres | x s | thigh radius (the haunch is haunch x this) |  |  |
| `lowerRadius` | float | Metres | x s | shin radius (slim: the hock, metatarsus and toes follow it) |  |  |
| `pole` | Vector3 | Direction | - | knee direction hint in the attach frame (x mirrored); a hind leg's knee points forward (z > 0) |  |  |
| `ankleHeight` | float | Metres | x s | the hock's height above the toes' contact point (the metatarsus' drop; the gait's ankle height), 0 = 0.3 x (upperLength + lowerLength) | 0.. |  |
| `toes` | int | Count | - | forward toes | 2..4 | default 3 |
| `hallux` | bool | Flag | - | a hallux: a short toe at the back of the metatarsus, off the ground |  |  |
| `talonLength` | float | Metres | x s | talon length (the middle toe's), 0 = 0.3 x footLength |  |  |
| `talonCurveDeg` | float | Degrees | - | talon curl over its length | 20..160 | default 110 |
| `haunch` | float | Fraction | - | haunch radius, x upperRadius | 1..3 | default 1.5 |

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

### DigitWing

A wing that is its fingers: on an arm limb (a Leg2Bone it names), flown, folded and walked on like a Wing (CreatureFlight + CreatureWings), with one jointed 3D chain per finger rooted at the wrist and aimed by the flight's finger directions - the fingers flex and lag with the beat and billow gliding in the air, droop under gravity (above the ground) when folded on the ground, bend-limited - and a small forward thumb claw curling down. Skeleton only for now (no mesh: needs SkeletonOnly until the skin comes in Vesper step 3); joint radii authored, drawn by F3. System: CreatureFlight + CreatureWings (beat, fold, wing-walk) + DigitWingRuntime (finger chains, thumb; stage Late).

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
| `spec` | WingSpec | Spec | - | the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm` - fields: leg, restTip, comfortUp, comfortDown, strokePlaneDeg, pronationDeg, overshootUp, upstrokeDuration, upstrokeFold, foldReach, beatGroup, fingerLengths, fingerSpreadDeg, fingerFoldDeg, fingerMinGapDeg, fingerFoldGapDeg, groundLiftDeg, groundTallStance, flankPatches, groundWristLift, thumbPlant, fanRearRollsWithFan, groundFlankRise, fingerRadius, thumbLength, membraneRearJoint, membraneFrontJoint, trailingRib, membraneInset, area, scallop, scallopByChord, membraneBillow, membraneDetail, fingerKnuckles, wristKnuckle, armMuscle, forearmTwin, thumbRadius |  |  |
| `fingerSegments` | int | Count | - | segments (bones) per finger chain | 1..8 | default 4 |
| `fingerRadius` | float[] | Metres | x s | each finger's joint radius at its root (one per finger, leading first; the skin later) |  | vesper 0.018 0.016 0.014 0.012 |
| `fingerTaper` | float | Fraction | - | the finger tip's radius as a fraction of its root's (linear along the chain) | 0.05..1 | default 0.4 |
| `flexDeg` | float | Degrees | - | air: the most the beat bends a joint over the segment before it (0 = stiff fingers) | 0..45 | default 10 |
| `flexHz` | float | Hz | / sqrt(s) | air flex and ground droop: natural frequency of a segment following the one before (the leading finger's; a shorter finger is stiffer) |  | vesper 2 |
| `flexDamping` | float | Fraction | - | air flex and ground droop: damping ratio (< 0.7: the tips overshoot - a whip) | 0.05..2 | default 0.5 |
| `droopDeg` | float | Degrees | - | ground: the most gravity droops a joint toward world down (a segment lying level; less as it hangs) | 0..45 | default 8 |
| `maxBendDeg` | float | Degrees | - | the most any joint bends over the segment before it (air and ground) | 1..90 | default 25 |
| `thumbLength` | float | Metres | x s | the thumb claw's length along its curve |  | vesper 0.08 |
| `thumbCurlDeg` | float | Degrees | - | the thumb claw's curl down from its root to its tip | 0..120 | default 40 |
| `thumbSegments` | int | Count | - | segments (bones) of the thumb claw | 1..4 | default 2 |

### FleshWing

A soft, thick, folding wing on an arm limb (a Leg2Bone it names), flown, folded and walked on like a Wing (CreatureFlight + CreatureWings): two skins offset by a thickness law (thick root and arm, thin tip and trailing edge) joined by a rounded rim, the arm buried in the leading edge's flesh (one soft loft bending at the elbow, a leaf running on into the sheet: folded, the sheet rolled round the forearm), a blunt soft pad at the wrist that it walks on, lengthwise stripes along the rays; span chains along the fingers and a chord chain across the rays (the outer wing lags and whips, the trailing edge feathers and billows). System: CreatureFlight + CreatureWings (beat, fold, wing-walk) + FleshWingRuntime (span and chord chains; stage Late) + FleshWingMesh.

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
| `spec` | WingSpec | Spec | - | the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm` - fields: leg, restTip, comfortUp, comfortDown, strokePlaneDeg, pronationDeg, overshootUp, upstrokeDuration, upstrokeFold, foldReach, beatGroup, fingerLengths, fingerSpreadDeg, fingerFoldDeg, fingerMinGapDeg, fingerFoldGapDeg, groundLiftDeg, groundTallStance, flankPatches, groundWristLift, thumbPlant, fanRearRollsWithFan, groundFlankRise, fingerRadius, thumbLength, membraneRearJoint, membraneFrontJoint, trailingRib, membraneInset, area, scallop, scallopByChord, membraneBillow, membraneDetail, fingerKnuckles, wristKnuckle, armMuscle, forearmTwin, thumbRadius |  |  |
| `rootThickness` | float | Metres | x s | thickness at the root (the body and the arm), dorsal to ventral skin |  | apex 0.07 |
| `tipThickness` | float | Metres | x s | thickness toward the tip (the law never goes below half of it) |  | apex 0.012 |
| `edgeRound` | float | Fraction | - | the rim's roundness: the free edge rolls into a bead at least this x tipThickness in radius | 0.4..2 |  |
| `armBulk` | float | Metres | x s | the fleshy arm's radius at the shoulder (it tapers to the wrist) |  | apex 0.06 |
| `armChord` | float | Fraction | - | the leading edge's flesh round the forearm runs on into the sheet as a leaf: its section reaches this x its radius from the bone toward the sail (1 = a round tube; folded, the blade's lower half is this broad down to the club) | 1..4 | apex 3 |
| `armRollDeg` | float | Degrees | - | the leaf's rest roll about the forearm, from the elbow's hinge axis toward the sail (the elbow and the leaf's upper part keep it; down the forearm the leaf turns on to face the sail, at most a radian either way): between the spread wing's sail side and the folded blade's | -180..180 | apex -55 |
| `leadFlesh` | float | Fraction | - | the leading edge's flesh, part of the sail's thickness: the arm's flesh runs on along the leading finger, this x armBulk extra half thickness at the root's leading edge, tapering with the span (0 = the plain thickness law) | 0..1.5 |  |
| `rootSwell` | float | Metres | x s | extra thickness swelling the root next to the body (the wing muscles), fading out by a quarter of the span |  | apex 0.03 |
| `stripes` | int | Count | - | dark stripes across the sail, running along the rays (0 = none) | 0..24 |  |
| `stripeDepth` | float | Metres | x s | the stripes' shallow ridges: each stripe stands this much proud of the skin |  | apex 0.003 |
| `padRadius` | float | Metres | x s | the wrist pad's radius: the blunt soft club the folded wing stands on |  | apex 0.06 |
| `clubLength` | float | Metres | x s | the club's length along the stance's up from the walked contact (the sail rolled round the arm's end; at least 2.2 x padRadius); the forearm swells into it over as much |  | apex 0.16 |
| `hookLength` | float | Metres | x s | a hook curling forward and down off the club's front (the hand's claw showing through; 0 = none) |  | apex 0.05 |
| `innerColour` | float | Fraction | - | the inner part of the sail in the body's dorsal colour, as a fraction of the rays' length from the wrist (the rest is wingfan with stripes) | 0..1 |  |
| `spanSegments` | int | Count | - | segments per finger (span chain links); >= 2 to whip | 1..4 |  |
| `bindFold` | float | Fraction | - | the bind pose's fold (0 = the spread glide pose): the mesh is built part-folded so neither the spread nor the ground fold asks two-ray skinning for too large a ratio | 0..0.8 |  |
| `spanHz` | float | Hz | / sqrt(s) | span chain: natural frequency of a finger segment following the one before (x 1/sqrt size) |  | apex 1.4 |
| `spanDamping` | float | Fraction | - | span chain: damping ratio (< 0.7: the tip overshoots - the whip) | 0.1..2 |  |
| `spanMaxDeg` | float | Degrees | - | span chain: the most a segment bends over the one before, either way | 1..30 |  |
| `chordHz` | float | Hz | / sqrt(s) | chord chain: natural frequency of a ray following the ray ahead |  | apex 1.8 |
| `chordDamping` | float | Fraction | - | chord chain: damping ratio | 0.1..2 |  |
| `featherDeg` | float | Degrees | - | chord chain: the feathering the beat asks of the first ray behind the leading finger at the fastest stroke (+ trailing edge up on the downstroke) | 0..30 |  |
| `chordMaxDeg` | float | Degrees | - | chord chain: the most a ray turns over the ray ahead | 1..25 |  |
| `billowDeg` | float | Degrees | - | gliding, the sail billows: each ray lifts this much over the ray ahead (and each finger segment over the one before) | 0..10 |  |
| `columns` | int | Count | - | mesh: columns per sail zone (across the rays) | 2..24 |  |
| `rows` | int | Count | - | mesh: rows along the rays (rounded up to a multiple of spanSegments) | 4..48 |  |

### PteroWing

A pterosaur's wing on an arm limb (a Leg2Bone it names), flown like a Wing (CreatureFlight + CreatureWings): crested humerus, two-bone forearm, long metacarpal to the knuckle, three small clawed walking digits, a pteroid with the propatagium in front of the arm, a four-phalanx wing finger that sweeps, lags the stroke and curls when folded, and a narrow brachiopatagium with a smooth trailing edge to the flank. System: CreatureFlight + CreatureWings (the beat, the arm, the rays) + PteroWingRuntime (phalanges, pteroid, propatagium; stage Late) + PteroWingMesh.

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
| `spec` | WingSpec | Spec | - | the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm` - fields: leg, restTip, comfortUp, comfortDown, strokePlaneDeg, pronationDeg, overshootUp, upstrokeDuration, upstrokeFold, foldReach, beatGroup, fingerLengths, fingerSpreadDeg, fingerFoldDeg, fingerMinGapDeg, fingerFoldGapDeg, groundLiftDeg, groundTallStance, flankPatches, groundWristLift, thumbPlant, fanRearRollsWithFan, groundFlankRise, fingerRadius, thumbLength, membraneRearJoint, membraneFrontJoint, trailingRib, membraneInset, area, scallop, scallopByChord, membraneBillow, membraneDetail, fingerKnuckles, wristKnuckle, armMuscle, forearmTwin, thumbRadius |  |  |
| `phalanges` | float[] | Fraction | - | the wing finger (fingerLengths[0]) split into its four phalanges, root to tip (sum 1) |  | azhdarchid 0.32 0.29 0.24 0.15 |
| `fingerRoot` | float | Metres | x s | wing finger radius at its root (it tapers to a fine tip) |  | pterodactyl 0.017 |
| `sweepDeg` | float | Degrees | - | static sweep back (toward the trailing edge) at each of the three interphalangeal joints | -10..15 |  |
| `flexDeg` | float | Degrees | - | the tip's lag against the stroke: each interphalangeal joint bends up to this much (up on the downstroke) | 0..20 |  |
| `flexSeconds` | float | Seconds | x sqrt(s) | time constant of that lag (and of the curl's ease) | 0.01..1 |  |
| `foldCurlDeg` | float | Degrees | - | folded on the ground: each interphalangeal joint curls this much dorsally (the folded blade arches back) | -20..30 |  |
| `carpusAt` | float | Fraction | - | the carpus (the pteroid's root, the forearm's end) along the lower arm bone, 0 elbow .. 1 knuckle | 0.3..0.9 |  |
| `pteroidLength` | float | Metres | x s | pteroid length |  | pterodactyl 0.13 |
| `pteroidDeg` | float | Degrees | - | the pteroid turned from the carpus-shoulder line toward the leading edge, in the arm's plane | 5..80 |  |
| `crest` | float | Fraction | - | the deltopectoral crest's height on the humerus, x the upper arm radius (0 = none) | 0..3 |  |
| `digitLength` | float | Metres | x s | the three walking digits' length (0 = none) |  | pterodactyl 0.07 |
| `trailingCurve` | float | Fraction | - | the trailing edge's concavity between neighbouring rays, a fraction of the zone's reach | 0..0.3 |  |
| `leadingEdge` | float | Metres | x s | the leading edge's thickness: the membrane rolls over the wing finger into a rounded edge this thick (0 = a flat sheet to the finger) |  | pterodactyl 0.012 |
| `camber` | float | Fraction | - | the membrane's camber: it bellies up between the leading edge and the trailing edge by this x the local chord | 0..0.15 |  |
| `membraneColumns` | int | Count | - | mesh: membrane columns per zone (across the rays) | 4..32 |  |
| `membraneRows` | int | Count | - | mesh: membrane rows (out from the knuckle) | 4..48 |  |
| `chain` | bool | Flag | - | the wing as chains (AngleChain, the sky manta's rule): the wing finger's phalanges follow one another root to tip (spanHz, spanDamping: a growing lag, the tip whips at the stroke's turn), and the membrane's chord rays (the hidden fingers and the trailing rib) follow the leading edge one after another (chordHz, chordDamping: the trailing edge lags and feathers, the sheet billows); off: the single-lag flex (flexDeg, flexSeconds) |  |  |
| `spanHz` | float | Hz | / sqrt(s) | chain: natural frequency of a phalanx following the one before (x 1/sqrt size) |  | pterodactyl 1.6 |
| `spanDamping` | float | Fraction | - | chain: the phalanges' damping ratio (< 0.7: the tip overshoots - the whip) | 0.1..2 |  |
| `spanMaxDeg` | float | Degrees | - | chain: the most an interphalangeal joint bends either way | 1..30 |  |
| `chordHz` | float | Hz | / sqrt(s) | chain: natural frequency of a chord ray following the ray ahead |  | pterodactyl 2.2 |
| `chordDamping` | float | Fraction | - | chain: the chord rays' damping ratio | 0.1..2 |  |
| `chordFeatherDeg` | float | Degrees | - | chain: the feathering the leading edge's beat asks of the first chord ray at the fastest stroke (+ trailing edge up on the downstroke) | 0..30 |  |
| `chordMaxDeg` | float | Degrees | - | chain: the most a chord ray turns over the ray ahead about the leading edge | 1..25 |  |
| `billowDeg` | float | Degrees | - | chain: gliding, the membrane billows - each chord ray lifts this much over the one ahead | 0..10 |  |
| `bindFold` | float | Fraction | - | the bind pose's fold (0 = the spread glide pose, as a Wing): the mesh is built part-folded, so the flight's spread opens the membrane's zones and the ground fold closes them each by a ratio two-ray skinning can follow (a pterosaur folds its finger along the arm: from the spread bind its zones close to a seventh and turn inside out) | 0..0.8 |  |

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
| `spec` | WingSpec | Spec | - | the wing (WingSpec: rest tip, comfort band, stroke, fingers, membrane joints); its `leg` is set from `arm` - fields: leg, restTip, comfortUp, comfortDown, strokePlaneDeg, pronationDeg, overshootUp, upstrokeDuration, upstrokeFold, foldReach, beatGroup, fingerLengths, fingerSpreadDeg, fingerFoldDeg, fingerMinGapDeg, fingerFoldGapDeg, groundLiftDeg, groundTallStance, flankPatches, groundWristLift, thumbPlant, fanRearRollsWithFan, groundFlankRise, fingerRadius, thumbLength, membraneRearJoint, membraneFrontJoint, trailingRib, membraneInset, area, scallop, scallopByChord, membraneBillow, membraneDetail, fingerKnuckles, wristKnuckle, armMuscle, forearmTwin, thumbRadius |  |  |

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

### AttachedChain

A secondary chain attached directly onto the mother (spine) chain's joints: at one end (start socket; a free end that lags, swings and droops) or at both ends (start socket + endJoint/endOffset: a strand that hangs between two mother joints, its length the bind's chord x (1 + slack)). Each attached end follows its mother joint exactly; the rest shape (built once per species from the bind) is carried by the mother joints and pulled toward by a spring (stiffnessHz), damped relative to the mother's motion, gravity, exact lengths, a bend cone, optional keep-out from the mother body's drawn surface (collideRadius; a FlowBody's section). One bone per chain joint, frames transported from the mother joint's up; per-joint radii (the skin later), drawn by F3. Skeleton only for now (no mesh). System: AttachedChainRuntime (part runtime, stage Late: its own PBD sub-steps).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `endJoint` | int | Joint | - | the mother joint the far end is attached to; -1 = a free end (one-ended). May be before or after the start joint | -1..127 | default -1 |
| `endOffset` | Vector3 | Metres | x s | the far end's attachment in endJoint's frame (x mirrored by side) |  |  |
| `segments` | int | Count | - | segments along the chain; bones = segments + 1 (one per chain joint) | 2..24 | default 8 |
| `length` | float | Metres | x s | one-ended: the rest length along the chain (> 0). Two-ended: must be 0 - the length is the bind's chord x (1 + slack) | 0.. | default 0; wattle 0.2; pouch 0 |
| `slack` | float | Fraction | - | two-ended: extra length over the bind's distance between the ends, so it hangs (>= 0.1: a spine bending or straightening must never pull it taut) | 0.1..2 | default 0.25 |
| `rootRadius` | float | Metres | x s | joint radius at the start (the skin later) |  | default 0.02 |
| `tipRadius` | float | Metres | x s | joint radius at the end |  | default 0.01 |
| `radii` | float[] | Metres | x s | per-joint radii, start first (segments + 1 values); empty = rootRadius .. tipRadius linearly |  |  |
| `direction` | Vector3 | Direction | - | rest: the direction it leaves the start joint (one-ended) or bulges toward (two-ended), start joint frame, x mirrored by side |  | default (0, -1, 0) |
| `curlDeg` | float | Degrees | - | one-ended rest curl, spread over the interior joints, toward the direction frame's -Y (+Y = the joint's up made square to the direction; a vertical direction: the joint's back) | -180..180 | default 0 |
| `stiffnessHz` | float | Hz | / sqrt(s) | stiffness: natural frequency of the pull toward the rest shape carried by the mother joints (0 = limp: it only hangs) | 0..10 | default 1.5 |
| `damping` | float | Hz | / sqrt(s) | damping rate (1/s) of its velocity relative to the rest shape's own motion | 0..40 | default 3 |
| `gravity` | float | Fraction | - | share of gravity it feels (1 = hangs fully, 0 = weightless) | 0..1 | default 1 |
| `maxBendDeg` | float | Degrees | - | bend cone per interior chain joint (two-ended: raised to the rest arc's bend if that is sharper) | 5..90 | default 35 |
| `collideRadius` | float | Metres | x s | keep the free joints this far outside the mother body's surface (a FlowBody's drawn section, else the joints' halfWidth x halfHeight ellipses; swept between joints); 0 = off. Attach the ends on or just under that surface | 0.. | default 0 |

### Dewlap

A broad pale throat sail or pouch hanging from a FlowBody's neck joints (fromJoint..toJoint, jaw end first, the last may be the chest) on its own little chain: `stations` stations of `links` links, a lower edge from curved to straight (edgeStraightness), a soft sheet thick at the root and bellied (rootThickness, billow) with faint lengthwise creases and a round rim, its rear sweeping up into the chest (rearSweep). It swings behind the neck's motion (each station's acceleration drives two damped AngleChains, gravity pulls toward hanging), the stations coupled so even a heavy flight bob never turns the rim over (coupling, maxTwistDeg); it never rises above the neck. System: DewlapRuntime (part runtime, stage AfterLimbs: two AngleChains per station) + Dewlap.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `fromJoint` | int | Count | - | the body joint the sail's front (jaw) end hangs from | 0..127 | apex fixture 1 |
| `toJoint` | int | Count | - | the body joint its back (chest) end hangs from (> fromJoint) | 1..127 | apex fixture 7 |
| `stations` | int | Count | - | stations along the sail (each a chain of links) | 2..16 | default 5 |
| `links` | int | Count | - | links per station, root to edge (under a heavy flight bob - the Apex's, ~13 % of the body - 3 or fewer: 4 folded the swept rear) | 1..6 | default 3 |
| `depthAt` | float[] | Metres | x s | the sail's depth below the throat at each station (one per station, jaw end first) |  | apex fixture 0.12 0.2 0.24 0.22 0.16 |
| `edgeStraightness` | float | Fraction | - | 0 = the edge follows depthAt (a curved belly) .. 1 = a straight lower edge from the first station's edge to the last's. A straight edge under a strongly curved neck (or a shallow station between deep ones) tilts a station's rest hang toward its lateral plane: within PlaneMarginDeg (20 deg) of it the plane stop holds that station still - the bind warns (Dewlap.RestPlaneProblem) | 0..1 | default 0.8 |
| `rim` | float | Metres | x s | radius of the soft round rim along the lower edge (>= thickness / 2) |  | default 0.012 |
| `thickness` | float | Metres | x s | the sheet's thickness |  | default 0.008 |
| `swingHz` | float | Hz | / sqrt(s) | natural frequency of a link following the one above it (x 1/sqrt size) |  | default 1.6 |
| `swingDamping` | float | Fraction | - | the links' damping ratio (< 0.7: the edge overshoots a little) | 0.05..2 | default 0.35 |
| `maxSwingDeg` | float | Degrees | - | the most the drive leans a station and a link bends over the one above it, either way (0 = a stiff sail) | 0..60 | default 25 |
| `gravityDeg` | float | Degrees | - | gravity: each link bends this much x sin(its angle to the down direction) toward hanging | -90..90 | default 6 |
| `rootThickness` | float | Metres | x s | the sheet's thickness where it leaves the throat (>= thickness): it thins toward the rim. Keep it under ~0.02: a wider root sits inside the body's facets, which its two-bone skin follows only to first order, and the root row leaves the bending throat by more than 2.5 mm |  | default 0.012 |
| `billow` | float | Fraction | - | the pouch's belly: each face bulges outward by this x the local depth halfway down (0 = a flat card), and puffs up to 3 x billow wider while it swings | 0..0.3 | default 0.05 |
| `rearSweep` | float | Fraction | - | the share of the sail's length (from the chest end) over which the edge sweeps up and the sheet thins into the chest skin (0 = a square rear end; under a heavy flight bob keep it >= ~0.5: 0.35 packs the shrinking rows into one span and folded them) | 0..1 | default 0.6 |
| `coupling` | float | Fraction | - | how strongly each station's swing follows its neighbours' (0 = independent stations: under a heavy bob the rim turns over) | 0..1 | default 0.6 |
| `maxTwistDeg` | float | Degrees | - | the most a link's swing, and its bend over the link above, may differ from the same link's at the neighbouring station, fore-aft and sideways (the rows' relative twist) | 1..60 | default 8 |

### LongNeck

A long neck on bones of its own from a spine joint (the chest), held in a posture (raised on the ground, stretched forward in the air) instead of dragged along the path: a stabilised head that glides while the body bobs, glances, calls, turns toward an aim point and strikes. Its last bone carries a head part (BeakHead). System: LongNeckRuntime (part runtime, stage Late: posture, head follower, Hermite neck) + LongNeck.EmitMesh (rings).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `segments` | int | Count | - | bones: neck segments + the head's (the last) | 4..24 | pterodactyl 11 |
| `length` | float | Metres | x s | neck length, root to the head bone |  | pterodactyl 0.9 |
| `rootRadius` | float | Metres | x s | half width at the root (it flares into the chest) |  |  |
| `headRadius` | float | Metres | x s | half width at the head |  |  |
| `depth` | float | Fraction | - | cross-section height / width | 0.6..1.6 |  |
| `baseDeg` | float | Degrees | - | on the ground: the base's elevation above the heading | -30..90 |  |
| `headDeg` | float | Degrees | - | on the ground: the head's elevation (its forward axis) above the heading | -60..60 |  |
| `flightBaseDeg` | float | Degrees | - | in the air: the base's elevation (stretched forward) | -30..60 |  |
| `flightHeadDeg` | float | Degrees | - | in the air: the head's elevation | -60..60 |  |
| `bindBaseDeg` | float | Degrees | - | the bind (mesh) pose's base elevation: between the stance and the flight pose | -30..90 |  |
| `bindHeadDeg` | float | Degrees | - | the bind pose's head elevation | -60..60 |  |
| `reach` | float | Fraction | - | the head point's distance from the root, x the neck's chord for its bend (< 1 leaves the neck slack) | 0.7..1 |  |
| `stabiliseSeconds` | float | Seconds | x sqrt(s) | the head follower's time constant (larger: steadier head, more lag) | 0.02..2 |  |
| `glanceDeg` | float | Degrees | - | idle glances: yaw / pitch amplitude (0 = none) | 0..80 |  |
| `glanceSeconds` | float | Seconds | x sqrt(s) | idle glances: mean interval |  |  |
| `callDeg` | float | Degrees | - | a call (on the ground, standing): the base and head rise by this much | 0..60 |  |
| `callSeconds` | float | Seconds | x sqrt(s) | a call's length (0 = it never calls) |  |  |
| `callInterval` | float | Seconds | x sqrt(s) | mean interval between calls while standing |  |  |
| `aimYawDeg` | float | Degrees | - | the most the head turns toward an aim point off the heading | 0..120 |  |
| `strikeReach` | float | Fraction | - | striking: the head's reach toward the aim point, x the neck length | 0.5..1 |  |
| `headClearance` | float | Metres | x s | the head never comes lower than this over the ground under the chest |  |  |
| `maxBaseDeg` | float | Degrees | - | the base's highest elevation above the chest's line (a call or a glance never leans the neck back past this) | 30..89 |  |
| `basePitchFollow` | float | Fraction | - | in the air: how much of the chest's pitch the neck's base follows (1 = it continues the chest's line; less raises it over a nose-down body) | 0..1 |  |
| `headPitchFollow` | float | Fraction | - | in the air: how much of the chest's pitch the head follows (0 = always level) | 0..1 |  |
| `headLevelDeg` | float | Degrees | - | in the air: the head's elevation stays within this of level (aiming and striking excepted) | 5..90 |  |
| `strikeConeDeg` | float | Degrees | - | in the air, aiming and striking: the neck, the reach and the head's turn stay within this cone round the neck's forward (the head never swings below or behind the neck); on the ground it pecks anywhere within aimYawDeg | 10..180 |  |
| `maxLagDeg` | float | Degrees | - | follow-through: the most the head lags its posture (seen from the neck's root) | 5..180 |  |
| `tipReach` | float | Metres | x s | the head part's reach past the head bone along the head (a beak's tip): a strike lands this point on the aim point |  | pterodactyl 0.6 |
| `ringSides` | int | Count | - | mesh: vertices round each ring | 8..32 |  |
| `ringsPerSegment` | int | Count | - | mesh: rings per neck segment | 1..8 |  |

### TailFlourish

The living tail: a pose drawn over the sim's tail joints (the walk and the flight untouched). Standing, the tail rises in a calm loop above the hips and drifts; in the air (eased in by the flight's air weight) a travelling roll about each bone's own axis, growing to the tip, and a lazy curl travelling down it. Added bend clamped to each joint's bend limit. System: TailFlourishRuntime (part runtime, stage Late; the LAST part: writes over the body's tail bones).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `fromJoint` | int | Joint | - | the tail root: the first joint of the tail (it and the one behind it stay as the sim has them on the ground) |  | fixture 12 |
| `airRollDeg` | float | Degrees | - | air: the tip's roll amplitude about its own long axis (grows linearly from 0 at the root) | 0..180 | default 70 |
| `airCurlDeg` | float | Degrees | - | air: the travelling curl's amplitude per joint (sideways, in the ribbon's rolled frame) | 0..45 | default 9 |
| `airHz` | float | Hz | / sqrt(s) | air: the roll and curl frequency | 0..5 | default 0.35 |
| `waveLength` | float | Fraction | - | the travelling wave's length, in tail lengths (the tip lags the root by 1 / waveLength cycles) | 0.05..10 | default 0.6 |
| `groundLoopDeg` | float | Degrees | - | ground: the upward curl per joint (behind the first two tail joints) building the loop | 0..45 | default 7 |
| `groundDriftDeg` | float | Degrees | - | ground: the slow drift's amplitude per joint (up-down on the loop and sideways) | 0..30 | default 2 |
| `groundHz` | float | Hz | / sqrt(s) | ground: the drift's frequency | 0..5 | default 0.12 |
| `easeSeconds` | float | Seconds | x sqrt(s) | time to blend between the ground loop and the air motion (the flight's air weight at the tail root, eased) | 0..10 | default 0.6 |

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
| `mode` | int | Count | - | behaviour: 0 feels the ground when the body stops; 1 maw: always writhing, probing the ground ahead and lashing | 0..1 |  |
| `probeHz` | float | Hz | / sqrt(s) | maw: how fast a probing tip sweeps side to side | 0..5 | 0.3 |
| `lashEvery` | float | Seconds | x sqrt(s) | maw: mean time between lashes (0 = never) | 0.. | 8 |
| `clearance` | float | Metres | x s | kept this far outside the body's obstacles (a DimpledOrb) and the limbs (BendyLegs), plus its radius; 0 = off | 0.. |  |

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

### WhipTail

A long thin tapering tail that trails the path its root flew, easing there more slowly toward the tip (the tip lags most), sagging a little; exact lengths, bend cone; a fine tapered tube. System: WhipTailRuntime (part runtime, stage AfterLimbs) + WhipTail.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `length` | float | Metres | x s | length |  | manta 1.8 |
| `segments` | int | Count | - | segments (one bone each) | 2..24 | default 14 |
| `radiusRoot` | float | Metres | x s | radius at the root |  |  |
| `radiusTip` | float | Metres | x s | radius at the tip |  |  |
| `followSeconds` | float | Seconds | x sqrt(s) | the tip's lag: time constant of its follow (the root's is 0, growing along the tail) |  | manta 0.35 |
| `sagDeg` | float | Degrees | - | the tail sags by this much per segment under its own weight | 0..10 |  |
| `maxBendDeg` | float | Degrees | - | bend cone between segments | 5..60 |  |
| `sides` | int | Count | - | mesh: sides of the tube | 4..16 | default 8 |

### CephalicLobe

A head fin rolled up into a short horn (manta cephalic lobes): a thick sheet curled about its length, tighter toward the tip, jutting forward from the head. The base sways slowly and leans out of turns; the tip follows the base with lag (damped spring). System: CephalicLobeRuntime (part runtime, stage Late) + CephalicLobe.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `length` | float | Metres | x s | length, base to tip |  | manta 0.22 |
| `width` | float | Metres | x s | unrolled width at the base |  |  |
| `thickness` | float | Metres | x s | sheet thickness at the base |  |  |
| `curlDeg` | float | Degrees | - | curl along its length: the tip turns down (and in, half as much) by this much | 0..180 |  |
| `tipCurlDeg` | float | Degrees | - | how far the flap is rolled up at the tip (0 flat, 360 a round horn) | 0..360 |  |
| `pitchDeg` | float | Degrees | - | points down from the head's forward | -60..80 |  |
| `splayDeg` | float | Degrees | - | splays out to the side | -45..45 |  |
| `swayDeg` | float | Degrees | - | the base's slow sway amplitude | 0..30 |  |
| `swayHz` | float | Hz | / sqrt(s) | the base's sway frequency |  | manta 0.25 |
| `turnLean` | float | Fraction | - | lean out of turns: degrees per deg/s of the body's yaw rate | 0..1 |  |
| `followHz` | float | Hz | / sqrt(s) | the tip follows the base at this natural frequency |  | manta 2.5 |
| `damping` | float | Fraction | - | damping ratio of the tip's follow | 0.1..2 |  |

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

### RayWing

A thick pectoral wing that is a 2D chain: a lattice of stations (span) x chordRows (chord). The root segment is driven by the locomotion mode's stroke (IFlapDriver), every outer segment follows the one before it (a damped spring: the beat travels to a whipping tip), every chord row follows the row ahead (feathering wave to the trailing edge); the body's heave reaches it as inertia. Mesh: thick airfoil sections continuing the RayBody's edge (RayShape), round leading edge, falcate pointed tip. System: RayWingRuntime (part runtime, stage AfterLimbs) + RayWing.EmitMesh.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `span` | float | Metres | x s | span from the body's edge to the tip |  | manta 1.35 |
| `sweepDeg` | float | Degrees | - | leading edge sweep from straight out sideways | 0..60 |  |
| `tipSweep` | float | Metres | x s | extra sweep of the tip (x u^3: a falcate, swept-back tip) |  |  |
| `tipChord` | float | Metres | x s | chord at the tip |  |  |
| `chordExponent` | float | Fraction | - | chord falls as (1 - u)^this (> 1: a concave trailing edge) | 0.5..3 |  |
| `tipThickness` | float | Metres | x s | full thickness at the tip |  |  |
| `thicknessFalloff` | float | Fraction | - | how fast the thick root thins out along the span (1 = the plain cubic) | 1..4 |  |
| `stations` | int | Count | - | span stations (bones along the span) | 3..12 | default 7 |
| `chordRows` | int | Count | - | chord rows (bones along the chord) | 3..8 | default 6 |
| `subdivisions` | int | Count | - | mesh columns per station interval | 1..6 | default 3 |
| `flapDeg` | float | Degrees | - | root stroke amplitude (the driven segment's dihedral, + up) | 0..60 | manta 30 |
| `glideDeg` | float | Degrees | - | root dihedral while gliding (+ up) | -30..30 |  |
| `glideTipDeg` | float | Degrees | - | gliding: extra bend of the outer wing, growing to the tip (+ tips up, - drooped) | -40..40 |  |
| `spanHz` | float | Hz | / sqrt(s) | span chain: natural frequency of a segment following the one before |  | manta 2.4 |
| `spanDamping` | float | Fraction | - | span chain: damping ratio (< 0.7: the tip overshoots - the whip) | 0.1..2 |  |
| `chordHz` | float | Hz | / sqrt(s) | chord chain: natural frequency of a row following the row ahead |  | manta 3 |
| `chordDamping` | float | Fraction | - | chord chain: damping ratio | 0.1..2 |  |
| `featherDeg` | float | Degrees | - | feathering: trailing rows' pitch per m/s of the leading edge's beat speed (scale 1) | 0..40 |  |
| `inertiaDeg` | float | Degrees | - | inertia: outer wing's lag per m/s^2 of the body's vertical acceleration | 0..10 |  |
| `maxBendDeg` | float | Degrees | - | largest bend between neighbouring span segments or chord rows | 5..60 |  |
| `asymmetry` | float | Fraction | - | turns: the outer wing's stroke grows / shrinks by this x the turn's asymmetry | 0..1 |  |

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

### BandShell

A carapace of overlapping transverse bands over the back, one per joint, each rigid to its joint: arched sections standing off the body, shingled front over back (rear edge proud, front edge tucked) so it bends without gaps or clipping, rolled scalloped rear lips, a hump over its middle, and an optional hood - bands ahead of the first, curling down over the head like a shield. System: mesh only (BandShell.EmitMesh, ShellBand).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `toJoint` | int | Joint | - | last joint with a band (the first is at.joint) |  |  |
| `lift` | float | Metres | x s | the shell's stand-off above the body at the top |  | delver 0.06 |
| `sideLift` | float | Metres | x s | stand-off at the flanks |  |  |
| `hump` | float | Metres | x s | extra height over the middle of the run (a hunched back) |  |  |
| `humpAt` | float | Fraction | - | where along the run the hump peaks (0 first band .. 1 last) | 0..1 | default 0.45 |
| `humpWidth` | float | Fraction | - | the hump's half-width, fraction of the run | 0.1..2 | default 0.6 |
| `edgeDrop` | float | Metres | x s | the section's equator below the joint (the shell reaches down the flanks) |  |  |
| `skirtDeg` | float | Degrees | - | how far the lower edge curls under past the equator | -30..60 | default 8 |
| `flare` | float | Metres | x s | the lower edges flare out by this |  |  |
| `thickness` | float | Metres | x s | shell thickness |  | delver 0.008 |
| `shingle` | float | Metres | x s | shingle step: the rear edge stands this proud, the front edge tucks this far in (> thickness / 2 + the bend's sweep) |  |  |
| `overlap` | float | Fraction | - | overlap of neighbouring bands, fraction of the joint spacing | 0..1 | default 0.45 |
| `lobes` | int | Count | - | rounded lobes along each rear edge (0 = straight) | 0..40 |  |
| `lobeDepth` | float | Fraction | - | how far the rear edge draws back between lobes, fraction of the joint spacing | 0..0.6 |  |
| `lip` | float | Metres | x s | rolled lip along the rear edge |  |  |
| `square` | float | Fraction | - | section squareness: 2 = round, up to 4 = boxy shoulders | 2..4 | default 2.2 |
| `hoodBands` | int | Count | - | hood bands ahead of the first band (0 = none) | 0..8 |  |
| `hoodStep` | float | Fraction | - | each hood band's step forward, fraction of the joint spacing | 0.2..1.5 | default 0.75 |
| `hoodCurlDeg` | float | Degrees | - | the front hood band's forward lean about its equator (its top overhangs the head; each band leans its share more) | 0..120 | default 45 |
| `hoodFlare` | float | Metres | x s | each hood band grows by this (wider and taller: a flaring shield) |  |  |
| `hoodDrop` | float | Metres | x s | each hood band's lower edge drops by this |  |  |
| `around` | int | Count | - | mesh: vertices across each band, 0 = 40 | 0..96 |  |
| `layer` | int | Count | - | material layer: 10 armour plate (restyle with Layers) | 0..15 | default 10 |

### BeakHead

A beaked head from head-scale numbers: an elongated skull with eye sockets and an antorbital groove, a long keeled keratin beak, a lower jaw on its own bone that gapes when the neck calls or strikes, and a blade crest in its own layer; SDF solids (SculptField / SculptMesher). Rides a LongNeck's head bone or a spine joint; carries the species' eyes. System: BeakHeadRuntime (part runtime, stage Late: jaw, eye bones) + BeakHead.EmitMesh (SculptMesher pieces).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `follow` | string | PartRef | - | the LongNeck whose head bone it rides (empty: rigid to at.joint, at.offset) |  |  |
| `skullLength` | float | Metres | x s | skull length behind the beak (occiput to the beak's root) |  | pterodactyl 0.16 |
| `skullHeight` | float | Metres | x s | skull half height |  |  |
| `skullWidth` | float | Metres | x s | skull half width |  |  |
| `beakLength` | float | Metres | x s | beak length from its root to the tip |  | pterodactyl 0.46 |
| `beakDepth` | float | Metres | x s | the beak's half height at its root (upper beak) |  |  |
| `beakCompress` | float | Fraction | - | beak cross-section height / width | 0.8..3 |  |
| `beakDroopDeg` | float | Degrees | - | the beak bends down (+) or up (-) toward its tip | -20..30 |  |
| `keel` | float | Fraction | - | the keel (culmen ridge) along the upper beak, x the beak depth (0 = round) | 0..1 |  |
| `gapeDeg` | float | Degrees | - | the widest gape (a strike); a call opens it 0.6 x as far | 0..80 |  |
| `clackInterval` | float | Seconds | x sqrt(s) | mean interval between idle clacks (0 = none) |  |  |
| `crestHeight` | float | Metres | x s | crest height above the skull (0 = none) |  | pterodactyl 0.07 |
| `crestLength` | float | Metres | x s | crest length along the head |  |  |
| `crestSweepDeg` | float | Degrees | - | crest sweep back (0 = straight up) | -30..80 |  |
| `crestThickness` | float | Metres | x s | crest half thickness |  |  |
| `crestAt` | float | Fraction | - | the crest's root along the skull, 0 occiput .. 1 the beak's root | 0..1.5 |  |
| `eyeOffset` | Vector3 | Metres | x s | eye centre in the head frame (x mirrored) |  |  |
| `skinLayer` | int | Count | - | material layer of the skull and rostrum (0 = the body's skin) | 0..15 |  |
| `beakLayer` | int | Count | - | material layer of the beak and jaw (2 = claw keratin) | 0..15 |  |
| `crestLayer` | int | Count | - | material layer of the crest (2 = claw keratin; a species may restyle a spare row for it with Layers: the Pterodactyl's teal crest is row 13) | 0..15 |  |
| `cell` | float | Metres | x s | polygoniser cell (~1/100 of the head's length) |  |  |

### Burrow

A digger's postures and trips underground: scratch and sniff, rest curled or flat; dig in (forelimbs scooping, head first), travel hidden along a real carved tunnel (or ride the enclosure's existing tunnels), burst out jaws first (CreatureSim.Display) and climb out. Shared TunnelNetwork per enclosure; carves through CreatureDigging, inside the enclosure only. System: BurrowRuntime (part runtime, stage Late; the LAST part: moves the other parts' bones) + CreatureSim Halt / Crouch / Display + CreatureDigging / TunnelNetwork.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `depth` | float | Metres | x s | how far the body sinks under the surface (every joint, at the full dive) |  | delver 0.6 |
| `diveLength` | float | Metres | x s | the dive's (and the climb-out's) run along the path: a joint sinks over this much travel past the hole |  |  |
| `digSeconds` | float | Seconds | x sqrt(s) | scooping before the dive into new ground |  |  |
| `slipSeconds` | float | Seconds | x sqrt(s) | the pause before slipping into an existing hole |  |  |
| `lungeSeconds` | float | Seconds | x sqrt(s) | the burst: the head rises out of the ground |  |  |
| `roarSeconds` | float | Seconds | x sqrt(s) | held agape after the burst |  |  |
| `lungeJoints` | int | Count | - | joints the burst lifts (head first, fading) | 1..16 | default 6 |
| `lungeLift` | float | Metres | x s | how high above its walking height the head bursts |  |  |
| `lungePitchDeg` | float | Degrees | - | the head's pitch up at the burst | 0..80 | default 40 |
| `tunnelRadius` | float | Metres | x s | tunnel radius (the carve spheres') |  |  |
| `carveStep` | float | Fraction | - | a carve every this x the radius of the head's travel underground | 0.3..2 | default 0.7 |
| `carvePhysics` | bool | Flag | - | carve into the terrain's physical layer (colliders and creatures feel the holes); off = rendered only |  |  |
| `holeSnap` | float | Metres | x s | a trip starts or ends at an existing hole within this distance |  |  |
| `digCost` | float | Fraction | - | planning: a metre dug costs this many metres walked or ridden (> 1 favours existing tunnels) | 1..20 | default 4 |
| `scoopDeg` | float | Degrees | - | the digging limbs' scoop stroke | 0..90 | default 45 |
| `scoopHz` | float | Hz | / sqrt(s) | scoop strokes a second |  |  |
| `sniffDeg` | float | Degrees | - | the head lowered to sniff and dig | 0..60 | default 22 |
| `restCrouch` | float | Metres | x s | crouch at rest |  |  |
| `restCurlDeg` | float | Degrees | - | the head tucked down under the hood at a curled rest | 0..70 | default 35 |
| `blendSeconds` | float | Seconds | x sqrt(s) | postures blend in and out over this |  |  |
| `minUnderSeconds` | float | Seconds | x sqrt(s) | out of sight at least this long: a trip that reaches its exit sooner tunnels on (turning off the wall) until then |  | delver 8 |
| `debris` | int | Count | - | clods of earth flung by the digging and the burst (one bone each; 0 = none) | 0..48 |  |
| `debrisSize` | float | Metres | x s | a clod's size |  |  |
| `debrisLayer` | int | Count | - | material layer of the clods (0 the body's skin: the palette's sandy colours) | 0..15 |  |
| `carapaceFrom` | int | Joint | - | a rigid carapace: joints from here to carapaceTo dive and climb as one (their sinks on a line), so the plates on them never turn against each other; -1 = none | -1.. |  |
| `carapaceTo` | int | Joint | - | the carapace's last joint |  |  |
| `hiddenShare` | float | Fraction | - | out of sight (Under) once this share of the joints, head first, is at the full depth (a tail tip may still show) | 0.3..1 | default 0.8 |

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

### CrestBeakHead

The beaked serpent head: a long, slightly flattened SDF skull flowing into a pointed, hooked beak, small eyes under a soft brow, a mouth line running back past the eye with rows of small interlocking teeth (fangs at the lower jaw's tip, a knob under it), a deep fleshy lower jaw running back into the throat, a pink mouth inside with cheek webs that stretch when it gapes, sickle crests (a prong widening into a crescent that hooks back) on top of the upper beak, and a jaw on its own bone that hangs a little open at rest and gapes wide on its own call timer while the upper head tips up. Rigid to the body's head joint; its back overlaps the neck so the outline runs on; carries the species' eyes. System: CrestBeakHeadRuntime (part runtime, stage Late: jaw, upper head, eye bones) + CrestBeakHead.EmitMesh (SculptMesher pieces, cached; teeth as horns; cheek webs skinned between the bones).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `skullLength` | float | Metres | x s | skull length; the skull reaches half of it behind the head joint (into the neck) and half ahead to the beak's root |  | fixture 0.26 |
| `skullWidth` | float | Metres | x s | skull half width (>= skullDepth: slightly flattened) |  |  |
| `skullDepth` | float | Metres | x s | skull half depth (the head's back starts at ~0.6 x it inside the neck) |  |  |
| `beakLength` | float | Metres | x s | beak length from its root (skullLength / 2 ahead of the joint) to the tip |  |  |
| `beakHookDeg` | float | Degrees | - | the upper beak's tip hooks down by this much over the beak's last third, over the lower jaw's tip | -10..30 |  |
| `browRidge` | float | Fraction | - | how far the brow juts out over the eye socket | 0..1 |  |
| `crests` | CrestDef[] | Struct | - | crests on top of the upper beak: each one blade, or (mirror) an equal pair splayed into a V |  |  |
| `crests[].along` | float | Fraction | - | where it stands along the beak: 0 the beak's root .. 1 its tip | 0..1 |  |
| `crests[].height` | float | Metres | x s | how high its centre line rises above the beak's top (vertically; a splayed blade is longer) |  |  |
| `crests[].length` | float | Metres | x s | its centre line's length, root to tip (>= height / cos splayDeg) |  |  |
| `crests[].curlDeg` | float | Degrees | - | how far it curls backward from its root to its tip (0 = a straight blade leaning forward) | 0..200 |  |
| `crests[].thickness` | float | Metres | x s | blade half thickness at its widest (its root; with flare, where it flares out) - it tapers to a point at the tip |  |  |
| `crests[].chord` | float | Metres | x s | blade half chord (its width seen from the side) at its widest; 0 = 0.35 x length; chord / thickness is capped at 4 (it follows the thickness) |  |  |
| `crests[].flare` | float | Fraction | - | a sickle: the blade rises from a narrow prong and flares out to its widest (at flareAt of its length) this many times its root's width, then sweeps to the point; 0 = a plain taper from the root (a horn) | 0..6 |  |
| `crests[].flareAt` | float | Fraction | - | where along the blade it is widest (with flare): toward its end, a crescent (0 = 0.4). The Apex's 0.62 is the latest that keeps the outline smooth: past it the fall to the point is steeper, and with outside's arch (widest at 0.5) the measured widest moves from 0.61 toward the point as this grows | 0..0.9 |  |
| `crests[].outside` | float | Fraction | - | the blade's width shifted to the outside (front, convex side) of its curl: its axis arches out from the centre line by up to this share of the widest half chord (sin(pi u): none at the root and the point) - a broad crescent outside, a thin inner edge hooking to the point (0 = symmetric) | 0..0.8 |  |
| `crests[].hookDeg` | float | Degrees | - | the tip hooks back by this much more over the last quarter of the blade (on top of curlDeg) | 0..120 |  |
| `crests[].splayDeg` | float | Degrees | - | the blade leans out from upright by this much (toward +x; a mirrored pair: each away from the midline - a V from the front) | 0..75 |  |
| `crests[].mirror` | bool | Flag | - | an equal pair rooted side by side on the midline, mirrored left and right (else one blade on the midline) |  |  |
| `grooves` | int | Count | - | lamellae: fine grooves across the mouth line (the teeth took their place: 0) | 0..32 |  |
| `grooveDepth` | float | Metres | x s | groove depth |  |  |
| `jawFraction` | float | Fraction | - | the lower jaw's share of the head's depth (the mouth line's height) | 0.15..0.7 |  |
| `mouthCornerAt` | float | Fraction | - | the gape's corner (the jaw's hinge): fraction of skullLength ahead of the head joint (negative = behind it: the mouth line runs back under and past the eye) | -0.4..0.3 |  |
| `hingeBehind` | float | Fraction | - | the jaw's hinge: it turns about a point this far (x the jaw's depth at the gape's corner) behind the corner and half as far below it - the gape opens back into the throat | 0..1.5 |  |
| `jawDepth` | float | Fraction | - | the lower jaw's own depth below the mouth line at mid-length, x skullDepth (a deep, fleshy jaw; 0 = only what the head's outline gives) | 0..2 |  |
| `chinDepth` | float | Fraction | - | the lower jaw's depth at its chin, x skullDepth | 0..2 |  |
| `throatDepth` | float | Fraction | - | the lower jaw's depth at the gape's corner, where its underside runs back into the throat, x skullDepth | 0..2 |  |
| `cheekWeb` | float | Fraction | - | cheek webs: skin from the upper lip to the lower one, reaching this far (x skullLength) forward from the gape's corner, stretched between the head and the jaw when it gapes (0 = none: the open mouth is a slot at the corner) | 0..0.5 |  |
| `throatRadius` | float | Metres | x s | the throat: a pink plug at the back of the mouth hiding the body's front end (the neck's front rings and cap run into the head; an open mouth would show them) - its half size, about the neck's half height at the head joint (0 = none) |  |  |
| `throatReach` | float | Metres | x s | how far ahead of the head joint the body's front end reaches (the FlowBody's front ring plus its headCapLength): the throat plug covers it to here |  |  |
| `gapeDeg` | float | Degrees | - | the jaw's widest gape (a call): the lower jaw drops this far about the hinge | 0..80 |  |
| `callPitchDeg` | float | Degrees | - | on a call the upper head (skull, beak, crests) tips up this far at the full gape, about the skull's back | 0..30 |  |
| `restGapeDeg` | float | Degrees | - | the mouth hangs this far open at rest, showing the teeth (< gapeDeg) | 0..30 |  |
| `teeth` | int | Count | - | teeth in each row (upper and lower, each side): small pointed teeth along the lip line, the lower row between the upper's, showing even at rest | 0..40 |  |
| `toothLength` | float | Metres | x s | tooth length (the ones near the gape's corner shorter) |  |  |
| `toothRadius` | float | Metres | x s | tooth base radius |  |  |
| `toothKind` | string | Text | - | the material kind of the teeth and fangs: ivory-teeth (the kit's ivory keratin row, a kit slot of its own). A species short of kit slots uses the legacy "teeth" (registry row 13: no kit slot) with a Layers row restyling 13 to ivory Claw. Validate: a registry / NamedKinds kind or one a Layers row names |  |  |
| `toothRakeDeg` | float | Degrees | - | the teeth lean back toward the throat by this much (both rows) | -45..60 |  |
| `fangPairs` | int | Count | - | fang pairs at the lower jaw's tip (0..2: up to four small fangs) | 0..2 |  |
| `fangLength` | float | Metres | x s | fang length (the second pair 0.85 x) |  |  |
| `fangLiftDeg` | float | Degrees | - | the fangs point forward out of the jaw's tip, raised this far above its line | -30..90 |  |
| `chinKnob` | float | Metres | x s | radius of the rounded knob under the lower jaw's tip (0 = none) |  |  |
| `callEverySeconds` | Vector2 | Seconds | x sqrt(s) | time between calls: x the shortest, y the longest (each individual draws its own, seeded) |  |  |
| `callSeconds` | float | Seconds | x sqrt(s) | one call: open, hold, close |  |  |
| `eyeAt` | Vector2 | Fraction | - | eye centre: x along the skull (fraction of skullLength ahead of the joint), y height (fraction of skullDepth) | -0.5..0.5 |  |
| `pitchDeg` | float | Degrees | - | the head's long axis tilts nose-down (+) or up (-) from the neck's line at the head joint in the rest pose (0 = the head continues the neck) | -45..45 |  |
| `cell` | float | Metres | x s | polygoniser cell of the skull and jaw (the crests use a finer one from their thickness) |  |  |

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
| `pitJitter` | float | Fraction | - | uneven pits: most a little shallower, a seeded few much deeper and darker (0 = all alike) | 0..1 |  |
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
| `keys[].belly` | float | Fraction | - | depth of the lower half / halfHeight: a flat belly under a dome (a crab's sternum under its carapace); 0 = 1, as deep as the top is high | 0..1 |  |
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

### Maw

A big-mouthed head: a lofted skull rigid to its joint (brow ridges, nostril bosses, cheek flare) and a hinged mandible on its own bone, both lined with the mouth layer, with interlocking raked conical teeth along every lip (fangs at the front). Gapes with CreatureSim.Display (a burst, a threat) and breathes slowly otherwise. System: MawRuntime (part runtime, stage Late) + Maw.EmitMesh (LimbLoft).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `skull` | MawKey[] | Struct | - | skull sections, back to front (>= 3) |  |  |
| `skull[].z` | float | Metres | x s | place along the head from the hinge (+ forward) |  |  |
| `skull[].halfWidth` | float | Metres | x s | half width |  |  |
| `skull[].top` | float | Metres | x s | top of the section above the hinge (the skull's roof; a mandible's floor) |  |  |
| `skull[].bottom` | float | Metres | x s | bottom of the section (the skull's palate; a mandible's chin) |  |  |
| `mandible` | MawKey[] | Struct | - | mandible sections, back to front (>= 3), closed against the palate |  |  |
| `mandible[].z` | float | Metres | x s | place along the head from the hinge (+ forward) |  |  |
| `mandible[].halfWidth` | float | Metres | x s | half width |  |  |
| `mandible[].top` | float | Metres | x s | top of the section above the hinge (the skull's roof; a mandible's floor) |  |  |
| `mandible[].bottom` | float | Metres | x s | bottom of the section (the skull's palate; a mandible's chin) |  |  |
| `square` | float | Fraction | - | how squared the lower halves are (superellipse exponent) | 2..6 | default 3 |
| `brow` | float | Metres | x s | brow ridge height over the eyes |  |  |
| `browAt` | float | Metres | x s | where along the head the brows peak (from the hinge) |  |  |
| `nostril` | float | Metres | x s | nostril boss height at the snout's tip |  |  |
| `gapeDeg` | float | Degrees | - | the widest gape (CreatureSim.Display 1) | 0..85 | default 60 |
| `breathDeg` | float | Degrees | - | the breathing gape at rest (0 = shut) | 0..15 | default 3 |
| `breathSeconds` | float | Seconds | x sqrt(s) | the breath's period |  |  |
| `gapeSeconds` | float | Seconds | x sqrt(s) | time to swing from shut to the full gape |  |  |
| `teethPerSide` | int | Count | - | teeth along each lip side, upper and lower (0 = none) | 0..24 |  |
| `toothLength` | float | Metres | x s | a back tooth's length |  |  |
| `fang` | float | Fraction | - | the front teeth (fangs) are this much longer | 1..3 | default 1.8 |
| `toothWidth` | float | Fraction | - | tooth base radius, x its length | 0.1..0.6 | default 0.28 |
| `toothRakeDeg` | float | Degrees | - | teeth rake back from straight down / up | 0..50 | default 18 |
| `teethFrom` | float | Fraction | - | teeth start this far along the lip (0 = the hinge .. 1 = the tip) | 0..1 | default 0.2 |
| `toothInset` | float | Metres | x s | teeth sit this far in from the lip's edge |  |  |
| `layer` | int | Count | - | material layer of the skull and mandible (0 the body's skin) | 0..15 |  |
| `toothLayer` | int | Count | - | material layer of the teeth (13 teeth) | 0..15 | default 13 |
| `mouthLayer` | int | Count | - | material layer of the mouth's lining (14 the mouth's soft tissue) | 0..15 | default 14 |
| `around` | int | Count | - | mesh: vertices round a section, 0 = 28 | 0..64 |  |

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

### PitRow

A row of recessed pits with soft pale raised rims on a FlowBody's surface (count from fromU to toU at section angle angleDeg; mirror for both sides): spiracles, nostrils, sensory pits - oblique slits (aspect, slantDeg) growing toward the head (sizeGrowth). Dark cup (kind pit), belly-coloured rim, skinned with the body's rings. System: mesh only (PitRow.EmitMesh), skinned like the FlowBody rings under it.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `fromU` | float | Fraction | - | body fraction of the first pit (FlowBody.Section's u: 0 front tip .. 1 tail tip) | 0..1 | 0.12 |
| `toU` | float | Fraction | - | body fraction of the last pit | 0..1 | 0.3 |
| `angleDeg` | float | Degrees | - | place around the section, FlowBody's phi: -90 belly .. 0 flank .. +90 back (the right side; the left is mirrored) | -90..90 | default -35 |
| `count` | int | Count | - | pits in the row (per side) | 1..32 | default 6 |
| `radius` | float | Metres | x s | the rearmost pit's half width (across the slit; its half length is aspect x this) | 0.001..0.2 | default 0.012 |
| `depth` | float | Fraction | - | cup depth x the pit's half width | 0..2 | default 0.6 |
| `rim` | float | Fraction | - | rim height and width x the pit's half width (the pale raised lip around the cup) | 0..1 | default 0.35 |
| `aspect` | float | Fraction | - | each pit is an elongated slit this many times as long as it is wide (1 = round) | 1..5 | default 2.2 |
| `slantDeg` | float | Degrees | - | the slit's long axis turns this far from the body's long axis toward its back (0 = along the body, 90 = around it; the left side mirrored) | -90..90 | default 50 |
| `sizeGrowth` | float | Fraction | - | the pits grow toward the head: the frontmost is 1 + this times the rearmost (0 = all alike) | 0..3 | default 0.6 |

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
| `keys[].belly` | float | Fraction | - | depth of the lower half / halfHeight: a flat belly under a dome (a crab's sternum under its carapace); 0 = 1, as deep as the top is high | 0..1 |  |
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

### SerratedRings

Segmented ring armour along a run of the spine (a tail): one shingled ring per joint wrapping the top and flanks, each with a flat saw-tooth blade on either flank pointing out and back (and an optional dorsal tooth), blades shingled so a bend slides them under each other. System: mesh only (SerratedRings.EmitMesh, ShellBand).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `toJoint` | int | Joint | - | last joint with a ring (the first is at.joint) |  |  |
| `lift` | float | Metres | x s | the rings' stand-off from the body |  |  |
| `skirtDeg` | float | Degrees | - | how far each ring wraps under past the equator (90 = all but a slit) | -30..85 | default 40 |
| `thickness` | float | Metres | x s | ring thickness |  |  |
| `shingle` | float | Metres | x s | shingle step (rear proud, front tucked), > thickness / 2 |  |  |
| `overlap` | float | Fraction | - | overlap of neighbouring rings, fraction of the joint spacing | 0..1 | default 0.4 |
| `lip` | float | Metres | x s | rolled lip along each rear edge |  |  |
| `square` | float | Fraction | - | section squareness: 2 round .. 4 boxy | 2..4 | default 2.4 |
| `bladeLength` | float | Fraction | - | blade length (out from the ring), x the ring's half-width at the first ring | 0..3 | default 0.7 |
| `bladeEnd` | float | Fraction | - | blade length at the last ring, x the first's | 0..2 | default 0.6 |
| `bladeBase` | float | Fraction | - | blade base, fraction of the ring's length (from its rear edge forward) | 0.2..1 | default 0.6 |
| `bladeSweepDeg` | float | Degrees | - | blade sweep back from straight out | 0..75 | default 30 |
| `bladeAtDeg` | float | Degrees | - | where round the ring the blade sits: 0 = the equator, + higher up the flank | -40..60 | default 0 |
| `bladeThick` | float | Metres | x s | blade thickness |  |  |
| `bladeStep` | float | Metres | x s | blade shingle: its front edge this much lower than its rear (> bladeThick / 2) |  |  |
| `dorsalTooth` | float | Fraction | - | dorsal tooth height, x the blade length (0 = none) | 0..1 |  |
| `around` | int | Count | - | mesh: vertices across each ring, 0 = 28 | 0..96 |  |
| `layer` | int | Count | - | material layer: 10 armour plate (restyle with Layers) | 0..15 | default 10 |

### Shag

Shaggy fur: flattened tapering locks rooted in a band round the body over a run of joints (and on named limbs' upper bones), staggered, length-jittered, drooping and swept back, never reaching the ground. System: mesh only (Shag.EmitMesh).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `toJoint` | int | Joint | - | last joint of the run (the first is at.joint) |  |  |
| `perJoint` | float | Fraction | - | rows of locks per joint step along the run | 0.5..8 | default 2 |
| `around` | int | Count | - | rows round the body on each side (and under the midline) | 1..16 | default 4 |
| `dorsalFrom` | float | Fraction | - | the band round the body: from (-1 the belly's midline) | -1..1 | default -1 |
| `dorsalTo` | float | Fraction | - | the band round the body: to (+1 the back's midline) | -1..1 | default -0.2 |
| `length` | float | Metres | x s | lock length |  | delver 0.05 |
| `jitter` | float | Fraction | - | length jitter (+-) | 0..0.9 | default 0.35 |
| `width` | float | Metres | x s | lock width at the root |  |  |
| `droop` | float | Fraction | - | 0 straight out from the skin .. 1 hanging straight down | 0..1 | default 0.6 |
| `sweep` | float | Fraction | - | sweep toward the tail | 0..1 | default 0.35 |
| `fade` | float | Fraction | - | the locks taper off over the run's first and last joints (fraction of the run) | 0..0.5 | default 0.15 |
| `groundClear` | float | Fraction | - | a lock's tip stays above this fraction of its root's height over the rest pose's ground | 0..0.9 | default 0.25 |
| `limbs` | string[] | Text | - | limbs whose upper bones get locks too (ids; a mirrored limb's base id names both) |  |  |
| `limbLength` | float | Fraction | - | locks on a limb, x length | 0..2 | default 0.8 |
| `layer` | int | Count | - | material layer (0 the body's skin; a species may put its fur on any id it does not use and restyle that row with Layers - the Delver: 12) | 0..15 | default 0 |

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

### ArthropodPlan

Archetype: a whole arthropod - spider (prosoma + opisthosoma, 7-segment legs, palps, chelicerae) or crab (carapace, 6-segment legs on its margin, a crusher and a cutter cheliped, folding eye stalks, optional rock tuck, sideways walking) - from a few anatomical numbers. Rest stance solved from the leg shape (RestStance), gait from leg length (GaitDefaults, Froude). Explicit parts with a generated id override it; added parts layer on. System: compose (CompiledSpecies, IArchetype) + RestStance, GaitDefaults, LegFamilies, CrabShell, CrabFold.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `id` | string | Text | - | unique within the species; other parts reference it (mirror appends .L / .R, Repeat its index) |  |  |
| `at` | PartSocket | Struct | - | where it attaches: spine joint, side, offset in the joint frame |  |  |
| `at.joint` | int | Joint | - | spine joint the part hangs from (soft bodies: unused) |  |  |
| `at.side` | int | Count | - | -1 left, +1 right, 0 on the midline; set by mirror | -1..1 |  |
| `at.offset` | Vector3 | Metres | x s | offset in the joint frame, x mirrored by side (a leg's shoulder, the eyes' centre) |  |  |
| `at.slot` | int | Count | - | ring slot on a soft body (octopus web), 0 = the first |  |  |
| `mirror` | bool | Flag | - | authoring sugar: expand into <id>.L (side -1) then <id>.R (side +1), the shipped leg order |  |  |
| `form` | Spider / Crab | Enum | - | body plan: Spider (prosoma + opisthosoma, arachnid legs) or Crab (carapace, decapod legs, chelipeds) |  |  |
| `bodyLength` | float | Metres | x s | spider: prosoma + abdomen length; crab: carapace length |  | tarantula 0.41, stone crab 0.31 |
| `bodyWidth` | float | Fraction | - | spider: prosoma width / bodyLength (0 = 0.34); crab: carapace width / length (0 = 1.3) | 0..3 |  |
| `bodyBulk` | float | Fraction | - | spider: abdomen size x the tarantula's (0 = 1); crab: dome height / carapace half width (0 = 0.42) | 0..3 |  |
| `legPairs` | int | Count | - | walking leg pairs (0 = 4) | 0..6 |  |
| `legLength` | float | Fraction | - | longest walking leg / body (spider: / bodyLength; crab: / carapace WIDTH) (0 = 1.05) | 0..4 |  |
| `legFormula` | float[] | Fraction | - | relative leg lengths front to back, one per pair, normalised to the longest (empty = the family's: spider 4-1-2-3, crab II-III longest) |  |  |
| `frontYawDeg` | float | Degrees | - | rest yaw of the front pair from forward (0 = spider 32, crab 52) | 0..180 |  |
| `backYawDeg` | float | Degrees | - | rest yaw of the hind pair from forward (0 = spider 152, crab 142) | 0..180 |  |
| `legBulk` | float | Fraction | - | leg thickness x the template's (0 = 1) | 0..3 |  |
| `legFlatten` | float | Fraction | - | crab: walking legs flattened (broad, flat merus and propodus: height = (1 - this) x width) (0 = 0.35, < 0 = round) | -1..0.8 |  |
| `posture` | float | Fraction | - | posture: rest bends x this (0 = spider 1, crab 1.15 - arched legs with room to reach out and pull in sideways; > 1 higher knees, a lower body) | 0..1.4 |  |
| `stepFroude` | float | Fraction | - | step time in Froude units t sqrt(g / L) (0 = spider 1.8, crab 1.25) | 0..5 |  |
| `speedFroude` | float | Fraction | - | cruise speed in Froude units v / sqrt(g L) (0 = spider 0.08, crab 0.1) | 0..1 |  |
| `clawSize` | float | Fraction | - | crab: claw size x the default (0 = 1, < 0 = no chelipeds); spider: palp size (0 = 1, < 0 = none) | -1..3 |  |
| `crusherRatio` | float | Fraction | - | crab: the crusher (right) claw x the cutter (0 = 1.3) | 0..3 |  |
| `lumps` | float | Fraction | - | crab: carapace lumps / its width (0 = 0.035, < 0 = smooth) | -1..0.2 |  |
| `lumpRidge` | float | Fraction | - | crab: the lumps 0 = smooth bumps .. 1 = ridged facets (weathered rock) (0 = 0.35) | 0..1 |  |
| `outline` | Oval / Pear | Enum | - | crab: carapace outline - Oval (stone crab: widest ahead of the middle, a blunt front arc) or Pear (king crab: widest behind, narrowing to the front) |  |  |
| `clawYawDeg` | float | Degrees | - | crab: rest yaw of the chelipeds from forward (0 = 30; small = held across the face) | 0..90 |  |
| `tuck` | bool | Flag | - | crab: hides as a rock when scared - a Tuck part whose folds are derived from the legs and the shell (legs flat on the ground under the shell's margin, claws across the face, stalks into their orbits) |  |  |
| `tuckScareSeconds` | float | Seconds | x sqrt(s) | crab tuck: mean time up and about between random scares (0 = only real scares, CreatureSim.Scare) | 0.. | stone crab 14 |
| `travel` | FormDefault / Forward / Sideways | Enum | - | how it travels: FormDefault (crab sideways, spider forward), Forward, Sideways (the generated Walk's travel) |  |  |

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

### Bask

Lies down where it is for a while: curled, the head tucked under its hood, or flat in the sun (the Burrow part's Rest posture); comes due every so often per individual.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `everySeconds` | float | Seconds | x sqrt(s) | mean time between rests (uniform 0.5..1.5 x, per individual) |  |  |
| `restSeconds` | float | Seconds | x sqrt(s) | a rest's length (x 0.7..1.3) |  |  |
| `curlShare` | float | Fraction | - | share of the rests taken curled up (the rest flat in the sun) | 0..1 | default 0.5 |
| `utility` | float | Fraction | - | utility when due | 0.2..2 | default 0.4 |

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

### Follow

Trails a visible member of its group (same species, same enclosure: CreatureGroup) at a distance for a while, every so often per individual.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `everySeconds` | float | Seconds | x sqrt(s) | mean time between follows (uniform 0.5..1.5 x, per individual) |  |  |
| `followSeconds` | float | Seconds | x sqrt(s) | a follow's length |  |  |
| `distance` | float | Fraction | - | how far behind the leader's tail it keeps, x its body length | 0..3 | default 0.4 |
| `radius` | float | Fraction | - | a leader must be within this x its body length | 1..20 | default 6 |
| `utility` | float | Fraction | - | utility when due and a leader is in sight | 0.2..2 | default 0.3 |

### Forage

Walks a little way, then sniffs and scratches at the ground (the Burrow part's Scratch posture: head down, digging limbs scooping) for a while; comes due every so often per individual.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `everySeconds` | float | Seconds | x sqrt(s) | mean time between forays (uniform 0.5..1.5 x, per individual) |  |  |
| `scratchSeconds` | float | Seconds | x sqrt(s) | time spent scratching at the spot |  |  |
| `walk` | float | Fraction | - | how far it walks to the spot, x its body length | 0..5 | default 1.2 |
| `utility` | float | Fraction | - | utility when due | 0.2..2 | default 0.35 |

### School

Flying in a loose group: separation (3D, strongest), alignment, cohesion, a shared waypoint that moves round the enclosure at a height in the band, the wall and the aviary's obstacles; vetoes the locomotion's manoeuvres while a neighbour is near. Members of a lab group find each other by species and enclosure.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `separation` | float | Fraction | - | keep at least this many spans from a neighbour (separation acts inside it) | 0.5..6 | manta 3 |
| `cohesion` | float | Fraction | - | weight of the pull to the group's centre | 0..3 |  |
| `alignment` | float | Fraction | - | weight of the group's mean heading | 0..3 |  |
| `waypoint` | float | Fraction | - | weight of the shared waypoint | 0..3 |  |
| `waypointSeconds` | float | Seconds | x sqrt(s) | a new shared waypoint about this often |  |  |
| `bandLow` | float | Fraction | - | height band of the waypoints: lowest, as a fraction of the room under the roof | 0..1 |  |
| `bandHigh` | float | Fraction | - | height band of the waypoints: highest | 0..1 |  |
| `manoeuvreGap` | float | Fraction | - | no manoeuvres while a neighbour is within this many spans (or will come within 3/4 of it in the next seconds) | 0..10 |  |

### SkyHunt

A soaring predator's hunt: climb to soarAltitude, circle in long glides and bursts of slow deep beats, stoop on prey below (HuntPrey; with none, a practice mark on the ground) with the wings folded and the neck reaching, pull out over it with a strike, climb away. In bouts; the species' FlightExcursion flies between them. Gives the species a ModularBrain.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `soarAltitude` | float | Metres | x s | circling height over the ground (the aviary is made 1.25 x this tall) |  | pterodactyl 9 |
| `soarBand` | float | Metres | x s | the soaring band: glides down this far below soarAltitude, beats back up |  | pterodactyl 1.2 |
| `circleRadius` | float | Fraction | - | circle radius, x the area's safe radius | 0.2..0.9 |  |
| `boutSeconds` | float | Seconds | x sqrt(s) | a hunting bout's length (then the other flight behaviour has the brain) |  |  |
| `restSeconds` | float | Seconds | x sqrt(s) | rest between bouts |  |  |
| `stoopInterval` | float | Seconds | x sqrt(s) | mean time between stoops within a bout |  |  |
| `stoopDeg` | float | Degrees | - | the stoop is at least this steep (the line from where it folds to the pull-out over the mark) | 15..85 |  |
| `pullUpHeight` | float | Metres | x s | pulls out this high over the target (the head reaches below it) |  | pterodactyl 0.9 |
| `stoopFold` | float | Fraction | - | the wings' fold in the stoop at its steepest | 0.3..1 |  |
| `practice` | bool | Flag | - | with no prey in its area it stoops at a practice mark on the ground |  |  |
| `utility` | float | Fraction | - | utility while hunting (FlightExcursion 0.2, Wander 0.1) | 0.25..2 |  |
| `handBack` | bool | Flag | - | when it gives up the brain (a bout ends mid-glide, a landing starts) it hands the flight back: the wings free to beat (Model.DemandCap), no dive fold, the trunk's level. Off = shipped: a bout that ended in a held glide left the wings held, and the next landing glided round its point at floor height without a beat until it timed out |  |  |

### Tunnel

Every so often (per individual) goes underground to a goal across the enclosure: the Burrow part plans the trip through the enclosure's shared tunnels (walk to a hole, ride, dig only the rest), digs in, travels hidden and bursts out. Holds until the trip ends; never while more than maxHiddenShare of its group is under. Gives the species a ModularBrain.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `surfaceSeconds` | float | Seconds | x sqrt(s) | mean time on the surface between trips (uniform 0.5..1.5 x, per individual) |  | delver 45 |
| `minTrip` | float | Fraction | - | the shortest trip, fraction of the safe disc's radius | 0.2..2 | default 0.7 |
| `maxTrip` | float | Fraction | - | the longest trip, fraction of the safe disc's radius | 0.2..2 | default 1.6 |
| `maxHiddenShare` | float | Fraction | - | at most this share of the group underground at once (always lets one go) | 0..1 | default 0.5 |
| `utility` | float | Fraction | - | utility when due (Wander idles at 0.1) | 0.2..2 | default 0.9 |

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
| `spec` | FlightSpec | Spec | - | flight (FlightSpec: speeds, lift/drag, altitude PID, stroke, banking, flare, air undulation) - fields: cruiseSpeed, minSpeed, maxSpeed, kGrip, vGripMin, kLift, kDrag, upstrokeArea, featherDeg, climbGain, climbDamping, climbIntegral, climbIntegralLimit, thrustGain, demandFloor, descendCutoff, glideDemand, amplitudeMin, strokeRateMin, strokeRateMax, glideRelax, maxBankDeg, bankGain, bankDamping, maxAsymmetry, rollGain, rollDamping, glideAlphaDeg, flareAlphaDeg, launchSpeed, flareDistance, minAltitude, cruiseAltitudeFrac, airUndulationDeg, airUndulationHz, tailSag, headStabilise, headStabiliseBounded, airNeckStance, launchHeadOnNeck, touchdownAtStance, launchTailBySegment, touchdownFromHover, perchTubeOnApproach, drapeOwnHeading, perchFrontLevel, avoidLookahead, avoidMargin, perchGrip, perchMinRadius, pursuitGain, legTuckOut, legTuckDown, legTuckReach, touchdownTogether, airLevel, glideEnergy, approachBleedsSpeed |  |  |
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

### QuadLaunch

A quadrupedal launch: a crouch on the four limbs (the body lowered by crouchDepth over crouchSeconds), then a vault at vaultSpeed off the folded wing arms; the wings open at the top (CreatureFlight.Launch). Without it a flyer hops off at the shipped 2.4 m/s.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `crouchSeconds` | float | Seconds | x sqrt(s) | the crouch before the vault | 0.05..2 | pterodactyl 0.4 |
| `crouchDepth` | float | Metres | x s | how far the body sinks in the crouch (every joint's clearance) |  | pterodactyl 0.12 |
| `vaultSpeed` | float | MetresPerSecond | x sqrt(s) | the vault's upward speed (the shipped launch: 2.4) |  | pterodactyl 4 |

### Slither

Lateral head wave the drag chain carries back (undulation in the walk). Long wave: lambda = speed / Hz >= 0.7 body length (R27).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `undulationDeg` | float | Degrees | - | head wave amplitude |  | snake 45, monitor 6 |
| `undulationHz` | float | Hz | / sqrt(s) | wave frequency |  | snake 0.625 |

### Soar

Always-airborne flight without legs or legacy wings (manta rays, gliding fish): flap bouts alternating with long glides, coordinated banked turns, an altitude band over the ground and under the roof, barrel rolls and loops; the body lies on the head's 3D path and heaves opposite the stroke. Drives flapping parts (RayWing) through IFlapDriver; sizes its own aviary (IAirborne).

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `cruiseSpeed` | float | MetresPerSecond | x sqrt(s) | cruising speed |  | manta 3 |
| `minSpeed` | float | MetresPerSecond | x sqrt(s) | slowest flight: below it a flap bout starts |  | manta 2 |
| `maxSpeed` | float | MetresPerSecond | x sqrt(s) | fastest flight (a dive) |  |  |
| `glideRatio` | float | Fraction | - | glide ratio: distance per height lost in a glide at cruise | 2..30 | manta 11 |
| `climbDeg` | float | Degrees | - | climb angle of a flap bout at cruise | 0..40 |  |
| `diveDeg` | float | Degrees | - | steepest dive | 0..60 |  |
| `flapHz` | float | Hz | / sqrt(s) | wingbeat frequency |  | manta 0.5 |
| `beatsMin` | int | Count | - | fewest beats in a bout | 1..12 |  |
| `beatsMax` | int | Count | - | most beats in a bout | 1..12 |  |
| `glideMin` | float | Seconds | x sqrt(s) | shortest glide between bouts |  |  |
| `glideMax` | float | Seconds | x sqrt(s) | longest glide between bouts |  |  |
| `maxBankDeg` | float | Degrees | - | steepest bank of a turn | 5..75 |  |
| `bankSeconds` | float | Seconds | x sqrt(s) | time constant of banking into a turn |  |  |
| `steerSeconds` | float | Seconds | x sqrt(s) | steering: the heading error is turned out in about this time |  |  |
| `altitudeSeconds` | float | Seconds | x sqrt(s) | altitude: the error is flown out in about this time |  |  |
| `pitchRateDeg` | float | DegPerSecond | / sqrt(s) | fastest change of the climb angle |  |  |
| `cruiseAltitude` | float | Metres | x s | cruising height above the ground when the brain's target is on the ground |  | manta 6 |
| `minAltitude` | float | Metres | x s | lowest height of the body above the ground (hard floor) |  | manta 2.5 |
| `altitudeBand` | float | Metres | x s | half height of the altitude band a glide may sink through before a bout |  |  |
| `heave` | float | Metres | x s | body heave amplitude at a full beat (up on the downstroke) |  | manta 0.05 |
| `heaveLag` | float | Fraction | - | heave wave: phase lag head -> tail, in cycles | 0..0.5 |  |
| `barrelRadius` | float | Metres | x s | barrel roll: helix radius (0 = none) |  | manta 0.7 |
| `rollSeconds` | float | Seconds | x sqrt(s) | barrel roll: duration |  |  |
| `loopRadius` | float | Metres | x s | loop: radius (0 = none) |  | manta 1.8 |
| `manoeuvreMin` | float | Seconds | x sqrt(s) | shortest time between manoeuvres |  |  |
| `manoeuvreMax` | float | Seconds | x sqrt(s) | longest time between manoeuvres |  |  |

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
| `travel` | Forward / Sideways | Enum | - | Forward (head first) or Sideways (a crab: the body stays across its travel; turns pivot about the girdles' middle; needs a rigid body) |  |  |
| `forwardFactor` | float | Fraction | - | Sideways: speed factor when it walks forward toward a target dead ahead (0 = 0.35, < 0 = never forward) | -1..1 |  |

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

### Fur

Hair in regions: an Undercoat of shells (short dense fur) and Strands (long fur, manes, tufts) as GPU blades skinned with the body's bone matrices and simulated as springs that trail the motion and droop (Scripts/Creatures/Fur, Resources/CreatureFur). Off = no extra draw.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `regions` | FurRegion[] | Struct | - | the furry regions |  |  |
| `regions[].name` | string | Text | - | a name for the region (debug, the catalogue) |  |  |
| `regions[].kind` | Undercoat / Strands | Enum | - | Undercoat (shells: short dense fur) or Strands (blades: long fur, manes, tufts) |  |  |
| `regions[].partMask` | int | Count | - | bit mask of the part ids it grows on (bit 0 body, bit 1 limb, ...; the claw/eye/membrane/jaw/teeth ids are refused) |  | body 1, body + limbs 3 |
| `regions[].arcFrom` | float | Metres | x s | arc window start along the part (uv0.x, rest metres: the body from the snout, a limb from its root) |  |  |
| `regions[].arcTo` | float | Metres | x s | arc window end |  |  |
| `regions[].arcEdge` | float | Metres | x s | soft edge outside the arc window (0 = hard) |  |  |
| `regions[].dorsalFrom` | float | Fraction | - | dorsal window start (-1 belly .. +1 ridge) | -1..1 |  |
| `regions[].dorsalTo` | float | Fraction | - | dorsal window end | -1..1 |  |
| `regions[].dorsalEdge` | float | Fraction | - | soft edge outside the dorsal window (0 = hard) | 0..2 |  |
| `regions[].length` | float | Metres | x s | hair length (strands: the whole lock; undercoat: the outer shell's height) |  | mane 0.06, undercoat 0.008 |
| `regions[].lengthJitter` | float | Fraction | - | length jitter (+-) | 0..0.9 |  |
| `regions[].density` | float | Fraction | - | strands: roots per square metre of the rest (scale-1) surface; undercoat: hairs per square centimetre of it |  | mane 30000, undercoat 60 |
| `regions[].width` | float | Metres | x s | strands: the lock's width at the root; undercoat: a hair's radius at the root |  | mane 0.004, undercoat 0.0006 |
| `regions[].rootColour` | Color | SrgbColour | - | colour at the root |  |  |
| `regions[].tipColour` | Color | SrgbColour | - | colour at the tip |  |  |
| `regions[].bodyTint` | float | Fraction | - | toward the body's own palette and pattern at the root (0 = the region colours only) | 0..1 |  |
| `regions[].liftDeg` | float | Degrees | - | how far the hair rises off the skin (0 flat .. 90 standing up) | 0..90 | pelt 20, mane 55 |
| `regions[].flowBack` | float | Fraction | - | comb toward the tail (along the body) | 0..1 |  |
| `regions[].flowDown` | float | Fraction | - | comb toward the ground (down the flanks and legs) | 0..1 |  |
| `regions[].jitterDeg` | float | Degrees | - | per-hair direction jitter | 0..60 |  |
| `regions[].clump` | float | Fraction | - | strands: how much the tips gather into locks with their neighbours | 0..1 |  |
| `regions[].clumpSize` | int | Count | - | strands: strands per lock | 1..32 |  |
| `regions[].stiffnessHz` | float | Hz | / sqrt(s) | strands: spring frequency (how fast a hair swings back; / sqrt(s)) | 0.2..20 | mane 3, tufts 2 |
| `regions[].damping` | float | Fraction | - | strands: damping ratio (0.2 swishy .. 1 dead) | 0..2 |  |
| `regions[].droop` | float | Fraction | - | how much gravity pulls the hair down (strands: x g against the spring; undercoat: the outer shells' sag) | 0..2 |  |
| `seed` | int | Count | - | seed of the root placement (deterministic per species) |  |  |
| `maxStrands` | int | Count | - | most strands per individual (densities scale down to fit) | 0..65536 |  |
| `shells` | int | Count | - | undercoat shells (0 = no undercoat) | 0..32 |  |
| `maxSimulated` | int | Count | - | individuals of this species with strands at once (the nearest) | 0..64 |  |
| `strandFadeStart` | float | Metres | x s | strands: full count closer than this to the camera (x sizeScale) |  | 6 |
| `strandFadeEnd` | float | Metres | x s | strands: none past this (x sizeScale) |  | 14 |
| `shellFadeEnd` | float | Metres | x s | undercoat: shells thin to 3 by this distance (x sizeScale); none past twice it |  |  |
| `pattern` | float | Fraction | - | how much of the body's pattern shows through the fur | 0..1 |  |
| `rootOcclusion` | float | Fraction | - | darker roots (the fur's ambient occlusion) | 0..1 |  |
| `highlight` | float | Fraction | - | soft anisotropic highlight along the hair (Kajiya-Kay) | 0..1 |  |
| `translucency` | float | Fraction | - | light through the tips against the sun | 0..2 |  |

### Layers

Restyles part ids (material layers) for this species: shading model (Skin, Chitin, Membrane, Eye, Claw), tint, smoothness, and which layers (pattern, scale grain, sheen, shimmer) each has. Rows not listed keep the registry's (Kit/PartLayers.cs). A new part needs a row, never a shader edit.

| field | type | unit | size | meaning | range | example |
|---|---|---|---|---|---|---|
| `rows` | LayerDef[] | Struct | - | rows, one per restyled part id |  |  |
| `rows[].partId` | int | Count | - | the part id (material layer) this row styles: 0 body, 1 limb, 2 foot/claw/cone, 3 eye, 8 membrane, 9 band, 10+ new parts (CATALOGUE.md registry) | 0..15 |  |
| `rows[].kind` | string | Text | - | instead of partId: the part KIND this row styles, by name (registry kinds: body, limb, teeth, ...; or a kind of this species' own: it gets a free slot, MaterialSlots) |  |  |
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

### SkeletonOnly

Draw nothing: the species is simulated and F3 shows its skeleton, but the renderer builds no mesh and submits no draw (CreatureSpecies.skeletonOnly). For a species built skeleton-first, before its skin.

No parameters.

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

## Material layers (part kinds and their slots; legacy ids next free: 16 - a new kind takes none: NamedKinds)

Creature.shader shades a part id by its row of the species' look-layer table (Kit/PartLayers.cs): a shading model (Skin, Chitin, Membrane, Eye, Claw) plus layers (pattern, scale grain, sheen, shimmer) and a tint. The table is full at the ids below (each keeps its slot in every species). A NEW part kind takes no global id: it is named in PartLayers.NamedKinds (or [Part(MaterialKinds)], or a species' own Layers row with `kind`) and gets a free slot per species (MaterialSlots); the part emits with PartMeshEmitter.Slot(kind). A species restyles rows with the Layers module (by part id or by kind). Never reuse an id. A new shading MODEL is a shader edit: add it to CreatureLayers.hlsl + ShadingModel, with a CPU proof and the Unity pixel guard (CreaturePartLayerTests, CreatureShaderLayerTests).

| id | kind | use | row (model, layers, tint) |
|---|---|---|---|
| 0 | `body` | body skin | Skin (Sheen, Shimmer) |
| 1 | `limb` | limb | Skin (LimbPattern, FineGrain, Sheen, Shimmer) |
| 2 | `foot` | foot, claw, rigid cone | Skin (NoPattern, FineGrain, Sheen, Shimmer, tint Pattern 0.3) |
| 3 | `eye` | eye | Eye (NoPattern, NoGrain) |
| 4 | `octopus-arm` | octopus arm | octopus shader family (CreatureOctopus.shader) |
| 5 | `octopus-web` | octopus web | octopus shader family (CreatureOctopus.shader) |
| 6 | `octopus-mantle` | octopus mantle | octopus shader family (CreatureOctopus.shader) |
| 7 | `octopus-siphon` | octopus siphon | octopus shader family (CreatureOctopus.shader) |
| 8 | `membrane` | membrane (wings, fins, antennae) | Membrane (NoPattern, NoGrain) |
| 9 | `band` | arthropod band | Skin (Sheen, Shimmer, tint Band 0.9) |
| 10 | `plate` | armour plate (ArmourPlates) | Chitin (NoPattern, NoGrain) |
| 11 | `pincer` | pincer, jaw (Pincer) | Claw (NoPattern, NoGrain, tint Pattern 0.3) |
| 12 | `scute` | armour scutes (ScuteRow) | Skin (Sheen, Shimmer) |
| 13 | `teeth` | teeth (Jaw) | Skin (Sheen, Shimmer) |
| 14 | `tongue` | tongue (Tongue) | Skin (Sheen, Shimmer) |
| 15 | `ray-skin` | ray skin (RayBody, RayWing, WhipTail, CephalicLobe) | Skin (Sheen, Shimmer) |
| (per species) | `crest` | beak crests (CrestBeakHead) | Claw (NoPattern, NoGrain, tint Own 1) |
| (per species) | `ivory-teeth` | teeth and fangs, ivory (CrestBeakHead) | Claw (NoPattern, NoGrain, tint Own 1) |
| (per species) | `mouth` | inside of the mouth (CrestBeakHead) | Skin (NoPattern, FineGrain, Sheen, Shimmer, tint Own 1) |
| (per species) | `pit` | breathing holes / nostrils (PitRow) | Claw (NoPattern, NoGrain, tint Own 1) |
| (per species) | `dewlap` | throat sail (Dewlap) | Skin (NoPattern, FineGrain, Sheen, Shimmer, tint Own 1) |
| (per species) | `wingfan` | flesh wing sail (FleshWing) | Skin (NoPattern, FineGrain, Sheen, Shimmer, tint Own 1) |
| (per species) | `wingstripe` | flesh wing stripes (FleshWing) | Skin (NoPattern, FineGrain, Sheen, Shimmer, tint Own 1) |
