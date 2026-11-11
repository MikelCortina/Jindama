// Haz de luz plano y semitransparente para URP.
// Usa el alfa del color de vértice que genera LightShaft.cs (se apaga con la distancia)
// y se desvanece con la niebla de la escena.
Shader "Custom/LightShaft"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.75, 1.0, 0.9, 0.12)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5  // SrcAlpha
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend (One = aditivo)", Float) = 1  // One
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0  // Off: se ve desde dentro y fuera
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LightShaft"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                half fogFactor : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color * _Color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 c = i.color;
                // 1 sin niebla, tiende a 0 dentro de la niebla
                half visibility = MixFogColor(half3(1, 1, 1), half3(0, 0, 0), i.fogFactor).r;
                c.a *= visibility;
                return c;
            }
            ENDHLSL
        }
    }
}
