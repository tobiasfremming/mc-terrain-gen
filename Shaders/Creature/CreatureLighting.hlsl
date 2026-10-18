// "Stylized-realistic" creature lighting (Forward pass only; needs URP Lighting.hlsl first).
//   diffuse   wrapped Lambert; the light the wrap adds lives at the terminator and is tinted warm
//             by the subsurface amount (a cheap living-skin scatter)
//   specular  GGX from smoothness (URP's normalisation), plus environment reflection
//   ambient   SH, darkened under the belly, plus a warm bounce from sunlit ground
//   rim       a soft fresnel rim lit by the sky, stronger toward the sun side
// The main light carries real-time shadows (MainLightRealtimeShadow inside GetMainLight) and
// screen-space AO where the renderer has it.
#ifndef CREATURE_LIGHTING_INCLUDED
#define CREATURE_LIGHTING_INCLUDED

struct CreatureSurface
{
    half3 albedo;
    float3 normalWS;     // unit
    half smoothness;
    half rim;
    half subsurface;
    half dorsal;         // -1 belly .. +1 back: stands in for "faces the ground" on any planet
};

struct CreatureLightParams
{
    half wrap;
    half3 subsurfaceColor;
    half bellyOcclusion;
    half3 groundBounce;  // rgb, pre-multiplied by its strength
    half rimPower;
    half3 ambientTint;   // warm tint the sky ambient is pulled toward (same luminance)
    half ambientWarmth;  // 0 = raw sky SH (reads blue-grey on desert sand), 1 = fully the tint
};

// The sky SH pulled toward a warm tint of the same luminance: in a desert most of the fill light is
// bounced off sand, so shaded sides stay warm (as the sand shader's _ShadowWarmth does).
half3 CreatureWarmSH(half3 sh, CreatureLightParams p)
{
    const half3 lumW = half3(0.299h, 0.587h, 0.114h);
    half lum = dot(sh, lumW);
    half3 warm = p.ambientTint * (lum / max(dot(p.ambientTint, lumW), 1e-3h));
    return lerp(sh, warm, p.ambientWarmth);
}

half3 CreatureHueShift(half3 c, half turns)
{
    // rotate about the grey axis
    float a = turns * 6.2831853;
    float cs = cos(a), sn = sin(a);
    const float3 k = float3(0.57735027, 0.57735027, 0.57735027);
    float3 r = (float3)c * cs + cross(k, (float3)c) * sn + k * dot(k, (float3)c) * (1.0 - cs);
    return (half3)max(r, 0.0);
}

half3 CreatureShade(CreatureSurface s, CreatureLightParams p, float3 positionWS, float2 screenUV)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = float4(screenUV, 0.0, 1.0);
#else
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
#endif
    Light light = GetMainLight(shadowCoord, positionWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);

    float3 N = s.normalWS;
    float3 L = light.direction;
    float3 V = normalize(GetWorldSpaceViewDir(positionWS));
    float ndl = dot(N, L);
    float nv = saturate(dot(N, V)) + 1e-4;
    float grazeV = saturate(1.0 - nv);   // 1 - N.V, never negative (pow below)
    half shadow = light.shadowAttenuation * light.distanceAttenuation * ao.directAmbientOcclusion;

    // --- diffuse: wrapped Lambert, the wrap's extra light tinted warm (subsurface)
    half lam = (half)saturate(ndl);
    half wrapped = (half)saturate((ndl + p.wrap) / (1.0 + p.wrap));
    half extra = max(wrapped - lam, 0.0h);
    half3 sss = lerp(half3(1.0h, 1.0h, 1.0h), p.subsurfaceColor * 2.0h, s.subsurface);
    half3 diffuse = (lam + extra * sss) * light.color * shadow;

    // --- specular: GGX (URP's minimal-cost normalisation), dielectric F0 0.04
    float perceptualRough = max(1.0 - s.smoothness, 0.04);
    float rough = perceptualRough * perceptualRough;
    float rough2 = rough * rough;
    float3 H = SafeNormalize(L + V);
    float nh = saturate(dot(N, H));
    float lh = saturate(dot(L, H));
    float d = nh * nh * (rough2 - 1.0) + 1.00001;
    float specTerm = rough2 / ((d * d) * max(0.1, lh * lh) * (rough * 4.0 + 2.0));
    specTerm = min(specTerm, 100.0);
    half3 specular = (half3)(0.04 * specTerm * lam) * light.color * shadow;

    // --- ambient: sky SH (occluded under the belly) + warm bounce off the lit ground
    half up01 = saturate(0.5h + 0.5h * s.dorsal);
    half skyOcc = lerp(1.0h - p.bellyOcclusion, 1.0h, up01) * ao.indirectAmbientOcclusion;
    half below = (1.0h - up01) * (1.0h - up01);
    half3 ambient = CreatureWarmSH(SampleSH(N), p) * skyOcc + p.groundBounce * light.color * (below * ao.indirectAmbientOcclusion);

    // --- environment reflection (URP's environment BRDF terms)
    half fres = (half)pow(grazeV, 4.0);
    half grazing = saturate(s.smoothness + 0.04h);
    half3 env = GlossyEnvironmentReflection((half3)reflect(-V, N), (half)perceptualRough, skyOcc);
    half3 envSpec = env * (half)(1.0 / (rough2 + 1.0)) * lerp(0.04h, grazing, fres);

    // --- rim: sky-coloured, a little sun on the lit side, none under the belly
    half rimMask = (half)pow(grazeV, max(p.rimPower, 1.0h)) * s.rim * up01;
    half3 rimLight = rimMask * (CreatureWarmSH(SampleSH(V), p) * 0.6h + light.color * (0.4h * saturate((half)ndl + 0.5h) * shadow));

    half3 color = s.albedo * (diffuse + ambient) + specular + envSpec + rimLight * lerp(s.albedo, half3(1.0h, 1.0h, 1.0h), 0.5h);
    return color;
}

#endif
