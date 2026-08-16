Shader "Skybox/CartoonClouds"
{
    Properties
    {
        _BackgroundColor ("Background Color", Color) = (0,0,0,1) // flat sky behind clouds
        _CloudColor      ("Cloud Color", Color)      = (1,1,1,1)

        _Scale     ("Noise Scale", Float)        = 1.2
        _Speed     ("Wind Speed", Float)         = 0.05
        _WindDir   ("Wind Dir (xy)", Vector)     = (1, 0, 0, 0)

        _Coverage  ("Coverage (more = fewer clouds)", Range(0,1)) = 0.55
        _Softness  ("Edge Softness",                    Range(0,1)) = 0.12
        _Bands     ("Toon Bands",                       Range(1,8)) = 3
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Front
        ZWrite Off
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; };
            struct v2f     { float4 pos : SV_POSITION; float3 vdir : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos  = UnityObjectToClipPos(v.vertex);
                // View-space direction from camera to skybox vertex
                o.vdir = normalize(mul((float3x3)UNITY_MATRIX_MV, v.vertex.xyz));
                return o;
            }

            // ---- tiny value-noise + 3-octave fbm ----
            float hash21 (float2 p) {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float noise2D (float2 p) {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = hash21(i);
                float b = hash21(i + float2(1,0));
                float c = hash21(i + float2(0,1));
                float d = hash21(i + float2(1,1));
                float2 u = f*f*(3.0 - 2.0*f);
                return lerp(lerp(a,b,u.x), lerp(c,d,u.x), u.y);
            }

            float fbm2D (float2 p) {
                float f = 0.0;
                float a = 0.55;
                for (int i=0; i<3; i++) {
                    f += a * noise2D(p);
                    p = p*2.02 + 37.0;
                    a *= 0.5;
                }
                return f;
            }

            float4 _BackgroundColor, _CloudColor;
            float  _Scale, _Speed, _Coverage, _Softness, _Bands;
            float4 _WindDir; // xy used
            // _Time is already provided by Unity

            float4 frag (v2f i) : SV_Target
            {
                // Equirectangular UV from view direction
                float3 d = normalize(i.vdir);
                float u = atan2(d.x, d.z) / (2.0 * UNITY_PI) + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) / UNITY_PI + 0.5;
                float2 uv = float2(u, v);

                // Wind + scale
                uv = uv * _Scale + _WindDir.xy * (_Speed * _Time.y);

                // Puffy base + toon steps
                float n  = fbm2D(uv);
                float m  = saturate( (n - _Coverage) / max(1e-4, _Softness) ); // 0..1 mask
                float bs = max(1.0, _Bands);
                float q  = floor(m * bs) / bs; // quantized toon bands

                float3 col = lerp(_BackgroundColor.rgb, _CloudColor.rgb, q);
                return float4(col, 1.0); // skybox is opaque
            }
            ENDHLSL
        }
    }
    Fallback Off
}
