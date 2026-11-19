Shader "DungeonTycoon/DungeonWarning"
{
    Properties
    {
        [HDR] _BaseColor("Color", Color) = (1, 0.3, 0.2, 0.3)
        _BorderOpacity("Border Opacity", Range(0, 1)) = 0.92
        _Dashed("Dashed Border", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half _BorderOpacity;
                half _Dashed;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 edge = min(input.uv, 1.0 - input.uv);
                float distance = min(edge.x, edge.y);
                float smoothing = max(fwidth(distance), 0.001);
                float border = 1.0 - smoothstep(0.055 - smoothing, 0.055 + smoothing, distance);
                float alongEdge = edge.x < edge.y ? input.uv.y : input.uv.x;
                float dash = step(abs(frac(alongEdge * 5.0 + 0.5) - 0.5), 0.34);
                border *= lerp(1.0, dash, _Dashed);
                half4 color = _BaseColor;
                color.a = lerp(color.a, _BorderOpacity, border);
                return color;
            }
            ENDHLSL
        }
    }
}
