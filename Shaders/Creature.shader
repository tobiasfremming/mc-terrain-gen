// Procedural creatures (URP). Drawn by CreatureRenderer with ONE Graphics.RenderMeshPrimitives call
// per species: the species' rest mesh, instanced, skinned on the GPU from _Bones (see
// Creature/CreatureSkin.hlsl). Per-individual data comes from _Instances[SV_InstanceID]; the
// species look (_Dorsal .. _BodyParams) from the renderer's property block.
//
// Look ("stylized-realistic"): belly -> flank -> back colour gradient from the dorsal coordinate,
// a procedural pattern in the mesh's surface space (Creature/CreaturePattern.hlsl) so it sticks to
// the body as it bends, a fine scale grain, per-individual hue/value jitter, then wrapped diffuse
// with a warm terminator, GGX specular, SH ambient, rim, received shadows, SSAO and fog
// (Creature/CreatureLighting.hlsl). Eyes are black and glossy.
//
// The material's properties are look-tuning constants shared by all species; per-species values
// live in CreatureSpecies.look.
Shader "MarchingCubes/Creature"
{
    Properties
    {
        _Wrap ("Wrap lighting", Range(0, 1)) = 0.3
        _SubsurfaceColor ("Subsurface tint (terminator)", Color) = (1, 0.45, 0.28, 1)
        _BellyOcclusion ("Belly sky occlusion", Range(0, 1)) = 0.45
        _GroundBounce ("Ground bounce colour", Color) = (0.62, 0.46, 0.3, 1)
        _GroundBounceStrength ("Ground bounce strength", Range(0, 1)) = 0.3
        _RimPower ("Rim power", Range(1, 8)) = 4
        _AmbientTint ("Ambient warm tint", Color) = (1, 0.8, 0.58, 1)
        _AmbientWarmth ("Ambient warmth (sand bounce)", Range(0, 1)) = 0.5
        _EyeSmoothness ("Eye smoothness", Range(0, 1)) = 0.9
        _GradientWarp ("Colour gradient wobble", Range(0, 1)) = 0.25
        _Mottle ("Fine mottling", Range(0, 0.5)) = 0.08
        _ScaleSize ("Scale row spacing (m)", Range(0.001, 0.02)) = 0.0045
        _ScaleColumns ("Scales around a ring", Range(8, 256)) = 96
        _ScaleTint ("Per-scale tint", Range(0, 0.5)) = 0.07
        _ScaleGroove ("Scale groove darkening", Range(0, 1)) = 0.25
        _ScaleBump ("Scale bump height (m)", Range(0, 0.002)) = 0.0003
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back

        HLSLINCLUDE
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
        half _EyeSmoothness;
        half _GradientWarp;
        half _Mottle;
        float _ScaleSize;
        float _ScaleColumns;
        half _ScaleTint;
        half _ScaleGroove;
        float _ScaleBump;
        CBUFFER_END

        // Per species, from CreatureRenderer's property block.
        float4 _Dorsal;
        float4 _Flank;
        float4 _Belly;
        float4 _PatternColor;
        float _PatternType;     // CreaturePattern enum
        float4 _PatternParams;  // scale, contrast, softness, spotScale
        float4 _SurfaceParams;  // smoothness, rim, subsurface, 0
        float4 _BodyParams;     // body length, 1 / length, arc length where the tail begins, 0
        float4 _MembraneColor;  // wing membrane colour (winged species)
        float4 _MembraneParams; // translucency, vein contrast, trailing rim darkening, 0
        float4 _BandColor;      // arthropod knee bands (part 9)
        float4 _SheenColor;     // velvet sheen colour, alpha = strength (0 = none)
        float4 _ShimmerColor;   // iridescent shimmer: colour facing, alpha = strength (0 = none)
        float4 _ShimmerColor2;  // ... colour at grazing angles, alpha = glow
        float4 _ShimmerParams;  // bands per rest metre, bands per second, 0, 0
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
            #include "Creature/CreatureMembrane.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 positionWS : TEXCOORD0;               // w = rest radius (pattern space)
                float4 normalWS : TEXCOORD1;                 // w = fog factor
                float4 uv : TEXCOORD2;                       // uv0.xy, uv1.xy
                nointerpolation float4 inst : TEXCOORD3;     // seed, hueShift, valueShift, cloak
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
                return o;
            }

            // Bump from a scalar height field via screen derivatives (Mikkelsen 2010).
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

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv0 = i.uv.xy, uv1 = i.uv.zw;
                int part = (int)(uv1.x + 0.5);
                bool eye = part == CREATURE_PART_EYE;
                float3 posWS = i.positionWS.xyz;
                float3 n = normalize(i.normalWS.xyz);
                float seed = i.inst.x;

                CreatureLightParams lp;
                lp.wrap = _Wrap;
                lp.subsurfaceColor = _SubsurfaceColor.rgb;
                lp.bellyOcclusion = _BellyOcclusion;
                lp.groundBounce = _GroundBounce.rgb * _GroundBounceStrength;
                lp.rimPower = _RimPower;
                lp.ambientTint = _AmbientTint.rgb;
                lp.ambientWarmth = _AmbientWarmth;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // --- wing membrane: translucent skin with veins and a darker trailing rim (CreatureMembrane.hlsl)
                if (part == CREATURE_PART_MEMBRANE)
                {
                    half vein = CreatureMembraneVeins(uv0);
                    float edge = saturate((abs(uv1.y) - 0.1) / 0.9);   // radial fraction to the edge
                    half rimDark = (half)(smoothstep(0.82, 1.0, edge) * _MembraneParams.z);
                    float3 mc = _MembraneColor.rgb * (1.0 + _Mottle * (CreatureFbm(float3(uv0 * float2(6.0, 40.0), seed), 0x846CA68Bu) - 0.5) * 2.0);
                    mc = CreatureHueShift((half3)mc, (half)i.inst.y) * (1.0 + i.inst.z);
                    CreatureSurface ms;
                    ms.albedo = (half3)saturate(mc * (1.0 - _MembraneParams.y * vein) * (1.0 - rimDark));
                    ms.normalWS = n;
                    ms.smoothness = (half)(0.5 * _SurfaceParams.x);
                    ms.rim = (half)_SurfaceParams.y;
                    ms.subsurface = 1.0h;
                    ms.dorsal = 1.0h;                                   // no belly occlusion on a wing
                    half3 mrgb = CreatureMembraneShade(ms, lp, posWS, screenUV, (half3)_MembraneColor.rgb, (half)(_MembraneParams.x * (1.0 - rimDark)), vein);
                    return half4(MixFog(mrgb, (half)i.normalWS.w), 1.0h);
                }

                // --- base colour: belly -> flank -> back, the boundaries wobbling a little
                float3 P = CreatureSurfacePoint(uv0, uv1, i.positionWS.w, seed);
                // cloak: Forward only (the shadow and depth passes keep the body); a no-op at 0
                float glint = 0.0;
                if (i.inst.w > 0.0) glint = CreatureCloak(i.inst.w, i.positionCS.xy, CreatureValueNoise(P * 6.0, 0x2545F491u));
                float dorsal = uv1.y + _GradientWarp * (CreatureValueNoise(P * 9.0, 0x7FEB352Du) - 0.5) * 2.0;
                float3 base = lerp(_Belly.rgb, _Flank.rgb, smoothstep(-0.75, -0.15, dorsal));
                base = lerp(base, _Dorsal.rgb, smoothstep(0.05, 0.7, dorsal));
                base *= 1.0 + _Mottle * (CreatureFbm(P * 22.0, 0x846CA68Bu) - 0.5) * 2.0;

                // --- pattern (dark marks, pale spots)
                float2 pat = CreaturePattern(uv0, uv1, seed, _PatternType, _PatternParams, i.positionWS.w, _BodyParams);
                float3 pale = lerp(_Dorsal.rgb, _Belly.rgb, 0.65) * 1.08;
                float3 albedo = lerp(base, _PatternColor.rgb, pat.x);
                albedo = lerp(albedo, pale, pat.y);
                if (part == CREATURE_PART_FOOT) albedo = lerp(albedo, _PatternColor.rgb, 0.3);
                if (part == CREATURE_PART_BAND) albedo = lerp(albedo, _BandColor.rgb, 0.9);

                // --- scale grain: per-scale tint, darker grooves, a small bump, rougher grooves
                float3 g = CreatureScaleGrain(uv0, uv1, _ScaleSize, _ScaleColumns);
                float vis = eye ? 0.0 : g.z;
                albedo *= 1.0 + _ScaleTint * g.x * vis;
                albedo *= 1.0 - _ScaleGroove * (1.0 - g.y) * vis;
                float3 nb = PerturbNormal(n, posWS, g.y * _ScaleBump * vis);

                // --- per-individual jitter
                albedo = CreatureHueShift((half3)albedo, (half)i.inst.y) * (1.0 + i.inst.z);

                CreatureSurface s;
                s.albedo = (half3)saturate(albedo);
                s.normalWS = nb;
                s.smoothness = (half)(_SurfaceParams.x * lerp(0.7, 1.0, lerp(1.0, g.y, vis)));
                s.rim = (half)_SurfaceParams.y;
                s.subsurface = (half)_SurfaceParams.z;
                s.dorsal = (half)uv1.y;
                if (eye)
                {
                    s.albedo = half3(0.012h, 0.011h, 0.010h);
                    s.normalWS = n;
                    s.smoothness = _EyeSmoothness;
                    s.subsurface = 0.0h;
                    s.dorsal = 1.0h;
                }

                half3 rgb = CreatureShade(s, lp, posWS, screenUV);
                if (glint > 0.0) rgb += (half3)(glint * 0.6 * (SampleSH(n) + 0.3));
                // velvet sheen (a hairy body seen at grazing angles): a rim lobe in the light's and the sky's colour
                if (_SheenColor.a > 0.0 && !eye)
                {
                    float3 V = GetWorldSpaceNormalizeViewDir(posWS);
                    Light sun = GetMainLight();
                    half graze = (half)pow(1.0 - saturate(dot(nb, V)), 3.0);
                    half wrapLit = (half)saturate(dot(nb, sun.direction) * 0.5 + 0.5);
                    rgb += (half3)_SheenColor.rgb * (half)_SheenColor.a * graze * (sun.color * (0.6h * wrapLit) + SampleSH(nb) * 0.4h);
                }
                // iridescent shimmer (the dragon): thin-film colour bands travelling head -> tail along the rest arc,
                // on the back and flanks; the hue slides with the view angle, each band glows faintly
                if (_ShimmerColor.a > 0.0 && !eye)
                {
                    float3 Vs = GetWorldSpaceNormalizeViewDir(posWS);
                    half ndv = (half)saturate(dot(nb, Vs));
                    float ph = uv0.x * _ShimmerParams.x - _Time.y * _ShimmerParams.y + seed * 0.37;
                    half wave = (half)pow(0.5 + 0.5 * sin(6.2831853 * ph), 6.0);
                    half3 irid = lerp((half3)_ShimmerColor.rgb, (half3)_ShimmerColor2.rgb, 0.5h + 0.5h * (half)cos(6.2831853 * ((float)ndv * 1.5 + ph * 0.25)));
                    half onBack = (half)smoothstep(-0.35, 0.35, uv1.y);
                    half fres = (half)pow(1.0h - ndv, 2.0h);
                    Light sunS = GetMainLight();
                    rgb += irid * ((half)_ShimmerColor.a * onBack * (0.3h * fres + wave * (0.3h + 0.7h * fres))) * (half3)(SampleSH(nb) * 0.6 + sunS.color * 0.4)
                         + irid * ((half)_ShimmerColor2.a * onBack * wave);
                }
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

        // The project's renderer runs SSAO from depth + normals, so URP's depth prepass uses this
        // pass instead of DepthOnly: without it the creature would be missing from the depth and
        // normals textures (no contact AO under it, and the terrain's AO would show through it).
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
