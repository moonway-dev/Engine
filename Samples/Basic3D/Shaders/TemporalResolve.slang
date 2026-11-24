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
    return min(uint2((float2(pixel) + 0.5) * float2(constants.sourceExtent) / float2(constants.extent)), constants.sourceExtent - 1u);
}

uint2 neighborhoodPixel(uint2 pixel, int2 offset)
{
    return uint2(clamp(int2(colorPixel(pixel)) + offset, int2(0), int2(constants.sourceExtent) - 1));
}

float4 reconstructedColor(uint2 pixel, float depth)
{
    if (all(constants.sourceExtent == constants.extent))
        return currentColor.Load(int3(pixel, 0));
    float2 coordinate = (float2(pixel) + 0.5) * float2(constants.sourceExtent) / float2(constants.extent) - 0.5;
    int2 origin = int2(floor(coordinate));
    float2 fraction = frac(coordinate);
    float4 total = float4(0.0);
    float totalWeight = 0.0;
    for (int y = 0; y <= 1; y++)
    for (int x = 0; x <= 1; x++)
    {
        uint2 samplePixel = uint2(clamp(origin + int2(x, y), int2(0), int2(constants.sourceExtent) - 1));
        uint2 depthPixel = min(uint2((float2(samplePixel) + 0.5) * float2(constants.extent) / float2(constants.sourceExtent)), constants.extent - 1u);
        float sampleDepth = currentDepth.Load(int3(depthPixel, 0));
        float weight = (x == 0 ? 1.0 - fraction.x : fraction.x) * (y == 0 ? 1.0 - fraction.y : fraction.y);
        weight *= exp(-abs(sampleDepth - depth) / max(constants.depthThreshold * max(abs(depth), 1e-3), 1e-6));
        total += currentColor.Load(int3(samplePixel, 0)) * weight;
        totalWeight += weight;
    }
    return totalWeight > 1e-6 ? total / totalWeight : currentColor.Load(int3(colorPixel(pixel), 0));
}

float3 neighborhoodMinimum(uint2 pixel)
{
    float3 minimumColor = float3(1e20);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        uint2 samplePixel = neighborhoodPixel(pixel, int2(x, y));
        minimumColor = min(minimumColor, currentColor.Load(int3(samplePixel, 0)).rgb);
    }
    return minimumColor;
}

float3 neighborhoodMaximum(uint2 pixel)
{
    float3 maximumColor = float3(-1e20);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        uint2 samplePixel = neighborhoodPixel(pixel, int2(x, y));
        maximumColor = max(maximumColor, currentColor.Load(int3(samplePixel, 0)).rgb);
    }
    return maximumColor;
}

float3 neighborhoodMean(uint2 pixel)
{
    float3 total = float3(0.0);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        uint2 samplePixel = neighborhoodPixel(pixel, int2(x, y));
        total += currentColor.Load(int3(samplePixel, 0)).rgb;
    }
    return total / 9.0;
}

float3 neighborhoodVariance(uint2 pixel, float3 mean)
{
    float3 total = float3(0.0);
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        uint2 samplePixel = neighborhoodPixel(pixel, int2(x, y));
        float3 difference = currentColor.Load(int3(samplePixel, 0)).rgb - mean;
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

    float depth = currentDepth.Load(int3(pixel, 0));
    float4 current = reconstructedColor(pixel, depth);
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
