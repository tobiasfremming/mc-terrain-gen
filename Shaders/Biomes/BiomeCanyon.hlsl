#ifndef MC_BIOME_CANYON_INCLUDED
#define MC_BIOME_CANYON_INCLUDED

#include "BiomeCommon.hlsl"

// Canyon biome: warped sediment layers on rock walls, sandy floors/plateau
// tops. Weight comes from vertex color R (BiomeDensityField biome index 1).
// Sediment layering adapted from the user's MountainLayers shader.

TEXTURE2D(_RockTex);    SAMPLER(sampler_RockTex);
TEXTURE2D(_RockNormal); SAMPLER(sampler_RockNormal);

void EvaluateCanyon(float3 positionWS, float3 w, half3 texAlb, half3 canyonFloorColor,
                     half3 layer1, half3 layer2, half3 layer3, half3 layer4,
                     float layerScale, float layerDistortion, float layerSharpness,
                     float layerNoiseScale, float subLayerScale, half subLayerIntensity,
                     float rockTexScale, half steep, half normalStrength, float localHeight,
                     half weight, inout half3 albedo, inout half3 tnX, inout half3 tnY, inout half3 tnZ)
{
    float rS = 1.0 / max(rockTexScale, 0.01);

    tnX += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.zy * rS), normalStrength) * weight;
    tnY += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xz * rS), normalStrength) * weight;
    tnZ += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, positionWS.xy * rS), normalStrength) * weight;

    half3 rockTex = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.zy * rS).rgb * w.x
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xz * rS).rgb * w.y
                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, positionWS.xy * rS).rgb * w.z;

    // --- sediment layers (adapted from MountainLayers) ---
    // Uses localHeight (planet-aware "altitude") rather than raw positionWS.y
    // so sediment bands follow the local ground on a sphere instead of
    // cutting flat planes through it. On a flat world localHeight == positionWS.y
    // exactly (see SandTerrain.shader's Frag), so this is a no-op there.
    float height = localHeight;
    float2 largeUV = positionWS.xz * 0.005;
    float lwx = FbmN(largeUV + float2(100, 200), 4) * 2.0 - 1.0;
    float lwy = FbmN(largeUV + float2(300, 400), 4) * 2.0 - 1.0;
    float2 warpedPos = float2(localHeight, positionWS.z) + float2(lwx, lwy) * layerDistortion * 20.0;
    float tectonic = FbmN(float2(localHeight, positionWS.x) * 0.02, 2) * layerDistortion * 15.0;
    height += tectonic;
    float distortion = FbmN(warpedPos * layerNoiseScale * 0.01, 3) * 2.0 - 1.0;
    float dh = height + distortion * layerDistortion;

    float cycle = dh * layerScale * 0.1;
    float layerValue = frac(cycle);
    float layerIndex = floor(frac(cycle + 0.00001) * 4.0);
    float edgeNoise = FbmN(warpedPos * layerNoiseScale * 0.05, 2) * 0.5;
    layerValue = saturate(layerValue + edgeNoise * 0.2);
    layerValue = pow(layerValue, layerSharpness);

    half3 rock;
    if (layerIndex < 1.0)      rock = lerp(layer1, layer2, (half)layerValue);
    else if (layerIndex < 2.0) rock = lerp(layer2, layer3, (half)layerValue);
    else if (layerIndex < 3.0) rock = lerp(layer3, layer4, (half)layerValue);
    else                       rock = lerp(layer4, layer1, (half)layerValue);

    // thin sub-layer striations
    if (subLayerScale > 0.0)
    {
        float2 subUV = positionWS.xz * subLayerScale * 0.01;
        subUV += float2(FbmN(subUV * 0.5 + float2(700, 800), 2),
                        FbmN(subUV * 0.5 + float2(900, 1000), 2)) * 0.5;
        float sp = FbmN(subUV, 4);
        sp = sin(sp * 20.0 + dh * subLayerScale * 0.5) * 0.5 + 0.5;
        rock = lerp(rock, rock * 0.7h, (half)step(0.6, sp) * subLayerIntensity);
    }

    // sediment colors modulated by the rock texture on walls; sandy floors &
    // plateau tops keep the sand texture
    rock *= rockTex * 1.7h; // rock tex mean ~0.55 -> renormalize
    half3 canyonAlb = lerp(canyonFloorColor * texAlb, rock, steep);
    albedo += canyonAlb * weight;
}

#endif
