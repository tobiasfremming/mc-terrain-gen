// Creature kit service: gait numbers from the leg's length and Froude time (the legacy spider generator's ta_common,
// legs_impl/gen_species.py). A leg's step threshold and overshoot are fractions of its length; a step time and a
// cruise speed are Froude numbers of the reference (longest) leg:  t = F sqrt(L / g),  v = F sqrt(g L). Two animals
// of one build then step alike at any size. Pure; compose time only.
using UnityEngine;

namespace CreatureKit
{
    public static class GaitDefaults
    {
        public const float G = 9.81f;
        public const float Threshold = 0.12f;   // x leg length: a planted foot this far from its rest point steps
        public const float Overshoot = 0.07f;   // x leg length: a step lands this far past the rest point

        // Step time (s) of a step taking `stepFroude` Froude units on a leg of length lref (m).
        public static float StepDuration(float stepFroude, float lref) => stepFroude * Mathf.Sqrt(Mathf.Max(1e-6f, lref) / G);

        // Cruise speed (m/s) of `speedFroude` Froude units on a leg of length lref (m).
        public static float Speed(float speedFroude, float lref) => speedFroude * Mathf.Sqrt(G * Mathf.Max(1e-6f, lref));
    }
}
