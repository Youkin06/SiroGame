#ifndef SIROGAME_DOT_OUTLINE_FULLSCREEN_INCLUDED
#define SIROGAME_DOT_OUTLINE_FULLSCREEN_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

TEXTURE2D_X(_BlitTexture);

float _WorldModeVisualEnabled;
float4 _WorldModeTransitionOrigin;
float _WorldModeTransitionProgress;
float _WorldModeTransitionFrom;
float _WorldModeTransitionTo;
float _WorldModeTransitionFeather;
float4 _WorldModeShiroBackground;
float4 _WorldModeKuroBackground;

float3 DotOutlineSampleColor(float2 uv)
{
    float2 stereoUv = UnityStereoTransformScreenSpaceTex(saturate(uv));
    return SAMPLE_TEXTURE2D_X_LOD(
        _BlitTexture,
        sampler_PointClamp,
        stereoUv,
        0
    ).rgb;
}

float DotOutlineRelativeDepthDifference(float centerDepth, float sampleDepth)
{
    float centerEyeDepth = LinearEyeDepth(centerDepth, _ZBufferParams);
    float sampleEyeDepth = LinearEyeDepth(sampleDepth, _ZBufferParams);
    return abs(centerEyeDepth - sampleEyeDepth) / max(centerEyeDepth, 0.0001);
}

float DotOutlineNormalDifference(float3 centerNormal, float3 sampleNormal)
{
    centerNormal *= rsqrt(max(dot(centerNormal, centerNormal), 0.000001));
    sampleNormal *= rsqrt(max(dot(sampleNormal, sampleNormal), 0.000001));
    return 1.0 - saturate(dot(centerNormal, sampleNormal));
}

bool DotOutlineIsBackground(float rawDepth)
{
#if UNITY_REVERSED_Z
    return rawDepth <= 0.00001;
#else
    return rawDepth >= 0.99999;
#endif
}

void DotOutline_float(float4 UV, out float3 Out)
{
#if defined(SHADERGRAPH_PREVIEW)
    float checker = fmod(floor(UV.x * 16.0) + floor(UV.y * 16.0), 2.0);
    Out = lerp(float3(0.25, 0.25, 0.25), float3(0.75, 0.75, 0.75), checker);
#else
    float pixelSize = max(1.0, round(_PixelSize));
    float2 screenSize = max(_ScreenParams.xy, float2(1.0, 1.0));
    float2 pixelPosition = UV.xy * screenSize;
    float2 snappedPixel = (floor(pixelPosition / pixelSize) + 0.5) * pixelSize;
    float2 snappedUv = saturate(snappedPixel / screenSize);

    float centerDepth = SampleSceneDepth(snappedUv);

    // カメラ背景にはドット化・減色・アウトラインを適用せず、元の色を維持する。
    if (DotOutlineIsBackground(centerDepth))
    {
        float3 originalBackground = DotOutlineSampleColor(UV.xy);
        if (_WorldModeVisualEnabled < 0.5)
        {
            Out = originalBackground;
            return;
        }

        float feather = max(0.001, _WorldModeTransitionFeather);
        float aspect = _ScreenParams.x / max(1.0, _ScreenParams.y);
        float2 screenDelta = UV.xy - _WorldModeTransitionOrigin.xy;
        screenDelta.x *= aspect;

        float2 farthestCorner = max(
            _WorldModeTransitionOrigin.xy,
            1.0 - _WorldModeTransitionOrigin.xy
        );
        farthestCorner.x *= aspect;
        float maximumRadius = length(farthestCorner);
        float easedProgress = smoothstep(
            0.0,
            1.0,
            saturate(_WorldModeTransitionProgress)
        );
        float radius = lerp(
            -feather,
            maximumRadius + feather,
            easedProgress
        );
        float wave = 1.0 - smoothstep(
            radius - feather,
            radius + feather,
            length(screenDelta)
        );
        float mode = lerp(
            _WorldModeTransitionFrom,
            _WorldModeTransitionTo,
            wave
        );
        Out = lerp(
            _WorldModeShiroBackground.rgb,
            _WorldModeKuroBackground.rgb,
            saturate(mode)
        );
        return;
    }

    float3 sourceColor = DotOutlineSampleColor(snappedUv);
    float luminance = dot(sourceColor, float3(0.2126, 0.7152, 0.0722));
    float colorSteps = max(2.0, round(_ColorSteps));
    float gray = floor(saturate(luminance) * (colorSteps - 1.0) + 0.5) /
        (colorSteps - 1.0);
    float3 sceneColor = gray.xxx;

    float outlineWidth = max(0.5, _OutlineThickness) * pixelSize;
    float2 offsetX = float2(outlineWidth / screenSize.x, 0.0);
    float2 offsetY = float2(0.0, outlineWidth / screenSize.y);
    float2 uvLeft = saturate(snappedUv - offsetX);
    float2 uvRight = saturate(snappedUv + offsetX);
    float2 uvDown = saturate(snappedUv - offsetY);
    float2 uvUp = saturate(snappedUv + offsetY);

    float depthDifference = 0.0;
    depthDifference = max(depthDifference,
        DotOutlineRelativeDepthDifference(centerDepth, SampleSceneDepth(uvLeft)));
    depthDifference = max(depthDifference,
        DotOutlineRelativeDepthDifference(centerDepth, SampleSceneDepth(uvRight)));
    depthDifference = max(depthDifference,
        DotOutlineRelativeDepthDifference(centerDepth, SampleSceneDepth(uvDown)));
    depthDifference = max(depthDifference,
        DotOutlineRelativeDepthDifference(centerDepth, SampleSceneDepth(uvUp)));

    float3 centerNormal = SampleSceneNormals(snappedUv);
    float normalDifference = 0.0;
    normalDifference = max(normalDifference,
        DotOutlineNormalDifference(centerNormal, SampleSceneNormals(uvLeft)));
    normalDifference = max(normalDifference,
        DotOutlineNormalDifference(centerNormal, SampleSceneNormals(uvRight)));
    normalDifference = max(normalDifference,
        DotOutlineNormalDifference(centerNormal, SampleSceneNormals(uvDown)));
    normalDifference = max(normalDifference,
        DotOutlineNormalDifference(centerNormal, SampleSceneNormals(uvUp)));

    float depthThreshold = max(0.00001, _DepthThreshold);
    float normalThreshold = max(0.00001, _NormalThreshold);
    float depthEdge = smoothstep(depthThreshold, depthThreshold * 2.0, depthDifference);
    float normalEdge = smoothstep(normalThreshold, normalThreshold * 2.0, normalDifference);
    float edge = saturate(max(depthEdge, normalEdge));

    Out = lerp(sceneColor, _OutlineColor.rgb, edge);
#endif
}

void DotOutline_half(half4 UV, out half3 Out)
{
    float3 result;
    DotOutline_float(UV, result);
    Out = (half3)result;
}

#endif
