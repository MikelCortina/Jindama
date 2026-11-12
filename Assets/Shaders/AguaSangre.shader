// Agua estilizada para URP con:
//  - olas suaves (vértices) y normales procedurales que se mueven (no necesita texturas),
//  - color según la profundidad (poco profundo / profundo) y espuma en los bordes que tocan el suelo,
//  - refracción (distorsiona lo que hay debajo; usa la Opaque Texture del URP Asset),
//  - reflejo: planar real si el objeto tiene el script AguaReflejo, o el del cielo / reflection probes si no,
//  - sangre: una máscara (la pinta el script AguaSangre) que va tiñendo el agua de rojo por zonas,
//    con bordes orgánicos que se mueven, y ondas en los puntos donde caen las gotas.
// Necesita Depth Texture y Opaque Texture activadas en el URP Asset (en PC_RPAsset ya lo están).
Shader "Jindama/AguaSangre"
{
    Properties
    {
        [Header(Color)]
        _ColorSomero ("Color poco profundo (A = opacidad)", Color) = (0.22, 0.62, 0.78, 0.55)
        _ColorProfundo ("Color profundo (A = opacidad)", Color) = (0.03, 0.14, 0.30, 0.95)
        _Profundidad ("Distancia hasta el color profundo", Float) = 2.5
        _LuzMinima ("Luz mínima", Range(0, 1)) = 0.65

        [Header(Olas)]
        _EscalaOlas ("Escala de las olas", Float) = 0.6
        _VelocidadOlas ("Velocidad de las olas", Float) = 0.35
        _FuerzaNormales ("Fuerza de las normales", Range(0, 2)) = 0.45
        _AlturaOlas ("Altura de las olas (vértices)", Float) = 0.04

        [Header(Reflejo)]
        _Reflejo ("Fuerza del reflejo", Range(0, 1)) = 0.6
        _ReflejoMinimo ("Reflejo mirando desde arriba", Range(0, 1)) = 0.2
        _Fresnel ("Potencia Fresnel", Range(0.5, 8)) = 3
        _DistorsionReflejo ("Distorsión del reflejo", Range(0, 0.2)) = 0.03
        [Toggle] _InvertirReflejoY ("Invertir reflejo en vertical (si sale al revés)", Float) = 0
        _Brillo ("Brillo especular", Range(0, 4)) = 1.2
        _Dureza ("Dureza especular", Range(8, 512)) = 160

        [Header(Refraccion y espuma)]
        _Refraccion ("Refracción", Range(0, 0.1)) = 0.02
        _ColorEspuma ("Color de la espuma (A = fuerza)", Color) = (0.85, 0.95, 1, 0.8)
        _AnchoEspuma ("Ancho de la espuma", Float) = 0.25

        [Header(Sangre)]
        _ColorSangre ("Color de la sangre", Color) = (0.55, 0.02, 0.03, 1)
        _ColorSangreProfunda ("Color de la sangre profunda", Color) = (0.16, 0.0, 0.01, 1)
        _UmbralSangre ("Umbral", Range(0, 1)) = 0.22
        _SuavidadSangre ("Suavidad del borde", Range(0.01, 0.5)) = 0.12
        _BordeSangre ("Brillo del borde", Range(0, 1)) = 0.35
        _RuidoSangre ("Bordes irregulares", Range(0, 0.15)) = 0.04
        _FuerzaOndas ("Fuerza de las ondas de las gotas", Range(0, 2)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Agua"
            Tags { "LightMode" = "UniversalForward" }
            // Mezcla a mano con la Opaque Texture (refracción), así que no hace falta Blend.
            Blend Off
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorSomero, _ColorProfundo, _ColorEspuma, _ColorSangre, _ColorSangreProfunda;
                float _Profundidad, _LuzMinima;
                float _EscalaOlas, _VelocidadOlas, _FuerzaNormales, _AlturaOlas;
                float _Reflejo, _ReflejoMinimo, _Fresnel, _DistorsionReflejo, _InvertirReflejoY, _Brillo, _Dureza;
                float _Refraccion, _AnchoEspuma;
                float _UmbralSangre, _SuavidadSangre, _BordeSangre, _RuidoSangre, _FuerzaOndas;
            CBUFFER_END

            // Los pone cada objeto con un MaterialPropertyBlock (scripts AguaSangre y AguaReflejo).
            TEXTURE2D(_MascaraSangre);    SAMPLER(sampler_MascaraSangre);
            TEXTURE2D(_AguaReflejoTex);   SAMPLER(sampler_AguaReflejoTex);
            float4 _AguaLimites;          // xy = esquina mínima (x, z) del agua, zw = tamaño. z = 0 → sin sangre
            float  _AguaReflejoActivo;    // 1 = hay reflejo planar
            float4 _Ondas[8];             // xy = posición (x, z), z = instante, w = fuerza

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float  fog        : TEXCOORD2;
            };

            // ---------------------------------------------------------------- Ruido
            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float Ruido(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }
            float AlturaOla(float2 p, float t)
            {
                float2 q = p * _EscalaOlas;
                return Ruido(q + float2(t, t * 0.6) * _VelocidadOlas) * 0.6
                     + Ruido(q * 2.3 - float2(t * 0.8, -t) * _VelocidadOlas) * 0.4;
            }

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                float t = _Time.y;
                ws.y += (sin(ws.x * 0.8 + t * 1.3) + sin(ws.z * 0.6 - t * 1.1)) * 0.5 * _AlturaOlas;
                o.positionWS = ws;
                o.positionCS = TransformWorldToHClip(ws);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 xz = i.positionWS.xz;

                // ---- Normal: derivadas del ruido + ondas de las gotas.
                const float e = 0.08;
                float h0 = AlturaOla(xz, t);
                float dx = (AlturaOla(xz + float2(e, 0), t) - h0) / e;
                float dz = (AlturaOla(xz + float2(0, e), t) - h0) / e;
                float2 inclinacion = float2(dx, dz) * _FuerzaNormales;

                [unroll] for (int k = 0; k < 8; k++)
                {
                    float4 o = _Ondas[k];
                    float edad = t - o.z;
                    if (o.w <= 0 || edad < 0 || edad > 2.5) continue;
                    float2 d = xz - o.xy;
                    float dist = length(d) + 1e-4;
                    float frente = edad * 1.6;                                  // radio del anillo
                    float anillo = cos((dist - frente) * 14.0) * exp(-abs(dist - frente) * 4.0);
                    inclinacion += (d / dist) * anillo * o.w * _FuerzaOndas * (1.0 - edad / 2.5);
                }
                float3 n = normalize(float3(-inclinacion.x, 1.0, -inclinacion.y));

                // ---- Profundidad y refracción.
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float profSuperficie = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float2 uvR = uv + n.xz * _Refraccion;
                float profEscena = LinearEyeDepth(SampleSceneDepth(uvR), _ZBufferParams);
                if (profEscena < profSuperficie) { uvR = uv; profEscena = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams); }
                float diferencia = max(0.0, profEscena - profSuperficie);
                float3 fondo = SampleSceneColor(uvR);

                float tProf = saturate(diferencia / max(0.01, _Profundidad));
                half3 color = lerp(_ColorSomero.rgb, _ColorProfundo.rgb, tProf);
                float opacidad = lerp(_ColorSomero.a, _ColorProfundo.a, tProf);

                // ---- Sangre.
                float sangre = 0, borde = 0;
                if (_AguaLimites.z > 0)
                {
                    float2 uvS = (xz - _AguaLimites.xy) / _AguaLimites.zw;
                    float2 remolino = float2(Ruido(xz * 1.3 + t * 0.07), Ruido(xz * 1.3 - t * 0.05 + 17.0)) - 0.5;
                    uvS += remolino * _RuidoSangre * 2.0 + n.xz * 0.01;
                    float m = SAMPLE_TEXTURE2D(_MascaraSangre, sampler_MascaraSangre, uvS).r;
                    sangre = smoothstep(_UmbralSangre - _SuavidadSangre, _UmbralSangre + _SuavidadSangre, m);
                    borde = sangre * (1.0 - sangre) * 4.0 * _BordeSangre;
                    half3 colorSangre = lerp(_ColorSangre.rgb, _ColorSangreProfunda.rgb, saturate(tProf * 0.7 + m * 0.4));
                    color = lerp(color, colorSangre, sangre) + _ColorSangre.rgb * borde;
                    opacidad = lerp(opacidad, 0.97, sangre);
                }

                // ---- Luz.
                Light luz = GetMainLight();
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                float ndl = saturate(dot(n, luz.direction));
                color *= lerp(_LuzMinima, 1.0, ndl);

                // ---- Reflejo.
                float fresnel = pow(1.0 - saturate(dot(n, V)), _Fresnel);
                float fuerzaReflejo = lerp(_ReflejoMinimo, 1.0, fresnel) * _Reflejo * (1.0 - sangre * 0.35);
                half3 reflejo;
                if (_AguaReflejoActivo > 0.5)
                {
                    float2 uvRef = i.screenPos.xy / i.screenPos.w;
                    uvRef.x = 1.0 - uvRef.x;   // la cámara del reflejo da la imagen volteada en horizontal
                    if (_InvertirReflejoY > 0.5) uvRef.y = 1.0 - uvRef.y;
                    uvRef += n.xz * _DistorsionReflejo;
                    reflejo = SAMPLE_TEXTURE2D(_AguaReflejoTex, sampler_AguaReflejoTex, uvRef).rgb;
                }
                else
                {
                    reflejo = GlossyEnvironmentReflection(reflect(-V, n), 0.05, 1.0);
                }
                color = lerp(color, reflejo, fuerzaReflejo);

                // ---- Espuma donde el agua toca algo.
                float espuma = 1.0 - saturate(diferencia / max(0.001, _AnchoEspuma));
                espuma = step(0.5, espuma * (0.6 + 0.8 * Ruido(xz * 6.0 + t * 0.5))) * _ColorEspuma.a * (1.0 - sangre * 0.6);
                color = lerp(color, lerp(_ColorEspuma.rgb, _ColorSangre.rgb, sangre), espuma);

                // ---- Brillo especular.
                float3 H = normalize(luz.direction + V);
                color += luz.color * pow(saturate(dot(n, H)), _Dureza) * _Brillo * (1.0 - sangre * 0.3);

                half3 final = lerp(fondo, color, saturate(opacidad + espuma));
                final = MixFog(final, i.fog);
                return half4(final, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
