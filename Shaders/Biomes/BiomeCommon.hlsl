#ifndef MC_BIOME_COMMON_INCLUDED
#define MC_BIOME_COMMON_INCLUDED

// Shared procedural-noise + anti-tiling helpers used by the per-biome
// shading modules (BiomeSand.hlsl, BiomeCanyon.hlsl, BiomeAlien.hlsl).

float Hash21(float2 p)
{
    p = frac(p * float2(123.34, 345.45));
    p += dot(p, p + 34.345);
    return frac(p.x * p.y);
}

float VNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = Hash21(i);
    float b = Hash21(i + float2(1, 0));
    float c = Hash21(i + float2(0, 1));
    float d = Hash21(i + float2(1, 1));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

float FbmN(float2 p, int octaves)
{
    float v = 0.0;
    float amp = 0.5;
    float freq = 1.0;
    for (int k = 0; k < octaves; k++)
    {
        v += amp * VNoise(p * freq);
        freq *= 2.0;
        amp *= 0.5;
    }
    return v;
}

// Anti-tiling: blend two samples of the same texture, the second rotated +
// rescaled, switched by low-frequency noise so the repeat pattern never
// lines up over distance.
#define DETILE_ROT_C 0.7986
#define DETILE_ROT_S 0.6018

float2 DetileUV(float2 uv)
{
    float2 r = float2(DETILE_ROT_C * uv.x - DETILE_ROT_S * uv.y,
                      DETILE_ROT_S * uv.x + DETILE_ROT_C * uv.y);
    return r * 1.37 + float2(7.31, 3.17);
}

half DetileMask(float2 uv)
{
    return (half)smoothstep(0.35, 0.65, VNoise(uv * 0.31 + 11.7));
}

// Remaps a biome's raw partition-of-unity weight so each biome can be given
// its own shading transition sharpness, independent of the terrain field's
// own density-blend sharpness (BiomeDensityField.sharpness), which only
// shapes the ground SURFACE, not how it's SHADED. sharpness = 1 is a no-op.
half BiomeSharpen(half w, half sharpness)
{
    return pow(max(w, 0.0001h), 1.0h / max(sharpness, 0.01h));
}

#endif
