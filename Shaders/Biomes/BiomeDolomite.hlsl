#ifndef MC_BIOME_DOLOMITE_INCLUDED
#define MC_BIOME_DOLOMITE_INCLUDED

#include "BiomeCommon.hlsl"
#include "BiomeCanyon.hlsl" // for _RockTex / _RockNormal

// Dolomite biome: pale banded limestone on the walls, grey scree on the
// steep-but-not-vertical aprons, alpine meadow on flat ground. Fully
// procedural apart from the rock detail texture it shares with Canyon.
//
//  - Bands: thin horizontal strata by localHeight, distorted by a slow fbm so
//    they undulate like real bedding planes, alternating between two pale
//    rock tones. Only on steep faces (they are the wall's signature).
//  - Scree: a mid-slope grey that takes over between the meadow and the wall
//    and gets pebbly detail from the rock texture.
//  - Meadow: colorFlat on flat ground, with a little value noise so it does
//    not read as a flat fill.
//  - Snow: optional cap above _DoloSnowLine on flat-ish ground.

void EvaluateDolomite(float3 positionWS, float3 w, half steep, float localHeight,
                       half3 meadowColor, half3 rockColor, half3 rockBandColor, half3 screeColor,
                       float bandSpacing, float bandDistortion, half bandContrast,
                       half screeStart, half screeEnd, float snowLine, half snowBlend,
                       float rockLine, float rockLineBlend,
                       float rockTexScale, half normalStrength,
                       half weight, inout half3 albedo, inout half3 tnX, inout half3 tnY, inout half3 tnZ)
{
    float rS = 1.0 / max(rockTexScale, 0.01);

    half3 rockTex = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.zy * rS).rgb * w.x
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xz * rS).rgb * w.y
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xy * rS).rgb * w.z;
    // Luminance only: _RockTex is the canyon's orange sandstone, and its hue
    // must not leak into pale limestone. Mean ~0.55 -> renormalise to ~1.
    half rockLum = dot(rockTex, half3(0.3h, 0.59h, 0.11h)) * 1.8h;
    rockTex = half3(rockLum, rockLum, rockLum);

    // --- bedding planes ---
    float2 slow = positionWS.xz * 0.004;
    float undulate = (FbmN(slow + float2(31.0, 17.0), 3) * 2.0 - 1.0) * bandDistortion;
    float bandCoord = (localHeight + undulate) / max(bandSpacing, 0.1);
    float bandPhase = frac(bandCoord);
    // two-tone strata with soft edges, plus a thin dark parting line
    half tone = (half)smoothstep(0.15, 0.5, bandPhase) * (half)(1.0 - smoothstep(0.55, 0.95, bandPhase));
    half parting = 1.0h - (half)smoothstep(0.0, 0.06, min(bandPhase, 1.0 - bandPhase));
    half3 banded = lerp(rockColor, rockBandColor, tone * bandContrast);
    banded *= 1.0h - 0.25h * parting * bandContrast;
    half3 wall = banded * rockTex;

    // --- scree: mid slopes, pebbly ---
    half screeMask = (half)smoothstep(screeStart, screeEnd, steep) * (1.0h - (half)smoothstep(screeEnd, 1.0h, steep));
    half3 scree = screeColor * lerp(0.8h, 1.2h, (half)VNoise(positionWS.xz * 0.9)) * lerp(1.0h, rockTex.g, 0.6h);

    // --- meadow: flat ground with a little tonal noise, only below the rock
    // line; above it flat ground is a pale scree-and-slab plateau ---
    half3 meadow = meadowColor * lerp(0.85h, 1.15h, (half)FbmN(positionWS.xz * 0.15, 2));
    half3 plateau = lerp(screeColor, rockColor, 0.5h) * lerp(0.9h, 1.1h, (half)VNoise(positionWS.xz * 0.5)) * lerp(1.0h, rockTex.g, 0.5h);
    half above = (half)smoothstep(rockLine, rockLine + max(rockLineBlend, 1.0), localHeight);
    meadow = lerp(meadow, plateau, above);

    // --- optional snow on high, flat-ish ground ---
    half snow = (half)smoothstep(snowLine, snowLine + max(snowBlend, 1.0h), localHeight) * (1.0h - steep);

    half wallMask = (half)smoothstep(screeEnd, 1.0h, steep);
    half3 col = lerp(meadow, scree, screeMask);
    col = lerp(col, wall, wallMask);
    col = lerp(col, half3(0.93h, 0.94h, 0.97h), snow);

    albedo += col * weight;

    // rock normal on walls and scree only; meadows stay smooth
    half nStr = normalStrength * max(screeMask * 0.5h, wallMask);
    tnX += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.zy * rS), nStr) * weight;
    tnY += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xz * rS), nStr) * weight;
    tnZ += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xy * rS), nStr) * weight;
}

#endif
