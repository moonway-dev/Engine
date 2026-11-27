[[vk::binding(0, 0)]] Texture2D<float4> currentColor;
[[vk::binding(1, 0)]] Texture2D<float4> motionDepth;
[[vk::binding(2, 0)]] Texture2D<float> currentDepth;
[[vk::binding(3, 0)]] Texture2D<float4> currentNormals;
[[vk::binding(4, 0)]] Texture2D<float4> previousColor;
[[vk::binding(5, 0)]] Texture2D<float4> previousMoments;
[[vk::binding(6, 0)]] Texture2D<float4> previousGeometry;
[[vk::binding(7, 0)]] Texture2D<float4> validity;
[[vk::binding(8, 0)]] RWTexture2D<float4> outputColor;
[[vk::binding(9, 0)]] RWTexture2D<float4> outputMoments;
[[vk::binding(10, 0)]] RWTexture2D<float4> outputGeometry;

struct DenoiseConstants
{
    uint2 extent;
    uint2 sourceExtent;
    uint historyValid;
    uint effect;
};
[[vk::push_constant]] DenoiseConstants constants;

float luminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

uint2 sourcePixel(uint2 pixel)
{
    return min(uint2((float2(pixel) + 0.5) * float2(constants.sourceExtent) / float2(constants.extent)), constants.sourceExtent - 1u);
}

uint2 surfacePixel(uint2 pixel)
{
    return min(uint2((float2(pixel) + 0.5) * float2(constants.extent) / float2(constants.sourceExtent)), constants.extent - 1u);
}

float depthTolerance(float depth)
{
    return max(0.025 * max(1.0 - depth, 1e-5), 2e-6);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    if (any(pixel >= constants.extent))
        return;
    uint2 sampledPixel = sourcePixel(pixel);
    float4 sample = currentColor.Load(int3(sampledPixel, 0));
    float4 geometry = currentNormals.Load(int3(pixel, 0));
    float depth = currentDepth.Load(int3(pixel, 0));
    float4 motion = motionDepth.Load(int3(pixel, 0));
    float hitDistance = constants.effect == 1u ? validity.Load(int3(sampledPixel, 0)).y : 0.0;
    float3 current = sample.rgb;
    bool gi = constants.effect == 2u;
    bool shadow = constants.effect == 0u;
    if (depth >= 1.0 || dot(geometry.xyz, geometry.xyz) < 1e-6 || (gi && sample.a < 0.0) || (constants.effect == 1u && sample.a <= 0.0))
    {
        outputColor[pixel] = float4(current, gi ? -1.0 : depth);
        outputMoments[pixel] = float4(0.0);
        outputGeometry[pixel] = geometry;
        return;
    }
    float3 normal = normalize(geometry.xyz);
    float3 minimumColor = current;
    float3 maximumColor = current;
    float sum = 0.0;
    float sumSquared = 0.0;
    float maximumLuminance = -1e20;
    float neighbors = 0.0;
    float noiseSquared = 0.0;
    int radius = shadow ? 2 : 1;
    for (int y = -radius; y <= radius; y++)
    for (int x = -radius; x <= radius; x++)
    {
        int2 neighbor = int2(sampledPixel) + int2(x, y);
        if (any(neighbor < 0) || any(neighbor >= int2(constants.sourceExtent)))
            continue;
        uint2 tap = uint2(neighbor);
        uint2 surface = surfacePixel(tap);
        float4 tapNormal = currentNormals.Load(int3(surface, 0));
        if (dot(tapNormal.xyz, tapNormal.xyz) < 1e-6 || dot(normal, normalize(tapNormal.xyz)) < 0.85
            || abs(currentDepth.Load(int3(surface, 0)) - depth) > depthTolerance(depth) * 2.0)
            continue;
        float4 tapColor = currentColor.Load(int3(tap, 0));
        if (gi && tapColor.a < 0.0)
            continue;
        float3 color = tapColor.rgb;
        float value = shadow ? color.r : luminance(color);
        minimumColor = min(minimumColor, color);
        maximumColor = max(maximumColor, color);
        sum += value;
        sumSquared += value * value;
        maximumLuminance = max(maximumLuminance, value);
        if (gi)
            noiseSquared += tapColor.a * tapColor.a;
        neighbors += 1.0;
    }
    float mean = sum / max(neighbors, 1.0);
    float variance = max(sumSquared / max(neighbors, 1.0) - mean * mean, 0.0);
    float3 history = float3(0.0);
    float4 historyMoments = float4(0.0);
    float totalWeight = 0.0;
    float2 previousCoordinate = (float2(pixel) + 0.5 - motion.xy * float2(constants.extent)) - 0.5;
    int2 previousOrigin = int2(floor(previousCoordinate));
    float2 fraction = frac(previousCoordinate);
    if (constants.historyValid != 0u && motion.z >= 0.0 && motion.z <= 1.0)
    {
        for (int y = 0; y <= 1; y++)
        for (int x = 0; x <= 1; x++)
        {
            int2 tap = previousOrigin + int2(x, y);
            if (any(tap < 0) || any(tap >= int2(constants.extent)))
                continue;
            float4 oldColor = previousColor.Load(int3(tap, 0));
            float4 oldGeometry = previousGeometry.Load(int3(tap, 0));
            float4 oldMoments = previousMoments.Load(int3(tap, 0));
            if (oldMoments.z < 1.0 || abs(oldColor.a - motion.z) > depthTolerance(motion.z)
                || dot(oldGeometry.xyz, oldGeometry.xyz) < 1e-6 || dot(normal, normalize(oldGeometry.xyz)) < 0.9
                || (constants.effect != 0u && abs(oldGeometry.w - geometry.w) > 0.1))
                continue;
            if (constants.effect == 1u && abs(oldMoments.w - hitDistance) > max(0.1, max(hitDistance, oldMoments.w) * lerp(0.04, 0.5, geometry.w)))
                continue;
            float weight = (x == 0 ? 1.0 - fraction.x : fraction.x) * (y == 0 ? 1.0 - fraction.y : fraction.y);
            history += oldColor.rgb * weight;
            historyMoments += oldMoments * weight;
            totalWeight += weight;
        }
    }
    bool historyValid = totalWeight > 0.1;
    if (historyValid)
    {
        history /= totalWeight;
        historyMoments /= totalWeight;
    }
    float historyVariance = max(historyMoments.y - historyMoments.x * historyMoments.x, 0.0);
    float velocity = length(motion.xy * float2(constants.extent));
    if (shadow)
    {
        float count = historyValid ? min(historyMoments.z, 4.0) : 0.0;
        float spread = sqrt(variance);
        float clamped = clamp(history.r, max(minimumColor.r, mean - 1.5 * spread), min(maximumColor.r, mean + 1.5 * spread));
        float mismatch = abs(history.r - current.r);
        float consistentChange = smoothstep(0.04, 0.24, min(mismatch, abs(history.r - mean)) - 0.75 * spread);
        count *= (1.0 - consistentChange) * saturate(1.0 - velocity / 16.0);
        count *= saturate(1.0 - 2.0 * abs(clamped - history.r));
        outputColor[pixel] = float4(lerp(current.r, clamped, count / (1.0 + count)), current.g, 0.0, depth);
        outputMoments[pixel] = float4(0.0, 0.0, min(count + 1.0, 4.0), 0.0);
        outputGeometry[pixel] = geometry;
        return;
    }
    float currentLuminance = luminance(current);
    if (constants.effect == 2u || (constants.effect == 1u && geometry.w >= 0.15))
    {
        float trimmedMean = (sum - maximumLuminance) / max(neighbors - 1.0, 1.0);
        float trimmedVariance = max((sumSquared - maximumLuminance * maximumLuminance) / max(neighbors - 1.0, 1.0) - trimmedMean * trimmedMean, 0.0);
        float signalScale = max(abs(trimmedMean), 1e-4);
        float limit = trimmedMean + 4.0 * sqrt(trimmedVariance) + max(0.002, signalScale * 0.5);
        if (historyValid)
            limit = max(limit, historyMoments.x + 4.0 * sqrt(historyVariance) + max(0.002, abs(historyMoments.x) * 0.5));
        if (neighbors >= 5.0 && currentLuminance > max(limit, 0.004))
        {
            float target = max(limit, 0.0);
            current = current * (target / max(currentLuminance, 1e-5));
            currentLuminance = target;
        }
    }
    float maximumFrames = gi ? 64.0 : 64.0;
    float count = historyValid ? min(historyMoments.z, maximumFrames - 1.0) : 0.0;
    float currentNoise = gi ? max(sample.a, sqrt(noiseSquared / max(neighbors, 1.0))) : 0.0;
    if (historyValid)
    {
        if (gi)
        {
            float oldLuminance = luminance(history);
            float temporalNoise = max(currentNoise, historyMoments.w * rsqrt(max(count, 1.0)));
            float signal = max(mean, oldLuminance);
            float threshold = 0.004 + 0.06 * signal + max(temporalNoise, 0.5 * sqrt(variance));
            float delta = min(abs(mean - oldLuminance), abs(currentLuminance - oldLuminance));
            float change = smoothstep(1.25, 2.75, delta / threshold);
            float margin = max(1.5 * temporalNoise, sqrt(variance) + 0.05 * signal + 0.0005) * lerp(1.0, 0.5, change);
            float3 clipped = clamp(history, minimumColor - margin, maximumColor + margin);
            float3 clipDelta = abs(history - clipped);
            float clipChange = smoothstep(1.0, 2.5, max(max(clipDelta.r, clipDelta.g), clipDelta.b) / threshold);
            count = max(count * (1.0 - max(change, clipChange)) * saturate(1.0 - velocity / 40.0), min(count, 3.0));
            history = clipped;
        }
        else
        {
            float deviation = abs(mean - historyMoments.x) / (3.0 * sqrt(variance / max(neighbors, 1.0) + historyVariance / max(count, 1.0)) + 0.02 * abs(mean) + 1e-4);
            if (deviation > 0.6)
                count = min(count, 2.0);
            if (deviation > 1.5)
                count = 0.0;
            float margin = sqrt(max(historyVariance, variance)) + 0.02;
            history = clamp(history, minimumColor - margin, maximumColor + margin);
        }
    }
    float weight = count / (count + 1.0);
    if (!gi)
        weight *= saturate(1.0 - velocity / 48.0);
    float3 result = lerp(current, history, weight);
    float moment1 = lerp(currentLuminance, historyMoments.x, weight);
    float moment2 = lerp(currentLuminance * currentLuminance, historyMoments.y, weight);
    outputColor[pixel] = float4(result, depth);
    float noise = lerp(currentNoise, historyMoments.w, weight * 0.8);
    outputMoments[pixel] = float4(moment1, min(moment2, 65000.0), count + 1.0, gi ? noise : hitDistance);
    outputGeometry[pixel] = geometry;
}
