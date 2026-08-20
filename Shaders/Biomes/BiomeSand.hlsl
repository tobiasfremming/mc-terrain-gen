#ifndef MC_BIOME_SAND_INCLUDED
#define MC_BIOME_SAND_INCLUDED

#include "BiomeCommon.hlsl"

// Base sand shading: procedural triplanar grain (no UVs needed) + slope
// tint. Every biome's detail-normal contribution sits on top of this layer,
// since fine sand grain reads through even where canyon/alien color wins.

TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);

half3 SandSampleAlbedo(float2 uv, half m)
{
    half3 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
    half3 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, DetileUV(uv)).rgb;
    return lerp(a, b, m);
}

half3 SandSampleNormalTS(float2 uv, half m, half normalStrength)
{
    half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv), normalStrength);
    half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, DetileUV(uv)), normalStrength);
    // counter-rotate the rotated sample's tangent-plane component
    b.xy = half2(DETILE_ROT_C * b.x + DETILE_ROT_S * b.y,
                 -DETILE_ROT_S * b.x + DETILE_ROT_C * b.y);
    return lerp(a, b, m);
}

// Raw (unweighted) triplanar sand albedo. Also sampled by BiomeCanyon.hlsl
// to tint the canyon floor/plateau tops with the same grain as open desert.
half3 SandDetailAlbedo(float2 uvX, float2 uvY, float2 uvZ, half mX, half mY, half mZ, float3 w)
{
    return SandSampleAlbedo(uvX, mX) * w.x
         + SandSampleAlbedo(uvY, mY) * w.y
         + SandSampleAlbedo(uvZ, mZ) * w.z;
}

// Adds sand's weighted albedo + detail-normal contribution. Called
// unconditionally (weight may still be near 1 even in "pure" other biomes'
// territory during transitions).
void EvaluateSand(float2 uvX, float2 uvY, float2 uvZ, half mX, half mY, half mZ,
                   half3 texAlb, half3 colorFlat, half3 colorSteep, half steep, half normalStrength,
                   half weight, inout half3 albedo, inout half3 tnX, inout half3 tnY, inout half3 tnZ)
{
    albedo += lerp(colorFlat, colorSteep, steep) * texAlb * weight;

    tnX += SandSampleNormalTS(uvX, mX, normalStrength) * weight;
    tnY += SandSampleNormalTS(uvY, mY, normalStrength) * weight;
    tnZ += SandSampleNormalTS(uvZ, mZ, normalStrength) * weight;
}

#endif
