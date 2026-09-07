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
        // --- Per-biome identity is NOT a material property ---------------------
        // Which biome a pixel belongs to is evaluated from world position
        // (Biomes/BiomeSelectGlobals.hlsl -> Compute/BiomeSelect.hlsl), and each
        // biome's style, palette, blend sharpness and height lines arrive as
        // shader globals published by MCChunkManager. See BIOME_SHADING.md.
        // Below: only the global, style-level tuning shared by whichever
        // biomes use a style.

        // Planet mode: when enabled, "up" for slope tinting and sediment
        // banding is radial from a center point instead of world Y. Pushed
        // automatically by MCChunkManager when the active field is a
        // PlanetField -- see MCChunkManager.ApplyPlanetShaderParams. 0 (flat
        // world) with _PlanetCenter at the origin reduces every formula below
        // to exactly the original flat-world math, so this is fully backward
        // compatible with non-planet materials.
        _UseSphericalUp ("Planet-relative up (0=flat world Y, 1=radial from center)", Range(0, 1)) = 0
        _PlanetCenter ("Planet center (world)", Vector) = (0, 0, 0, 0)
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
        // Dolomite: pale banded limestone walls, grey scree aprons, meadow floors
        // (BiomeDolomite.hlsl). Flat/Steep colours of the channel are the
        // meadow and the rock; these are the rest of its palette.
        _DoloBandColor ("Dolomite band color (alternate stratum)", Color) = (0.86, 0.80, 0.72, 1)
        _DoloScreeColor ("Dolomite scree color", Color) = (0.58, 0.56, 0.53, 1)
        _DoloBandSpacing ("Dolomite band spacing (m)", Range(0.5, 40)) = 6.0
        _DoloBandDistortion ("Dolomite band undulation (m)", Range(0, 20)) = 3.0
        _DoloBandContrast ("Dolomite band contrast", Range(0, 1)) = 0.6
        _DoloScreeStart ("Dolomite scree starts (steep)", Range(0, 1)) = 0.25
        _DoloScreeEnd ("Dolomite scree ends / wall starts (steep)", Range(0, 1)) = 0.75
        // NOTE: localHeight is the distance from the planet centre in globe mode (~radius), so this is absolute, not above-nominal.
        _DoloSnowLine ("Dolomite snow line (local height, m; off by default)", Float) = 1000000000
        _DoloSnowBlend ("Dolomite snow blend (m)", Range(1, 100)) = 25
        // Mountain: eroded alpine rock (BiomeMountain.hlsl). The biome's Flat is
        // the grass, Steep the rock; its snow/grass lines come from the globals.
        _MtnRockColor2 ("Mountain rock stratum color", Color) = (0.42, 0.40, 0.38, 1)
        _MtnScreeColor ("Mountain scree color", Color) = (0.50, 0.47, 0.43, 1)
        _MtnSnowColor ("Mountain snow color", Color) = (0.93, 0.95, 0.98, 1)
        _MtnRockSlope ("Mountain rock starts (cos slope)", Range(0, 1)) = 0.80
        _MtnRockSlopeWidth ("Mountain rock slope band", Range(0.01, 0.5)) = 0.14
        _MtnBandSpacing ("Mountain strata spacing (m)", Range(0.5, 60)) = 9
        _MtnBandContrast ("Mountain strata contrast", Range(0, 1)) = 0.35
        // Desert Mountain: eroded sandstone (BiomeDesertMountain.hlsl). The biome's
        // Flat is the sand tint, Steep the rock tint over the canyon rock texture.
        _DsrtBandColor ("Desert mountain stratum color", Color) = (0.62, 0.36, 0.22, 1)
        _DsrtVarnishColor ("Desert varnish color", Color) = (0.30, 0.20, 0.16, 1)
        _DsrtBandSpacing ("Desert mountain strata spacing (m)", Range(0.5, 60)) = 7
        _DsrtBandContrast ("Desert mountain strata contrast", Range(0, 1)) = 0.45
        _DsrtVarnish ("Desert varnish strength", Range(0, 1)) = 0.45
        _DsrtSandSlope ("Desert mountain sand holds up to (cos slope)", Range(0, 1)) = 0.86
        _DsrtSandSlopeWidth ("Desert mountain sand slope band", Range(0.01, 0.5)) = 0.12
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
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            #include "Biomes/BiomeSelectGlobals.hlsl"
            #include "Biomes/BiomeCommon.hlsl"
            #include "Biomes/BiomeSand.hlsl"
            #include "Biomes/BiomeCanyon.hlsl"
            #include "Biomes/BiomeAlien.hlsl"
            #include "Biomes/BiomeFrost.hlsl"
            #include "Biomes/BiomeDolomite.hlsl"
            #include "Biomes/BiomeMountain.hlsl"
            #include "Biomes/BiomeDesertMountain.hlsl"

            // Per-pixel biome weights (1) or per-vertex weights interpolated
            // across the triangle (0). Per-pixel gives exact boundaries on every
            // LOD ring; per-vertex is free and looks like the old vertex-colour
            // bake. Flip to 0 if the selection noise measures too costly.
            #define MC_BIOME_WEIGHTS_PER_PIXEL 1

            // Shades ONE biome slot with whichever module its style selects.
            // Style values must match Biome.SurfaceStyle:
            // 0=Sand,1=Canyon,2=Alien,3=Frost,4=Dolomite,5=Mountain,6=DesertMountain.
            // FLATCOL/STEEPCOL/LINES are that biome's table entries.
            #define EVALUATE_STYLE(STYLE, FLATCOL, STEEPCOL, LINES, CHW) \
                { \
                    int _style = (STYLE); \
                    if (_style == 0) \
                        EvaluateSand(uvX, uvY, uvZ, mX, mY, mZ, texAlb, FLATCOL, STEEPCOL, steep, _NormalStrength, \
                                     CHW, albedo, tnX, tnY, tnZ); \
                    else if (_style == 1) \
                        EvaluateCanyon(i.positionWS, w, texAlb, _CanyonFloorColor.rgb, \
                                       _Layer1Color.rgb, _Layer2Color.rgb, _Layer3Color.rgb, _Layer4Color.rgb, \
                                       _LayerScale, _LayerDistortion, _LayerSharpness, _LayerNoiseScale, \
                                       _SubLayerScale, _SubLayerIntensity, _RockTexScale, steep, _NormalStrength, localHeight, \
                                       CHW, albedo, tnX, tnY, tnZ); \
                    else if (_style == 2) \
                        EvaluateAlien(i.positionWS, w, FLATCOL, STEEPCOL, _PebbleTexScale, steep, _NormalStrength, \
                                      CHW, albedo, tnX, tnY, tnZ); \
                    else if (_style == 4) \
                        EvaluateDolomite(i.positionWS, w, steep, localHeight, FLATCOL, STEEPCOL, \
                                         _DoloBandColor.rgb, _DoloScreeColor.rgb, \
                                         _DoloBandSpacing, _DoloBandDistortion, _DoloBandContrast, \
                                         _DoloScreeStart, _DoloScreeEnd, _DoloSnowLine, _DoloSnowBlend, \
                                         (LINES).x, (LINES).y, \
                                         _RockTexScale, _NormalStrength, \
                                         CHW, albedo, tnX, tnY, tnZ); \
                    else if (_style == 5) \
                        EvaluateMountain(i.positionWS, w, n, up, localHeight, FLATCOL, STEEPCOL, \
                                         _MtnRockColor2.rgb, _MtnScreeColor.rgb, _MtnSnowColor.rgb, \
                                         _MtnRockSlope, _MtnRockSlopeWidth, _MtnBandSpacing, _MtnBandContrast, \
                                         (LINES).x, (LINES).y, (LINES).z, (LINES).w, \
                                         _RockTexScale, _NormalStrength, \
                                         CHW, albedo, tnX, tnY, tnZ); \
                    else if (_style == 6) \
                        EvaluateDesertMountain(i.positionWS, w, n, up, localHeight, uvX, uvY, uvZ, mX, mY, mZ, texAlb, \
                                         FLATCOL, STEEPCOL, _DsrtBandColor.rgb, _DsrtVarnishColor.rgb, \
                                         _DsrtBandSpacing, _DsrtBandContrast, _DsrtVarnish, \
                                         _DsrtSandSlope, _DsrtSandSlopeWidth, (LINES).x, (LINES).y, \
                                         _RockTexScale, _NormalStrength, \
                                         CHW, albedo, tnX, tnY, tnZ); \
                    else \
                        EvaluateFrost(i.positionWS, w, steep, FLATCOL, STEEPCOL, _FrostCrackColor.rgb, \
                                      _FrostGlowColor.rgb, _FrostGlowStrength, _FrostScale, _FrostCrackFreq, _FrostCrackWidth, \
                                      _FrostWarpStrength, _FrostBumpStrength, _FrostSparkleStrength, \
                                      CHW, albedo, emissive, tnX, tnY, tnZ); \
                }

            CBUFFER_START(UnityPerMaterial)
                half _UseSphericalUp;
                float4 _PlanetCenter;
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
                half4 _DoloBandColor;
                half4 _DoloScreeColor;
                float _DoloBandSpacing;
                float _DoloBandDistortion;
                half _DoloBandContrast;
                half _DoloScreeStart;
                half _DoloScreeEnd;
                float _DoloSnowLine;
                half _DoloSnowBlend;
                half4 _MtnRockColor2;
                half4 _MtnScreeColor;
                half4 _MtnSnowColor;
                half _MtnRockSlope;
                half _MtnRockSlopeWidth;
                float _MtnBandSpacing;
                half _MtnBandContrast;
                half4 _DsrtBandColor;
                half4 _DsrtVarnishColor;
                float _DsrtBandSpacing;
                half _DsrtBandContrast;
                half _DsrtVarnish;
                half _DsrtSandSlope;
                half _DsrtSandSlopeWidth;
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
                float4 color      : COLOR;   // A = baked AO. RGB still carry the old biome weights for the grass scatter (transitional, see BIOME_SHADING.md); this shader ignores them.
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half   fogFactor  : TEXCOORD2;
                half4  vcol       : COLOR;      // .a = baked AO
            #if !MC_BIOME_WEIGHTS_PER_PIXEL
                float4 bw0        : TEXCOORD3;  // biome weights 0..3 (per-vertex variant)
                float4 bw1        : TEXCOORD4;  // biome weights 4..7
            #endif
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                o.vcol = (half4)v.color;
            #if !MC_BIOME_WEIGHTS_PER_PIXEL
                float vw[MC_MAX_BIOMES]; int vn;
                MC_BiomeShadeWeights(o.positionWS, vw, vn);
                o.bw0 = float4(vw[0], vw[1], vw[2], vw[3]);
                o.bw1 = float4(vw[4], vw[5], vw[6], vw[7]);
            #endif
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

                // "up" for slope tinting / sediment banding: radial from
                // _PlanetCenter when in planet mode, otherwise plain world Y.
                // With _UseSphericalUp=0 and _PlanetCenter=(0,0,0) (the
                // defaults) this reduces to exactly (0,1,0) and localHeight
                // reduces to exactly positionWS.y -- the original flat-world
                // math, unchanged.
                float3 rel = i.positionWS - _PlanetCenter.xyz;
                float3 up = lerp(float3(0, 1, 0), rel / max(length(rel), 1e-4), (float)_UseSphericalUp);
                float localHeight = dot(rel, up);

                // slope tint from the GEOMETRIC normal: slip faces (near the
                // angle of repose) read darker/warmer than flat windward
                // sand. Shared by every biome module below.
                half cosStart = cos(radians(_SlopeStart));
                half cosEnd = cos(radians(_SlopeEnd));
                half steep = 1.0h - smoothstep(cosEnd, cosStart, (half)saturate(dot(n, up)));

                // Biome weights from world position (BiomeSelect.hlsl), sharpened
                // per biome and renormalised. No vertex data involved: the same
                // function the density compute selected the geometry with.
                float bw[MC_MAX_BIOMES];
                int biomeCount;
            #if MC_BIOME_WEIGHTS_PER_PIXEL
                MC_BiomeShadeWeights(i.positionWS, bw, biomeCount);
            #else
                biomeCount = (int)_BiomeSelParams.w;
                bw[0] = i.bw0.x; bw[1] = i.bw0.y; bw[2] = i.bw0.z; bw[3] = i.bw0.w;
                bw[4] = i.bw1.x; bw[5] = i.bw1.y; bw[6] = i.bw1.z; bw[7] = i.bw1.w;
            #endif

                half3 texAlb = SandDetailAlbedo(uvX, uvY, uvZ, mX, mY, mZ, w);

                half3 albedo = 0;
                half3 emissive = 0;
                half3 tnX = 0, tnY = 0, tnZ = 0;

                // Sheen/glitter below is a Sand-style-only effect: `sandy` sums the
                // weight of every Sand-styled biome present at this pixel.
                half sandy = 0.0h;
                // The trip count is uniform (a global), so this is not divergent
                // flow control around the texture samples inside the modules;
                // the per-pixel weight test is an ordinary branch.
                [loop] for (int b = 0; b < biomeCount; b++)
                {
                    half chw = (half)bw[b];
                    if (chw > 0.003h)
                    {
                        int style = (int)round(_BiomeShadeA[b].x);
                        half3 flatCol = (half3)_BiomeShadeFlat[b].rgb;
                        half3 steepCol = (half3)_BiomeShadeSteep[b].rgb;
                        float4 lines = _BiomeShadeLines[b];
                        EVALUATE_STYLE(style, flatCol, steepCol, lines, chw)
                        if (style == 0) sandy += chw;
                    }
                }
                sandy = saturate(sandy);

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
                half occ = lerp(1.0h, i.vcol.a, _VertexAO);
                ambient *= occ;
                lighting *= lerp(1.0h, occ, 0.35h);

                half3 color = albedo * (lighting + ambient) + emissive;

                // grazing-angle sheen: quartz grains scatter toward the eye at
                // low view angles (bright rims on lit crests)
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                half fres = pow(1.0h - (half)saturate(dot(n, V)), _SheenPower);
                half sunlit = saturate(dot(nDetail, mainLight.direction)) * mainLight.shadowAttenuation;
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
