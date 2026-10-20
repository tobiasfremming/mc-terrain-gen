// Creature fur: long hair as GPU blades (URP). Drawn by CreatureFur.cs with ONE Graphics.RenderPrimitives call
// for every simulated individual of a species: no mesh, kVerts vertices per strand (non-indexed). Each strand
// is a lock: a quadratic curve through the root, its first simulated point (at the middle) and its tip
// (FurSim.compute), as a camera-facing ribbon tapering to the tip, cut into a few hairs that part toward the
// tip. Distance LOD: a strand draws while its rank < the fraction left at its distance (the roots are shuffled,
// so the survivors stay uniform), the survivors widen to keep the coverage.
Shader "MarchingCubes/CreatureFurStrands"
{
    Properties
    {
        _HairsPerLock ("Hairs across a lock", Range(1, 8)) = 4
        _TipSplit ("Hairs part from (0 root .. 1 tip)", Range(0, 1)) = 0.35
        _Widen ("Widen faded locks", Range(0, 2)) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../../Shaders/Creature/CreatureSkin.hlsl"
        #include "../../Shaders/Creature/CreaturePattern.hlsl"
        #include "FurCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float _HairsPerLock;
        float _TipSplit;
        float _Widen;
        CBUFFER_END

        StructuredBuffer<FurStrand> _FurStrands;
        StructuredBuffer<uint4> _FurSlots;
        uint _FurRootCount;
        uint _FurSlotCount;
        float4 _FurFade;          // strand fade start, end (x sizeScale), 0, 0

        #define FUR_SEGS 4
        #define FUR_VERTS (FUR_SEGS * 6)

        struct StrandVertex
        {
            float3 posWS;
            float3 tangentWS;
            float3 normalWS;
            half3 albedo;
            float t;            // 0 root .. 1 tip
            float across;       // -1 .. 1 over the lock's width
            float seedF;
            bool valid;
        };

        StrandVertex BuildStrand(uint vid)
        {
            StrandVertex o = (StrandVertex)0;
            uint strand = vid / FUR_VERTS, local = vid - strand * FUR_VERTS;
            uint slot = strand / max(_FurRootCount, 1u);
            if (slot >= _FurSlotCount) return o;
            uint4 sl = _FurSlots[slot];
            if (sl.z == 0u) return o;
            FurStrand s = _FurStrands[strand];
            if (s.v2.w != 1.0) return o;

            float scale = max(_Instances[sl.x].sizeScale, 1e-3);
            float dist = distance(_WorldSpaceCameraPos, s.p0.xyz);
            float keep = 1.0 - smoothstep(_FurFade.x * scale, _FurFade.y * scale, dist);
            if (s.p2.w >= keep) return o;

            // corner: two triangles per segment, (row, side) of the ribbon
            uint quad = local / 6u, corner = local - quad * 6u;
            uint dr = (corner == 2u || corner == 3u || corner == 5u) ? 1u : 0u;
            float side = (corner == 1u || corner == 4u || corner == 5u) ? 1.0 : -1.0;
            float t = (float)(quad + dr) / FUR_SEGS;

            float3 p0 = s.p0.xyz, p1 = s.p1.xyz, p2 = s.p2.xyz;
            float3 c = 2.0 * p1 - 0.5 * (p0 + p2);          // the quadratic passes through p1 at t = 0.5
            float u = 1.0 - t;
            float3 pos = u * u * p0 + 2.0 * u * t * c + t * t * p2;
            float3 tan = FurSafeNormalize(2.0 * u * (c - p0) + 2.0 * t * (p2 - c), FurSafeNormalize(p2 - p0, float3(0, 1, 0)));
            float3 V = FurSafeNormalize(_WorldSpaceCameraPos - pos, float3(0, 0, 1));
            float3 nSkin = FurUnpackNormal(s.v1.w);
            float3 sideDir = FurSafeNormalize(cross(tan, V), FurSafeNormalize(cross(tan, nSkin), float3(1, 0, 0)));
            float widen = 1.0 + _Widen * (1.0 - keep);
            float width = s.p0.w * widen * (1.0 - 0.8 * t * t);
            pos += sideDir * (side * 0.5 * width);

            float3 rootC, tipC;
            FurUnpackLook(s.look, rootC, tipC);
            o.posWS = pos;
            o.tangentWS = tan;
            // lit like the coat it belongs to: the skin normal, bent toward the ribbon's face
            o.normalWS = FurSafeNormalize(nSkin + cross(sideDir, tan) * (dot(cross(sideDir, tan), V) >= 0.0 ? 0.6 : -0.6), nSkin);
            o.albedo = (half3)lerp(rootC, tipC, smoothstep(0.0, 0.85, t));
            o.t = t;
            o.across = side;
            o.seedF = (float)(s.look.w & 0xFFFFu) * (1.0 / 65536.0);
            o.valid = true;
            return o;
        }

        float4 FurCullPosition()
        {
            return float4(asfloat(0x7FC00000u), asfloat(0x7FC00000u), asfloat(0x7FC00000u), asfloat(0x7FC00000u));   // NaN: dropped
        }

        // the lock's hairs: _HairsPerLock lines across, parting toward the tip; 0 = cut
        float FurLockMask(float across, float t, float seedF)
        {
            float x = (across * 0.5 + 0.5) * _HairsPerLock + seedF * 7.0;
            float f = abs(frac(x) - 0.5) * 2.0;                  // 0 in a hair's middle, 1 between two hairs
            float split = saturate((t - _TipSplit) / max(1.0 - _TipSplit, 1e-3));
            float edge = abs(across);
            return (f < 1.0 - 0.75 * split) && (edge < 1.0 - 0.3 * split * split) ? 1.0 : 0.0;
        }
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
            #include "FurLighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 tangentWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half3 albedo : TEXCOORD3;
                float4 misc : TEXCOORD4;      // t, across, seed, fog
            };

            Varyings Vert(uint vid : SV_VertexID)
            {
                StrandVertex b = BuildStrand(vid);
                Varyings o = (Varyings)0;
                if (!b.valid) { o.positionCS = FurCullPosition(); return o; }
                o.positionCS = TransformWorldToHClip(b.posWS);
                o.positionWS = b.posWS;
                o.tangentWS = b.tangentWS;
                o.normalWS = b.normalWS;
                o.albedo = b.albedo;
                o.misc = float4(b.t, b.across, b.seedF, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                clip(FurLockMask(i.misc.y, i.misc.x, i.misc.z) - 0.5);
                float3 T = normalize(i.tangentWS);
                float3 N = normalize(i.normalWS);
                half3 rgb = FurShade(i.positionWS, T, N, i.albedo, (half)i.misc.x, GetNormalizedScreenSpaceUV(i.positionCS));
                rgb = MixFog(rgb, (half)i.misc.w);
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

            struct Varyings { float4 positionCS : SV_POSITION; float3 misc : TEXCOORD0; };

            Varyings ShadowVert(uint vid : SV_VertexID)
            {
                StrandVertex b = BuildStrand(vid);
                Varyings o = (Varyings)0;
                if (!b.valid) { o.positionCS = FurCullPosition(); return o; }
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDir = normalize(_LightPosition - b.posWS);
            #else
                float3 lightDir = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(b.posWS, b.normalWS, lightDir));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                o.misc = float3(b.t, b.across, b.seedF);
                return o;
            }

            half4 ShadowFrag(Varyings i) : SV_Target
            {
                clip(FurLockMask(i.misc.y, i.misc.x, i.misc.z) - 0.5);
                return 0;
            }
            ENDHLSL
        }
    }
}
