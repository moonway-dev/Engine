[[vk::binding(0, 0)]] Texture2D<float4> currentColor;
#if defined(RT_GI_DENOISE) && RT_GI_DENOISE
[[vk::binding(1, 0)]] Texture2D<float4> motionVectors;
#else
[[vk::binding(1, 0)]] Texture2D<float2> motionVectors;
#endif
[[vk::binding(2, 0)]] Texture2D<float> currentDepth;
[[vk::binding(3, 0)]] Texture2D<float4> previousHistory;
[[vk::binding(4, 0)]] RWTexture2D<float4> resolvedHistory;
[[vk::binding(5, 0)]] SamplerState linearSampler;

struct TemporalConstants
{
    uint2 extent;
    uint2 sourceExtent;
    uint historyValid;
    float historyWeight;
    float depthThreshold;
};
[[vk::push_constant]] TemporalConstants constants;

uint2 colorPixel(uint2 pixel)
{
    uint width;
    uint height;
    currentColor.GetDimensions(width, height);
    return min(uint2((float2(pixel) + 0.5) * float2(constants.sourceExtent) / float2(constants.extent)), uint2(width, height) - 1u);
}

float3 neighborhoodMinimum(uint2 pixel)
{
    float3 minimumColor = float3(1e20);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int2 samplePixel = clamp(int2(pixel) + int2(x, y), int2(0), int2(constants.extent) - 1);
        minimumColor = min(minimumColor, currentColor.Load(int3(colorPixel(uint2(samplePixel)), 0)).rgb);
    }
    return minimumColor;
}

float3 neighborhoodMaximum(uint2 pixel)
{
    float3 maximumColor = float3(-1e20);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int2 samplePixel = clamp(int2(pixel) + int2(x, y), int2(0), int2(constants.extent) - 1);
        maximumColor = max(maximumColor, currentColor.Load(int3(colorPixel(uint2(samplePixel)), 0)).rgb);
    }
    return maximumColor;
}

float3 neighborhoodMean(uint2 pixel)
{
    float3 total = float3(0.0);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int2 samplePixel = clamp(int2(pixel) + int2(x, y), int2(0), int2(constants.extent) - 1);
        total += currentColor.Load(int3(colorPixel(uint2(samplePixel)), 0)).rgb;
    }
    return total / 9.0;
}

float3 neighborhoodVariance(uint2 pixel, float3 mean)
{
    float3 total = float3(0.0);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int2 samplePixel = clamp(int2(pixel) + int2(x, y), int2(0), int2(constants.extent) - 1);
        float3 difference = currentColor.Load(int3(colorPixel(uint2(samplePixel)), 0)).rgb - mean;
        total += difference * difference;
    }
    return total / 9.0;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    if (any(pixel >= constants.extent))
        return;

    float4 current = currentColor.Load(int3(colorPixel(pixel), 0));
    float depth = currentDepth.Load(int3(pixel, 0));
    if (constants.historyValid == 0)
    {
        resolvedHistory[pixel] = float4(current.rgb, depth);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(constants.extent);
#if defined(RT_GI_DENOISE) && RT_GI_DENOISE
    float4 motionDepth = motionVectors.Load(int3(pixel, 0));
    float2 motion = motionDepth.xy;
#else
    float2 motion = motionVectors.Load(int3(pixel, 0));
#endif
    float2 previousUv = uv - motion;
    bool inside = all(previousUv >= 0.0) && all(previousUv <= 1.0);
    if (!inside)
    {
        resolvedHistory[pixel] = float4(current.rgb, depth);
        return;
    }

    float4 history = previousHistory.SampleLevel(linearSampler, previousUv, 0);
#if defined(RT_GI_DENOISE) && RT_GI_DENOISE
    float expectedDepth = motionDepth.z;
#else
    float expectedDepth = depth;
#endif
    float depthDifference = abs(history.a - expectedDepth);
    float allowedDepthDifference = constants.depthThreshold * max(max(abs(expectedDepth), abs(history.a)), 1e-3);
    if (depthDifference > allowedDepthDifference)
    {
        resolvedHistory[pixel] = float4(current.rgb, depth);
        return;
    }

    float3 minimumColor = neighborhoodMinimum(pixel);
    float3 maximumColor = neighborhoodMaximum(pixel);
#if defined(RT_GI_DENOISE) && RT_GI_DENOISE
    float3 margin = (maximumColor - minimumColor) * 0.5 + float3(0.02);
    minimumColor -= margin;
    maximumColor += margin;
#else
    float3 meanColor = neighborhoodMean(pixel);
    float3 variance = neighborhoodVariance(pixel, meanColor);
    float3 deviation = sqrt(max(variance, float3(0.0)));
    minimumColor = max(meanColor - deviation * 1.25f, minimumColor);
    maximumColor = min(meanColor + deviation * 1.25f, maximumColor);
#endif
    float3 clippedHistory = clamp(history.rgb, minimumColor, maximumColor);
    float velocityPixels = length(motion * float2(constants.extent));
    float weight = constants.historyWeight * saturate(1.0 - velocityPixels / 32.0);
    resolvedHistory[pixel] = float4(lerp(current.rgb, clippedHistory, weight), depth);
}
