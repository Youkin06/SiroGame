Shader "SiroGame/Dot Outline Fullscreen"
{
    Properties
    {
        _PixelSize("Pixel Size", Range(1, 16)) = 4
        _ColorSteps("Color Steps", Range(2, 16)) = 4
        _OutlineThickness("Outline Thickness", Range(0.5, 3)) = 1
        _DepthThreshold("Depth Threshold", Range(0.001, 0.2)) = 0.02
        _NormalThreshold("Normal Threshold", Range(0.01, 1)) = 0.2
        _OutlineColor("Outline Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "DotOutline"
            ZTest Always
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PixelSize;
                float _ColorSteps;
                float _OutlineThickness;
                float _DepthThreshold;
                float _NormalThreshold;
                float4 _OutlineColor;
            CBUFFER_END

            #define SIROGAME_BLIT_TEXTURE_DECLARED 1
            #include "DotOutlineFullscreen.hlsl"

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 color;
                DotOutline_float(float4(input.texcoord, 0.0, 1.0), color);
                return float4(color, 1.0);
            }
            ENDHLSL
        }
    }
}
