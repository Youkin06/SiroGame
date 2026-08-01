Shader "SiroGame/UI Dot Image"
{
    Properties
    {
        [PerRendererData] _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1,1,1,1)
        _PixelSize("Pixel Size", Range(1,16)) = 4
        _ColorSteps("Color Steps", Range(2,16)) = 4
        _OutlineColor("Outline Color", Color) = (0,0,0,1)

        _StencilComp("Stencil Comparison", Float) = 8
        _Stencil("Stencil ID", Float) = 0
        _StencilOp("Stencil Operation", Float) = 0
        _StencilWriteMask("Stencil Write Mask", Float) = 255
        _StencilReadMask("Stencil Read Mask", Float) = 255
        _ColorMask("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIDot"

            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 positionOS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _Color;
            float4 _OutlineColor;
            float4 _ClipRect;
            float _PixelSize;
            float _ColorSteps;

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = UnityObjectToClipPos(input.positionOS);
                output.positionOS = input.positionOS;
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            float SampleAlpha(float2 uv)
            {
                return tex2D(_MainTex, uv).a;
            }

            fixed4 Frag(Varyings input) : SV_Target
            {
                float pixelSize = max(1.0, round(_PixelSize));
                float2 snappedPosition =
                    (floor(input.positionCS.xy / pixelSize) + 0.5) * pixelSize;
                float2 pixelDelta = snappedPosition - input.positionCS.xy;
                float2 uvDx = ddx(input.uv);
                float2 uvDy = ddy(input.uv);
                float2 snappedUv = input.uv + uvDx * pixelDelta.x + uvDy * pixelDelta.y;

                fixed4 source = tex2D(_MainTex, snappedUv) * input.color;
                float steps = max(2.0, round(_ColorSteps));
                float luminance = dot(source.rgb, float3(0.2126, 0.7152, 0.0722));
                float gray = floor(saturate(luminance) * (steps - 1.0) + 0.5) /
                    (steps - 1.0);

                float2 outlineDx = uvDx * pixelSize;
                float2 outlineDy = uvDy * pixelSize;
                float alphaDifference = 0.0;
                alphaDifference = max(alphaDifference,
                    abs(source.a - SampleAlpha(snappedUv - outlineDx)));
                alphaDifference = max(alphaDifference,
                    abs(source.a - SampleAlpha(snappedUv + outlineDx)));
                alphaDifference = max(alphaDifference,
                    abs(source.a - SampleAlpha(snappedUv - outlineDy)));
                alphaDifference = max(alphaDifference,
                    abs(source.a - SampleAlpha(snappedUv + outlineDy)));
                float edge = smoothstep(0.05, 0.25, alphaDifference);

                fixed4 result = fixed4(gray.xxx, source.a);
                result.rgb = lerp(result.rgb, _OutlineColor.rgb, edge);
                result.a = max(result.a, edge * _OutlineColor.a);

                #ifdef UNITY_UI_CLIP_RECT
                result.a *= UnityGet2DClipping(input.positionOS.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a - 0.001);
                #endif

                return result;
            }
            ENDCG
        }
    }
}
