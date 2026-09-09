Shader "Hidden/Tardigrade/UnderwaterCamera"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _WaterTint ("Water tint", Color) = (0.72, 0.96, 1.02, 1)
        _HazeColor ("Haze", Color) = (0.02, 0.15, 0.18, 1)
        _Clarity ("Clarity", Range(0, 1)) = 0.9
        _Noise ("Sensor noise", Range(0, 0.1)) = 0.008
        _Vignette ("Lens vignette", Range(0, 1)) = 0.16
        _FrameSeed ("Frame seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _WaterTint;
            float4 _HazeColor;
            float _Clarity;
            float _Noise;
            float _Vignette;
            float _FrameSeed;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            float Hash(float2 pixel)
            {
                return frac(sin(dot(pixel + _FrameSeed, float2(12.9898, 78.233))) * 43758.5453);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).rgb;
                color = pow(max(color, 0.0), 0.96) * _WaterTint.rgb;
                color = lerp(_HazeColor.rgb, color, _Clarity);

                float2 centered = input.uv * 2.0 - 1.0;
                float lensFalloff = saturate(1.0 - dot(centered, centered) * _Vignette);
                color *= lerp(0.72, 1.0, lensFalloff);

                float grain = Hash(floor(input.positionHCS.xy)) - 0.5;
                color += grain * _Noise;
                return half4(saturate(color), 1.0);
            }
            ENDHLSL
        }
    }
}
