// Procedural sand terrain shader (URP).
// - No UVs needed: detail grain is generated procedurally with TRIPLANAR
//   projection (sampled from the three world axes, blended by the normal).
// - Slope-based tinting: slip faces (near the angle of repose) shade darker
//   and warmer than the flat windward slopes, which is what visually sells
//   dunes.
// - Double-sided with normal flip so carved caves are lit correctly inside.
// - Fog-aware (works with the linear fog set up by DesertAtmosphere).
Shader "MarchingCubes/Sand Terrain"
{
    Properties
    {
        // Palette sampled from Sahara (Merzouga) reference photography
        _ColorFlat  ("Sand - flat / windward", Color) = (0.83, 0.55, 0.26, 1)
        _ColorSteep ("Sand - slip face",       Color) = (0.68, 0.40, 0.17, 1)
        _SlopeStart ("Slope tint start (deg)", Range(0, 90)) = 21
        _SlopeEnd   ("Slope tint full (deg)",  Range(0, 90)) = 33
        _ShadowWarmth ("Shadow warmth (bounced sand light)", Range(0, 1)) = 0.55
        _SheenColor   ("Grazing sheen color", Color) = (1.0, 0.80, 0.55, 1)
        _SheenStrength ("Grazing sheen strength", Range(0, 1)) = 0.18
        _SheenPower    ("Grazing sheen tightness", Range(1, 8)) = 3.5
        _GlitterStrength ("Sun glitter strength", Range(0, 1)) = 0.25
        // Canyon biome (blended in by vertex color R = biome 1 weight).
        // Sediment layering adapted from the user's MountainLayers shader.
        _CanyonFloorColor ("Canyon floor sand", Color) = (0.84, 0.58, 0.34, 1)
        _Layer1Color ("Sediment 1 (bottom)", Color) = (0.60, 0.30, 0.20, 1)
        _Layer2Color ("Sediment 2", Color) = (0.70, 0.50, 0.30, 1)
        _Layer3Color ("Sediment 3", Color) = (0.80, 0.60, 0.40, 1)
        _Layer4Color ("Sediment 4 (top)", Color) = (0.90, 0.80, 0.70, 1)
        _LayerScale ("Layer Scale", Range(0.01, 50)) = 0.6
        _LayerDistortion ("Layer Distortion", Range(0, 20)) = 1.5
        _LayerSharpness ("Layer Edge Sharpness", Range(0.1, 10)) = 1.6
        _LayerNoiseScale ("Layer Noise Scale", Range(1, 50)) = 15.0
        _SubLayerScale ("Sub-Layer Scale", Range(0, 100)) = 30.0
        _SubLayerIntensity ("Sub-Layer Intensity", Range(0, 1)) = 0.25
        _RockTex    ("Canyon rock albedo (triplanar)", 2D) = "white" {}
        _RockNormal ("Canyon rock normal", 2D) = "bump" {}
        _RockTexScale ("Rock texture scale (m)", Float) = 6.0
        // Alien biome (vertex color G = biome 2 weight)
        _AlienFlat  ("Alien rock - flat", Color)  = (0.47, 0.45, 0.44, 1)
        _AlienSteep ("Alien rock - steep", Color) = (0.32, 0.30, 0.32, 1)
        _PebbleTex    ("Alien pebble albedo (triplanar)", 2D) = "white" {}
        _PebbleNormal ("Alien pebble normal", 2D) = "bump" {}
        _PebbleTexScale ("Pebble texture scale (m)", Float) = 1.7
        _VertexAO ("Baked vertex AO strength", Range(0, 1)) = 0.75
        _MainTex   ("Sand albedo (triplanar)", 2D) = "white" {}
        _NormalTex ("Sand normal (triplanar)", 2D) = "bump" {}
        _TexScale       ("Texture scale (m per tile)", Float) = 3.5
        _TexBrightness  ("Texture brightness", Range(0.5, 2)) = 1.22
        _NormalStrength ("Normal strength", Range(0, 2)) = 1.0
        _TintScale    ("Large tint variation scale (m)", Float) = 13
        _TintStrength ("Large tint variation strength", Range(0, 0.3)) = 0.06
        _BackfaceDarken ("Backface darken (cave walls seen from wrong side)", Range(0, 1)) = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_MainTex);   SAMPLER(sampler_MainTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_RockTex);      SAMPLER(sampler_RockTex);
            TEXTURE2D(_RockNormal);   SAMPLER(sampler_RockNormal);
            TEXTURE2D(_PebbleTex);    SAMPLER(sampler_PebbleTex);
            TEXTURE2D(_PebbleNormal); SAMPLER(sampler_PebbleNormal);

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorFlat;
                half4 _ColorSteep;
                half4 _SheenColor;
                half4 _CanyonFloorColor;
                half4 _Layer1Color;
                half4 _Layer2Color;
                half4 _Layer3Color;
                half4 _Layer4Color;
                float _LayerScale;
                float _LayerDistortion;
                float _LayerSharpness;
                float _LayerNoiseScale;
                float _SubLayerScale;
                half _SubLayerIntensity;
                float _RockTexScale;
                half4 _AlienFlat;
                half4 _AlienSteep;
                float _PebbleTexScale;
                half _VertexAO;
                half _SlopeStart;
                half _SlopeEnd;
                half _ShadowWarmth;
                half _SheenStrength;
                half _SheenPower;
                half _GlitterStrength;
                float _TexScale;
                half _TexBrightness;
                half _NormalStrength;
                float _TintScale;
                half _TintStrength;
                half _BackfaceDarken;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;   // biome weights (R = biome 1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                half4  biome      : COLOR;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.biome = (half4)v.color;
                return o;
            }

            // --- cheap 2D value noise for the grain ---
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float VNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float FbmN(float2 p, int octaves)
            {
                float v = 0.0;
                float amp = 0.5;
                float freq = 1.0;
                for (int k = 0; k < octaves; k++)
                {
                    v += amp * VNoise(p * freq);
                    freq *= 2.0;
                    amp *= 0.5;
                }
                return v;
            }

            // --- anti-tiling: blend two samples of the same texture, the
            // second rotated + rescaled, switched by low-frequency noise so
            // the repeat pattern never lines up over distance ---
            #define DETILE_ROT_C 0.7986
            #define DETILE_ROT_S 0.6018

            float2 DetileUV(float2 uv)
            {
                float2 r = float2(DETILE_ROT_C * uv.x - DETILE_ROT_S * uv.y,
                                  DETILE_ROT_S * uv.x + DETILE_ROT_C * uv.y);
                return r * 1.37 + float2(7.31, 3.17);
            }

            half DetileMask(float2 uv)
            {
                return (half)smoothstep(0.35, 0.65, VNoise(uv * 0.31 + 11.7));
            }

            half3 SampleAlbedo(float2 uv, half m)
            {
                half3 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).rgb;
                half3 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, DetileUV(uv)).rgb;
                return lerp(a, b, m);
            }

            half3 SampleNormalTS(float2 uv, half m)
            {
                half3 a = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, uv), _NormalStrength);
                half3 b = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, DetileUV(uv)), _NormalStrength);
                // counter-rotate the rotated sample's tangent-plane component
                b.xy = half2(DETILE_ROT_C * b.x + DETILE_ROT_S * b.y,
                             -DETILE_ROT_S * b.x + DETILE_ROT_C * b.y);
                return lerp(a, b, m);
            }

            half4 Frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                if (!isFront) n = -n;

                // --- triplanar sampling (mesh has no UVs) ---
                float3 w = abs(n);
                w /= max(w.x + w.y + w.z, 1e-4);
                float invS = 1.0 / max(_TexScale, 0.01);
                float2 uvX = i.positionWS.zy * invS;
                float2 uvY = i.positionWS.xz * invS;
                float2 uvZ = i.positionWS.xy * invS;
                half mX = DetileMask(uvX);
                half mY = DetileMask(uvY);
                half mZ = DetileMask(uvZ);

                // biome weights (vertex-baked; A holds baked ambient occlusion)
                half canyonW = i.biome.r;
                half alienW  = i.biome.g;
                half sandW   = saturate(1.0h - canyonW - alienW);

                half3 texAlb = SampleAlbedo(uvX, mX) * w.x
                             + SampleAlbedo(uvY, mY) * w.y
                             + SampleAlbedo(uvZ, mZ) * w.z;

                // per-biome detail normals, blended in tangent space per plane
                half3 tnX = SampleNormalTS(uvX, mX) * sandW;
                half3 tnY = SampleNormalTS(uvY, mY) * sandW;
                half3 tnZ = SampleNormalTS(uvZ, mZ) * sandW;
                if (canyonW > 0.003h)
                {
                    float rS = 1.0 / max(_RockTexScale, 0.01);
                    tnX += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, i.positionWS.zy * rS), _NormalStrength) * canyonW;
                    tnY += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, i.positionWS.xz * rS), _NormalStrength) * canyonW;
                    tnZ += UnpackNormalScale(SAMPLE_TEXTURE2D(_RockNormal, sampler_RockNormal, i.positionWS.xy * rS), _NormalStrength) * canyonW;
                }
                if (alienW > 0.003h)
                {
                    float pS = 1.0 / max(_PebbleTexScale, 0.01);
                    tnX += UnpackNormalScale(SAMPLE_TEXTURE2D(_PebbleNormal, sampler_PebbleNormal, i.positionWS.zy * pS), _NormalStrength) * alienW;
                    tnY += UnpackNormalScale(SAMPLE_TEXTURE2D(_PebbleNormal, sampler_PebbleNormal, i.positionWS.xz * pS), _NormalStrength) * alienW;
                    tnZ += UnpackNormalScale(SAMPLE_TEXTURE2D(_PebbleNormal, sampler_PebbleNormal, i.positionWS.xy * pS), _NormalStrength) * alienW;
                }
                tnX = half3(tnX.xy + (half2)n.zy, abs(tnX.z) * (half)n.x);
                tnY = half3(tnY.xy + (half2)n.xz, abs(tnY.z) * (half)n.y);
                tnZ = half3(tnZ.xy + (half2)n.xy, abs(tnZ.z) * (half)n.z);
                float3 nDetail = normalize(tnX.zyx * w.x + tnY.xzy * w.y + tnZ.xyz * w.z);

                // large-scale brightness variation to break texture tiling
                float tv = VNoise(i.positionWS.xz / max(_TintScale, 0.5));
                half tintVar = 1.0h + (half)(tv - 0.5) * 2.0h * _TintStrength;

                // slope tint from the GEOMETRIC normal: slip faces (near the
                // angle of repose) read darker/warmer than flat windward sand
                half cosStart = cos(radians(_SlopeStart));
                half cosEnd = cos(radians(_SlopeEnd));
                half steep = 1.0h - smoothstep(cosEnd, cosStart, (half)saturate(n.y));

                // each biome contributes its own colors x its own texture
                half3 albedo = lerp(_ColorFlat.rgb, _ColorSteep.rgb, steep) * texAlb * sandW;

                // canyon biome: warped sediment layers modulated by the rock
                // texture on the walls, sandy floors/plateau tops; cross-faded
                // by the vertex-baked biome weight — no hard color borders
                if (canyonW > 0.003h)
                {
                    float rS = 1.0 / max(_RockTexScale, 0.01);
                    half3 rockTex = SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, i.positionWS.zy * rS).rgb * w.x
                                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, i.positionWS.xz * rS).rgb * w.y
                                  + SAMPLE_TEXTURE2D(_RockTex, sampler_RockTex, i.positionWS.xy * rS).rgb * w.z;
                    // --- sediment layers (adapted from MountainLayers) ---
                    float height = i.positionWS.y;
                    float2 largeUV = i.positionWS.xz * 0.005;
                    float lwx = FbmN(largeUV + float2(100, 200), 4) * 2.0 - 1.0;
                    float lwy = FbmN(largeUV + float2(300, 400), 4) * 2.0 - 1.0;
                    float2 warpedPos = i.positionWS.yz + float2(lwx, lwy) * _LayerDistortion * 20.0;
                    float tectonic = FbmN(i.positionWS.yx * 0.02, 2) * _LayerDistortion * 15.0;
                    height += tectonic;
                    float distortion = FbmN(warpedPos * _LayerNoiseScale * 0.01, 3) * 2.0 - 1.0;
                    float dh = height + distortion * _LayerDistortion;

                    float cycle = dh * _LayerScale * 0.1;
                    float layerValue = frac(cycle);
                    float layerIndex = floor(frac(cycle + 0.00001) * 4.0);
                    float edgeNoise = FbmN(warpedPos * _LayerNoiseScale * 0.05, 2) * 0.5;
                    layerValue = saturate(layerValue + edgeNoise * 0.2);
                    layerValue = pow(layerValue, _LayerSharpness);

                    half3 rock;
                    if (layerIndex < 1.0)      rock = lerp(_Layer1Color.rgb, _Layer2Color.rgb, (half)layerValue);
                    else if (layerIndex < 2.0) rock = lerp(_Layer2Color.rgb, _Layer3Color.rgb, (half)layerValue);
                    else if (layerIndex < 3.0) rock = lerp(_Layer3Color.rgb, _Layer4Color.rgb, (half)layerValue);
                    else                       rock = lerp(_Layer4Color.rgb, _Layer1Color.rgb, (half)layerValue);

                    // thin sub-layer striations
                    if (_SubLayerScale > 0.0)
                    {
                        float2 subUV = i.positionWS.xz * _SubLayerScale * 0.01;
                        subUV += float2(FbmN(subUV * 0.5 + float2(700, 800), 2),
                                        FbmN(subUV * 0.5 + float2(900, 1000), 2)) * 0.5;
                        float sp = FbmN(subUV, 4);
                        sp = sin(sp * 20.0 + dh * _SubLayerScale * 0.5) * 0.5 + 0.5;
                        rock = lerp(rock, rock * 0.7h, (half)step(0.6, sp) * _SubLayerIntensity);
                    }

                    // sediment colors modulated by the rock texture on walls;
                    // sandy floors & plateau tops keep the sand texture
                    rock *= rockTex * 1.7h; // rock tex mean ~0.55 -> renormalize
                    half3 canyonAlb = lerp(_CanyonFloorColor.rgb * texAlb, rock, steep);
                    albedo += canyonAlb * canyonW;
                }

                // alien biome: cold rock colors x packed-pebble texture
                if (alienW > 0.003h)
                {
                    float pS = 1.0 / max(_PebbleTexScale, 0.01);
                    half3 pebTex = SAMPLE_TEXTURE2D(_PebbleTex, sampler_PebbleTex, i.positionWS.zy * pS).rgb * w.x
                                 + SAMPLE_TEXTURE2D(_PebbleTex, sampler_PebbleTex, i.positionWS.xz * pS).rgb * w.y
                                 + SAMPLE_TEXTURE2D(_PebbleTex, sampler_PebbleTex, i.positionWS.xy * pS).rgb * w.z;
                    half3 alienAlb = lerp(_AlienFlat.rgb, _AlienSteep.rgb, steep) * pebTex * 1.9h;
                    albedo += alienAlb * alienW;
                }

                albedo *= _TexBrightness * tintVar;
                if (!isFront) albedo *= 1.0h - _BackfaceDarken;

                // lighting with the detail normal: main light + shadows + SH
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ndotl = saturate(dot(nDetail, mainLight.direction));
                // soften the terminator a touch: sand scatters light
                ndotl = ndotl * 0.85h + 0.15h * sqrt(ndotl);
                half3 lighting = mainLight.color * (mainLight.shadowAttenuation * ndotl);

                // desert shadows stay WARM: sand bounces orange light into
                // them, so pull the (usually blue-ish sky) ambient toward a
                // warm tint of the same brightness
                half3 ambient = SampleSH(nDetail);
                half ambLum = dot(ambient, half3(0.299h, 0.587h, 0.114h));
                ambient = lerp(ambient, ambLum * half3(1.30h, 0.80h, 0.52h), _ShadowWarmth);

                // baked vertex AO: darkens crevices, strata seams, overhang
                // undersides (the raymarchers' curvature/occlusion analog)
                half occ = lerp(1.0h, i.biome.a, _VertexAO);
                ambient *= occ;
                lighting *= lerp(1.0h, occ, 0.35h);

                half3 color = albedo * (lighting + ambient);

                // grazing-angle sheen: quartz grains scatter toward the eye at
                // low view angles (bright rims on lit crests)
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half fres = pow(1.0h - (half)saturate(dot(n, V)), _SheenPower);
                half sunlit = saturate(dot(nDetail, mainLight.direction)) * mainLight.shadowAttenuation;
                half sandy = saturate(1.0h - 0.7h * canyonW - 0.85h * alienW); // rock doesn't sheen/glitter like quartz sand
                color += _SheenColor.rgb * mainLight.color *
                         (fres * _SheenStrength * sandy * (0.25h + 0.75h * sunlit));

                // sparse sparkle glints near the sun's mirror direction
                if (_GlitterStrength > 0.001h)
                {
                    float camDist = distance(_WorldSpaceCameraPos, i.positionWS);
                    half gFade = (half)saturate(1.0 - camDist / 45.0);
                    if (gFade > 0.001h)
                    {
                        float3 c3 = floor(i.positionWS * 41.0);
                        half gMask = (half)step(0.88, Hash21(c3.xz + c3.y * 0.731));
                        float3 jitter = float3(Hash21(c3.xy + 3.1), Hash21(c3.yz + 7.7), Hash21(c3.zx + 9.3)) - 0.5;
                        float3 gN = normalize(nDetail + jitter * 1.1);
                        half glint = pow(saturate(dot(reflect(-mainLight.direction, gN), V)), 48.0h);
                        color += mainLight.color * (glint * gMask * gFade * _GlitterStrength * sandy * mainLight.shadowAttenuation);
                    }
                }

                color = MixFog(color, i.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Off
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 DepthFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
