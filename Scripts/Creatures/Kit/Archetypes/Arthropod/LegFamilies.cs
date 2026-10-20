// Creature kit, arthropod archetype: the leg-family templates. One anatomical template per family - each segment's
// share of the leg, its radii as shares of the leg length (at bulk 1), its rest bend and hinge range, its share of
// the reach synergy, knuckle and band - scaled to a leg length per pair. The arachnid template is the shipped
// tarantula's leg IV (legs_impl/gen_species.py TA_TEMPLATE), number for number; the decapod template is a brachyuran
// walking leg with the arachnid's distributed arch (one rising pair, three falling hinges) on decapod proportions.
// Default leg formulae (relative lengths, front to back) per family. Pure data; compose time only.
using UnityEngine;

namespace CreatureKit
{
    public enum LegFamily { Arachnid = 0, Decapod = 1 }

    public static class LegFamilies
    {
        // One template segment (see the file header): len share of the leg, r0 / r1 radii / leg length, rest bend,
        // hinge range below / above the rest, synergy weight, knuckle, band.
        public struct Seg { public float len, r0, r1, rest, below, above, flex, knuckle, b0, b1; }

        static Seg S(float len, float r0, float r1, float rest, float lo, float hi, float flex, float knuckle = 0f, float b0 = 0f, float b1 = 0f)
            => new Seg { len = len, r0 = r0, r1 = r1, rest = rest, below = rest - lo, above = hi - rest, flex = flex, knuckle = knuckle, b0 = b0, b1 = b1 };

        // Tarantula leg IV (total 0.43 m): coxa, trochanter, femur, patella, tibia, metatarsus, tarsus. Lengths and
        // radii / 0.43; bends as authored.
        static readonly Seg[] kArachnid =
        {
            S(0.03f / 0.43f, 0.022f / 0.43f, 0.02f / 0.43f, 5, -30, 40, 0f),
            S(0.02f / 0.43f, 0.02f / 0.43f, 0.018f / 0.43f, 20, -10, 60, 0.1f, 1.1f),
            S(0.11f / 0.43f, 0.017f / 0.43f, 0.016f / 0.43f, 20, -20, 60, 0.3f, 1.15f, 0.9f, 1f),
            S(0.05f / 0.43f, 0.016f / 0.43f, 0.015f / 0.43f, -55, -120, 10, -1f, 1.1f, 0f, 1f),
            S(0.085f / 0.43f, 0.014f / 0.43f, 0.013f / 0.43f, -30, -80, 20, -0.6f, 1.1f, 0f, 0.12f),
            S(0.09f / 0.43f, 0.012f / 0.43f, 0.0105f / 0.43f, -15, -60, 20, -0.4f, 1.1f),
            S(0.045f / 0.43f, 0.009f / 0.43f, 0.007f / 0.43f, 30, -40, 90, 0f),
        };

        // Decapod pereiopod: coxa, basis-ischium, merus, carpus, propodus, dactyl. A long stout merus rising to the knee,
        // a short carpus, the propodus and a pointed dactyl set steeply on its tip. Radii: a crab's legs are stout
        // (about 2x a tarantula's for the same length).
        static readonly Seg[] kDecapod =
        {
            S(0.07f, 0.075f, 0.068f, 0, -30, 35, 0f),
            S(0.08f, 0.068f, 0.062f, 22, -10, 60, 0.1f, 1.08f),
            S(0.34f, 0.062f, 0.055f, 18, -20, 60, 0.3f, 1.1f),
            S(0.13f, 0.052f, 0.048f, -62, -125, 5, -1f, 1.08f),
            S(0.22f, 0.046f, 0.036f, -32, -85, 20, -0.6f, 1.05f),
            S(0.16f, 0.032f, 0.006f, 30, -40, 90, 0f, 0f, 0.55f, 1f),
        };

        public static Seg[] Template(LegFamily f) => f == LegFamily.Decapod ? kDecapod : kArachnid;

        // The tarsus (dactyl) angle to the ground the family stands on.
        public static float ContactDeg(LegFamily f) => f == LegFamily.Decapod ? 58f : 25f;

        // Default leg formulae, front to back: the tarantula's IV > I > II > III; a brachyuran's II-III longest, V shortest.
        static readonly float[] kArachnidFormula = { 0.97f, 0.88f, 0.84f, 1f };
        static readonly float[] kDecapodFormula = { 0.92f, 1f, 0.97f, 0.84f };

        // Pair p of n's length relative to the longest: the given formula (one value per pair), else the family's
        // (for 4 pairs) or a smooth spread of it.
        public static float Formula(LegFamily f, float[] given, int p, int n)
        {
            float[] v = given != null && given.Length == n ? given : null;
            if (v == null)
            {
                float[] d = f == LegFamily.Decapod ? kDecapodFormula : kArachnidFormula;
                if (n == d.Length) v = d;
                else { float u = n == 1 ? 0f : p / (n - 1f); return f == LegFamily.Decapod ? 1f - 0.12f * u * u : 0.9f + 0.1f * Mathf.Abs(2f * u - 1f); }
            }
            float mx = 0f; foreach (float x in v) mx = Mathf.Max(mx, x);
            return v[p] / Mathf.Max(1e-6f, mx);
        }

        // The family's template as a leg of total length L: radii x bulk, every rest bend but the tarsus' x posture (its
        // hinge range moves with it, so the rest bend always lies inside the range).
        // How much of the leg's flattening each decapod segment takes (a crab's broad merus and propodus, a rounder
        // coxa); the arachnid's legs stay round.
        static readonly float[] kDecapodFlat = { 0f, 0.4f, 1f, 0.8f, 1f, 0.5f };

        public static SegmentDef[] Segments(LegFamily f, float L, float bulk, float posture, float flatten = 0f)
        {
            Seg[] fam = Template(f);
            var s = new SegmentDef[fam.Length];
            for (int i = 0; i < fam.Length; i++)
            {
                Seg g = fam[i];
                float rest = i == fam.Length - 1 ? g.rest : g.rest * posture;
                s[i] = new SegmentDef
                {
                    length = g.len * L, radiusStart = g.r0 * L * bulk, radiusEnd = g.r1 * L * bulk,
                    restBendDeg = rest, minBendDeg = rest - g.below, maxBendDeg = rest + g.above, flexWeight = g.flex,
                    knuckle = g.knuckle, bandStart = g.b0, bandEnd = g.b1,
                    flatten = f == LegFamily.Decapod ? flatten * kDecapodFlat[i] : 0f,
                };
            }
            return s;
        }
    }
}
