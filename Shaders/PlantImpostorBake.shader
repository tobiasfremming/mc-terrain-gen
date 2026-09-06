// Editor-only: renders one plant mesh into an impostor atlas frame.
//
// Drawn through a CommandBuffer by PlantPrototypeBaker, never by a camera, so
// it deliberately uses the classic CG path: no per-camera URP constants are
// set up in that context, and UnityObjectToClipPos only needs the matrices
// the command buffer itself provides.
//
// Flat colour per submesh (the same _BaseColor the runtime bark/leaf material
// uses, so the card matches the mesh it replaces) with a mild form term from a
// fixed light in the plant's own space. abs() on the dot because leaf cards
// are single quads facing random ways; a canopy half in black is wrong.
Shader "Hidden/MarchingCubes/Plant Impostor Bake"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _LightDir ("Light direction (object space)", Vector) = (0.4, 1, 0.3, 0)
        _Ambient ("Ambient", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Color;
            float4 _LightDir;
            float _Ambient;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 n : TEXCOORD0;
                fixed4 col : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.n = v.normal;
                o.col = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float nl = abs(dot(normalize(i.n), normalize(_LightDir.xyz)));
                float s = _Ambient + (1.0 - _Ambient) * nl;
                // vertex alpha is the non-emission mask (see MarchingCubes/Plant):
                // emissive parts bake at full brightness instead of being shaded.
                float lit = lerp(1.0, s, i.col.a);
                return fixed4(_Color.rgb * i.col.rgb * lit, 1.0);
            }
            ENDCG
        }
    }
}
