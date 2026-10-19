// Creature kit T2.2: the module types for everything that exists today (design §2.4). Locomotion and look
// modules carry the legacy species fields they replace (CompiledSpecies writes them back for the runtime);
// behaviour modules choose the brain (Kit/Brains.cs: BrainFactory; a new behaviour joins a ModularBrain).
using System;
using UnityEngine;

namespace CreatureKit
{
    // ------------------------------------------------------------------ locomotion

    // How Walk assigns gaitGroup to walking limbs left at -1. Pair = a limb's place among the walking limbs of
    // its side, head first (by attach joint, then part order).
    //   Diagonal / Tripod / Tetrapod: (pair + right) % 2 - opposite corners together (the shipped quadrupeds,
    //            beetles and spiders: one alternating rule, three names for the leg counts it suits).
    //   Alternate: the sides alternate (left 0, right 1): a centipede's two waves, with ArthropodLeg.liftDelay.
    //   Wave: a metachronal wave over waveGroups groups: left pair p in group p % G, right pair p half a wave
    //         later ((p + G/2) % G), and the groups step strictly in turn (CreatureSpecies.waveGait, the T5.8
    //         scheduler in CreatureLegs.StartWave): the lift-offs run head to tail (a centipede's retrograde
    //         wave), or tail to head with waveTailToHead (a millipede's direct wave).
    //   Explicit: every limb sets gaitGroup itself.
    public enum GaitPattern { Explicit = 0, Diagonal = 1, Tripod = 2, Tetrapod = 3, Alternate = 4, Wave = 5 }

    [Serializable]
    public struct GaitBobDef
    {
        [Param(ParamUnit.Metres, "peak dip of the body per lift-off (0 = off)")] public float amplitude;
        [Param(ParamUnit.Hz, "spring frequency")] public float hz;
        [Param(ParamUnit.Fraction, "damping ratio (0.2 bouncy .. 1 dead)", Min = 0, Max = 2)] public float damping;
    }

    [Serializable]
    public struct GaitBurstDef
    {
        [Param(ParamUnit.Fraction, "cruise speed x this at the peak of a burst (<= 1 = off); the gait's stride limit still caps it", Min = 0, Max = 4, Example = "scolopendra 2.2")] public float factor;
        [Param(ParamUnit.Seconds, "mean time between bursts (each gap 0.5..1.5 x this, per individual)")] public float every;
        [Param(ParamUnit.Seconds, "one burst: a quick surge, a hold, an easing back")] public float seconds;
    }

    [Serializable]
    [Module("Walk", CreatureModule.Locomotion, Summary = "Head steering in the tangent plane, chain, grounding, gait groups, girdle sway (CreatureSim.SubStep, CreatureLegs). The default locomotion of a spine body.",
            Needs = "SpineBody")]
    public sealed class Walk : CreatureModule
    {
        [Param(ParamUnit.MetresPerSecond, "head cruise speed", Example = "monitor 0.9, tarantula 0.16")] public float speed;
        [Param(ParamUnit.DegPerSecond, "turn rate", Example = "walkers 160, snake 240")] public float turnRateDeg;
        [Param(ParamUnit.Acceleration, "acceleration")] public float accel;
        [Param(ParamUnit.Fraction, "speed lost in a full-rate turn")] public float turnSlowdown;
        [Param(ParamUnit.Degrees, "peak yaw of the trunk between girdles toward the reaching leg (0 = none)")] public float gaitSwayDeg;
        [Param(ParamUnit.Degrees, "girdle roll over its planted feet")] public float gaitRollDeg;
        [Param(ParamUnit.Count, "fixed sub-steps per tick (cost!)", Min = 1, Max = 4, Default = "2")] public int subSteps = 2;
        [Param(ParamUnit.Enum, "assigns gaitGroup to walking limbs left at -1")] public GaitPattern pattern = GaitPattern.Diagonal;
        [Param(ParamUnit.Count, "groups of a Wave pattern: legs per side in one wavelength (each foot is in the air 1/waveGroups of the time)", Min = 2, Max = 15, Example = "centipede 5")] public int waveGroups = 4;
        [Param(ParamUnit.Flag, "Wave: the lift-offs run tail to head (a millipede's direct wave) instead of head to tail (a centipede's retrograde wave)")] public bool waveTailToHead;
        [Param(ParamUnit.Struct, "vertical body bounce kicked by each lift-off")] public GaitBobDef bob;
        [Param(ParamUnit.Struct, "speed bursts: every so often the cruise speed surges (a predator's dash), seeded per individual; factor 0 = off")] public GaitBurstDef burst;

        public override string Validate(in SpeciesCtx c)
        {
            if (c.Soft) return "Walk needs a SpineBody";
            if (pattern == GaitPattern.Wave && (waveGroups < 2 || waveGroups > 15)) return "Walk.waveGroups must be 2..15 for the Wave pattern";
            if (burst.factor > 1f && (!(burst.every > 0f) || !(burst.seconds > 0f))) return "Walk.burst: every and seconds must be > 0 when factor > 1";
            return null;
        }
    }

    [Serializable]
    [Module("Slither", CreatureModule.Locomotion, Summary = "Lateral head wave the drag chain carries back (undulation in the walk). Long wave: lambda = speed / Hz >= 0.7 body length (R27).",
            Needs = "Walk")]
    public sealed class Slither : CreatureModule
    {
        [Param(ParamUnit.Degrees, "head wave amplitude", Example = "snake 45, monitor 6")] public float undulationDeg;
        [Param(ParamUnit.Hz, "wave frequency", Example = "snake 0.625")] public float undulationHz;
        public override string Validate(in SpeciesCtx c) => c.Module<Walk>() == null ? "Slither needs Walk" : null;
    }

    [Serializable]
    [Module("Climb", CreatureModule.Locomotion, Summary = "Per-joint up field, real-geometry contact via ISurfaceProbe, pitch limiter (CreatureClimb, ClimbSpec). Terrain walls >= 50 deg over >= 1/2 body length (R30/R31). 0 = default for every number.",
            Needs = "Walk")]
    public sealed class Climb : CreatureModule
    {
        [Param(ParamUnit.Degrees, "steepest surface, from gravity's up (0 = 140)")] public float maxSurfaceAngleDeg;
        [Param(ParamUnit.Fraction, "highest climb in body lengths (0 = 1.5)")] public float maxHeightBodyLengths;
        [Param(ParamUnit.Fraction, "speed factor on a vertical surface, up (0 = 0.5)")] public float speedFactor;
        [Param(ParamUnit.Fraction, "speed factor going down head-first (0 = 0.4)")] public float descentSpeedFactor;
        [Param(ParamUnit.Fraction, "body clearance on a wall vs walking (0 = 0.65)")] public float clearanceFactor;
        [Param(ParamUnit.Fraction, "stride on a wall (0 = 0.6)")] public float strideFactor;
        [Param(ParamUnit.Fraction, "step time on a wall (0 = 1.2)")] public float stepTimeFactor;
        [Param(ParamUnit.Fraction, "foot lift on a wall (0 = 0.6)")] public float liftFactor;
        [Param(ParamUnit.DegPerSecond, "fastest turn of a joint's up (0 = 270)")] public float upRateDeg;
        [Param(ParamUnit.Count, "joints each side smoothed into the up field (0 = 3)")] public int upSmoothJoints;
        [Param(ParamUnit.Degrees, "largest pitch bend between segments (0 = 25)")] public float pitchLimitDeg;
        [Param(ParamUnit.Fraction, "a climb goal needs a surface this many body widths across (0 = 1.5)")] public float minWidthFactor;

        public ClimbSpec ToRuntime() => new ClimbSpec
        {
            canClimb = true, maxSurfaceAngleDeg = maxSurfaceAngleDeg, maxHeightBodyLengths = maxHeightBodyLengths, speedFactor = speedFactor,
            descentSpeedFactor = descentSpeedFactor, clearanceFactor = clearanceFactor, strideFactor = strideFactor, stepTimeFactor = stepTimeFactor,
            liftFactor = liftFactor, upRateDeg = upRateDeg, upSmoothJoints = upSmoothJoints, pitchLimitDeg = pitchLimitDeg, minWidthFactor = minWidthFactor,
        };

        public static Climb From(in ClimbSpec c) => new Climb
        {
            maxSurfaceAngleDeg = c.maxSurfaceAngleDeg, maxHeightBodyLengths = c.maxHeightBodyLengths, speedFactor = c.speedFactor,
            descentSpeedFactor = c.descentSpeedFactor, clearanceFactor = c.clearanceFactor, strideFactor = c.strideFactor, stepTimeFactor = c.stepTimeFactor,
            liftFactor = c.liftFactor, upRateDeg = c.upRateDeg, upSmoothJoints = c.upSmoothJoints, pitchLimitDeg = c.pitchLimitDeg, minWidthFactor = c.minWidthFactor,
        };

        public override string Validate(in SpeciesCtx c) => c.Module<Walk>() == null ? "Climb needs Walk" : null;
    }

    [Serializable]
    [Module("Fly", CreatureModule.Locomotion, Summary = "Ground -> Launch -> Air <-> Glide -> Flare -> Touchdown -> Perched (CreatureFlight, FlightModel, AirChain). Froude-scaled. twist: the twisting Chinese-dragon air chain (AirTwist).",
            Needs = "Wing parts")]
    public sealed class Fly : CreatureModule
    {
        [Param(ParamUnit.Spec, "flight (FlightSpec: speeds, lift/drag, altitude PID, stroke, banking, flare, air undulation)")]
        public FlightSpec spec = new FlightSpec();
        [Param(ParamUnit.Struct, "twisting flight (AirTwist, Chinese dragon): the body follows the head's path; 3D swimming, corkscrews, barrel rolls, loops, slow rolls that travel head to tail. All 0 = the serpent's trailing air chain")]
        public AirTwistSpec twist;

        // the flight modes (CreatureFlight) for a species that can fly (wings, a flight spec, a cruise speed)
        public override ILocomotionMode CreateMode(CreatureSim sim, in ModeCtx c)
            => sim.Species.CanFly ? new CreatureFlight(sim, c.Ground, c.UpAt) : null;

        public override string Validate(in SpeciesCtx c)
        {
            if (spec == null) return "Fly: no spec";
            if (twist.swimAmplitude < 0f || twist.corkscrewAmplitude < 0f || twist.barrelRadius < 0f || twist.loopRadius < 0f) return "Fly.twist: amplitudes must be >= 0";
            if (twist.CanCorkscrew && twist.corkscrewTurns < 0) return "Fly.twist: corkscrewTurns must be >= 0";
            for (int i = 0; i < c.Parts.Count; i++) if (c.Parts[i] is Wing) return null;
            return "Fly needs Wing parts";
        }
    }

    // ------------------------------------------------------------------ behaviour (the brain is chosen from these: BrainFactory)

    [Serializable]
    [Module("Wander", CreatureModule.Behaviour, Summary = "Safe-disc points, walk, pause, glance (WanderBrain). Containment is structural: the head is clamped to the safe radius.")]
    public sealed partial class Wander : CreatureModule
    {
        [Param(ParamUnit.Metres, "a goal counts as reached within this distance (0 = 0.4)", Example = "scolopendra 0.12")] public float arriveRadius;
        [Param(ParamUnit.Seconds, "shortest pause at a goal (with pauseMax > 0; Froude-timed)")] public float pauseMin;
        [Param(ParamUnit.Seconds, "longest pause at a goal (0 = the default 0.5..3 s, not size-scaled)", Example = "scolopendra 0.5..1.2")] public float pauseMax;

        public bool Custom => arriveRadius > 0f || pauseMax > 0f;

        public override string Validate(in SpeciesCtx c)
            => arriveRadius < 0f || pauseMin < 0f || pauseMax < 0f || (pauseMax > 0f && pauseMin > pauseMax) ? "Wander: arriveRadius, pauseMin, pauseMax >= 0, pauseMin <= pauseMax" : null;
    }

    [Serializable]
    [Module("ClimbExcursion", CreatureModule.Behaviour, Summary = "Approach a climbable obstacle, climb to a goal on its real surface, rest, descend (ClimbingBrain wrapping Wander).", Needs = "Climb")]
    public sealed partial class ClimbExcursion : CreatureModule
    {
        public override string Validate(in SpeciesCtx c) => c.Module<Climb>() == null ? "ClimbExcursion needs Climb" : null;
    }

    [Serializable]
    [Module("FlightExcursion", CreatureModule.Behaviour, Summary = "Rest, Face, Patrol, Soar, Landing on perches; predictive wall turn-back (FlightBrain).", Needs = "Fly")]
    public sealed partial class FlightExcursion : CreatureModule
    {
        public override string Validate(in SpeciesCtx c) => c.Module<Fly>() == null ? "FlightExcursion needs Fly" : null;
    }

    [Serializable]
    [Module("Ambush", CreatureModule.Behaviour, Summary = "Rest / explore / stalk / ambush / strike / hold / retreat (OctopusBrain + prey adapters; arm roles, grooming). Hold 1.5 s, no damage.",
            Needs = "SoftBody + Tentacle parts (spine bodies: T5.9)")]
    public sealed class Ambush : CreatureModule
    {
        public override string Validate(in SpeciesCtx c) => c.Soft ? null : "Ambush on a spine body needs the T5.9 adapter (not available yet)";
    }

    // ------------------------------------------------------------------ look layers

    [Serializable]
    [Module("Skin", CreatureModule.Look, Summary = "Palette dorsal/flank/belly + procedural pattern by rest arc length (CreaturePattern.hlsl); per-individual size/hue/value jitter. Layers of parts 0-2.")]
    public sealed class Skin : CreatureModule
    {
        [Param(ParamUnit.SrgbColour, "back colour")] public Color dorsal;
        [Param(ParamUnit.SrgbColour, "flank colour")] public Color flank;
        [Param(ParamUnit.SrgbColour, "belly colour")] public Color belly;
        [Param(ParamUnit.SrgbColour, "pattern colour")] public Color pattern;
        [Param(ParamUnit.Enum, "pattern")] public CreaturePattern patternType;
        [Param(ParamUnit.Fraction, "pattern repeats per metre of rest arc", Example = "monitor 9, snake 11")] public float patternScale;
        [Param(ParamUnit.Fraction, "pattern contrast", Min = 0, Max = 1)] public float patternContrast;
        [Param(ParamUnit.Fraction, "pattern edge softness", Min = 0, Max = 1)] public float patternSoftness;
        [Param(ParamUnit.Fraction, "spot size")] public float spotScale;
        [Param(ParamUnit.Fraction, "smoothness", Min = 0, Max = 1)] public float smoothness;
        [Param(ParamUnit.Fraction, "rim light strength", Min = 0, Max = 1)] public float rimStrength;
        [Param(ParamUnit.Fraction, "subsurface", Min = 0, Max = 1)] public float subsurface;
        [Param(ParamUnit.Fraction, "per-individual size jitter (+-), sizeScale = scale x (1 +- this)", Min = 0, Max = 0.2f)] public float sizeJitter;
        [Param(ParamUnit.Fraction, "per-individual hue jitter")] public float hueJitter;
        [Param(ParamUnit.Fraction, "per-individual value jitter")] public float valueJitter;

        public void WriteTo(ref CreatureLook l)
        {
            l.dorsal = dorsal; l.flank = flank; l.belly = belly; l.pattern = pattern; l.patternType = patternType; l.patternScale = patternScale;
            l.patternContrast = patternContrast; l.patternSoftness = patternSoftness; l.spotScale = spotScale; l.smoothness = smoothness;
            l.rimStrength = rimStrength; l.subsurface = subsurface; l.sizeJitter = sizeJitter; l.hueJitter = hueJitter; l.valueJitter = valueJitter;
        }

        public static Skin From(in CreatureLook l) => new Skin
        {
            dorsal = l.dorsal, flank = l.flank, belly = l.belly, pattern = l.pattern, patternType = l.patternType, patternScale = l.patternScale,
            patternContrast = l.patternContrast, patternSoftness = l.patternSoftness, spotScale = l.spotScale, smoothness = l.smoothness,
            rimStrength = l.rimStrength, subsurface = l.subsurface, sizeJitter = l.sizeJitter, hueJitter = l.hueJitter, valueJitter = l.valueJitter,
        };
    }

    [Serializable]
    [Module("Chitin", CreatureModule.Look, Summary = "Knee/segment bands (part 9) and a velvet sheen lobe: arthropod legs.")]
    public sealed class Chitin : CreatureModule
    {
        [Param(ParamUnit.SrgbColour, "band colour (SegmentDef.bandStart/End)")] public Color bandColor;
        [Param(ParamUnit.Fraction, "velvet sheen strength (0 = none)", Min = 0, Max = 1)] public float sheen;
        [Param(ParamUnit.SrgbColour, "sheen colour")] public Color sheenColor;
    }

    [Serializable]
    [Module("Membrane", CreatureModule.Look, Summary = "Translucent veined membrane (part 8, the Membrane shading model, CreatureMembrane.hlsl): wings, FinStrip fins, antennae - any part whose layer row is Membrane.")]
    public sealed class Membrane : CreatureModule
    {
        [Param(ParamUnit.SrgbColour, "membrane colour")] public Color colour;
        [Param(ParamUnit.Fraction, "how much light shines through", Min = 0, Max = 1)] public float translucency;
        [Param(ParamUnit.Fraction, "how dark the veins are", Min = 0, Max = 1)] public float veinContrast;
        [Param(ParamUnit.Fraction, "how dark the trailing rim is", Min = 0, Max = 1)] public float rim;
        [Param(ParamUnit.Fraction, "toward its root (the wrist and the body, a fin's base) the membrane takes on the body's dorsal colour, fading to its own at the edge (0 = one colour)", Min = 0, Max = 1, Example = "dragon 0.6")] public float inner;
    }

    [Serializable]
    [Module("Shimmer", CreatureModule.Look, Summary = "Iridescent scale shimmer: thin-film colour bands (colour -> colour2 with the view angle) travelling head to tail along the body's back and flanks, a faint glow in each band (Creature.shader, part 0). The SkyDragon's signature; cheap (one pow, one cos per body pixel).")]
    public sealed class Shimmer : CreatureModule
    {
        [Param(ParamUnit.SrgbColour, "colour facing the viewer")] public Color colour;
        [Param(ParamUnit.SrgbColour, "colour at grazing angles (the thin-film hue shift)")] public Color colour2;
        [Param(ParamUnit.Fraction, "strength (0 = none)", Min = 0, Max = 2)] public float strength;
        [Param(ParamUnit.Metres, "distance between bands along the body (rest arc)", Example = "dragon 0.22")] public float wavelength = 0.2f;
        [Param(ParamUnit.Seconds, "a band passes a point every this many seconds (x sqrt of the species scale)", Example = "dragon 2.2")] public float period = 2f;
        [Param(ParamUnit.Fraction, "glow of the bands (emission, also in shadow)", Min = 0, Max = 1)] public float glow;

        public override string Validate(in SpeciesCtx c)
            => strength < 0f || !(wavelength > 0f) || !(period > 0f) ? "Shimmer: strength >= 0, wavelength and period > 0" : null;
    }

    [Serializable]
    [Module("Cloak", CreatureModule.Look, Summary = "Dissolve in Forward only: the body vanishes, shadow + SSAO stay (the user's favourite bug). Per-instance, brain-driven, with hysteresis. Octopus today.",
            Needs = "SoftBody (spine bodies: Phase 4)")]
    public sealed class Cloak : CreatureModule { }

    [Serializable]
    [Module("Chromatophore", CreatureModule.Look, Summary = "Dynamic colour cells, flash and passing-cloud phase (octopus shader). Makes the look dynamic.", Needs = "SoftBody")]
    public sealed class Chromatophore : CreatureModule { }
}
