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

        static LegSpec Leg(int joint, int side, Vector3 rest, Vector3 pole, int group) => new LegSpec
        {
            attachJoint = joint, side = side,
            shoulderOffset = new Vector3(0.05f, -0.01f, 0f), restOffset = rest,
            upperLength = 0.11f, lowerLength = 0.10f, upperRadius = 0.022f, lowerRadius = 0.015f,
            pole = pole, stepThreshold = 0.11f, stepDuration = 0.16f, stepLift = 0.04f, overshoot = 0.06f,
            gaitGroup = group, footLength = 0.045f, toeCount = 5,
        };
    }
}
