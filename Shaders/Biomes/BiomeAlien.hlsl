#ifndef MC_BIOME_ALIEN_INCLUDED
#define MC_BIOME_ALIEN_INCLUDED

// Alien biome: cold rock colors x packed-pebble texture. Weight comes from
// vertex color G (BiomeDensityField biome index 2).

TEXTURE2D(_PebbleTex);    SAMPLER(sampler_PebbleTex);
TEXTURE2D(_PebbleNormal); SAMPLER(sampler_PebbleNormal);

void EvaluateAlien(float3 positionWS, float3 w, half3 alienFlat, half3 alienSteep,
                    float pebbleTexScale, half steep, half normalStrength,
                    half weight, inout half3 albedo, inout half3 tnX, inout half3 tnY, inout half3 tnZ)
{
    float pS = 1.0 / max(pebbleTexScale, 0.01);

    tnX += UnpackNormalScale(SAMPLE_TEXTURE2D(_PebbleNormal, sampler_PebbleNormal, positionWS.zy * pS), normalStrength) * weight;
    tnY += UnpackNormalScale(SAMPLE_TEXTURE2D(_PebbleNormal, sampler_PebbleNormal, positionWS.xz * pS), normalStrength) * weight;
    tnZ += UnpackNormalScale(SAMPLE_TEXTURE2D(_PebbleNormal, sampler_PebbleNormal, positionWS.xy * pS), normalStrength) * weight;

    half3 pebTex = SAMPLE_TEXTURE2D(_PebbleTex, sampler_PebbleTex, positionWS.zy * pS).rgb * w.x
                 + SAMPLE_TEXTURE2D(_PebbleTex, sampler_PebbleTex, positionWS.xz * pS).rgb * w.y
                 + SAMPLE_TEXTURE2D(_PebbleTex, sampler_PebbleTex, positionWS.xy * pS).rgb * w.z;
    half3 alienAlb = lerp(alienFlat, alienSteep, steep) * pebTex * 1.9h;
    albedo += alienAlb * weight;
}

#endif
