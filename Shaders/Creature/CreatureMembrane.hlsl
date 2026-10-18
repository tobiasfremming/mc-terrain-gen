// Wing membrane shading (part 8, WingedMeshBuilder.PartMembrane): a thin translucent skin, double-sided
// (two sheets with opposite normals). Forward pass only; needs CreatureLighting.hlsl first.
//   front     the creature's own lighting (CreatureShade) on the membrane colour, veins darker
//   back-lit  light from the far side shines through: transmission tinted by the membrane colour,
//             strongest looking toward the light (forward scattering), dimmed by the veins
//   rim       the trailing edge (and the flank seam) darker: thicker skin at the border
// Vertex data: uv0 = (distance from the wrist m, zone + angle fraction / 5 zones), uv1.y = sheet sign x
// (0.1 + 0.9 x radial fraction to the edge).
#ifndef CREATURE_MEMBRANE_INCLUDED
#define CREATURE_MEMBRANE_INCLUDED

#define CREATURE_PART_MEMBRANE 8

// 0 between veins .. 1 on one: fine veins fanning out along the rays, three per zone, a little wavy.
half CreatureMembraneVeins(float2 uv0)
{
    float a = uv0.y * 15.0 + 0.03 * sin(uv0.x * 23.0);
    float d = abs(frac(a + 0.5) - 0.5) * 2.0;           // 0 on a vein line
    half vein = (half)(1.0 - smoothstep(0.0, 0.14, d));
    // the main rays (zone borders: fingers and ribs) are thicker
    float z = abs(frac(uv0.y * 5.0 + 0.5) - 0.5) * 2.0;
    vein = max(vein, (half)(1.0 - smoothstep(0.0, 0.05, z)));
    return vein;
}

half3 CreatureMembraneShade(CreatureSurface s, CreatureLightParams p, float3 positionWS, float2 screenUV,
                            half3 membraneColor, half translucency, half vein)
{
    half3 front = CreatureShade(s, p, positionWS, screenUV);
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = float4(screenUV, 0.0, 1.0);
#else
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
#endif
    Light light = GetMainLight(shadowCoord, positionWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
    float3 V = normalize(GetWorldSpaceViewDir(positionWS));
    half back = (half)saturate(dot(-s.normalWS, light.direction));            // lit from the far side
    half forward = (half)pow(saturate(dot(V, -light.direction)), 4.0);        // looking into the light
    half thru = back * (0.35h + 0.65h * forward) * translucency * (1.0h - 0.6h * vein);
    half3 transmit = membraneColor * membraneColor * 2.0h * light.color * (thru * light.shadowAttenuation * light.distanceAttenuation);
    return front + transmit;
}

#endif
