#ifndef MC_EROSION_NOISE_GPU_INCLUDED
#define MC_EROSION_NOISE_GPU_INCLUDED

#include "TerrainNoiseGPU.hlsl"

// GPU port of Scripts/ErosionNoise.cs -- Rune Skovbo Johansen's erosion
// filter (MPL-2.0, https://www.shadertoy.com/view/wXcfWn; see the C# header
// for the lineage). Keep in lockstep with the C#: same loop order, same
// constants, same band-limit rule, or CPU normals disagree with GPU geometry.
// Units are metres; slopes are metres per metre.

struct MC_ErosionParams
{
    float scale;
    float strength;
    float gullyWeight;
    float detail;
    float4 rounding;
    float4 onset;
    float2 assumedSlope;
    float cellScale;
    int octaves;
    float gain;
    float lacunarity;
    float normalization;
};

float MC_EroPowInv(float t, float power) { return 1.0 - pow(1.0 - saturate(t), power); }
float MC_EroEaseOut(float t) { float v = 1.0 - saturate(t); return 1.0 - v * v; }
float MC_EroSmoothStart(float t, float smoothing)
{
    if (t >= smoothing) return t - 0.5 * smoothing;
    return 0.5 * t * t / smoothing;
}

// Cell point jitter in [-0.5, 0.5] per axis -- mirrors ErosionNoise.CellJitter.
float2 MC_EroCellJitter(int gx, int gy, uint seed)
{
    uint h = MC_Hash((uint)gx, (uint)gy, seed);
    return float2((float)(h & 0xFFFFu) / 65536.0 - 0.5, (float)((h >> 16) & 0xFFFFu) / 65536.0 - 0.5);
}

// Phacelle noise: stripes perpendicular to normalised `dir`, `freq` stripes
// per cell, blended over the 4x4 cells around p. xy = (cos, sin) of the
// interpolated phase after partial renormalisation, zw = the side vector
// (d cos / dp = -sin * zw).
float4 MC_Phacelle(float2 p, float2 dir, float freq, float offset, float normalization, uint seed)
{
    float2 side = float2(-dir.y, dir.x) * freq * 6.2831853071795864;
    offset *= 6.2831853071795864;

    float2 pInt = floor(p);
    float2 pFrac = p - pInt;
    int2 cell = (int2)pInt;

    float2 phase = 0.0;
    float weightSum = 0.0;
    // Outer loop rolled to keep code size down (this sits inside the leaf
    // branch tree that once made FXC time out); the inner 4 unroll.
    [loop] for (int i = -1; i <= 2; i++)
    {
        for (int j = -1; j <= 2; j++)
        {
            float2 r = MC_EroCellJitter(cell.x + i, cell.y + j, seed);
            float2 v = pFrac - float2(i, j) - r;
            float sqrDist = dot(v, v);
            float w = max(0.0, exp(-sqrDist * 2.0) - 0.01111);
            weightSum += w;
            float wave = dot(v, side) + offset;
            float sn, cs;
            sincos(wave, sn, cs);
            phase += float2(cs, sn) * w;
        }
    }
    float2 interp = phase / weightSum;
    float magnitude = sqrt(dot(interp, interp));
    magnitude = max(1.0 - normalization, magnitude);
    return float4(interp / magnitude, side);
}

// The filter -- see ErosionNoise.Filter. heightAndSlope = (h, dh/dx, dh/dz)
// in metres; fadeTarget in [-1, 1]. Returns the delta (xyz) and the summed
// octave strength (w); ridgeMap is -1 on creases, +1 on ridges.
float4 MC_ErosionFilter(float2 wp, float3 heightAndSlope, float fadeTarget, MC_ErosionParams p, uint seed,
                        float filterWidth, out float ridgeMap)
{
    float strength = p.strength * p.scale;
    fadeTarget = clamp(fadeTarget, -1.0, 1.0);

    float3 input = heightAndSlope;
    float freq = 1.0 / (p.scale * p.cellScale);
    float wavelength = p.scale;
    float slopeLength = max(length(heightAndSlope.yz), 1e-10);
    float magnitude = 0.0;
    float roundingMult = 1.0;

    float roundingForInput = lerp(p.rounding.y, p.rounding.x, saturate(fadeTarget + 0.5)) * p.rounding.z;
    float combiMask = MC_EroEaseOut(MC_EroSmoothStart(slopeLength * p.onset.x, roundingForInput * p.onset.x));

    float ridgeCombiMask = MC_EroEaseOut(slopeLength * p.onset.z);
    float ridgeFadeTarget = fadeTarget;

    float2 gully = lerp(heightAndSlope.yz, heightAndSlope.yz / slopeLength * p.assumedSlope.x, p.assumedSlope.y);

    [loop] for (int i = 0; i < p.octaves; i++)
    {
        float w = MC_DetailFade(filterWidth, wavelength);
        if (w <= 0.0) break;

        float gl = length(gully);
        float2 nd = gl > 1e-10 ? gully / gl : float2(0.0, 0.0);
        float4 ph = MC_Phacelle(wp * freq, nd, p.cellScale, 0.25, p.normalization, seed);
        float2 side = ph.zw * -freq;

        float sloping = abs(ph.y);
        float sg = sign(ph.y);
        gully += sg * side * strength * p.gullyWeight;

        float gH = ph.x;
        float2 gS = ph.y * side;

        float fH = lerp(fadeTarget, gH * p.gullyWeight, combiMask);
        float2 fS = gS * p.gullyWeight * combiMask;

        float sw = strength * w;
        heightAndSlope += float3(fH, fS) * sw;
        magnitude += sw;

        fadeTarget = fH;

        float roundingForOctave = lerp(p.rounding.y, p.rounding.x, saturate(ph.x + 0.5)) * roundingMult;
        float newMask = MC_EroEaseOut(MC_EroSmoothStart(sloping * p.onset.y, roundingForOctave * p.onset.y));
        combiMask = MC_EroPowInv(combiMask, p.detail) * newMask;

        ridgeFadeTarget = lerp(ridgeFadeTarget, gH, ridgeCombiMask);
        ridgeCombiMask *= MC_EroEaseOut(sloping * p.onset.w);

        strength *= p.gain;
        freq *= p.lacunarity;
        wavelength /= p.lacunarity;
        roundingMult *= p.rounding.w;
    }

    ridgeMap = ridgeFadeTarget * (1.0 - ridgeCombiMask);
    return float4(heightAndSlope - input, magnitude);
}

#endif
