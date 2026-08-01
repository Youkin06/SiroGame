Shader "SiroGame/Goal Portal Spiral"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        _BlackColor("Black", Color) = (0.015, 0.015, 0.015, 1)
        _WhiteColor("White", Color) = (0.94, 0.94, 0.94, 1)
        _RotationSpeed("Rotation Speed", Range(-2, 2)) = 0.3
        _SpiralTurns("Spiral Turns", Range(1, 12)) = 4
        _StripeCount("Stripe Count", Range(1, 8)) = 2
        _StripeSoftness("Stripe Softness", Range(0.001, 0.5)) = 0.06
        _Reveal("Reveal", Range(0, 1)) = 0
        _RevealTurns("Reveal Turns", Range(1, 12)) = 4
        _RevealSoftness("Reveal Softness", Range(0.001, 0.2)) = 0.025
        _Aspect("Aspect Correction", Range(0.1, 2)) = 0.7413
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            // 全画面ドット処理が、後ろに物体のないPortalを背景扱いしないよう
            // Opaqueの後・Transparentの前に描画して深度へ書き込む。
            "Queue" = "AlphaTest"
            "RenderType" = "TransparentCutout"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Pass
        {
            Name "GoalPortalSpiral"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BlackColor;
                half4 _WhiteColor;
                float _RotationSpeed;
                float _SpiralTurns;
                float _StripeCount;
                float _StripeSoftness;
                float _Reveal;
                float _RevealTurns;
                float _RevealSoftness;
                float _Aspect;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                // SpriteRendererと頂点Colorを持たないQuadの両方で同じ色になるよう、
                // PortalのTintはマテリアル側だけから取得する。
                output.color = _Color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float aspect = max(0.001, _Aspect);
                float2 centered = input.uv - 0.5;
                centered.x *= aspect;

                float outerRadius = 0.5 * sqrt(aspect * aspect + 1.0);
                float radius01 = saturate(length(centered) / max(outerRadius, 0.001));
                float angle = atan2(centered.y, centered.x);
                float angle01 = frac(angle / TWO_PI + 1.0);

                // 黒白の境界自体も螺旋にし、表示後は時間で回転し続ける。
                float rotation = _Time.y * _RotationSpeed * TWO_PI;
                float stripePhase =
                    angle * _StripeCount +
                    radius01 * _SpiralTurns * TWO_PI -
                    rotation;
                float stripeWave = sin(stripePhase);
                float stripe = smoothstep(
                    -max(0.001, _StripeSoftness),
                    max(0.001, _StripeSoftness),
                    stripeWave
                );
                half3 spiralColor = lerp(_BlackColor.rgb, _WhiteColor.rgb, stripe);

                // 外周から中心へ進む順番に角度差を加え、表示境界を螺旋状にする。
                float revealTurns = max(1.0, _RevealTurns);
                float revealOrder =
                    ((1.0 - radius01) * revealTurns + angle01) /
                    (revealTurns + 1.0);
                float revealSoftness = max(0.0001, _RevealSoftness);
                float revealMask = 1.0 - smoothstep(
                    _Reveal - revealSoftness,
                    _Reveal + revealSoftness,
                    revealOrder
                );

                // 端数誤差に関係なく、idle時は完全透明、完了時は完全表示にする。
                revealMask = _Reveal <= 0.0001 ? 0.0 : revealMask;
                revealMask = _Reveal >= 0.9999 ? 1.0 : revealMask;

                // doorOpenはSquare Spriteなので、元Textureの色やImport時のAlphaには依存させない。
                half alpha = input.color.a * revealMask;
                clip(alpha - 0.001);
                return half4(spiralColor * input.color.rgb, alpha);
            }
            ENDHLSL
        }

        // PC RendererではSSAOがDepthNormals prepassを要求するため、
        // Main PassのZWriteだけでなく専用の深度パスにもPortal形状を書き込む。
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            Cull Off
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthVaryings
            {
                float4 positionHCS : SV_POSITION;
                half alpha : TEXCOORD0;
                float2 uv : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BlackColor;
                half4 _WhiteColor;
                float _RotationSpeed;
                float _SpiralTurns;
                float _StripeCount;
                float _StripeSoftness;
                float _Reveal;
                float _RevealTurns;
                float _RevealSoftness;
                float _Aspect;
            CBUFFER_END

            float DepthRevealMask(float2 uv)
            {
                float aspect = max(0.001, _Aspect);
                float2 centered = uv - 0.5;
                centered.x *= aspect;

                float outerRadius = 0.5 * sqrt(aspect * aspect + 1.0);
                float radius01 = saturate(length(centered) / max(outerRadius, 0.001));
                float angle = atan2(centered.y, centered.x);
                float angle01 = frac(angle / TWO_PI + 1.0);
                float revealTurns = max(1.0, _RevealTurns);
                float revealOrder =
                    ((1.0 - radius01) * revealTurns + angle01) /
                    (revealTurns + 1.0);
                float softness = max(0.0001, _RevealSoftness);
                float mask = 1.0 - smoothstep(
                    _Reveal - softness,
                    _Reveal + softness,
                    revealOrder
                );
                mask = _Reveal <= 0.0001 ? 0.0 : mask;
                return _Reveal >= 0.9999 ? 1.0 : mask;
            }

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.alpha = _Color.a;
                output.uv = input.uv;
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(input.alpha * DepthRevealMask(input.uv) - 0.001);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct DepthNormalsAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct DepthNormalsVaryings
            {
                float4 positionHCS : SV_POSITION;
                half alpha : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _BlackColor;
                half4 _WhiteColor;
                float _RotationSpeed;
                float _SpiralTurns;
                float _StripeCount;
                float _StripeSoftness;
                float _Reveal;
                float _RevealTurns;
                float _RevealSoftness;
                float _Aspect;
            CBUFFER_END

            float DepthNormalsRevealMask(float2 uv)
            {
                float aspect = max(0.001, _Aspect);
                float2 centered = uv - 0.5;
                centered.x *= aspect;

                float outerRadius = 0.5 * sqrt(aspect * aspect + 1.0);
                float radius01 = saturate(length(centered) / max(outerRadius, 0.001));
                float angle = atan2(centered.y, centered.x);
                float angle01 = frac(angle / TWO_PI + 1.0);
                float revealTurns = max(1.0, _RevealTurns);
                float revealOrder =
                    ((1.0 - radius01) * revealTurns + angle01) /
                    (revealTurns + 1.0);
                float softness = max(0.0001, _RevealSoftness);
                float mask = 1.0 - smoothstep(
                    _Reveal - softness,
                    _Reveal + softness,
                    revealOrder
                );
                mask = _Reveal <= 0.0001 ? 0.0 : mask;
                return _Reveal >= 0.9999 ? 1.0 : mask;
            }

            DepthNormalsVaryings DepthNormalsVert(DepthNormalsAttributes input)
            {
                DepthNormalsVaryings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.alpha = _Color.a;
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 DepthNormalsFrag(DepthNormalsVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(input.alpha * DepthNormalsRevealMask(input.uv) - 0.001);

                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normalWS);
                    float2 remapped = saturate(octNormal * 0.5 + 0.5);
                    return half4(PackFloat2To888(remapped), 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }

    Fallback Off
}
