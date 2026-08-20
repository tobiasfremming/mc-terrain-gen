#ifndef MC_BIOME_FROST_INCLUDED
#define MC_BIOME_FROST_INCLUDED

#include "BiomeCommon.hlsl"

// Frost biome: cracked glacier ice. Fully procedural -- no textures needed --
// so it renders correctly with zero extra asset setup. Weight comes from
// vertex color B (BiomeDensityField biome index 3).
//
//  - Crack veins: domain-warped fbm, thresholded into thin lines along its
//    contours (the standard cheap "marble vein" trick) -- looks like a
//    frozen crack network without any texture sampling.
//  - Glow: the crack lines get a faint cold-blue self-illumination, added
//    post-lighting so it reads even in shadow (like light catching inside
//    a crevasse).
//  - Bump: screen-space derivatives (ddx/ddy) of the same crack field fake
//    a groove normal along the cracks -- cheap procedural detail normal.
//  - Sparkle: sparse per-position flecks on the flat ice (mica-in-rock style)
//    for a bit of glint without needing view/light-dependent glitter.

// Thin lines along each integer contour of value*freq (the crack veins).
half ContourLines(float value, float freq, half width)
{
    float t = frac(value * freq);
    float d = min(t, 1.0 - t);
    return 1.0h - (half)smoothstep(0.0, width, d);
}

// Domain-warped fbm: gives the crack network an organic, non-grid-aligned
// wiggle instead of following raw Perlin contours.
float FrostField(float2 p, float warpStrength)
{
    float2 warp = float2(FbmN(p * 0.5 + 17.3, 3), FbmN(p * 0.5 + 91.7, 3)) - 0.5;
    return FbmN(p + warp * warpStrength, 4);
}

void EvaluateFrost(float3 positionWS, float3 w, half steep,
                    half3 colorFlat, half3 colorSteep, half3 crackColor,
                    half3 glowColor, half glowStrength,
                    float scale, float crackFreq, half crackWidth, float warpStrength,
                    half bumpStrength, half sparkleStrength,
                    half weight, inout half3 albedo, inout half3 emissive,
                    inout half3 tnX, inout half3 tnY, inout half3 tnZ)
{
    float invS = 1.0 / max(scale, 0.01);
    float2 uvX = positionWS.zy * invS;
    float2 uvY = positionWS.xz * invS;
    float2 uvZ = positionWS.xy * invS;

    float fX = FrostField(uvX, warpStrength);
    float fY = FrostField(uvY, warpStrength);
    float fZ = FrostField(uvZ, warpStrength);

    half crackX = ContourLines(fX, crackFreq, crackWidth);
    half crackY = ContourLines(fY, crackFreq, crackWidth);
    half crackZ = ContourLines(fZ, crackFreq, crackWidth);
    half crack = crackX * (half)w.x + crackY * (half)w.y + crackZ * (half)w.z;

    half3 ice = lerp(colorFlat, colorSteep, steep);
    half3 iceAlb = lerp(ice, crackColor, crack);

    // sparse mica-like sparkle on flat ice, absent inside cracks
    half3 cellId = (half3)floor(positionWS * 3.0);
    half sparkleMask = (half)step(0.985, Hash21(cellId.xz + cellId.y * 0.617));
    iceAlb += sparkleStrength * sparkleMask * (1.0h - crack);

    albedo += iceAlb * weight;
    emissive += glowColor * (crack * glowStrength) * weight;

    // fake bump from the crack field's screen-space slope: grooves along the
    // cracks, flat elsewhere. Same accumulation convention as the texture-
    // sampled biomes (small xy tilt, ~1 z), so it combines identically.
    half2 gX = (half2)float2(ddx(fX), ddy(fX)) * bumpStrength;
    half2 gY = (half2)float2(ddx(fY), ddy(fY)) * bumpStrength;
    half2 gZ = (half2)float2(ddx(fZ), ddy(fZ)) * bumpStrength;
    tnX += half3(gX, 1.0h) * weight;
    tnY += half3(gY, 1.0h) * weight;
    tnZ += half3(gZ, 1.0h) * weight;
}

#endif
