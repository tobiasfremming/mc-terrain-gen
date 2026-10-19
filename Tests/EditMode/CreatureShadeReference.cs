using System;
using UnityEngine;
using CreatureKit;

namespace TerrainGen.Tests
{
    // Creature kit T4.1: Creature.shader's fragment SURFACE (everything before the lighting call) ported to C#, twice:
    //   Legacy   the shader before the part-layer table: branches on the part id (CREATURE_PART_FOOT, _BAND, ...)
    //            and the part-id helpers (Tests/EditMode/Shaders/CreaturePatternLegacy.hlsl, frozen)
    //   Layered  the shader now: the part id picks a _PartLayers row (Kit/PartLayers.cs encoding), the row's model
    //            and flags pick the branches, the pattern / grain helpers take a mode instead of the id
    // The lighting after the surface (CreatureShade, membrane shading, fog) and the per-individual jitter take no
    // part id, so equal surfaces mean equal pixels. Both ports share the noise; what they differ in is exactly the
    // dispatch that T4.1 changed. CreaturePartLayerTests requires them bit-equal for every legacy id.
    // HLSL -> C#: float math in the same order (MathF), uint hashing exact, fwidth given as an input.
    public static class CreatureShadeReference
    {
        public struct Palette
        {
            public Vector3 dorsal, flank, belly, pattern, band, membrane;
            public float patternType; public Vector4 patternParams, surfaceParams, bodyParams;
            public float sheenA, shimmerA;
            // material constants (Creature.mat defaults)
            public float gradientWarp, mottle, scaleSize, scaleColumns, scaleTint, scaleGroove, scaleBump, eyeSmoothness;
        }

        public struct Fragment
        {
            public Vector2 uv0, uv1;     // uv1.x = part id
            public float radius, seed;
            public Vector2 fwUv0;        // fwidth(uv0) of the fragment
        }

        public struct Surface
        {
            public bool membrane;        // took the membrane branch (its own shading, no part dependence after)
            public Vector3 albedo;       // before the per-individual hue / value jitter
            public bool bumped; public float bumpHeight;
            public float smoothness, rim, subsurface, dorsal;
            public bool sheen, shimmer;  // the sheen / shimmer lobes run for this fragment

            public bool BitEquals(in Surface o) => membrane == o.membrane && (membrane || (Eq(albedo, o.albedo) && bumped == o.bumped
                && bumpHeight.Equals(o.bumpHeight) && smoothness.Equals(o.smoothness) && rim.Equals(o.rim) && subsurface.Equals(o.subsurface)
                && dorsal.Equals(o.dorsal) && sheen == o.sheen && shimmer == o.shimmer));
            static bool Eq(Vector3 a, Vector3 b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);
            public override string ToString() => membrane ? "membrane" :
                $"albedo ({albedo.x:R}, {albedo.y:R}, {albedo.z:R}) bump {bumped} {bumpHeight:R} smooth {smoothness:R} rim {rim:R} sss {subsurface:R} dorsal {dorsal:R} sheen {sheen} shimmer {shimmer}";
        }

        // ------------------------------------------------------------ the old fragment (part ids)

        const int kBody = 0, kLeg = 1, kFoot = 2, kEye = 3, kMembrane = 8, kBand = 9;

        public static Surface Legacy(in Palette p, in Fragment f)
        {
            int part = (int)(f.uv1.x + 0.5f);
            bool eye = part == kEye;
            if (part == kMembrane) return new Surface { membrane = true };
            Vector3 P = SurfacePoint(f.uv0, f.uv1, f.radius, f.seed);
            Vector3 albedo = BaseColour(p, f, P);
            Vector2 pat = PatternLegacy(f.uv0, f.uv1, f.seed, p.patternType, p.patternParams, f.radius, p.bodyParams);
            albedo = Patterned(p, albedo, pat);
            if (part == kFoot) albedo = Lerp(albedo, p.pattern, 0.3f);
            if (part == kBand) albedo = Lerp(albedo, p.band, 0.9f);
            Vector3 g = ScaleGrainLegacy(f.uv0, f.uv1, p.scaleSize, p.scaleColumns, f.fwUv0);
            float vis = eye ? 0f : g.z;
            Surface s = Grained(p, f, albedo, g, vis);
            if (eye) Eye(p, ref s);
            s.sheen = p.sheenA > 0f && !eye;
            s.shimmer = p.shimmerA > 0f && !eye;
            return s;
        }

        // ------------------------------------------------------------ the layered fragment (the table)

        public static Surface Layered(in Palette p, Vector4[] table, in Fragment f)
        {
            int part = (int)(f.uv1.x + 0.5f);
            int row = Math.Min(Math.Max(part, 0), PartLayers.MaxParts - 1) * 2;
            Vector4 a = table[row], b = table[row + 1];
            int model = (int)(a.x + 0.5f);
            uint flags = (uint)(a.y + 0.5f);
            float tintAmount = a.z, smoothnessScale = a.w;
            int src = (int)(b.w + 0.5f);
            Vector3 tint = src == 1 ? p.pattern : src == 2 ? p.band : src == 3 ? p.dorsal : src == 4 ? p.belly : src == 5 ? p.membrane : new Vector3(b.x, b.y, b.z);
            bool Has(LayerFlags bit) => (flags & (uint)bit) != 0u;

            bool eye = model == (int)ShadingModel.Eye;
            if (model == (int)ShadingModel.Membrane) return new Surface { membrane = true };
            Vector3 P = SurfacePoint(f.uv0, f.uv1, f.radius, f.seed);
            Vector3 albedo = BaseColour(p, f, P);
            int mode = eye || Has(LayerFlags.NoPattern) ? 0 : Has(LayerFlags.LimbPattern) ? 2 : 1;
            Vector2 pat = PatternMode(f.uv0, f.uv1, f.seed, p.patternType, p.patternParams, f.radius, p.bodyParams, mode);
            albedo = Patterned(p, albedo, pat);
            if (tintAmount > 0f) albedo = Lerp(albedo, tint, tintAmount);
            Vector3 g = ScaleGrainFine(f.uv0, f.uv1, p.scaleSize, p.scaleColumns, f.fwUv0, Has(LayerFlags.FineGrain));
            bool noGrain = eye || model == (int)ShadingModel.Chitin || model == (int)ShadingModel.Claw || Has(LayerFlags.NoGrain);
            float vis = noGrain ? 0f : g.z;
            Surface s = Grained(p, f, albedo, g, vis);
            if (eye) Eye(p, ref s);
            else if (model == (int)ShadingModel.Chitin) { s.smoothness = Saturate(p.surfaceParams.x * smoothnessScale); s.subsurface = p.surfaceParams.z * 0.3f; }
            else if (model == (int)ShadingModel.Claw) { s.smoothness = Saturate(p.surfaceParams.x * smoothnessScale); s.subsurface = 0f; s.dorsal = 1f; }
            s.sheen = p.sheenA > 0f && Has(LayerFlags.Sheen);
            s.shimmer = p.shimmerA > 0f && Has(LayerFlags.Shimmer);
            return s;
        }

        // ------------------------------------------------------------ shared steps (no part id)

        static Vector3 BaseColour(in Palette p, in Fragment f, Vector3 P)
        {
            float dorsal = f.uv1.y + p.gradientWarp * (ValueNoise(P * 9f, 0x7FEB352Du) - 0.5f) * 2f;
            Vector3 b = Lerp(p.belly, p.flank, Smoothstep(-0.75f, -0.15f, dorsal));
            b = Lerp(b, p.dorsal, Smoothstep(0.05f, 0.7f, dorsal));
            return b * (1f + p.mottle * (Fbm(P * 22f, 0x846CA68Bu) - 0.5f) * 2f);
        }

        static Vector3 Patterned(in Palette p, Vector3 b, Vector2 pat)
        {
            Vector3 pale = Lerp(p.dorsal, p.belly, 0.65f) * 1.08f;
            Vector3 albedo = Lerp(b, p.pattern, pat.x);
            return Lerp(albedo, pale, pat.y);
        }

        static Surface Grained(in Palette p, in Fragment f, Vector3 albedo, Vector3 g, float vis)
        {
            albedo *= 1f + p.scaleTint * g.x * vis;
            albedo *= 1f - p.scaleGroove * (1f - g.y) * vis;
            return new Surface
            {
                albedo = new Vector3(Saturate(albedo.x), Saturate(albedo.y), Saturate(albedo.z)),
                bumped = true, bumpHeight = g.y * p.scaleBump * vis,
                smoothness = p.surfaceParams.x * Lerp(0.7f, 1f, Lerp(1f, g.y, vis)),
                rim = p.surfaceParams.y, subsurface = p.surfaceParams.z, dorsal = f.uv1.y,
            };
        }

        static void Eye(in Palette p, ref Surface s)
        {
            s.albedo = new Vector3(0.012f, 0.011f, 0.010f);
            s.bumped = false; s.bumpHeight = 0f;
            s.smoothness = p.eyeSmoothness; s.subsurface = 0f; s.dorsal = 1f;
        }

        // ------------------------------------------------------------ CreaturePattern.hlsl

        static uint[] Pcg3(uint x, uint y, uint z)
        {
            x = x * 1664525u + 1013904223u; y = y * 1664525u + 1013904223u; z = z * 1664525u + 1013904223u;
            x += y * z; y += z * x; z += x * y;
            x ^= x >> 16; y ^= y >> 16; z ^= z >> 16;
            x += y * z; y += z * x; z += x * y;
            return new[] { x, y, z };
        }

        static uint AsUint(int i) => unchecked((uint)i);
        static uint AsUint(float f) => BitConverter.ToUInt32(BitConverter.GetBytes(f), 0);

        static float Hash01(int cx, int cy, int cz, uint salt) => (Pcg3(AsUint(cx) ^ salt, AsUint(cy) ^ salt, AsUint(cz) ^ salt)[0] >> 8) * (1f / 16777216f);

        public static float ValueNoise(Vector3 p, uint salt)
        {
            float ix = MathF.Floor(p.x), iy = MathF.Floor(p.y), iz = MathF.Floor(p.z);
            float fx = p.x - ix, fy = p.y - iy, fz = p.z - iz;
            float ux = fx * fx * (3f - 2f * fx), uy = fy * fy * (3f - 2f * fy), uz = fz * fz * (3f - 2f * fz);
            int cx = (int)ix, cy = (int)iy, cz = (int)iz;
            float n000 = Hash01(cx, cy, cz, salt), n100 = Hash01(cx + 1, cy, cz, salt);
            float n010 = Hash01(cx, cy + 1, cz, salt), n110 = Hash01(cx + 1, cy + 1, cz, salt);
            float n001 = Hash01(cx, cy, cz + 1, salt), n101 = Hash01(cx + 1, cy, cz + 1, salt);
            float n011 = Hash01(cx, cy + 1, cz + 1, salt), n111 = Hash01(cx + 1, cy + 1, cz + 1, salt);
            float x00 = Lerp(n000, n100, ux), x10 = Lerp(n010, n110, ux);
            float x01 = Lerp(n001, n101, ux), x11 = Lerp(n011, n111, ux);
            return Lerp(Lerp(x00, x10, uy), Lerp(x01, x11, uy), uz);
        }

        public static float Fbm(Vector3 p, uint salt)
        {
            float sum = 0f, amp = 0.65f, norm = 0f;
            for (int o = 0; o < 2; o++)
            {
                sum += amp * ValueNoise(p, salt + (uint)o * 0x9E3779B9u);
                norm += amp;
                p = p * 2.31f + new Vector3(17.3f, -9.1f, 5.7f);
                amp *= 0.5f;
            }
            return sum / norm;
        }

        static Vector3 SurfacePoint(Vector2 uv0, Vector2 uv1, float radius, float seed)
        {
            uint[] h = Pcg3(AsUint(seed), 0x68E31DA4u, 0xB5297A4Du);
            Vector3 offset = new Vector3(h[0] & 0xFFFFu, h[1] & 0xFFFFu, h[2] & 0xFFFFu) * (61f / 65536f);
            float phi = uv0.y * 6.2831853f;
            float part = MathF.Floor(uv1.x + 0.5f);
            return new Vector3(uv0.x + part * 7.31f, radius * MathF.Cos(phi), radius * MathF.Sin(phi)) + offset;
        }

        // the frozen part-id form (CreaturePatternLegacy.hlsl)
        static Vector2 PatternLegacy(Vector2 uv0, Vector2 uv1, float seed, float patternType, Vector4 prm, float radius, Vector4 body)
        {
            int type = (int)(patternType + 0.5f);
            int part = (int)(uv1.x + 0.5f);
            if (type == 0 || part == kEye || part == kFoot) return Vector2.zero;
            return PatternBody(uv0, uv1, seed, type, prm, radius, body, part == kLeg);
        }

        // the mode form (CreaturePattern.hlsl CreaturePatternMode)
        static Vector2 PatternMode(Vector2 uv0, Vector2 uv1, float seed, float patternType, Vector4 prm, float radius, Vector4 body, int mode)
        {
            int type = (int)(patternType + 0.5f);
            if (type == 0 || mode == 0) return Vector2.zero;
            return PatternBody(uv0, uv1, seed, type, prm, radius, body, mode == 2);
        }

        static Vector2 PatternBody(Vector2 uv0, Vector2 uv1, float seed, int type, Vector4 prm, float radius, Vector4 body, bool limb)
        {
            float s = uv0.x, dorsal = uv1.y;
            float scale = MathF.Max(prm.x, 0.01f);
            float contrast = Saturate(prm.y);
            float soft = Clamp(prm.z, 0.02f, 1f);
            float spotFreq = scale / Clamp(prm.w, 0.02f, 4f);
            Vector3 P = SurfacePoint(uv0, uv1, radius, seed);
            float spotN = 0.8f * ValueNoise(P * spotFreq, 0xA511E9B3u) + 0.2f * ValueNoise(P * (spotFreq * 2.7f), 0x63D83595u);
            float spots = Smoothstep(0.64f - 0.12f * soft, 0.64f + 0.12f * soft, spotN);
            if (limb)
            {
                float mottle = Smoothstep(0.52f, 0.78f, Fbm(P * (scale * 1.6f), 0x1B873593u));
                float paleL = type == 2 ? 0f : (type == 4 ? 0.7f * spots : 0f);
                float darkL = type == 2 ? spots : 0.55f * mottle;
                return new Vector2(darkL, paleL) * contrast;
            }
            float len = MathF.Max(body.x, 1e-3f);
            float tailK = Saturate((s - body.z) / MathF.Max(len - body.z, 1e-3f));
            float headFade = Smoothstep(0.04f * len, 0.14f * len, s);
            float warp = Fbm(P * (scale * 0.8f), 0xCC9E2D51u) - 0.5f;
            float edgeN = ValueNoise(P * (scale * 3f), 0xE6546B64u) - 0.5f;
            float dark = 0f, pale = 0f;
            if (type == 1 || type == 4)
            {
                float b = P.x * scale + 0.9f * warp - 0.25f * (1f - dorsal);
                float d = MathF.Abs(Frac(b) - 0.5f) * 2f;
                d += 0.35f * edgeN;
                float band = 1f - Smoothstep(0.45f - 0.5f * soft, 0.45f + 0.5f * soft, d);
                float strength = Lerp(0.7f, 1f, Smoothstep(0f, 0.5f, tailK)) * Lerp(0.2f, 1f, headFade);
                float bellyEdge = Lerp(-0.3f, -1.3f, Smoothstep(0.2f, 0.75f, tailK));
                float side = Smoothstep(bellyEdge - 0.25f, bellyEdge + 0.25f, dorsal);
                dark = band * strength * side;
                if (type == 4)
                {
                    float sp = spots * side * Lerp(0.4f, 1f, band) * Lerp(0.5f, 1f, headFade);
                    pale = sp;
                    dark *= 1f - sp;
                }
            }
            else if (type == 2) dark = spots * Smoothstep(-0.65f, -0.2f, dorsal);
            else if (type == 3)
            {
                float count = MathF.Max(2f, MathF.Floor(scale * 0.5f + 0.5f));
                float b = (uv0.y - 0.5f) * count + 0.3f * warp;
                float d = MathF.Abs(b - MathF.Floor(b + 0.5f)) * 2f;
                d += 0.25f * edgeN;
                float stripe = 1f - Smoothstep(0.4f - 0.5f * soft, 0.4f + 0.5f * soft, d);
                dark = stripe * Smoothstep(-0.7f, -0.3f, dorsal);
            }
            return new Vector2(dark, pale) * contrast;
        }

        static Vector3 ScaleGrainLegacy(Vector2 uv0, Vector2 uv1, float rowSpacing, float columns, Vector2 fw)
        {
            int part = (int)(uv1.x + 0.5f);
            return ScaleGrainFine(uv0, uv1, rowSpacing, columns, fw, part == kLeg || part == kFoot);
        }

        static Vector3 ScaleGrainFine(Vector2 uv0, Vector2 uv1, float rowSpacing, float columns, Vector2 fw, bool fine)
        {
            int part = (int)(uv1.x + 0.5f);
            if (fine) { rowSpacing *= 0.6f; columns *= 0.4f; }
            columns = MathF.Max(MathF.Floor(columns + 0.5f), 4f);
            float gx = uv0.x / MathF.Max(rowSpacing, 1e-4f), gy = uv0.y * columns;
            float wx = fw.x / MathF.Max(rowSpacing, 1e-4f), wy = fw.y * columns;   // fwidth(g), before the row offset
            float row = MathF.Floor(gx);
            gy += 0.5f * (row - 2f * MathF.Floor(row * 0.5f));
            float cx = MathF.Floor(gx), cy = MathF.Floor(gy);
            float fx = gx - cx, fy = gy - cy;
            cy -= columns * MathF.Floor(cy / columns);
            float tint = Hash01((int)cx, (int)cy, part, 0x2545F491u) * 2f - 1f;
            float ex = MathF.Min(fx, 1f - fx) * 2f, ey = MathF.Min(fy, 1f - fy) * 2f;
            float height = Smoothstep(0f, 0.55f, ex) * Smoothstep(0f, 0.45f, ey);
            float vis = Saturate(1.6f - 2.2f * MathF.Max(wx, wy));
            return new Vector3(tint, height, vis);
        }

        // ------------------------------------------------------------ HLSL intrinsics

        static float Saturate(float x) => x < 0f ? 0f : (x > 1f ? 1f : x);
        static float Clamp(float x, float a, float b) => x < a ? a : (x > b ? b : x);
        static float Frac(float x) => x - MathF.Floor(x);
        static float Lerp(float a, float b, float t) => a + t * (b - a);
        static Vector3 Lerp(Vector3 a, Vector3 b, float t) => new Vector3(Lerp(a.x, b.x, t), Lerp(a.y, b.y, t), Lerp(a.z, b.z, t));
        static float Smoothstep(float a, float b, float x) { float t = Saturate((x - a) / (b - a)); return t * t * (3f - 2f * t); }
    }
}
