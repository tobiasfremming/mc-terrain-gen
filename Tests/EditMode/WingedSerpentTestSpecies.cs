using UnityEngine;

namespace TerrainGen.Tests
{
    // The winged serpent (design 2026-10-17-winged-serpent-design.md, section 2) as a test factory until
    // Phase B adds wings/flight to CreatureSpecies and the WingedSerpent.asset can hold them.
    // 33 joints at 0.08 m (2.9 m at scale 1), slim snake build, girdle (wing arms) at joint 6 (a 4-segment neck
    // behind the head joint, 0.40 m; was 3 segments until the 2026-10-18 polish), small hind
    // legs at joint 15, long tail. Legs 0/1 = wing arms (wing-walk on the ground), 2/3 = hind legs.
    public static class WingedSerpentTestSpecies
    {
        public const int Girdle = 6, Hips = 15, Joints = 33;

        public sealed class Serpent
        {
            public CreatureSpecies species;
            public WingSpec[] wings;
            public FlightSpec flight;
        }

        // Callers destroy .species with Object.DestroyImmediate.
        public static Serpent Create()
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            sp.name = "WingedSerpent";
            sp.spacing = 0.08f;
            sp.joints = new SpineJoint[Joints];
            for (int i = 0; i < Joints; i++)
            {
                float hw = HalfWidth(i);
                sp.joints[i] = new SpineJoint
                {
                    halfWidth = hw, halfHeight = 0.85f * hw, clearance = Mathf.Max(0.1f, 1.2f * hw),
                    bendLimitDeg = i < Girdle ? 18f : 14f, rigid = i == 1,
                };
            }
            sp.headCapLength = 0.09f; sp.tailTipLength = 0.06f;
            sp.legs = new[]
            {
                Arm(-1, 0), Arm(+1, 1),
                Hind(-1, 1), Hind(+1, 0),
            };
            sp.speed = 0.8f; sp.turnRateDeg = 120f; sp.accel = 2.5f; sp.turnSlowdown = 0.5f;
            sp.undulationDeg = 12f; sp.undulationHz = 1f;
            sp.subSteps = 2;
            sp.eyeJoint = 1; sp.eyeOffset = new Vector3(0.032f, 0.022f, 0.02f); sp.eyeRadius = 0.011f;
            sp.ringSegments = 16; sp.ringsPerSegment = 3;
            sp.look = new CreatureLook
            {
                dorsal = new Color(0.30f, 0.30f, 0.17f), flank = new Color(0.45f, 0.38f, 0.22f),
                belly = new Color(0.82f, 0.76f, 0.58f), pattern = new Color(0.20f, 0.17f, 0.10f),
                patternType = CreaturePattern.Bands, patternScale = 3f, patternContrast = 0.6f, patternSoftness = 0.4f,
                smoothness = 0.35f, rimStrength = 0.3f, subsurface = 0.3f,
                sizeJitter = 0.05f, hueJitter = 0.02f, valueJitter = 0.06f,
                // dusky olive-bronze back with bands, cream belly, a warm ochre membrane that glows back-lit
                membrane = new Color(0.78f, 0.52f, 0.26f), membraneTranslucency = 0.8f, veinContrast = 0.35f, membraneRim = 0.45f,
            };

            // the membrane attaches along the flank from the girdle to 8 joints behind it
            var wings = new[] { Wing(0), Wing(1) };
            // deep downstroke (user feedback 2026-10-18, twice): comfortDown 0.55 -> 0.95 -> 1.03 (the band bottom
            // 87 deg below the glide pose, the wrist nearly straight down: the arm's 1.0 m reach is the limit, 1.04
            // reaches it and folds the elbow skin), and every beat uses most of it (amplitudeMin 0.3 -> 0.75): level
            // flight sweeps the wrist ~0.96 m down (70 deg; 0.54 m, 33 deg before). The deeper beat lifts more: a
            // lower grip (kGrip 0.733 -> 0.6 -> 0.5), a faster slowest stroke (strokeRateMin 2 -> 3) and more
            // integral authority (climbIntegralLimit 0.3 -> 0.6)
            // hold the altitude at fewer, bigger beats (~1.3-1.7 Hz, was 2.2) with a bigger body bob (0.33 m p-p);
            // the head is held stiller (headStabilise 0.15 -> 0.35 s) and the roll authority restored (rollGain
            // 0.7 -> 1.0: the lower grip rolled to 30 deg in 1.4 s instead of 1.1, and the wall turn came late)
            sp.wings = wings;
            sp.flight = new FlightSpec { kGrip = 0.5f, amplitudeMin = 0.75f, strokeRateMin = 3f, climbIntegralLimit = 0.6f, headStabilise = 0.35f, rollGain = 1f };
            return new Serpent { species = sp, wings = wings, flight = sp.flight };
        }

        // monitor-like head, thin neck, widest at the girdle, slim trunk, small hips, long tapering tail
        static float HalfWidth(int i)
        {
            if (i == 0) return 0.042f;
            if (i == 1) return 0.046f;
            if (i < Girdle) return Mathf.Lerp(0.034f, 0.05f, (i - 2f) / (Girdle - 2f));
            if (i <= Hips) return Mathf.Lerp(0.07f, 0.055f, (i - Girdle) / (float)(Hips - Girdle));
            float t = (i - Hips) / (float)(Joints - 1 - Hips);
            return Mathf.Lerp(0.055f, 0.009f, Mathf.Pow(t, 0.8f));
        }

        static WingSpec Wing(int leg) => new WingSpec { leg = leg, membraneFrontJoint = Girdle, membraneRearJoint = Girdle + 8, comfortDown = 1.03f,
            // on the ground (user feedback 2026-10-18): folded fingers swept back and 30 deg up, the wrist on a
            // longer thumb claw (0.07 -> 0.09), the membrane tucked up the flank and its trailing rib near the flank
            // rib (0.5 -> 0.8: the narrow zone between them no longer sags to the ground when folded)
            groundLiftDeg = 30f, groundWristLift = 0.1f, groundFlankRise = 0.8f, thumbLength = 0.09f, trailingRib = 0.8f };

        static LegSpec Arm(int side, int group) => new LegSpec
        {
            attachJoint = Girdle, side = side,
            shoulderOffset = new Vector3(0.06f, 0.02f, 0f), restOffset = new Vector3(0.85f, 0f, 0.25f),
            upperLength = 0.5f, lowerLength = 0.6f, upperRadius = 0.034f, lowerRadius = 0.024f,
            pole = new Vector3(0f, 0.25f, -1f),
            stepThreshold = 0.16f, stepDuration = 0.2f, stepLift = 0.06f, overshoot = 0.06f,
            gaitGroup = group, footLength = 0.06f, toeCount = 2,
        };

        static LegSpec Hind(int side, int group) => new LegSpec
        {
            attachJoint = Hips, side = side,
            shoulderOffset = new Vector3(0.04f, -0.01f, 0f), restOffset = new Vector3(0.15f, 0f, -0.02f),
            upperLength = 0.1f, lowerLength = 0.095f, upperRadius = 0.022f, lowerRadius = 0.015f,
            pole = new Vector3(0.3f, 0.6f, 1f),
            // the arms' cadence (stepDuration 0.2) and an early threshold: the hind legs step with their gait
            // group's arm instead of lagging to full stretch (was 0.09 / 0.16: stop-go)
            stepThreshold = 0.05f, stepDuration = 0.2f, stepLift = 0.035f, overshoot = 0.04f,
            gaitGroup = group, footLength = 0.045f, toeCount = 4,
        };
    }
}
