// Procedural sand terrain shader (URP).
// - No UVs needed: detail grain is generated procedurally with TRIPLANAR
//   projection (sampled from the three world axes, blended by the normal).
// - Slope-based tinting: slip faces (near the angle of repose) shade darker
//   and warmer than the flat windward slopes, which is what visually sells
//   dunes.
// - Double-sided with normal flip so carved caves are lit correctly inside.
// - Fog-aware (works with the linear fog set up by DesertAtmosphere).
// - Each biome's shading lives in its own module under Biomes/ (BiomeSand,
//   BiomeCanyon, BiomeAlien, BiomeFrost) so they can be authored/iterated
//   independently; this shader composes them into one pass/one draw call,
//   weighted by the vertex-baked biome weights from BiomeDensityField.
Shader "MarchingCubes/Sand Terrain"
{
    Properties
    {
        // --- Per vertex-color CHANNEL identity -------------------------------
        // Channel 0 = implicit remainder weight, 1 = vertex R, 2 = G, 3 = B
        // (fixed by BiomeDensityField.GetVertexColor). WHICH biome asset landed
        // on a channel, and therefore its style/colors/sharpness, is pushed at
        // runtime by MCChunkManager from that Biome asset's own fields — these
        // defaults just mirror the original Desert/Canyon/Alien/Frost setup.
        // Style: 0 = Sand, 1 = Canyon, 2 = Alien, 3 = Frost (Biome.SurfaceStyle).
        _Chan0Style ("Channel 0 (implicit) style", Float) = 0
        _Chan0Flat  ("Channel 0 flat color",  Color) = (0.83, 0.55, 0.26, 1)
        _Chan0Steep ("Channel 0 steep color", Color) = (0.68, 0.40, 0.17, 1)
        _Chan0Sharpness ("Channel 0 blend sharpness", Range(0.2, 6)) = 1
        _Chan1Style ("Channel 1 (vertex R) style", Float) = 1
        _Chan1Flat  ("Channel 1 flat color",  Color) = (0.84, 0.58, 0.34, 1)
        _Chan1Steep ("Channel 1 steep color", Color) = (0.60, 0.35, 0.22, 1)
        _Chan1Sharpness ("Channel 1 blend sharpness", Range(0.2, 6)) = 1
        _Chan2Style ("Channel 2 (vertex G) style", Float) = 2
        _Chan2Flat  ("Channel 2 flat color",  Color) = (0.47, 0.45, 0.44, 1)
        _Chan2Steep ("Channel 2 steep color", Color) = (0.32, 0.30, 0.32, 1)
        _Chan2Sharpness ("Channel 2 blend sharpness", Range(0.2, 6)) = 1
        _Chan3Style ("Channel 3 (vertex B) style", Float) = 3
        _Chan3Flat  ("Channel 3 flat color",  Color) = (0.80, 0.90, 0.96, 1)
        _Chan3Steep ("Channel 3 steep color", Color) = (0.55, 0.70, 0.82, 1)
        _Chan3Sharpness ("Channel 3 blend sharpness", Range(0.2, 6)) = 1

        _SlopeStart ("Slope tint start (deg)", Range(0, 90)) = 21
        _SlopeEnd   ("Slope tint full (deg)",  Range(0, 90)) = 33
        _ShadowWarmth ("Shadow warmth (bounced sand light)", Range(0, 1)) = 0.55
        _SheenColor   ("Grazing sheen color", Color) = (1.0, 0.80, 0.55, 1)
        _SheenStrength ("Grazing sheen strength", Range(0, 1)) = 0.18
        _SheenPower    ("Grazing sheen tightness", Range(1, 8)) = 3.5
        _GlitterStrength ("Sun glitter strength", Range(0, 1)) = 0.25
        // --- Style-specific tuning (shared globally by whichever channel(s)
        // use that style; see Biome.albedo's tooltip for the tradeoff) ------
        // Canyon: sediment layering adapted from the user's MountainLayers shader.
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
        // Alien: pebble textures (flat/steep color comes from the per-channel properties above)
        _PebbleTex    ("Alien pebble albedo (triplanar)", 2D) = "white" {}
        _PebbleNormal ("Alien pebble normal", 2D) = "bump" {}
        _PebbleTexScale ("Pebble texture scale (m)", Float) = 1.7
        // Frost: fully procedural, no textures needed (flat/steep color also
        // comes from the per-channel properties above)
        _FrostCrackColor ("Frost crack color", Color) = (0.14, 0.24, 0.32, 1)
        _FrostGlowColor  ("Frost crack glow color", Color) = (0.35, 0.75, 1.0, 1)
        _FrostGlowStrength ("Frost crack glow strength", Range(0, 2)) = 0.6
        _FrostScale      ("Frost pattern scale (m)", Float) = 9
        _FrostCrackFreq  ("Frost crack density", Range(1, 20)) = 6
        _FrostCrackWidth ("Frost crack width", Range(0.01, 0.5)) = 0.08
        _FrostWarpStrength ("Frost crack warp (organic wiggle)", Range(0, 3)) = 1.2
        _FrostBumpStrength ("Frost bump strength", Range(0, 5)) = 1.5
        _FrostSparkleStrength ("Frost sparkle strength", Range(0, 1)) = 0.35
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

            #include "Biomes/BiomeCommon.hlsl"
            #include "Biomes/BiomeSand.hlsl"
            #include "Biomes/BiomeCanyon.hlsl"
            #include "Biomes/BiomeAlien.hlsl"
            #include "Biomes/BiomeFrost.hlsl"

            // Dispatches ONE vertex-color channel's weight to whichever
            // shading module its STYLE (read from the Biome asset that
            // landed on this channel, via MCChunkManager) selects. This is
            // what makes rendering data-driven: style travels with the Biome
            // asset, not with a fixed channel/array-index assumption.
            // Style values must match Biome.SurfaceStyle: 0=Sand,1=Canyon,2=Alien,3=Frost.
            #define EVALUATE_CHANNEL(STYLE, FLATCOL, STEEPCOL, CHW) \
                if ((CHW) > 0.003h) \
                { \
                    int _style = (int)round(STYLE); \
                    if (_style == 0) \
                        EvaluateSand(uvX, uvY, uvZ, mX, mY, mZ, texAlb, FLATCOL, STEEPCOL, steep, _NormalStrength, \
                                     CHW, albedo, tnX, tnY, tnZ); \
                    else if (_style == 1) \
                        EvaluateCanyon(i.positionWS, w, texAlb, _CanyonFloorColor.rgb, \
                                       _Layer1Color.rgb, _Layer2Color.rgb, _Layer3Color.rgb, _Layer4Color.rgb, \
                                       _LayerScale, _LayerDistortion, _LayerSharpness, _LayerNoiseScale, \
                                       _SubLayerScale, _SubLayerIntensity, _RockTexScale, steep, _NormalStrength, \
                                       CHW, albedo, tnX, tnY, tnZ); \
                    else if (_style == 2) \
                        EvaluateAlien(i.positionWS, w, FLATCOL, STEEPCOL, _PebbleTexScale, steep, _NormalStrength, \
                                      CHW, albedo, tnX, tnY, tnZ); \
                    else \
                        EvaluateFrost(i.positionWS, w, steep, FLATCOL, STEEPCOL, _FrostCrackColor.rgb, \
                                      _FrostGlowColor.rgb, _FrostGlowStrength, _FrostScale, _FrostCrackFreq, _FrostCrackWidth, \
                                      _FrostWarpStrength, _FrostBumpStrength, _FrostSparkleStrength, \
                                      CHW, albedo, emissive, tnX, tnY, tnZ); \
                }

            CBUFFER_START(UnityPerMaterial)
                half _Chan0Style;
                half4 _Chan0Flat;
                half4 _Chan0Steep;
                half _Chan0Sharpness;
                half _Chan1Style;
                half4 _Chan1Flat;
                half4 _Chan1Steep;
                half _Chan1Sharpness;
                half _Chan2Style;
                half4 _Chan2Flat;
                half4 _Chan2Steep;
                half _Chan2Sharpness;
                half _Chan3Style;
                half4 _Chan3Flat;
                half4 _Chan3Steep;
                half _Chan3Sharpness;
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
                float _PebbleTexScale;
                half4 _FrostCrackColor;
                half4 _FrostGlowColor;
                half _FrostGlowStrength;
                float _FrostScale;
                float _FrostCrackFreq;
                half _FrostCrackWidth;
                float _FrostWarpStrength;
                half _FrostBumpStrength;
                half _FrostSparkleStrength;
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
                float4 color      : COLOR;   // channel weights: R/G/B = channels 1/2/3, A = baked AO
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

                // slope tint from the GEOMETRIC normal: slip faces (near the
                // angle of repose) read darker/warmer than flat windward
                // sand. Shared by every biome module below.
                half cosStart = cos(radians(_SlopeStart));
                half cosEnd = cos(radians(_SlopeEnd));
                half steep = 1.0h - smoothstep(cosEnd, cosStart, (half)saturate(n.y));

                // vertex-baked channel weights (partition of unity): channel 0
                // is the implicit remainder, 1/2/3 are vertex R/G/B. Each is
                // independently re-sharpened using ITS OWN biome's sharpness,
                // then renormalized so they still sum to 1.
                half w1raw = i.biome.r;
                half w2raw = i.biome.g;
                half w3raw = i.biome.b;
                half w0raw = saturate(1.0h - w1raw - w2raw - w3raw);
                half w0s = BiomeSharpen(w0raw, _Chan0Sharpness);
                half w1s = BiomeSharpen(w1raw, _Chan1Sharpness);
                half w2s = BiomeSharpen(w2raw, _Chan2Sharpness);
                half w3s = BiomeSharpen(w3raw, _Chan3Sharpness);
                half wSum = max(w0s + w1s + w2s + w3s, 1e-4h);
                half chan0W = w0s / wSum;
                half chan1W = w1s / wSum;
                half chan2W = w2s / wSum;
                half chan3W = w3s / wSum;

                half3 texAlb = SandDetailAlbedo(uvX, uvY, uvZ, mX, mY, mZ, w);

                half3 albedo = 0;
                half3 emissive = 0;
                half3 tnX = 0, tnY = 0, tnZ = 0;

                EVALUATE_CHANNEL(_Chan0Style, _Chan0Flat.rgb, _Chan0Steep.rgb, chan0W)
                EVALUATE_CHANNEL(_Chan1Style, _Chan1Flat.rgb, _Chan1Steep.rgb, chan1W)
                EVALUATE_CHANNEL(_Chan2Style, _Chan2Flat.rgb, _Chan2Steep.rgb, chan2W)
                EVALUATE_CHANNEL(_Chan3Style, _Chan3Flat.rgb, _Chan3Steep.rgb, chan3W)

                tnX = half3(tnX.xy + (half2)n.zy, abs(tnX.z) * (half)n.x);
                tnY = half3(tnY.xy + (half2)n.xz, abs(tnY.z) * (half)n.y);
                tnZ = half3(tnZ.xy + (half2)n.xy, abs(tnZ.z) * (half)n.z);
                float3 nDetail = normalize(tnX.zyx * w.x + tnY.xzy * w.y + tnZ.xyz * w.z);

                // large-scale brightness variation to break texture tiling
                float tv = VNoise(i.positionWS.xz / max(_TintScale, 0.5));
                half tintVar = 1.0h + (half)(tv - 0.5) * 2.0h * _TintStrength;

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

                half3 color = albedo * (lighting + ambient) + emissive;

                // grazing-angle sheen: quartz grains scatter toward the eye at
                // low view angles (bright rims on lit crests)
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half fres = pow(1.0h - (half)saturate(dot(n, V)), _SheenPower);
                half sunlit = saturate(dot(nDetail, mainLight.direction)) * mainLight.shadowAttenuation;
                // Sheen/glitter is a Sand-style-only effect: sum the weight of
                // whichever channel(s) actually are Sand-styled right now
                // (data-driven -- not tied to a fixed channel index).
                half sandy = 0.0h;
                sandy += (round(_Chan0Style) < 0.5h) ? chan0W : 0.0h;
                sandy += (round(_Chan1Style) < 0.5h) ? chan1W : 0.0h;
                sandy += (round(_Chan2Style) < 0.5h) ? chan2W : 0.0h;
                sandy += (round(_Chan3Style) < 0.5h) ? chan3W : 0.0h;
                sandy = saturate(sandy);
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
