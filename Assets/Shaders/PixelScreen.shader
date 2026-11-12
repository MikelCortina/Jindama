Shader "Custom/PixelScreen"
{
    Properties
    {
        _Palette ("Palette", 2D) = "white" {}
        _PaletteSize ("Palette Size", Float) = 16
        _DitherStrength ("Dither Strength", Range(0, 0.5)) = 0.15
        _Vignette ("Vignette", Range(0, 1.5)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "EclipsiumPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            TEXTURE2D(_Palette);

            CBUFFER_START(UnityPerMaterial)
                float _PaletteSize;
                float _DitherStrength;
                float _Vignette;
            CBUFFER_END

            static const float bayer[16] =
            {
                0.0, 8.0, 2.0, 10.0,
                12.0, 4.0, 14.0, 6.0,
                3.0, 11.0, 1.0, 9.0,
                15.0, 7.0, 13.0, 5.0
            };

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.texcoord;
                float3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;

                // Viñeta suave
                float d = distance(uv, float2(0.5, 0.5));
                col *= 1.0 - smoothstep(0.3, 0.9, d) * _Vignette;

                // Dithering ordenado (por píxel de baja resolución)
                uint2 p = (uint2)input.positionCS.xy;
                float t = bayer[(p.x % 4) + (p.y % 4) * 4] / 16.0 - 0.5;
                col += t * _DitherStrength;

                // Color de la paleta más cercano
                float3 best = float3(0, 0, 0);
                float bestDist = 1e9;

                [loop]
                for (int i = 0; i < (int)_PaletteSize; i++)
                {
                    float2 puv = float2((i + 0.5) / _PaletteSize, 0.5);
                    float3 c = SAMPLE_TEXTURE2D_LOD(_Palette, sampler_PointClamp, puv, 0).rgb;
                    float3 diff = col - c;
                    float dd = dot(diff, diff);
                    if (dd < bestDist)
                    {
                        bestDist = dd;
                        best = c;
                    }
                }

                return half4(best, 1);
            }
            ENDHLSL
        }
    }
}