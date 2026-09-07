#ifndef MC_BIOME_DESERT_MOUNTAIN_INCLUDED
#define MC_BIOME_DESERT_MOUNTAIN_INCLUDED

#include "BiomeCommon.hlsl"
#include "BiomeSand.hlsl"   // sand albedo / normal samplers
#include "BiomeCanyon.hlsl" // _RockTex / _RockNormal (orange sandstone)

// Desert Mountain biome: eroded sandstone ranges rising out of sand basins
// (ErodedHeightField with the DesertMountain erosion settings + sand fill).
//
//  - Sand: on gentle ground and everywhere below the sand line (the field's
//    sand plain), the same triplanar grain as open desert tinted by
//    colorFlat, so basins match the neighbouring dune biome.
//  - Rock: the canyon sandstone texture tinted by colorSteep, with strata by
//    localHeight (undulating), and desert varnish -- a dark patina that
//    darkens the upper, steeper, long-exposed faces.
//  - The sand/rock line wanders with noise so pediments feather into rock.

void EvaluateDesertMountain(float3 positionWS, float3 w, float3 n, float3 up, float localHeight,
                             float2 uvX, float2 uvY, float2 uvZ, half mX, half mY, half mZ, half3 texAlb,
                             half3 sandColor, half3 rockColor, half3 bandColor, half3 varnishColor,
                             float bandSpacing, half bandContrast, half varnish,
                             half sandSlope, half sandSlopeWidth, float sandLine, float sandBlend,
                             float rockTexScale, half normalStrength,
                             half weight, inout half3 albedo, inout half3 tnX, inout half3 tnY, inout half3 tnZ)
{
    float rS = 1.0 / max(rockTexScale, 0.01);

    half3 rockTex = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.zy * rS).rgb * w.x
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xz * rS).rgb * w.y
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xy * rS).rgb * w.z;
    rockTex *= 1.7h; // mean ~0.55 -> ~1

    half upness = (half)saturate(dot(n, up));
    half jitter = (half)(FbmN(positionWS.xz * 0.04 + 7.3, 2) - 0.5) * 0.16h
                + (half)(VNoise(positionWS.xz * 0.5 + 2.9) - 0.5) * 0.06h;
    // sand holds on gentle ground, rock shows where it is steeper
    half rockMask = 1.0h - (half)smoothstep(sandSlope - sandSlopeWidth, sandSlope + sandSlopeWidth, upness + jitter);
    // and the basin floor is sand whatever its slope
    half basin = 1.0h - (half)smoothstep(sandLine, sandLine + max(sandBlend, 1.0), localHeight + jitter * 20.0h);
    rockMask *= 1.0h - basin;

    // --- strata ---
    float2 slow = positionWS.xz * 0.005;
    float undulate = (FbmN(slow + float2(41.0, 23.0), 3) * 2.0 - 1.0) * bandSpacing * 0.8;
    float bandPhase = frac((localHeight + undulate) / max(bandSpacing, 0.1));
    half tone = (half)smoothstep(0.15, 0.45, bandPhase) * (half)(1.0 - smoothstep(0.55, 0.9, bandPhase));
    half parting = 1.0h - (half)smoothstep(0.0, 0.05, min(bandPhase, 1.0 - bandPhase));
    half3 rock = lerp(rockColor, bandColor, tone * bandContrast);
    rock *= 1.0h - 0.2h * parting * bandContrast;
    rock *= rockTex;

    // --- desert varnish: steeper and higher faces darken, patchily ---
    half exposure = (1.0h - upness) * (half)smoothstep(sandLine, sandLine + 60.0, localHeight);
    half patch = (half)FbmN(positionWS.xz * 0.08 + 17.0, 3);
    half v = saturate(exposure * 1.6h) * (half)smoothstep(0.35, 0.7, patch) * varnish;
    rock = lerp(rock, rock * varnishColor * 1.6h, v);

    // --- sand: open-desert grain tinted by the channel's flat colour, a
    // little darker where it pools against rock ---
    half3 sand = sandColor * texAlb * lerp(0.9h, 1.05h, (half)VNoise(positionWS.xz * 0.2));

    half3 col = lerp(sand, rock, rockMask);
    albedo += col * weight;

    // rock normal on rock, sand grain on sand
    half rStr = normalStrength * rockMask;
    half sStr = normalStrength * (1.0h - rockMask);
    tnX += (UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.zy * rS), rStr)
            + SandSampleNormalTS(uvX, mX, sStr) - half3(0, 0, 1)) * weight;
    tnY += (UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xz * rS), rStr)
            + SandSampleNormalTS(uvY, mY, sStr) - half3(0, 0, 1)) * weight;
    tnZ += (UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xy * rS), rStr)
            + SandSampleNormalTS(uvZ, mZ, sStr) - half3(0, 0, 1)) * weight;
}

#endif
