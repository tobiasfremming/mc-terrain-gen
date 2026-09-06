// Plant mesh shader (URP, instanced): colour from the vertices, so one mesh
// carries bark, leaves, flowers and glowing parts in any mix.
//
//   vertex.rgb  albedo of the part this vertex belongs to (PlantMeshBuilder)
//   vertex.a    NON-emission mask: 1 = ordinary lit surface, 0 = fully
//               self-lit. Alpha rather than a separate stream because a mesh
//               with no colour stream reads as (1,1,1,1) -- white and unlit,
//               which is what Unity's primitives in the lab's Primitives view
//               should be.
//   uv2.x       wind stiffness, 1 at the trunk, 0 at the tips (unused here,
//               reserved for a sway pass).
//
// Lighting is done by hand from Core.hlsl's inputs (main light, ambient SH)
// rather than Lighting.hlsl: leaf cards are two-sided and wrap-lit, which the
// Lit template cannot express, and pulling the full lighting stack into the
// impostor shader once blew up the d3d11 compiler (see PlantImpostor.shader).
// Shadows are cast (ShadowCaster pass) but not received.
Shader "MarchingCubes/Plant"
{
    Properties
    {
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _EmissionStrength ("Emission strength", Range(0, 8)) = 1.6
        _Wrap ("Wrap lighting (leaves)", Range(0, 1)) = 0.35
        _Ambient ("Ambient floor", Range(0, 1)) = 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half _EmissionStrength;
        half _Wrap;
        half _Ambient;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                half4 color : COLOR;
                float fog : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 posWS = TransformObjectToWorld(input.positionOS);
                o.positionCS = TransformWorldToHClip(posWS);
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                o.color = input.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 n = normalize(i.normalWS);
                if (!isFront) n = -n;

                half3 albedo = i.color.rgb * _BaseColor.rgb;
                half3 sunDir = (half3)_MainLightPosition.xyz;
                // wrap lighting: thin leaves let light through, so the shadow
                // side is not black
                half ndl = dot(n, sunDir);
                half diffuse = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                diffuse = max(diffuse, _Ambient);
                half4 n4 = half4(n, 1.0h);
                half3 sh = max(half3(dot(unity_SHAr, n4), dot(unity_SHAg, n4), dot(unity_SHAb, n4)), 0.0h);
                half3 lit = albedo * (_MainLightColor.rgb * diffuse + sh);

                half emissive = 1.0h - i.color.a;
                // emissive parts show their albedo unlit and brightened, but
                // never past 1 per channel: without tonemapping anything
                // above clips to white and the colour is lost
                half3 glow = albedo * _EmissionStrength;
                glow /= max(1.0h, max(glow.r, max(glow.g, glow.b)));
                half3 rgb = lerp(lit, glow, emissive);
                rgb = MixFog(rgb, i.fog);
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
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 posWS = TransformObjectToWorld(input.positionOS);
                float3 nWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
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
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                o.positionCS = TransformObjectToHClip(input.positionOS);
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
