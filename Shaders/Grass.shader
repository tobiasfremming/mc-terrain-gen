// Procedural grass blades (URP). Drawn with Graphics.RenderPrimitivesIndexedIndirect
// by GrassSystem: there is no blade mesh. Each instance is one blade whose
// 24-byte record (root, up, seed, biome weights) the vertex shader turns into
// a tapered, bent, wind-swept strip of 1/3/5 segments depending on the LOD.
//
//   _Blades        the pool GrassScatter.compute filled
//   _VisibleBlades indices into it that survived this frame's CSCull, one
//                  region per LOD; _LodOffset selects the region
//   _Segments      strip segments for this draw (index buffer must match)
//
// Lighting follows Plant.shader (hand-lit: main light, SH ambient, wrap) plus
// main-light shadow reception, a root-to-tip AO gradient, tip translucency
// and a small specular sheen -- the four things that make blades read as
// grass rather than green triangles.
Shader "MarchingCubes/Grass"
{
    Properties
    {
        _Wrap ("Wrap lighting", Range(0, 1)) = 0.45
        _AmbientFloor ("Ambient floor", Range(0, 1)) = 0.12
        _Translucency ("Tip translucency", Range(0, 2)) = 0.6
        _Specular ("Specular sheen", Range(0, 1)) = 0.18
        _RootDarken ("Root darkening", Range(0, 1)) = 0.55
        _ViewThicken ("Edge-on thickening", Range(0, 2)) = 0.5
        _FarWiden ("Widen faded blades", Range(0, 4)) = 1.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Compute/GrassCommon.hlsl"

        CBUFFER_START(UnityPerMaterial)
        half _Wrap;
        half _AmbientFloor;
        half _Translucency;
        half _Specular;
        half _RootDarken;
        half _ViewThicken;
        half _FarWiden;
        CBUFFER_END

        // Per-draw / per-frame, from GrassSystem's property blocks.
        StructuredBuffer<Blade> _Blades;
        StructuredBuffer<uint> _VisibleBlades;
        uint _LodOffset;
        uint _Segments;
        float4 _ChanBase[4];
        float4 _ChanTip[4];
        float4 _BladeSize;    // x height, y width, z height variation, w width variation
        float4 _WindDir;      // xyz world direction (unit), w strength (dimensionless lean)
        float4 _WindParams;   // x speed, y gust scale (m), z flutter, w static lean
        float4 _BendPos;      // xyz player position, w radius (0 = off)
        float4 _FadeParams;   // x fade start, y fade end
        float4 _PlanetCenter; // w = 1 on a globe

        struct BladeVertex
        {
            float3 posWS;
            float3 normalWS;
            half3 albedo;
            half t;        // 0 root .. 1 tip
            half ao;
        };

        BladeVertex BuildBlade(uint vid, uint iid)
        {
            Blade b = _Blades[_VisibleBlades[_LodOffset + iid]];

            uint segs = max(_Segments, 1u);
            bool tip = vid >= segs * 2u;
            uint row = tip ? segs : (vid >> 1);
            float side = tip ? 0.0 : ((vid & 1u) ? 1.0 : -1.0);
            float t = (float)row / (float)segs;

            float r0 = GrassSeedByte(b.seed, 0);   // facing
            float r1 = GrassSeedByte(b.seed, 1);   // height / tint
            float r2 = GrassSeedByte(b.seed, 2);   // width / lean / phase
            float ao = GrassSeedByte(b.seed, 3);

            float3 up = GrassUnpackOct(b.normalOct);
            float3 refAxis = abs(up.z) < 0.9 ? float3(0, 0, 1) : float3(1, 0, 0);
            float3 ta = normalize(cross(up, refAxis));
            float3 tb = cross(up, ta);
            float ang = r0 * 6.2831853;
            float3 sideDir = cos(ang) * ta + sin(ang) * tb;   // across the blade
            float3 bendDir = cross(sideDir, up);               // blade face normal at rest

            float height = _BladeSize.x * lerp(1.0 - _BladeSize.z, 1.0 + _BladeSize.z, r1);
            float width  = _BladeSize.y * lerp(1.0 - _BladeSize.w, 1.0 + _BladeSize.w, r2);

            // --- wind: travelling gust waves along the wind direction, in
            // world space so they wrap a planet, plus a per-blade flutter.
            float3 wt = _WindDir.xyz - up * dot(_WindDir.xyz, up);
            float wl = length(wt);
            wt = wl > 1e-3 ? wt / wl : ta;
            float3 wperp = cross(up, wt);
            float time = _Time.y * _WindParams.x;
            float ph = dot(b.pos, _WindDir.xyz) / max(_WindParams.y, 0.5) - time;
            float gust = sin(ph) * 0.5
                       + sin(ph * 2.3 + dot(b.pos, wperp) * 0.7 / max(_WindParams.y, 0.5) + time * 0.7) * 0.3
                       + sin(ph * 0.37 + 1.7) * 0.2;
            gust = gust * 0.5 + 0.5;
            float flutter = sin(time * (2.5 + r2 * 2.0) + r2 * 6.2831853) * _WindParams.z;

            // Dimensionless lean (in units of blade height).
            float3 k = bendDir * (_WindParams.w * (r2 - 0.5) * 2.0)
                     + wt * (_WindDir.w * (0.25 + 0.75 * gust))
                     + bendDir * flutter + sideDir * (flutter * 0.3);

            // --- trample: push away from the player, strongest at the tip.
            if (_BendPos.w > 0.0)
            {
                float3 to = b.pos - _BendPos.xyz;
                to -= up * dot(to, up);
                float dist = length(to);
                float f = saturate(1.0 - dist / _BendPos.w);
                if (f > 0.0) k += (to / max(dist, 1e-3)) * (f * f * 1.5);
            }

            float k2 = dot(k, k);
            // Quadratic bend with a little length compensation so a leaning
            // blade does not read longer than a straight one.
            float3 curve = up * (height * t * (1.0 - 0.35 * k2 * t * t)) + k * (height * t * t);
            float3 tangent = up * (height * (1.0 - 1.05 * k2 * t * t)) + k * (2.0 * height * t);
            float3 faceN = normalize(cross(tangent, sideDir));

            float3 posWS = b.pos + curve;
            float3 V = normalize(_WorldSpaceCameraPos - posWS);

            // Edge-on blades thin to nothing; widen them as the view grazes the
            // face. Faded (distant) blades widen too, so the ground keeps its
            // coverage while the count drops.
            float camDist = distance(_WorldSpaceCameraPos, b.pos);
            float fade = smoothstep(_FadeParams.x, _FadeParams.y, camDist);
            float widen = 1.0 + _ViewThicken * (1.0 - abs(dot(V, faceN))) + _FarWiden * fade;
            float taper = 1.0 - t * (0.4 + 0.6 * t);   // slender: 0.86 at a quarter, 0.65 halfway, 0 at the tip
            posWS += sideDir * (side * 0.5 * width * widen * taper);

            float4 w = GrassUnpackUnorm4x8(b.weights);
            half3 base = (half3)(_ChanBase[0].rgb * w.x + _ChanBase[1].rgb * w.y + _ChanBase[2].rgb * w.z + _ChanBase[3].rgb * w.w);
            half3 tipC = (half3)(_ChanTip[0].rgb * w.x + _ChanTip[1].rgb * w.y + _ChanTip[2].rgb * w.z + _ChanTip[3].rgb * w.w);
            half tint = (half)lerp(0.8, 1.2, GrassHash01(b.seed ^ 0x51ED270Bu));

            BladeVertex o;
            o.posWS = posWS;
            o.normalWS = faceN;
            o.albedo = lerp(base, tipC, (half)t) * tint;
            o.t = (half)t;
            o.ao = (half)ao;
            return o;
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
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half4 color : COLOR;      // rgb albedo, a = t
                half2 misc : TEXCOORD2;   // x ao, y fog
            };

            Varyings Vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                BladeVertex b = BuildBlade(vid, iid);
                Varyings o;
                o.positionWS = b.posWS;
                o.positionCS = TransformWorldToHClip(b.posWS);
                o.normalWS = b.normalWS;
                o.color = half4(b.albedo, b.t);
                o.misc = half2(b.ao, ComputeFogFactor(o.positionCS.z));
                return o;
            }

            half4 Frag(Varyings i, bool isFront : SV_IsFrontFace) : SV_Target
            {
                half3 n = normalize(i.normalWS);
                if (!isFront) n = -n;
                half t = i.color.a;
                half3 albedo = i.color.rgb;

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(shadowCoord);
                half3 V = (half3)normalize(_WorldSpaceCameraPos - i.positionWS);

                half ndl = dot(n, (half3)L.direction);
                half diffuse = saturate((ndl + _Wrap) / (1.0h + _Wrap));
                diffuse = max(diffuse, _AmbientFloor);
                half3 sun = L.color * (L.shadowAttenuation * diffuse);

                // Thin blades glow when the sun is behind them.
                half trans = pow(saturate(dot(-V, (half3)L.direction)), 4.0h) * _Translucency * t * L.shadowAttenuation;
                sun += L.color * trans;

                half3 h = normalize((half3)L.direction + V);
                half spec = pow(saturate(dot(n, h)), 32.0h) * _Specular * t * L.shadowAttenuation;

                // Baked terrain AO plus a root-to-tip gradient: the base of a
                // clump is buried in its neighbours.
                half occ = lerp(1.0h - _RootDarken, 1.0h, t) * lerp(0.6h, 1.0h, i.misc.x);
                half3 ambient = SampleSH(n) * occ;

                half3 rgb = albedo * (sun * lerp(0.75h, 1.0h, occ) + ambient) + L.color * spec;
                rgb = MixFog(rgb, i.misc.y);
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

            Varyings ShadowVert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                BladeVertex b = BuildBlade(vid, iid);
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

            Varyings DepthVert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                BladeVertex b = BuildBlade(vid, iid);
                Varyings o;
                o.positionCS = TransformWorldToHClip(b.posWS);
                return o;
            }

            half DepthFrag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
