[[vk::binding(0, 0)]] Texture2D<float4> currentEffect;
[[vk::binding(1, 0)]] Texture2D<float> currentDepth;
[[vk::binding(2, 0)]] Texture2D<float4> currentNormals;
[[vk::binding(3, 0)]] Texture2D<float4> motionDepth;
[[vk::binding(4, 0)]] Texture2D<float4> previousHistory;
[[vk::binding(5, 0)]] Texture2D<float4> previousNormals;
[[vk::binding(6, 0)]] RWTexture2D<float4> filteredOutput;
[[vk::binding(7, 0)]] Texture2D<float4> validityInput;

struct FilterConstants
{
    uint2 extent;
    uint2 sourceExtent;
    uint historyValid;
    uint mode;
    float historyWeight;
    float depthThreshold;
    float normalThreshold;
    uint stepWidth;
    uint validityEnabled;
    uint useSceneDepth;
    uint2 padding;
};

[[vk::push_constant]] FilterConstants constants;

uint2 sourcePixel(uint2 pixel)
{
    return min(uint2((float2(pixel) + 0.5) * float2(constants.sourceExtent) / float2(constants.extent)), uint2(constants.sourceExtent) - 1u);
}

uint2 surfacePixel(uint2 pixel)
{
    uint width;
    uint height;
    currentNormals.GetDimensions(width, height);
    return min(uint2((float2(pixel) + 0.5) * float2(width, height) / float2(constants.extent)), uint2(width, height) - 1u);
}

uint2 validityPixel(uint2 pixel)
{
    return min(uint2((float2(pixel) + 0.5) * float2(constants.sourceExtent) / float2(constants.extent)), uint2(constants.sourceExtent) - 1u);
}

bool isValid(uint2 pixel)
{
    return constants.validityEnabled == 0u || validityInput.Load(int3(validityPixel(pixel), 0)).x > 0.5;
}

float3 temporalFilter(uint2 pixel, float3 current, float4 normal)
{
    if (constants.historyValid == 0u || dot(normal.xyz, normal.xyz) < 1e-6)
        return current;
    float4 motion = motionDepth.Load(int3(surfacePixel(pixel), 0));
    float2 uv = (float2(pixel) + 0.5) / float2(constants.extent);
    float2 previousUv = uv - motion.xy;
    if (any(previousUv < 0.0) || any(previousUv >= 1.0) || motion.z < 0.0 || motion.z > 1.0)
        return current;
    int2 previousPixel = int2(previousUv * float2(constants.extent));
    float4 history = previousHistory.Load(int3(previousPixel, 0));
    float4 previousNormal = previousNormals.Load(int3(previousPixel, 0));
    if (abs(history.w - motion.z) > max(constants.depthThreshold, 1e-6) || dot(previousNormal.xyz, previousNormal.xyz) < 1e-6)
        return current;
    float normalSimilarity = dot(normalize(normal.xyz), normalize(previousNormal.xyz));
    if (normalSimilarity < constants.normalThreshold || abs(normal.w - previousNormal.w) > 0.15)
        return current;
    float velocityPixels = length(motion.xy * float2(constants.extent));
    float weight = saturate(constants.historyWeight) * saturate(1.0 - velocityPixels / 64.0);
    return lerp(current, history.rgb, weight);
}

float3 spatialFilter(uint2 pixel, float3 current, float4 normal)
{
    if (dot(normal.xyz, normal.xyz) < 1e-6)
        return current;
    bool diffuse = constants.mode == 2u;
    bool useSceneDepth = constants.useSceneDepth != 0u;
    float3 centerNormal = normalize(normal.xyz);
    float linearDepth = useSceneDepth
        ? currentDepth.Load(int3(surfacePixel(pixel), 0))
        : motionDepth.Load(int3(surfacePixel(pixel), 0)).w;
    if (linearDepth <= 0.0 || (useSceneDepth && linearDepth >= 1.0))
        return current;
    float3 sum = float3(0.0);
    float totalWeight = 0.0;
    int stepWidth = int(clamp(constants.stepWidth, 1u, 16u));
    float depthScale = useSceneDepth
        ? max(constants.depthThreshold, 1e-5)
        : (diffuse ? max(constants.depthThreshold * 32.0, 0.01) : max(constants.depthThreshold * 4.0, 0.002)) * max(linearDepth, 0.01);
    for (int y = -2; y <= 2; y++)
    for (int x = -2; x <= 2; x++)
    {
        int2 samplePixel = int2(pixel) + int2(x, y) * stepWidth;
        if (any(samplePixel < 0) || any(samplePixel >= int2(constants.extent)))
            continue;
        if (!isValid(uint2(samplePixel)))
            continue;
        float4 sampleValue = currentEffect.Load(int3(sourcePixel(uint2(samplePixel)), 0));
        float4 sampleNormal = currentNormals.Load(int3(surfacePixel(uint2(samplePixel)), 0));
        if (dot(sampleNormal.xyz, sampleNormal.xyz) < 1e-6 || (!diffuse && abs(sampleNormal.w - normal.w) > 0.15))
            continue;
        float similarity = dot(centerNormal, normalize(sampleNormal.xyz));
        if (similarity < constants.normalThreshold)
            continue;
        float sampleDepth = useSceneDepth
            ? currentDepth.Load(int3(surfacePixel(uint2(samplePixel)), 0))
            : motionDepth.Load(int3(surfacePixel(uint2(samplePixel)), 0)).w;
        if (sampleDepth <= 0.0 || (useSceneDepth && sampleDepth >= 1.0))
            continue;
        float depthDifference = abs(sampleDepth - linearDepth);
        if (depthDifference > depthScale * float(stepWidth) * 2.0)
            continue;
        float kernelX = x == 0 ? 6.0 : abs(x) == 1 ? 4.0 : 1.0;
        float kernelY = y == 0 ? 6.0 : abs(y) == 1 ? 4.0 : 1.0;
        float roughnessTolerance = diffuse ? 16.0 : lerp(48.0, 6.0, saturate((normal.w - 0.15) * 4.0));
        float weight = kernelX * kernelY * exp(-depthDifference / max(depthScale * float(stepWidth), 1e-5)) * pow(saturate(similarity), roughnessTolerance);
        sum += sampleValue.rgb * weight;
        totalWeight += weight;
    }
    return totalWeight > 1e-6 ? sum / totalWeight : current;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    if (any(pixel >= constants.extent))
        return;
    uint2 sampledPixel = sourcePixel(pixel);
    float4 currentSample = currentEffect.Load(int3(sampledPixel, 0));
    float3 current = currentSample.rgb;
    float4 normal = currentNormals.Load(int3(surfacePixel(pixel), 0));
    bool valid = isValid(pixel);
    float3 result = valid && constants.mode == 3u
        ? current
        : valid && constants.mode == 1u && normal.w < 0.15
            ? current
            : valid
                ? (constants.mode == 0u ? temporalFilter(pixel, current, normal) : spatialFilter(pixel, current, normal))
                : spatialFilter(pixel, current, normal);
    filteredOutput[pixel] = float4(result, currentSample.a);
}
