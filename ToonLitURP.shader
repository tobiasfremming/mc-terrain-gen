Shader "Custom/ToonLitURP"
{
    Properties
    {
        _BaseMap ("Base (RGB) Alpha (A)", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)

        _Steps ("Light Bands", Range(2,8)) = 4
        _BandSoftness ("Band Softness", Range(0,0.2)) = 0.02
        
        _MinIntensity ("Min Intensity", Range(0,1)) = 0.1
        _MaxIntensity ("Max Intensity", Range(0,2)) = 1.0
        _ShadowDarkening ("Shadow Darkening", Range(0,1)) = 0.5

        _SpecIntensity ("Spec Intensity", Range(0,2)) = 0.5
        _SpecSize ("Spec Size", Range(0.01,0.5)) = 0.1

        _OutlineColor ("Outline Color", Color) = (0,0,0,1)
        _OutlineThickness ("Outline Thickness (view)", Range(0,3)) = 1.2
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        // Per-material data (SRP Batcher friendly)
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _Steps;
            float _BandSoftness;
            float _MinIntensity;
            float _MaxIntensity;
            float _ShadowDarkening;
            float _SpecIntensity;
            float _SpecSize;
            float4 _OutlineColor;
            float _OutlineThickness;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float2 uv         : TEXCOORD0;
        };

        struct Varyings
        {
            float4 positionHCS : SV_POSITION;
            float3 normalWS    : TEXCOORD0;
            float3 posWS       : TEXCOORD1;
            float2 uv          : TEXCOORD2;
            float3 viewDirWS   : TEXCOORD3;
        };

        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionHCS = TransformWorldToHClip(posWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.posWS = posWS;
            o.uv = v.uv;
            o.viewDirWS = GetWorldSpaceViewDir(posWS);
            return o;
        }

        // Quantize x into _Steps bands with optional softness at transitions
        float ToonStep(float x, float steps, float softness)
        {
            x = saturate(x);
            if (softness > 0.0001)
            {
                // soften edges by blending to nearest band edges
                float s = steps;
                float band = floor(x * s) / (s - 1.0);
                float nextBand = floor(x * s + 1.0) / (s - 1.0);
                float edge = frac(x * s);
                float t = smoothstep(0.5 - softness, 0.5 + softness, edge);
                return lerp(band, nextBand, t);
            }
            else
            {
                return floor(x * steps) / max(steps - 1.0, 1.0);
            }
        }

        // Simple toon specular: round the half-vector term into a hard spot
        float ToonSpecular(float3 N, float3 L, float3 V, float size, float intensity)
        {
            float3 H = normalize(L + V);
            float ndh = saturate(dot(N, H));
            float spot = step(1.0 - size, ndh); // hard spot
            return spot * intensity;
        }
        ENDHLSL

        // ---------- Main forward pass (lit, receives shadows & additional lights) ----------
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // Shadows & lights keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            float4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(i.viewDirWS);

                float4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                float3 albedo = baseSample.rgb;

                // Main light (directional)
                float4 shadowCoord = TransformWorldToShadowCoord(i.posWS);
                Light mainLight = GetMainLight(shadowCoord);

                float3 L = normalize(mainLight.direction);
                float nDotL = saturate(dot(N, L));
                float band = ToonStep(nDotL, _Steps, _BandSoftness);
                // Map band from [0,1] to [_MinIntensity, _MaxIntensity]
                band = lerp(_MinIntensity, _MaxIntensity, band);
                
                // Apply shadows as darkening multiplier
                float shadowMultiplier = lerp(_ShadowDarkening, 1.0, mainLight.shadowAttenuation);
                float3 lit = band * shadowMultiplier * mainLight.color.rgb;

                // Additional lights
                #if defined(_ADDITIONAL_LIGHTS)
                uint count = GetAdditionalLightsCount();
                for (uint li = 0u; li < count; li++)
                {
                    Light l = GetAdditionalLight(li, i.posWS);
                    float nl = saturate(dot(N, normalize(l.direction)));
                    float b = ToonStep(nl, _Steps, _BandSoftness);
                    // Map additional light band from [0,1] to [_MinIntensity, _MaxIntensity]
                    b = lerp(_MinIntensity, _MaxIntensity, b);
                    // Apply shadow darkening to additional lights too
                    float shadowMult = lerp(_ShadowDarkening, 1.0, l.shadowAttenuation);
                    lit += b * shadowMult * l.color.rgb * l.distanceAttenuation;
                }
                #endif

                // Toon specular from main light
                float spec = ToonSpecular(N, L, V, _SpecSize, _SpecIntensity);

                float3 color = albedo * lit + spec * mainLight.color.rgb;
                return float4(color, 1.0);
            }
            ENDHLSL
        }

        // ---------- Outline pass (world-space shell) ----------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On
            ZTest LEqual
            Blend One Zero

            HLSLPROGRAM
            #pragma vertex vertOutline
            #pragma fragment fragOutline

            struct VOut
            {
                float4 positionHCS : SV_POSITION;
            };

            VOut vertOutline(Attributes v)
            {
                VOut o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS  = normalize(TransformObjectToWorldNormal(v.normalOS));

                // Use _OutlineThickness as a world-space distance
                posWS += nWS * (_OutlineThickness * 0.01); // 0.01 helps keep numbers practical

                o.positionHCS = TransformWorldToHClip(posWS);
                return o;
            }

            float4 fragOutline(VOut i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        }

    FallBack Off
}
