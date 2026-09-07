#ifndef MC_DENSITY_ERODED_INCLUDED
#define MC_DENSITY_ERODED_INCLUDED

#include "TerrainNoiseGPU.hlsl"
#include "ErosionNoiseGPU.hlsl"

// GPU port of Scripts/Terrain/ErodedHeightField.cs's HeightAt(wx, wz).
// Returns HEIGHT (not density); the caller subtracts y. Field order/names
// mirror ErodedGpuParams exactly; keep the two in lockstep.
struct ErodedParams
{
    float seed;
    float baseHeight;
    float baseScale;
    float baseOctaves;
    float baseAmp;
    float fadeRange;
    float heightOffsetX;
    float heightOffsetY;
    float eScale;
    float eStrength;
    float eGullyWeight;
    float eDetail;
    float eRoundingX;
    float eRoundingY;
    float eRoundingZ;
    float eRoundingW;
    float eOnsetX;
    float eOnsetY;
    float eOnsetZ;
    float eOnsetW;
    float eAssumedSlopeX;
    float eAssumedSlopeY;
    float eCellScale;
    float eOctaves;
    float eGain;
    float eLacunarity;
    float eNormalization;
    float sandEnabled;
    float sandLevel;
    float sandScale;
    float sandOctaves;
    float sandAmp;
    float sandBlend;
};

float MC_ErodedSmoothMax(float a, float b, float k)
{
    if (k <= 0.0) return max(a, b);
    float h = saturate(0.5 + 0.5 * (a - b) / k);
    return b + (a - b) * h + k * h * (1.0 - h);
}

float EvaluateErodedHeight(float wx, float wz, ErodedParams p, float fw)
{
    uint s = (uint)(int)p.seed;

    float3 n = MC_FbmD(wx / p.baseScale, wz / p.baseScale, (int)p.baseOctaves, s + 1u, fw / p.baseScale);
    n.x *= p.baseAmp;
    n.yz *= p.baseAmp / p.baseScale;

    float fadeTarget = clamp(n.x / (p.baseAmp * p.fadeRange), -1.0, 1.0);

    MC_ErosionParams e;
    e.scale = p.eScale;
    e.strength = p.eStrength;
    e.gullyWeight = p.eGullyWeight;
    e.detail = p.eDetail;
    e.rounding = float4(p.eRoundingX, p.eRoundingY, p.eRoundingZ, p.eRoundingW);
    e.onset = float4(p.eOnsetX, p.eOnsetY, p.eOnsetZ, p.eOnsetW);
    e.assumedSlope = float2(p.eAssumedSlopeX, p.eAssumedSlopeY);
    e.cellScale = p.eCellScale;
    e.octaves = (int)p.eOctaves;
    e.gain = p.eGain;
    e.lacunarity = p.eLacunarity;
    e.normalization = p.eNormalization;

    float ridge;
    float4 d = MC_ErosionFilter(float2(wx, wz), n, fadeTarget, e, s + 101u, fw, ridge);
    float offset = lerp(p.heightOffsetX, -fadeTarget, p.heightOffsetY) * d.w;
    float h = p.baseHeight + n.x + d.x + offset;

    if (p.sandEnabled > 0.5)
    {
        float plain = p.sandLevel + MC_Fbm(wx / p.sandScale, wz / p.sandScale, (int)p.sandOctaves, s + 5u, fw / p.sandScale) * p.sandAmp;
        h = MC_ErodedSmoothMax(h, plain, p.sandBlend);
    }
    return h;
}

#endif
