// Creature kit, arthropod archetype: the crab carapace outlines. Each outline is a table of loft keys along the shell
// (t = 0 the back, 1 the front): half width / the widest half width, half height and rise / the dome height (the plan's
// bodyBulk x the widest half width), the superellipse exponents of the top and the bottom, the belly (a crab's flat sternum: the lower half's depth / the half height). The plan scales a table to
// its carapace (bodyLength x bodyWidth x bodyBulk) and attaches the legs on the margin it describes, between the legs'
// span of t (front pair .. hind pair); the chelipeds at tClaw, the eye stalks at tEye.
//   Oval: a xanthid / stone crab - widest just ahead of the middle, the front a blunt arc (the shipped RockCrab's keys).
//   Pear: a lithodid / king crab - widest behind the middle, narrowing to the front (the shipped TentacleCrab's keys).
using UnityEngine;

namespace CreatureKit
{
    public enum CrabOutline { Oval = 0, Pear = 1 }

    public static class CrabShell
    {
        // t, half width, half height, rise, squareTop, squareBottom, belly (the flat underside: lower half depth / half height)
        static readonly float[,] kOval =
        {
            { 0f, 0.15f, 0.18f, -0.12f, 2f, 3f, 0.4f }, { 0.02f, 0.39f, 0.44f, -0.12f, 2f, 3f, 0.4f }, { 0.12f, 0.63f, 0.7f, -0.05f, 2f, 3f, 0.4f },
            { 0.4f, 0.93f, 0.94f, 0f, 2f, 3f, 0.4f }, { 0.62f, 1f, 1f, 0.02f, 2f, 3f, 0.4f }, { 0.82f, 0.88f, 0.87f, 0f, 2.1f, 3f, 0.4f },
            { 0.94f, 0.61f, 0.66f, -0.05f, 2.3f, 3f, 0.4f }, { 0.985f, 0.37f, 0.42f, -0.1f, 2.4f, 3f, 0.4f }, { 1f, 0.12f, 0.16f, -0.12f, 2.2f, 3f, 0.4f },
        };
        static readonly float[,] kPear =
        {
            { 0f, 0.19f, 0.26f, -0.05f, 2f, 3f, 0.5f }, { 0.06f, 0.68f, 0.67f, 0f, 2f, 3f, 0.5f }, { 0.26f, 1f, 1f, 0.05f, 1.9f, 3f, 0.5f },
            { 0.46f, 0.96f, 0.97f, 0.05f, 1.9f, 3f, 0.5f }, { 0.7f, 0.74f, 0.79f, 0f, 2f, 3f, 0.5f }, { 0.88f, 0.45f, 0.565f, -0.077f, 2.1f, 3f, 0.5f },
            { 1f, 0.13f, 0.2f, -0.128f, 2f, 3f, 0.5f },
        };

        static float[,] Table(CrabOutline o) => o == CrabOutline.Pear ? kPear : kOval;

        public static int Keys(CrabOutline o) => Table(o).GetLength(0);
        public static float Key(CrabOutline o, int k, int ch) => Table(o)[k, ch];

        // The table's channel ch at t (smoothstep between keys).
        public static float At(CrabOutline o, float t, int ch)
        {
            float[,] s = Table(o);
            int n = s.GetLength(0);
            if (t <= 0f) return s[0, ch];
            for (int i = 1; i < n; i++)
                if (t <= s[i, 0])
                {
                    float u = (t - s[i - 1, 0]) / (s[i, 0] - s[i - 1, 0]);
                    u = u * u * (3f - 2f * u);
                    return Mathf.Lerp(s[i - 1, ch], s[i, ch], u);
                }
            return s[n - 1, ch];
        }

        // Where the walking legs attach along the shell (front pair, hind pair), the chelipeds and the eye stalks.
        public static Vector2 LegSpan(CrabOutline o) => o == CrabOutline.Pear ? new Vector2(0.56f, 0.2f) : new Vector2(0.62f, 0.2f);
        public static float ClawT(CrabOutline o) => o == CrabOutline.Pear ? 0.86f : 0.9f;
        public static float EyeT(CrabOutline o) => o == CrabOutline.Pear ? 0.95f : 0.96f;
    }
}
