Shader "Hidden/LeTai/TranslucentImage/Demo/ScratchBrush"
{
    Properties
    {
        _Hardness ("Hardness", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
        }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One One
        BlendOp Min
        ColorMask A

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _Hardness;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv  = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float d = length(i.uv * 2.0 - 1.0);
                float coverage = 1.0 - smoothstep(_Hardness, 1.0, d);
                return fixed4(0, 0, 0, 1.0 - coverage);
            }
            ENDCG
        }
    }
}
