Shader "LeTai/TranslucentImage/Demo/ShapeGrid"
{
    Properties
    {
        _Size ("Size", Range(0.01, 2)) = 1
        _Spacing ("Spacing", Range(0.2,4)) = 2
        _ScaleMin ("Scale Min", Range(0, 2)) = 0.64
        _ScaleMax ("Scale Max", Range(0, 2)) = 0.96
        _AnimationDuration ("Animation Duration", Range(0.01, 1)) = 0.28
        _AnimationInterval ("Animation Interval", Range(0.1, 5)) = 2
        _RowAnimationDuration ("Row Animation Duration", Range(0.01, 1)) = 0.4
        _RowAnimationInterval ("Row Animation Interval", Range(0.1, 10)) = 4
        _ScrollX ("Scroll X", Float) = 0.18
        _ScrollY ("Scroll Y", Float) = 0.08
        _Background ("Background", Color) = (1,1,1,1)
        _Color1 ("Palette 1", Color) = (1,1,1,1)
        _Color2 ("Palette 2", Color) = (1,1,1,1)
        _Color3 ("Palette 3", Color) = (1,1,1,1)
        _Color4 ("Palette 4", Color) = (1,1,1,1)
        _Color5 ("Palette 5", Color) = (1,1,1,1)
        _Color6 ("Palette 6", Color) = (1,1,1,1)
        _Color7 ("Palette 7", Color) = (1,1,1,1)
        _Color8 ("Palette 8", Color) = (1,1,1,1)
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
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 surfacePos : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                float3 axisX = mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0));
                float3 axisY = mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0));

                v2f o;
                o.pos        = UnityObjectToClipPos(v.vertex);
                o.surfacePos = v.vertex.xy * float2(length(axisX), length(axisY));
                return o;
            }

            float  _Size;
            float  _Spacing;
            float  _ScaleMin;
            float  _ScaleMax;
            float  _AnimationDuration;
            float  _AnimationInterval;
            float  _RowAnimationDuration;
            float  _RowAnimationInterval;
            float  _ScrollX;
            float  _ScrollY;
            float4 _Background;
            float4 _Color1;
            float4 _Color2;
            float4 _Color3;
            float4 _Color4;
            float4 _Color5;
            float4 _Color6;
            float4 _Color7;
            float4 _Color8;

            float getSalt(uint stream)
            {
                uint state = (stream + 12345u) * 747796405u + 2891336453u;
                uint value = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
                value      = (value >> 22u) ^ value;
                return 1 + (value >> 16u) * (63.0 / 65535.0);
            }

            float3 hash31(float seed)
            {
                float3 value = frac(seed * float3(0.1031, 0.1030, 0.0973));
                value += dot(value, value.yzx + 33.33);
                return frac((value.xxy + value.yzz) * value.zyx);
            }

            struct State
            {
                float scale;
                float rotationIndex;
                float shapeIndex;
            };

            State getState(float cellSeed, float interval)
            {
                float3 rand = hash31(cellSeed + interval * getSalt(0u));
                State  animation;
                animation.scale         = lerp(_ScaleMin, _ScaleMax, floor(rand.x * 3) * 0.5);
                animation.rotationIndex = rand.y >= 0.5 ? 1 : 0;
                animation.shapeIndex    = rand.z >= 0.5 ? 1 : 0;
                return animation;
            }

            float getBurst(float phase, float duration, float interval, out float burstIndex)
            {
                float t    = _Time.y + phase * interval;
                burstIndex = floor(t / interval);
                return smoothstep(0, duration, t - burstIndex * interval);
            }

            float getRowState(float rowSeed, float interval)
            {
                return hash31(rowSeed + interval * getSalt(1u)).x >= 0.5 ? 1 : 0;
            }

            float2 slideRows(float2 gridUv)
            {
                float  row  = floor(gridUv.y);
                float  seed = row * getSalt(2u);
                float3 rand = hash31(seed);
                float  burstIndex;
                float  burst = getBurst(rand.x, _RowAnimationDuration, _RowAnimationInterval, burstIndex);
                float  dir   = rand.y >= 0.5 ? 1 : -1;
                float  prev  = getRowState(seed, burstIndex - 1);
                float  curr  = getRowState(seed, burstIndex);
                float  slide = lerp(prev, curr, burst) * dir;

                return float2(gridUv.x - slide, gridUv.y);
            }

            float2 getShapeRotation(float rotationIndex)
            {
                float2 straightAxis = float2(1, 0);
                float2 diagonalAxis = (sqrt(2) / 2 / .9).xx;
                return rotationIndex >= 0.5 ? diagonalAxis : straightAxis;
            }

            float2 rotate(float2 position, float2 axis)
            {
                return float2(
                    axis.x * position.x - axis.y * position.y,
                    axis.y * position.x + axis.x * position.y
                );
            }

            float3 getPaletteColor(float random)
            {
                float index = floor(random * 8);
                // rider doesn't handle nested ternary well
                float3 color12 = index < 1 ? _Color1.rgb : _Color2.rgb;
                float3 color34 = index < 3 ? _Color3.rgb : _Color4.rgb;
                float3 color56 = index < 5 ? _Color5.rgb : _Color6.rgb;
                float3 color78 = index < 7 ? _Color7.rgb : _Color8.rgb;
                float3 color14 = index < 2 ? color12 : color34;
                float3 color58 = index < 6 ? color56 : color78;
                return index < 4 ? color14 : color58;
            }

            half4 frag(v2f input) : SV_Target
            {
                float2 gridUv = input.surfacePos / _Spacing + _Time.y * float2(_ScrollX, _ScrollY);
                gridUv        = slideRows(gridUv);

                float2 cell       = floor(gridUv);
                float2 posLocal   = (frac(gridUv) - 0.5) * _Spacing;
                float  cellSeed   = dot(cell, float2(getSalt(3u), getSalt(4u)));
                float3 cellRandom = hash31(cellSeed);

                float interval;
                float burst     = getBurst(cellRandom.x, _AnimationDuration, _AnimationInterval, interval);
                State statePrev = getState(cellSeed, interval - 1);
                State stateCurr = getState(cellSeed, interval);
                float scale     = lerp(statePrev.scale, stateCurr.scale, burst);

                float2 rotPrev = getShapeRotation(statePrev.rotationIndex);
                float2 rotCurr = getShapeRotation(stateCurr.rotationIndex);
                float2 rot     = lerp(rotPrev, rotCurr, burst);
                posLocal       = rotate(posLocal, rot);

                float morph      = lerp(statePrev.shapeIndex, stateCurr.shapeIndex, burst);
                float distCircle = length(posLocal);
                float distSquare = max(abs(posLocal.x), abs(posLocal.y));
                float dist       = lerp(distCircle, distSquare, morph) - 0.5 * _Size * scale;
                float edge       = fwidth(dist);
                float shape      = 1 - smoothstep(-edge, edge, dist);

                float3 color = getPaletteColor(cellRandom.y);
                return half4(lerp(_Background.rgb, color, shape), _Background.a);
            }
            ENDCG
        }
    }
}
