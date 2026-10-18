// The tree octopus (URP), drawn by CreatureRenderer like every creature: one instanced, GPU-skinned draw
// per species (Creature/CreatureSkin.hlsl with CREATURE_FOUR_BONES: the web blends four bones through uv3,
// normals go through the cofactor of the blended affine bone). Part ids (uv1.x): 3 eye, 4 arm, 5 web,
// 6 mantle/head, 7 siphon. uv0 = (arc length m, angle / 2pi); arm angle 0 = the ventral midline.
//
// Look (tree octopus design §5): wet skin (GGX + a clear coat), red-orange light through thin arms and the
// web, Voronoi chromatophores that expand with the per-instance `chroma` (0 pale .. 1 dark mottled), coarse
// camouflage mottling, passing-cloud bands driven by `cloudPhase`, papillae bumps, two staggered sucker rows
// with rim, cup and occlusion, counter-shading, golden eyes with a horizontal pupil, a deimatic `flash`, and
// the generic cloak (Forward only: the shadow and the depth/normals - contact AO - remain).
Shader "MarchingCubes/CreatureOctopus"
{
    Properties
    {
        _Wrap ("Wrap lighting", Range(0, 1)) = 0.35
        _SubsurfaceColor ("Subsurface tint (terminator)", Color) = (1, 0.42, 0.25, 1)
        _BellyOcclusion ("Belly sky occlusion", Range(0, 1)) = 0.4
        _GroundBounce ("Ground bounce colour", Color) = (0.55, 0.45, 0.32, 1)
        _GroundBounceStrength ("Ground bounce strength", Range(0, 1)) = 0.25
        _RimPower ("Rim power", Range(1, 8)) = 3.5
        _AmbientTint ("Ambient warm tint", Color) = (1, 0.82, 0.62, 1)
        _AmbientWarmth ("Ambient warmth", Range(0, 1)) = 0.35
        _Smoothness ("Skin smoothness (wet)", Range(0, 1)) = 0.6
        _CoatSmoothness ("Clear coat smoothness", Range(0, 1)) = 0.93
        _Transmission ("Light through thin parts", Range(0, 2)) = 0.9
        _ThinRadius ("Radius below which a part reads thin (m)", Range(0.005, 0.1)) = 0.035
        _MottleScale ("Camouflage mottle scale (1/m)", Range(1, 40)) = 9
        _PapillaeHeight ("Papillae height (m)", Range(0, 0.004)) = 0.0012
        _SuckerDepth ("Sucker cup depth (m)", Range(0, 0.004)) = 0.0015
        _IrisColor ("Iris colour", Color) = (0.78, 0.6, 0.22, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back

        HLSLINCLUDE
        #define CREATURE_FOUR_BONES 1
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Creature/CreatureSkin.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half _Wrap;
        half4 _SubsurfaceColor;
        half _BellyOcclusion;
        half4 _GroundBounce;
        half _GroundBounceStrength;
        half _RimPower;
        half4 _AmbientTint;
        half _AmbientWarmth;
        half _Smoothness;
        half _CoatSmoothness;
        half _Transmission;
        float _ThinRadius;
        float _MottleScale;
        float _PapillaeHeight;
        float _SuckerDepth;
        half4 _IrisColor;
        CBUFFER_END

        // Per species, from CreatureRenderer's property block.
        float4 _Dorsal;
        float4 _Flank;
        float4 _Belly;
        float4 _PatternColor;   // the dark chromatophore colour
        float4 _SurfaceParams;  // smoothness, rim, subsurface, 0
        float4 _BodyParams;     // arm length, 1 / length, web depth (m), 1
        float4 _OctoSucker;     // sucker colour
        float4 _OctoSubsurface; // light-through colour
        float4 _OctoParams;     // chromatophores per metre, papillae, sucker size (x arm radius), cloud speed
        float4 _OctoParams2;    // wetness, cloak glint, arm root radius, arm length
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Creature/CreaturePattern.hlsl"
            #include "Creature/CreatureLighting.hlsl"
            #include "Creature/CreatureCloak.hlsl"

            #define OCTO_PART_EYE 3
            #define OCTO_PART_ARM 4
            #define OCTO_PART_WEB 5
            #define OCTO_PART_MANTLE 6
            #define OCTO_PART_SIPHON 7

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionWS : TEXCOORD0;               // w = rest radius from the bone axis
                float4 normalWS : TEXCOORD1;                 // w = fog factor
                float4 uv : TEXCOORD2;                       // uv0.xy, uv1.xy
                nointerpolation float4 inst : TEXCOORD3;     // seed, hueShift, valueShift, cloak
                nointerpolation float4 inst2 : TEXCOORD4;    // chroma, flash, cloudPhase, sizeScale
            };

            Varyings Vert(CreatureVertexIn v, uint iid : SV_InstanceID)
            {
                CreatureSkinned s = CreatureSkin(v, iid);
                Varyings o;
                o.positionCS = TransformWorldToHClip(s.posWS);
                o.positionWS = float4(s.posWS, s.radius);
                o.normalWS = float4(s.normalWS, ComputeFogFactor(o.positionCS.z));
                o.uv = float4(v.uv0, v.uv1);
                o.inst = float4(s.inst.seed, s.inst.hueShift, s.inst.valueShift, s.inst.cloak);
                o.inst2 = float4(s.inst.chroma, s.inst.flash, s.inst.cloudPhase, s.inst.sizeScale);
                return o;
            }

            float3 PerturbNormal(float3 n, float3 posWS, float height)
            {
                float3 dpdx = ddx(posWS), dpdy = ddy(posWS);
                float dhdx = ddx(height), dhdy = ddy(height);
                float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
                float det = dot(dpdx, r1);
                float3 grad = (det < 0.0 ? -1.0 : 1.0) * (dhdx * r1 + dhdy * r2);
                float3 bumped = abs(det) * n - grad;
                float len2 = dot(bumped, bumped);
                return len2 > 1e-20 ? bumped * rsqrt(len2) : n;
            }

            // Voronoi in 2D: distance to the nearest feature point and its cell's random id.
            float2 Voronoi(float2 p, uint salt)
            {
                float2 c = floor(p);
                float best = 8.0, id = 0.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    int3 cell = int3((int)c.x + x, (int)c.y + y, 0);
                    float2 f = float2(CreatureHash01(cell, salt), CreatureHash01(cell, salt + 77u));
                    float2 d = (c + float2(x, y) + 0.15 + 0.7 * f) - p;
                    float dd = dot(d, d);
                    if (dd < best) { best = dd; id = CreatureHash01(cell, salt + 911u); }
                }
                return float2(sqrt(best), id);
            }

            // One GGX lobe (URP's normalisation), F0 0.04.
            float GGX(float3 N, float3 L, float3 V, float smoothness)
            {
                float rough = max(1.0 - smoothness, 0.04); rough *= rough;
                float rough2 = rough * rough;
                float3 H = SafeNormalize(L + V);
                float nh = saturate(dot(N, H)), lh = saturate(dot(L, H));
                float d = nh * nh * (rough2 - 1.0) + 1.00001;
                return min(rough2 / ((d * d) * max(0.1, lh * lh) * (rough * 4.0 + 2.0)), 100.0) * 0.04;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv0 = i.uv.xy, uv1 = i.uv.zw;
                int part = (int)(uv1.x + 0.5);
                float3 posWS = i.positionWS.xyz;
                float3 n = normalize(i.normalWS.xyz);
                float seed = i.inst.x;
                float chroma = i.inst2.x, flash = i.inst2.y, cloudPhase = i.inst2.z;
                bool arm = part == OCTO_PART_ARM, web = part == OCTO_PART_WEB, eye = part == OCTO_PART_EYE;

                // surface coordinates in metres: along the part, and around it (circumference from the rest
                // radius; the web and shell use a nominal one)
                float radius = max(i.positionWS.w, 0.004);
                float circ = 6.2831853 * (arm ? radius : 0.12);
                float2 sp = float2(uv0.x, uv0.y * circ);
                float3 P = float3(sp, 0.37 * seed + 7.1 * part);

                // cloak first: survivors only pay for the rest
                float glint = 0.0;
                // coarse surface patches (P x 2: hand-sized at scale 1) so a partial cloak reads as deliberate camouflage
                // blotches riding on the skin, not a fine speckle
                if (i.inst.w > 0.0) glint = CreatureCloak(i.inst.w, i.positionCS.xy, CreatureValueNoise(P * 2.0, 0x2545F491u));

                // --- counter-shaded base: pale oral side, warm flanks, dark aboral side
                float dorsal = uv1.y + 0.25 * (CreatureValueNoise(P * 7.0, 0x7FEB352Du) - 0.5) * 2.0;
                if (web) dorsal = uv1.y * 0.7;
                float3 base = lerp(_Belly.rgb, _Flank.rgb, smoothstep(-0.8, -0.1, dorsal));
                base = lerp(base, _Dorsal.rgb, smoothstep(0.0, 0.75, dorsal));

                // --- chromatophores: cells that expand with chroma, more on the aboral side
                float2 vor = Voronoi(sp * _OctoParams.x, 0x9E3779B9u + (uint)(seed * 13.0));
                float expand = saturate(chroma * 1.35 - 0.45 * vor.y + 0.15 * dorsal);
                float cellSpot = 1.0 - smoothstep(0.18 + 0.32 * expand, 0.26 + 0.34 * expand, vor.x);
                cellSpot *= step(0.02, expand);
                // coarse camouflage mottle (patches that come and go with chroma)
                float mottle = smoothstep(0.45, 0.75, CreatureFbm(P * _MottleScale, 0x846CA68Bu)) * chroma;
                // passing clouds: dark bands drifting along arms and body
                float band = sin(6.2831853 * (uv0.x * 1.4 - cloudPhase) + 3.0 * CreatureValueNoise(P * 2.0, 0x1B873593u));
                float cloud = smoothstep(0.55, 1.0, band) * saturate(chroma * 1.5) * 0.55;
                float darkness = saturate(cellSpot * (0.35 + 0.65 * chroma) + 0.6 * mottle + cloud);
                float3 albedo = lerp(base, _PatternColor.rgb, darkness * saturate(0.35 + 0.65 * (dorsal * 0.5 + 0.5)));
                // fine speckle
                albedo *= 1.0 + 0.06 * (CreatureValueNoise(P * 160.0, 0x27D4EB2Fu) - 0.5) * 2.0;

                // --- height field for bumps: papillae (rising when camouflaged), suckers on the arms
                float height = _PapillaeHeight * _OctoParams.y * (0.4 + chroma) * smoothstep(0.55, 0.9, CreatureValueNoise(P * 70.0, 0x165667B1u))
                               * saturate(dorsal + 0.6);
                float suckerAO = 0.0;
                if (arm && uv0.x > 0.02)
                {
                    // two staggered rows either side of the ventral midline (angle 0 / 1)
                    float a = uv0.y > 0.5 ? uv0.y - 1.0 : uv0.y;          // -0.5 .. 0.5 turns, 0 = midline
                    float across = a * 6.2831853 * radius;                  // metres around from the midline
                    float sr = _OctoParams.z * radius;                      // sucker radius
                    float pitch = 2.3 * sr;
                    float best = 8.0;
                    [unroll] for (int row = -1; row <= 1; row += 2)
                    {
                        float along = uv0.x - (row > 0 ? 0.5 * pitch : 0.0);
                        float local = along - round(along / pitch) * pitch;
                        float dd = length(float2(local, across - row * 0.55 * sr)) / sr;
                        best = min(best, dd);
                    }
                    float fade = smoothstep(0.003, 0.006, radius);
                    float mask = (1.0 - smoothstep(0.85, 1.0, best)) * fade;
                    float cup = (1.0 - smoothstep(0.0, 0.6, best)) * fade;
                    float rim = smoothstep(0.5, 0.75, best) * (1.0 - smoothstep(0.85, 1.0, best)) * fade;
                    albedo = lerp(albedo, _OctoSucker.rgb, mask * 0.85);
                    suckerAO = cup;
                    // the sucker field replaces the papillae: a raised rim around a cup
                    height = lerp(height, (rim * 0.4 - cup) * _SuckerDepth * min(1.0, radius / 0.02), mask);
                }
                albedo *= 1.0 - 0.45 * suckerAO;

                // --- deimatic flash: pale with dark eye rings and edges
                albedo = lerp(albedo, float3(0.93, 0.9, 0.86), flash * 0.85);

                float3 nb = PerturbNormal(n, posWS, height);

                // --- per-individual jitter
                albedo = CreatureHueShift((half3)albedo, (half)i.inst.y) * (1.0 + i.inst.z);

                // --- eyes: golden iris, horizontal black pupil, glossy
                float skinSmooth = _Smoothness * lerp(0.75, 1.0, saturate(_OctoParams2.x));
                if (eye)
                {
                    float th = uv0.x * 3.14159265, ph = uv0.y * 6.2831853;
                    float ex = sin(th) * cos(ph), ey = sin(th) * sin(ph);
                    float pupil = (1.0 - smoothstep(0.1, 0.13, abs(ey))) * (1.0 - smoothstep(0.34, 0.4, abs(ex))) * step(th, 1.2);
                    float iris = 1.0 - smoothstep(0.7, 0.85, th);
                    float streak = 0.75 + 0.25 * sin(ph * 23.0 + 5.0 * CreatureValueNoise(float3(th * 9.0, ph * 3.0, seed), 0x5BD1E995u));
                    albedo = lerp(base * 0.6, _IrisColor.rgb * streak, iris);
                    albedo = lerp(albedo, float3(0.01, 0.01, 0.012), pupil);
                    skinSmooth = 0.95;
                    nb = n;
                }

                CreatureSurface s;
                s.albedo = (half3)saturate(albedo);
                s.normalWS = nb;
                s.smoothness = (half)skinSmooth;
                s.rim = (half)_SurfaceParams.y;
                s.subsurface = (half)saturate(_SurfaceParams.z + (web ? 0.5 : 0.0));
                s.dorsal = (half)uv1.y;

                CreatureLightParams lp;
                lp.wrap = _Wrap;
                lp.subsurfaceColor = _SubsurfaceColor.rgb;
                lp.bellyOcclusion = _BellyOcclusion;
                lp.groundBounce = _GroundBounce.rgb * _GroundBounceStrength;
                lp.rimPower = _RimPower;
                lp.ambientTint = _AmbientTint.rgb;
                lp.ambientWarmth = _AmbientWarmth;

                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half3 rgb = CreatureShade(s, lp, posWS, screenUV);

                // --- light through thin parts (arm tips, the web) and the wet clear coat
            #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                float4 shadowCoord = float4(screenUV, 0.0, 1.0);
            #else
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
            #endif
                Light light = GetMainLight(shadowCoord, posWS, half4(1.0h, 1.0h, 1.0h, 1.0h));
                float3 V = normalize(GetWorldSpaceViewDir(posWS));
                float thin = web ? 1.0 : saturate(1.0 - radius / _ThinRadius);
                if (!eye && thin > 0.0)
                {
                    float back = pow(saturate(dot(-light.direction, V)), 3.0) * 0.8 + 0.2 * saturate(-dot(nb, light.direction));
                    rgb += (half3)(_Transmission * thin * back * _OctoSubsurface.rgb * s.albedo * light.color * light.shadowAttenuation);
                }
                float coat = GGX(n, light.direction, V, _CoatSmoothness) * saturate(dot(n, light.direction)) * _OctoParams2.x * 2.0;
                rgb += (half3)(coat * light.color * light.shadowAttenuation);

                if (glint > 0.0) rgb += (half3)(glint * _OctoParams2.y * (SampleSH(n) + 0.3));
                rgb = MixFog(rgb, (half)i.normalWS.w);
                return half4(rgb, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings ShadowVert(CreatureVertexIn v, uint iid : SV_InstanceID)
            {
                CreatureSkinned s = CreatureSkin(v, iid);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - s.posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(s.posWS, s.normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                Varyings o;
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
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings DepthVert(CreatureVertexIn v, uint iid : SV_InstanceID)
            {
                CreatureSkinned s = CreatureSkin(v, iid);
                Varyings o;
                o.positionCS = TransformWorldToHClip(s.posWS);
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings DepthNormalsVert(CreatureVertexIn v, uint iid : SV_InstanceID)
            {
                CreatureSkinned s = CreatureSkin(v, iid);
                Varyings o;
                o.positionCS = TransformWorldToHClip(s.posWS);
                o.normalWS = s.normalWS;
                return o;
            }

            half4 DepthNormalsFrag(Varyings i) : SV_Target
            {
            #if defined(_GBUFFER_NORMALS_OCT)
                float3 n = normalize(i.normalWS);
                float2 oct = PackNormalOctQuadEncode(n);
                return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
            #else
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0);
            #endif
            }
            ENDHLSL
        }
    }
}
