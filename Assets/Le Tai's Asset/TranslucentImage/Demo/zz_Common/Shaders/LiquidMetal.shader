Shader "LeTai/TranslucentImage/Demo/LiquidMetal"
{
    Properties
    {
        _Scale ("Scale", Float) = 2.5
        _Speed ("Speed", Float) = 0.12
        _Distort ("Distortion", Float) = 1.2
        _HueRange ("Hue Range", Float) = 0.4
        _HueOffset ("Hue Offset", Range(0, 1)) = 0.55
        _Saturation ("Saturation", Range(0, 1)) = 0.7
        _Brightness ("Brightness", Range(0, 2)) = 1

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
            float  _Distort;
            float  _HueRange;
            float  _HueOffset;
            float  _Saturation;
            float  _Brightness;
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

            float3 unity_hsv2rgb(float3 hsv)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 P = abs(frac(hsv.xxx + K.xyz) * 6.0 - K.www);
                return hsv.z * lerp(K.xxx, saturate(P - K.xxx), hsv.y);
            }

            half4 frag(v2f_img IN) : SV_Target
            {
                float  t      = _Time.y * _Speed;
                float  aspect = _OutputSize.x / _OutputSize.y;
                float2 uv     = float2(IN.uv.x * aspect, IN.uv.y) * _Scale;

                float2 warp = float2(
                    unity_gradientNoise(uv + t),
                    unity_gradientNoise(uv + 5.2 - t)
                );

                float  n   = unity_gradientNoise(uv + warp * _Distort + t * 0.5);
                float  hue = frac((n + 0.5) * _HueRange + _HueOffset);
                float3 col = unity_hsv2rgb(float3(hue, _Saturation, _Brightness));

                float edge  = IN.uv.y - _FadeLevel + n * _FadeWobble;
                float alpha = smoothstep(0.0, _FadeSoftness, edge) * _MaxAlpha;

                return half4(col, alpha);
            }
            ENDCG
        }
    }
}
