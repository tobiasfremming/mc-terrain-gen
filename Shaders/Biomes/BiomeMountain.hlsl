#ifndef MC_BIOME_MOUNTAIN_INCLUDED
#define MC_BIOME_MOUNTAIN_INCLUDED

#include "BiomeCommon.hlsl"
#include "BiomeCanyon.hlsl" // for _RockTex / _RockNormal

// Mountain biome: eroded alpine ranges (ErodedHeightField with the Mountain
// erosion settings). Fully procedural apart from the rock detail texture it
// shares with Canyon, used for its luminance only.
//
//  - Rock: anything steeper than the rock slope, broken up by noise so the
//    grass/rock line wanders. Grey, with faint strata by localHeight that
//    undulate like bedding planes, so the gully walls read as cut rock.
//  - Scree: the band between grass and rock, and everything flat above the
//    grass line -- pebbly grey-brown.
//  - Grass: colorFlat on gentle ground below the grass line, tonally noisy.
//  - Snow: above the snow line on ground that is not too steep, with a
//    noisy edge; steep rock sheds it, which is what draws the ridges.
//
// n/up are the geometric normal and the local up (radial on a planet):
// slope tests use dot(n, up) directly rather than the shared `steep`,
// whose 21-33 degree band was tuned for dune slip faces.

void EvaluateMountain(float3 positionWS, float3 w, float3 n, float3 up, float localHeight,
                       half3 grassColor, half3 rockColor, half3 rockColor2, half3 screeColor, half3 snowColor,
                       half rockSlope, half rockSlopeWidth, float bandSpacing, half bandContrast,
                       float snowLine, float snowBlend, float grassLine, float grassBlend,
                       float rockTexScale, half normalStrength,
                       half weight, inout half3 albedo, inout half3 tnX, inout half3 tnY, inout half3 tnZ)
{
    float rS = 1.0 / max(rockTexScale, 0.01);

    half3 rockTex = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.zy * rS).rgb * w.x
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xz * rS).rgb * w.y
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xy * rS).rgb * w.z;
    half rockLum = dot(rockTex, half3(0.3h, 0.59h, 0.11h)) * 1.8h; // orange sandstone -> grey
    rockTex = half3(rockLum, rockLum, rockLum);

    half upness = (half)saturate(dot(n, up));
    // wander the grass/rock boundary with two scales of noise
    half jitter = (half)(FbmN(positionWS.xz * 0.05 + 3.7, 2) - 0.5) * 0.18h
                + (half)(VNoise(positionWS.xz * 0.6 + 9.1) - 0.5) * 0.08h;
    half slopeU = upness + jitter;
    // steep -> rock; the band just gentler than rock -> scree; gentler still -> ground
    half rockMask = 1.0h - (half)smoothstep(rockSlope - rockSlopeWidth, rockSlope + rockSlopeWidth, slopeU);
    half flatMask = (half)smoothstep(rockSlope + rockSlopeWidth, rockSlope + rockSlopeWidth * 3.0h, slopeU);
    half screeMask = (1.0h - rockMask) * (1.0h - flatMask);

    // --- rock with strata ---
    float2 slow = positionWS.xz * 0.006;
    float undulate = (FbmN(slow + float2(21.0, 13.0), 3) * 2.0 - 1.0) * bandSpacing * 0.6;
    float bandPhase = frac((localHeight + undulate) / max(bandSpacing, 0.1));
    half tone = (half)smoothstep(0.2, 0.5, bandPhase) * (half)(1.0 - smoothstep(0.6, 0.95, bandPhase));
    half3 rock = lerp(rockColor, rockColor2, tone * bandContrast) * rockTex;

    // --- scree: pebbly, slightly warmer ---
    half3 scree = screeColor * lerp(0.82h, 1.18h, (half)VNoise(positionWS.xz * 1.1)) * lerp(1.0h, rockTex.g, 0.6h);

    // --- ground: grass below the grass line, alpine scree slabs above ---
    half3 grass = grassColor * lerp(0.82h, 1.18h, (half)FbmN(positionWS.xz * 0.12, 2));
    half above = (half)smoothstep(grassLine, grassLine + max(grassBlend, 1.0), localHeight + undulate * 0.5);
    half3 ground = lerp(grass, lerp(scree, rock, 0.35h), above);

    half3 col = lerp(ground, scree, screeMask);
    col = lerp(col, rock, rockMask);

    // --- snow: high, not too steep, noisy edge ---
    half snowEdge = (half)(FbmN(positionWS.xz * 0.04 + 5.5, 3) - 0.5) * (half)max(snowBlend, 1.0) * 1.2h;
    half snow = (half)smoothstep(snowLine, snowLine + max(snowBlend, 1.0), localHeight + snowEdge);
    snow *= (half)smoothstep(0.45, 0.8, upness); // rock faces shed it
    col = lerp(col, snowColor * lerp(0.92h, 1.0h, (half)VNoise(positionWS.xz * 0.3)), snow);

    albedo += col * weight;

    // rock normal on rock and scree, softened under snow; grass stays smooth
    half nStr = normalStrength * max(rockMask, screeMask * 0.5h) * (1.0h - 0.7h * snow);
    tnX += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.zy * rS), nStr) * weight;
    tnY += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xz * rS), nStr) * weight;
    tnZ += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xy * rS), nStr) * weight;
}

#endif
