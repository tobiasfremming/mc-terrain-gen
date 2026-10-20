// Creature fur: the undercoat as shells (URP). Drawn by CreatureFur.cs with ONE Graphics.RenderMeshPrimitives
// call per species: the furry triangles of the body (FurData's shell mesh, the body's own vertex streams, so they
// skin exactly like the body: Creature/CreatureSkin.hlsl) instanced count x shells times. Instance i is shell
// i / count of individual i % count, pushed out along the skinned normal to its height (outer shells sag with
// gravity), and cut by a hair mask: hairs are points of a jittered 3D cell grid in the REST position (they stay on
// the body as it bends, straight through the shells), round, tapering, each with its own length. The first
// shell is closed (the coat's floor hides the skin). Distance LOD: the shells thin to 3 by _FurShellFade.x and
// vanish by twice it (sizeScale x). No shadow or depth pass: the body casts and occludes.
Shader "MarchingCubes/CreatureFurShell"
{
    Properties
    {
        _HairTaper ("Hair taper to the tip", Range(0, 1)) = 0.75
        _ShellBias ("First shell offset (fraction of a shell)", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Back

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "../../Shaders/Creature/CreatureSkin.hlsl"
        #include "../../Shaders/Creature/CreaturePattern.hlsl"
        #include "FurCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
        float _HairTaper;
        float _ShellBias;
        CBUFFER_END

        uint _FurCreatureCount;
        uint _FurShells;
        float4 _FurShellFade;     // x = distance (x sizeScale) where the shells have thinned to 3

        struct FurShellIn
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float2 uv0 : TEXCOORD0;
            float2 uv1 : TEXCOORD1;
            float4 uv2 : TEXCOORD2;
            float4 uv3 : TEXCOORD3;    // shell height m (scale 1), hair radius m, hairs per metre, droop
            float4 uv4 : TEXCOORD4;    // root rgb (linear), bodyTint
            float4 uv5 : TEXCOORD5;    // tip rgb (linear), 0
        };
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
                float3 normalWS : TEXCOORD1;
                float3 restPos : TEXCOORD2;
                float4 uv : TEXCOORD3;                     // uv0, uv1
                float4 hair : TEXCOORD4;                   // shell height m, hair radius m, hairs per metre, h (0..1)
                float4 rootC : TEXCOORD5;                  // rgb, bodyTint
                float4 tipC : TEXCOORD6;                   // rgb, rest radius
                nointerpolation float4 inst : TEXCOORD7;   // seed, hueShift, valueShift, fog
                float3 hairDir : TEXCOORD8;
                nointerpolation float floorShell : TEXCOORD9; // 1 on the first shell (closed: the coat's floor)
            };

            Varyings Vert(FurShellIn v, uint iid : SV_InstanceID)
            {
                Varyings o = (Varyings)0;
                uint count = max(_FurCreatureCount, 1u);
                uint creature = iid % count, shell = iid / count;
                CreatureVertexIn cv;
                cv.positionOS = v.positionOS; cv.normalOS = v.normalOS; cv.uv0 = v.uv0; cv.uv1 = v.uv1; cv.uv2 = v.uv2;
                CreatureSkinned s = CreatureSkin(cv, creature);
                float scale = max(s.inst.sizeScale, 1e-3);

                // distance LOD: N shells near, 3 at the fade distance, none past twice it
                float far = max(_FurShellFade.x * scale, 1e-3);
                float d = distance(_WorldSpaceCameraPos, s.posWS);
                float n = (float)max(_FurShells, 1u);
                float active = d > 2.0 * far ? 0.0 : floor(lerp(n, min(3.0, n), saturate(d / far)) + 0.5);
                if ((float)shell >= active)
                {
                    o.positionCS = float4(asfloat(0x7FC00000u), asfloat(0x7FC00000u), asfloat(0x7FC00000u), asfloat(0x7FC00000u));
                    return o;
                }
                float h = ((float)shell + _ShellBias) / active;     // 0 at the skin .. 1 at the outer shell
                float len = v.uv3.x * scale;
                // pushed out along the normal; outer shells sag with gravity (but never back into the skin)
                float3 dir = s.normalWS + float3(0.0, -1.0, 0.0) * (v.uv3.w * h * 0.6);
                float rise = dot(dir, s.normalWS);
                if (rise < 0.5) dir += s.normalWS * (0.5 - rise);
                float3 pos = s.posWS + dir * (len * h);

                o.positionCS = TransformWorldToHClip(pos);
                o.positionWS = pos;
                o.normalWS = s.normalWS;
                o.hairDir = normalize(dir);
                o.floorShell = shell == 0u ? 1.0 : 0.0;
                o.restPos = v.positionOS.xyz;
                o.uv = float4(v.uv0, v.uv1);
                o.hair = float4(v.uv3.x, v.uv3.y, v.uv3.z, h);
                o.rootC = v.uv4;
                o.tipC = float4(v.uv5.xyz, s.radius);
                o.inst = float4(s.inst.seed, s.inst.hueShift, s.inst.valueShift, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float h = i.hair.w;
                float lenRest = i.hair.x;
                float hairR = i.hair.y, perM = max(i.hair.z, 1.0);
                // thin coat at the region's edges: the shell is within a fraction of a hair of the skin
                if (lenRest * h < 0.3 * hairR) discard;
                // the coat's floor (the first shell) is closed; above it, hairs
                uint seedU = asuint(i.inst.x);
                if (i.floorShell < 0.5)
                {
                    float3 q = i.restPos * perM;
                    float3 cellBase = floor(q - 0.5);
                    float best = 1e9, len01 = 1.0;
                    [unroll] for (int k = 0; k < 8; k++)
                    {
                        float3 cell = cellBase + float3(k & 1, (k >> 1) & 1, (k >> 2) & 1);
                        int3 ci = (int3)cell;
                        uint hx = FurHash(asuint(ci.x) * 0x8DA6B343u ^ asuint(ci.y) * 0xD8163841u ^ asuint(ci.z) * 0xCB1AB31Fu ^ seedU);
                        float3 jit = float3(hx & 0x3FFu, (hx >> 10) & 0x3FFu, (hx >> 20) & 0x3FFu) * (1.0 / 1023.0);
                        float3 fp = cell + 0.25 + 0.5 * jit;
                        float3 dq = q - fp;
                        float d2 = dot(dq, dq);
                        if (d2 < best) { best = d2; len01 = 0.55 + 0.45 * FurHash01(hx); }
                    }
                    float r = hairR * perM * (1.0 - _HairTaper * h);
                    if (h > len01 || best > r * r) discard;
                }

                float2 uv0 = i.uv.xy, uv1 = i.uv.zw;
                CreatureInstance inst = (CreatureInstance)0;
                inst.seed = i.inst.x; inst.hueShift = i.inst.y; inst.valueShift = i.inst.z;
                FurRegionRow g;
                g.root = i.rootC; g.tip = float4(i.tipC.xyz, 0.0); g.dyn = 0.0;
                float3 rootC, tipC;
                FurColours(g, uv0, uv1, i.tipC.w, inst, 1.0, rootC, tipC);
                half3 albedo = (half3)lerp(rootC, tipC, smoothstep(0.1, 1.0, h));
                float3 N = normalize(i.normalWS);
                half3 rgb = FurShade(i.positionWS, normalize(i.hairDir), N, albedo, (half)h, GetNormalizedScreenSpaceUV(i.positionCS));
                rgb = MixFog(rgb, (half)i.inst.w);
                return half4(rgb, 1.0h);
            }
            ENDHLSL
        }
    }
}
