using UnityEngine;

namespace TerrainGen.Tests
{
    public static class CreatureTestSpecies
    {
        // 20-joint lizard-like species with constant clearance 0.1. Callers destroy it with Object.DestroyImmediate.
        public static CreatureSpecies Lizardish()
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            const int n = 20;
            sp.spacing = 0.06f;
            sp.joints = new SpineJoint[n];
            for (int i = 0; i < n; i++)
                sp.joints[i] = new SpineJoint { halfWidth = 0.06f, halfHeight = 0.05f, clearance = 0.1f, bendLimitDeg = 20f, rigid = i == 1 };
            sp.headCapLength = 0.05f; sp.tailTipLength = 0.04f;
            sp.legs = new[]
            {
                Leg(5, -1, new Vector3(0.20f, 0f, 0.06f), new Vector3(0.3f, 0.6f, -1f), 0),
                Leg(5, +1, new Vector3(0.20f, 0f, 0.06f), new Vector3(0.3f, 0.6f, -1f), 1),
                Leg(11, -1, new Vector3(0.20f, 0f, -0.02f), new Vector3(0.3f, 0.6f, 1f), 1),
                Leg(11, +1, new Vector3(0.20f, 0f, -0.02f), new Vector3(0.3f, 0.6f, 1f), 0),
            };
            sp.speed = 0.9f; sp.turnRateDeg = 160f; sp.accel = 3f; sp.turnSlowdown = 0.5f;
            sp.undulationDeg = 0f; sp.undulationHz = 1f;
            sp.subSteps = 2;
            sp.eyeJoint = 1; sp.eyeOffset = new Vector3(0.024f, 0.014f, 0.008f); sp.eyeRadius = 0.007f;
            return sp;
        }

        // 40-joint legless snake, tapering to a thin tail. Callers destroy it.
        public static CreatureSpecies Snake()
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            const int n = 40;
            sp.spacing = 0.05f;
            sp.joints = new SpineJoint[n];
            for (int i = 0; i < n; i++)
            {
                float f = i / (n - 1f);
                float hw = Mathf.Lerp(0.04f, 0.008f, f * f) * (i < 3 ? 0.85f + 0.05f * i : 1f);
                sp.joints[i] = new SpineJoint { halfWidth = hw, halfHeight = 0.8f * hw, clearance = 0.8f * hw, bendLimitDeg = 25f, rigid = i == 1 };
            }
            sp.headCapLength = 0.05f; sp.tailTipLength = 0.03f;
            sp.legs = new LegSpec[0];
            sp.speed = 0.6f; sp.turnRateDeg = 120f; sp.accel = 2f; sp.turnSlowdown = 0.5f;
            sp.undulationDeg = 25f; sp.undulationHz = 1.2f;
            sp.subSteps = 2;
            sp.eyeJoint = 1; sp.eyeOffset = new Vector3(0.022f, 0.012f, 0.01f); sp.eyeRadius = 0.006f;
            return sp;
        }

        // 8-joint hexapod, legs at joints 2, 4, 6 on both sides, tripod gait. Callers destroy it.
        public static CreatureSpecies Hexapod()
        {
            var sp = ScriptableObject.CreateInstance<CreatureSpecies>();
            const int n = 8;
            sp.spacing = 0.06f;
            sp.joints = new SpineJoint[n];
            for (int i = 0; i < n; i++)
                sp.joints[i] = new SpineJoint { halfWidth = 0.05f, halfHeight = 0.04f, clearance = 0.12f, bendLimitDeg = 15f, rigid = i == 1 };
            sp.headCapLength = 0.04f; sp.tailTipLength = 0.03f;
            Vector3 pole = new Vector3(0f, 1f, 0f);
            sp.legs = new[]
            {
                Leg(2, -1, new Vector3(0.22f, 0f, 0.04f), pole, 0, 0.12f, 0.14f),
                Leg(2, +1, new Vector3(0.22f, 0f, 0.04f), pole, 1, 0.12f, 0.14f),
                Leg(4, -1, new Vector3(0.22f, 0f, 0f), pole, 1, 0.12f, 0.14f),
                Leg(4, +1, new Vector3(0.22f, 0f, 0f), pole, 0, 0.12f, 0.14f),
                Leg(6, -1, new Vector3(0.22f, 0f, -0.04f), pole, 0, 0.12f, 0.14f),
                Leg(6, +1, new Vector3(0.22f, 0f, -0.04f), pole, 1, 0.12f, 0.14f),
            };
            sp.speed = 0.6f; sp.turnRateDeg = 140f; sp.accel = 3f; sp.turnSlowdown = 0.5f;
            sp.undulationDeg = 0f; sp.undulationHz = 1f;
            sp.subSteps = 2;
            sp.eyeJoint = 1; sp.eyeOffset = new Vector3(0.03f, 0.02f, 0.01f); sp.eyeRadius = 0.008f;
            return sp;
        }

        static LegSpec Leg(int joint, int side, Vector3 rest, Vector3 pole, int group, float upper = 0.11f, float lower = 0.10f) => new LegSpec
        {
            attachJoint = joint, side = side,
            shoulderOffset = new Vector3(0.05f, -0.01f, 0f), restOffset = rest,
            upperLength = upper, lowerLength = lower, upperRadius = 0.022f, lowerRadius = 0.015f,
            pole = pole, stepThreshold = 0.11f, stepDuration = 0.16f, stepLift = 0.04f, overshoot = 0.06f,
            gaitGroup = group, footLength = 0.045f, toeCount = 5,
        };
    }
}
