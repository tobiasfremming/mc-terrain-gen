// Creature fur lighting (both fur shaders; needs URP Lighting.hlsl first). Hair is lit along its tangent:
//   diffuse   half wrapped Lambert on the skin normal, half Kajiya-Kay (sin of tangent and light), so fur never
//             goes as black at the terminator as skin does
//   specular  two Kajiya-Kay lobes: a narrow white one shifted toward the tip, a broad one tinted by the hair
//             (Marschner's R and TRT, cheaply), _FurLook.z strong
//   tips      light through the tips against the sun (_FurLook.w), stronger toward the tip
//   ambient   SH on the skin normal, darkened toward the root (_FurLook.y: the coat's own occlusion)
// Main-light shadows (screen-space or cascades) and SSAO like the body's lighting.
#ifndef CREATURE_FUR_LIGHTING_INCLUDED
#define CREATURE_FUR_LIGHTING_INCLUDED

half3 FurShade(float3 posWS, float3 T, float3 N, half3 albedo, half t, float2 screenUV)
{
#if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    float4 shadowCoord = float4(screenUV, 0.0, 1.0);
#else
    float4 shadowCoord = TransformWorldToShadowCoord(posWS);
#endif
    Light light = GetMainLight(shadowCoord, posWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);
    half shadow = light.shadowAttenuation * light.distanceAttenuation * ao.directAmbientOcclusion;

    float3 L = light.direction;
    float3 V = normalize(GetWorldSpaceViewDir(posWS));
    float tl = dot(T, L);
    half sinTL = (half)sqrt(saturate(1.0 - tl * tl));
    half wrapped = (half)saturate((dot(N, L) + 0.4) / 1.4);
    // the tangent term only on the lit side of the body (the skin normal's half-Lambert gates it)
    half diffuse = lerp(wrapped, sinTL * (half)saturate(dot(N, L) * 0.5 + 0.5), 0.5h);

    // Kajiya-Kay: T shifted along the normal (scales tilt the cuticle), the half vector's sine against it
    float3 H = SafeNormalize(L + V);
    float3 T1 = SafeNormalize(T + N * 0.12);
    float3 T2 = SafeNormalize(T - N * 0.18);
    float th1 = dot(T1, H), th2 = dot(T2, H);
    half s1 = (half)pow(saturate(1.0 - th1 * th1), 160.0);
    half s2 = (half)pow(saturate(1.0 - th2 * th2), 24.0);
    half facing = (half)saturate(dot(N, L) * 2.0 + 0.3);
    half hl = (half)_FurLook.z;
    half3 spec = (s1 * 0.6h + s2 * 0.5h * albedo * 2.0h) * hl * facing * t;

    half trans = (half)pow(saturate(dot(-V, L)), 3.0) * (half)_FurLook.w * t * t;

    half occ = lerp(1.0h - (half)_FurLook.y, 1.0h, t) * ao.indirectAmbientOcclusion;
    half3 ambient = SampleSH(N) * occ;

    half3 lit = light.color * shadow;
    half3 rgb = albedo * (lit * (diffuse * lerp(0.75h, 1.0h, occ)) + ambient) + lit * spec + light.color * (trans * light.shadowAttenuation) * albedo;
    return rgb;
}

#endif
