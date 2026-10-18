using UnityEngine;

namespace TerrainGen.Tests
{
    // The winged serpent (design 2026-10-17-winged-serpent-design.md, section 2) as a test factory until
    // Phase B adds wings/flight to CreatureSpecies and the WingedSerpent.asset can hold them.
    // 32 joints at 0.08 m (2.8 m at scale 1), slim snake build, girdle (wing arms) at joint 5, small hind
    // legs at joint 14, long tail. Legs 0/1 = wing arms (wing-walk on the ground), 2/3 = hind legs.
    public static class WingedSerpentTestSpecies
    {
        public const int Girdle = 5, Hips = 14, Joints = 32;

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

            var wings = new[] { new WingSpec { leg = 0 }, new WingSpec { leg = 1 } };
            sp.wings = wings; sp.flight = new FlightSpec();
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
            stepThreshold = 0.09f, stepDuration = 0.16f, stepLift = 0.035f, overshoot = 0.04f,
            gaitGroup = group, footLength = 0.045f, toeCount = 4,
        };
    }
}
