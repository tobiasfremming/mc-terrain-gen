// Far-LOD plant card (URP, instanced).
//
// A plant beyond its impostorDistance is one quad. The quad rotates about the
// plant's own local Y (which the scatter aligned to the ground normal, so it
// stays upright on a globe), faces the camera, and shows the atlas frame
// baked from the nearest of N horizontal view angles -- blended with the next
// frame so turning around a tree does not snap between pictures.
//
// The mesh is a per-variant quad from PlantPrototypeBaker: x in [-r, r], y in
// [ymin, ymax] in the plant's own metres, uv.x in [0, 1/N] and uv.y spanning
// that variant's atlas row. Only the frame offset along u is decided here.
//
// No shadow caster pass on purpose: PlantScatter draws impostors with shadows
// off. They live beyond any sane shadow distance anyway.
Shader "MarchingCubes/Plant Impostor"
{
    Properties
    {
        _MainTex ("Impostor atlas", 2D) = "white" {}
        _Frames ("Frames around Y", Float) = 12
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.4
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _Ambient ("Sun floor (how lit the shadow side is)", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);

        CBUFFER_START(UnityPerMaterial)
        float4 _MainTex_ST;
        float _Frames;
        float _Cutoff;
        half4 _Tint;
        float _Ambient;
        CBUFFER_END

        struct Attributes
        {
            float3 positionOS : POSITION;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        // Turns the flat quad into a card facing the camera and picks the two
        // atlas frames that bracket the view angle. Shared by every pass.
        //   frame.xy = u offsets of the two frames, frame.z = blend
        void CardVertex(float3 positionOS, out float3 posWS, out float3 upWS, out float3 frame)
        {
            // Camera in the plant's own space, flattened to its ground plane.
            // UNITY_MATRIX_I_M is per-instance under instancing, which is
            // what makes one draw call of a thousand cards each face the eye.
            float3 camOS = mul(UNITY_MATRIX_I_M, float4(_WorldSpaceCameraPos, 1.0)).xyz;
            float3 vd = float3(camOS.x, 0.0, camOS.z);
            float len = length(vd);
            vd = len > 1e-5 ? vd / len : float3(0.0, 0.0, 1.0);

            // Same convention as the bake camera: image u runs along
            // cross(viewDir, up), the camera's right when it looks down -vd.
            float3 right = normalize(cross(vd, float3(0.0, 1.0, 0.0)));
            float3 cardOS = right * positionOS.x + float3(0.0, positionOS.y, 0.0);
            posWS = TransformObjectToWorld(cardOS);
            upWS = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0.0, 1.0, 0.0)));

            // Bake frame i looked at the plant from angle 2*pi*i/N measured as
            // atan2(x, z), so the same measure of the live view direction is
            // the fractional frame index.
            float n = max(1.0, _Frames);
            float f = atan2(vd.x, vd.z) / (2.0 * PI) * n;
            f = f - n * floor(f / n);
            float i0 = floor(f);
            float i1 = i0 + 1.0;
            i1 = i1 >= n ? 0.0 : i1;
            frame = float3(i0 / n, i1 / n, f - i0);
        }

        half4 SampleCard(float2 uv, float3 frame)
        {
            half4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(frame.x, 0.0));
            half4 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(frame.y, 0.0));
            return lerp(a, b, (half)frame.z);
        }
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
            // Deliberately NOT Lighting.hlsl. Pulling the full URP lighting
            // stack into this two-triangle shader made the d3d11 compiler
            // fail with "out of memory while parsing" inside Shadows.hlsl on
            // the FOG_EXP2 INSTANCING_ON fragment variant, and a card wants
            // none of it: the sun's direction and colour and the ambient SH
            // coefficients are already in Core.hlsl's UnityInput.

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 frame : TEXCOORD1;
                half3 light : TEXCOORD2;
                float fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);

                float3 posWS, upWS, frame;
                CardVertex(input.positionOS, posWS, upWS, frame);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = input.uv;
                o.frame = frame;

                // The atlas already carries the plant's own form shading, so
                // light the whole card as one surface facing up: sun by
                // elevation with a floor for the shadow side, plus ambient
                // from the L0+L1 spherical harmonics. Close to what URP Lit
                // gives the real mesh's canopy, without any of its includes.
                half3 sunDir = (half3)_MainLightPosition.xyz;
                half ndl = saturate(dot((half3)upWS, sunDir));
                half4 n4 = half4((half3)upWS, 1.0);
                half3 sh = half3(dot(unity_SHAr, n4), dot(unity_SHAg, n4), dot(unity_SHAb, n4));
                o.light = _MainLightColor.rgb * lerp(ndl, (half)1.0, (half)_Ambient) + max(sh, (half3)0.0);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 c = SampleCard(i.uv, i.frame);
                clip(c.a - (half)_Cutoff);
                half3 rgb = c.rgb * _Tint.rgb * i.light;
                rgb = MixFog(rgb, i.fog);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 frame : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                float3 posWS, upWS, frame;
                CardVertex(input.positionOS, posWS, upWS, frame);
                o.positionCS = TransformWorldToHClip(posWS);
                o.uv = input.uv;
                o.frame = frame;
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                clip(SampleCard(i.uv, i.frame).a - (half)_Cutoff);
                return 0;
            }
            ENDHLSL
        }
    }
}
