Shader "LeTai/TranslucentImage/Demo/SoftLava"
{
    Properties
    {
        _Scale ("Scale", Float) = 2
        _Speed ("Speed", Float) = 0.15
        _ColorA ("Color A", Color) = (0.10, 0.30, 0.85, 1)
        _ColorB ("Color B", Color) = (0.65, 0.25, 0.85, 1)
        _ColorC ("Color C", Color) = (0.95, 0.55, 0.35, 1)

        [Space]
        _FadeLevel ("Fade Level", Range(0, 1)) = 0.4
        _FadeWobble ("Fade Wobble", Float) = 0.3
        _FadeSoftness ("Fade Softness", Range(0.001, 1)) = 0.15
        _MaxAlpha ("Max Alpha", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            float  _Scale;
            float  _Speed;
            float4 _ColorA;
            float4 _ColorB;
            float4 _ColorC;
            float  _FadeLevel;
            float  _FadeWobble;
            float  _FadeSoftness;
            float  _MaxAlpha;
            float4 _OutputSize; // (w, h, 1/w, 1/h), fed by TranslucentImageProceduralTexture

            float2 unity_gradientNoise_dir(float2 p)
            {
                p       = p % 289;
                float x = (34 * p.x + 1) * p.x % 289 + p.y;
                x       = (34 * x + 1) * x % 289;
                x       = frac(x / 41) * 2 - 1;
                return normalize(float2(x - floor(x + 0.5), abs(x) - 0.5));
            }

            float unity_gradientNoise(float2 p)
            {
                float2 ip  = floor(p);
                float2 fp  = frac(p);
                float  d00 = dot(unity_gradientNoise_dir(ip), fp);
                float  d01 = dot(unity_gradientNoise_dir(ip + float2(0, 1)), fp - float2(0, 1));
                float  d10 = dot(unity_gradientNoise_dir(ip + float2(1, 0)), fp - float2(1, 0));
                float  d11 = dot(unity_gradientNoise_dir(ip + float2(1, 1)), fp - float2(1, 1));
                fp         = fp * fp * fp * (fp * (fp * 6 - 15) + 10);
                return lerp(lerp(d00, d01, fp.y), lerp(d10, d11, fp.y), fp.x);
            }

            half4 frag(v2f_img IN) : SV_Target
            {
                float  t      = _Time.y * _Speed;
                float  aspect = _OutputSize.x / _OutputSize.y;
                float2 uv     = float2(IN.uv.x * aspect, IN.uv.y) * _Scale;

                float n1 = unity_gradientNoise(uv + float2(t, t * 0.3));
                float n2 = unity_gradientNoise(uv * 1.3 + float2(-t * 0.8, t * 0.5) + 7.3);
                float n  = saturate((n1 + n2) * 0.5 + 0.5);

                float3 col = n < 0.5
                             ? lerp(_ColorA.rgb, _ColorB.rgb, n * 2.0)
                             : lerp(_ColorB.rgb, _ColorC.rgb, (n - 0.5) * 2.0);

                float edge  = IN.uv.y - _FadeLevel + (n - 0.5) * _FadeWobble;
                float alpha = smoothstep(0.0, _FadeSoftness, edge) * _MaxAlpha;

                return half4(col, alpha);
            }
            ENDCG
        }
    }
}
