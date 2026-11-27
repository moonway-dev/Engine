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
    uint varianceEnabled;
    uint padding;
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

uint2 sourceSurfacePixel(uint2 pixel)
{
    return min(uint2((float2(pixel) + 0.5) * float2(constants.extent) / float2(constants.sourceExtent)), constants.extent - 1u);
}

bool isValid(uint2 pixel)
{
    return constants.validityEnabled == 0u || validityInput.Load(int3(sourcePixel(pixel), 0)).x > 0.5;
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

float giLuminance(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 spatialFilter(uint2 pixel, float3 current, float4 normal, out bool reconstructed, out float noise)
{
    reconstructed = false;
    noise = 0.0;
    if (dot(normal.xyz, normal.xyz) < 1e-6)
        return current;
    bool diffuse = constants.mode == 2u || constants.mode == 4u;
    bool useSceneDepth = constants.useSceneDepth != 0u;
    float3 centerNormal = normalize(normal.xyz);
    float linearDepth = useSceneDepth
        ? currentDepth.Load(int3(surfacePixel(pixel), 0))
        : motionDepth.Load(int3(surfacePixel(pixel), 0)).w;
    if (linearDepth <= 0.0 || (useSceneDepth && linearDepth >= 1.0))
        return current;
    float3 sum = float3(0.0);
    float totalWeight = 0.0;
    float4 centerMoments = constants.varianceEnabled != 0u ? previousHistory.Load(int3(surfacePixel(pixel), 0)) : float4(0.0);
    float centerNoise = centerMoments.w / sqrt(max(centerMoments.z, 1.0));
    float confidence = constants.varianceEnabled != 0u ? saturate((centerMoments.z - 1.0) / 8.0) : 1.0;
    int stepWidth = int(clamp(constants.stepWidth, 1u, 16u));
    float depthScale = useSceneDepth
        ? max(constants.depthThreshold * max(1.0 - linearDepth, 1e-4), 1e-6)
        : (diffuse ? max(constants.depthThreshold * 32.0, 0.01) : max(constants.depthThreshold * 4.0, 0.002)) * max(linearDepth, 0.01);
    bool gi = constants.mode == 2u;
    float inputVariance = !gi && constants.varianceEnabled == 0u
        ? inputNoiseVariance(pixel, normal, linearDepth, diffuse, depthScale, current)
        : 0.0;
    int filterStep = gi ? int(stepWidth * (1.0 + 2.0 * (1.0 - confidence)))
        : stepWidth * int(1.0 + saturate(sqrt(inputVariance) * 1.5) * 3.0);
    bool rawGi = gi && constants.validityEnabled != 0u;
    int2 center = rawGi ? int2(sourcePixel(pixel)) : int2(pixel);
    int2 tapExtent = int2(rawGi ? constants.sourceExtent : constants.extent);
    float2 luminanceMoments = float2(0.0);
    float squaredWeights = 0.0;
    for (int y = -2; y <= 2; y++)
    for (int x = -2; x <= 2; x++)
    {
        if (rawGi && abs(x) + abs(y) > 2)
            continue;
        if (!rawGi && gi && abs(x) + abs(y) > 1)
            continue;
        int2 tap = center + int2(x, y) * filterStep;
        if (any(tap < 0) || any(tap >= tapExtent))
            continue;
        uint2 samplePixel = rawGi ? sourceSurfacePixel(uint2(tap)) : uint2(tap);
        uint2 inputPixel = rawGi ? uint2(tap) : sourcePixel(samplePixel);
        if (constants.validityEnabled != 0u && validityInput.Load(int3(inputPixel, 0)).x <= 0.5)
            continue;
        float4 sampleValue = currentEffect.Load(int3(inputPixel, 0));
        if (gi && sampleValue.a < 0.0)
            continue;
        uint2 geometryPixel = surfacePixel(samplePixel);
        float4 sampleNormal = currentNormals.Load(int3(geometryPixel, 0));
        if (dot(sampleNormal.xyz, sampleNormal.xyz) < 1e-6 || (!diffuse && abs(sampleNormal.w - normal.w) > 0.15))
            continue;
        float similarity = dot(centerNormal, normalize(sampleNormal.xyz));
        if (similarity < constants.normalThreshold)
            continue;
        float sampleDepth = useSceneDepth
            ? currentDepth.Load(int3(geometryPixel, 0))
            : motionDepth.Load(int3(geometryPixel, 0)).w;
        if (sampleDepth <= 0.0 || (useSceneDepth && sampleDepth >= 1.0))
            continue;
        float depthDifference = abs(sampleDepth - linearDepth);
        if (depthDifference > depthScale * float(filterStep) * 2.0)
            continue;
        float kernelX = x == 0 ? 6.0 : abs(x) == 1 ? 4.0 : rawGi ? 3.0 : 1.0;
        float kernelY = y == 0 ? 6.0 : abs(y) == 1 ? 4.0 : rawGi ? 3.0 : 1.0;
        float roughnessTolerance = diffuse ? 16.0 : lerp(48.0, 6.0, saturate((normal.w - 0.15) * 4.0));
        float weight = kernelX * kernelY * exp(-depthDifference / max(depthScale * float(filterStep), 1e-5)) * pow(saturate(similarity), roughnessTolerance);
        if (rawGi)
        {
            float value = giLuminance(sampleValue.rgb);
            luminanceMoments += float2(value, value * value) * weight;
            squaredWeights += weight * weight;
        }
        else if (constants.varianceEnabled != 0u)
        {
            float4 sampleMoments = previousHistory.Load(int3(geometryPixel, 0));
            float sampleNoise = sampleMoments.w / sqrt(max(sampleMoments.z, 1.0));
            float centerLuminance = giLuminance(current);
            float tapLuminance = giLuminance(sampleValue.rgb);
            float luminanceScale = max(abs(centerLuminance), abs(tapLuminance));
            float tolerance = (centerNoise + sampleNoise) * lerp(4.0, 2.0, confidence)
                + lerp(0.10, 0.035, confidence) * luminanceScale
                + min(0.01, 0.25 * luminanceScale);
            tolerance *= (1.0 + 8.0 * saturate(1.0 - luminanceScale * 8.0)) * (1.0 + 6.0 * (1.0 - confidence));
            float difference = abs(tapLuminance - centerLuminance);
            weight *= lerp(0.55 * (1.0 - confidence), 1.0, exp(-difference / tolerance));
        }
        sum += sampleValue.rgb * weight;
        totalWeight += weight;
    }
    reconstructed = totalWeight > 1e-6;
    if (rawGi && reconstructed)
    {
        float2 moments = luminanceMoments / totalWeight;
        float invTaps = squaredWeights / (totalWeight * totalWeight);
        float variance = max(moments.y - moments.x * moments.x, 0.0);
        noise = max(sqrt(variance * invTaps / max(1.0 - invTaps, 0.25)), (min(0.01, 0.25 * abs(moments.x)) + 0.08 * abs(moments.x)) * sqrt(invTaps));
    }
    return reconstructed ? sum / totalWeight : current;
}

static const float2 SigmaPoisson[12] =
{
    float2(-0.326, -0.406), float2(-0.840, -0.074), float2(-0.696, 0.457), float2(-0.203, 0.621),
    float2(0.962, -0.195), float2(0.473, -0.480), float2(0.519, 0.767), float2(0.185, -0.893),
    float2(0.507, 0.064), float2(0.896, 0.412), float2(-0.322, -0.933), float2(-0.792, -0.598)
};

float2 sigmaDepthGradient(uint2 pixel, float linearDepth)
{
    uint2 leftPixel = uint2(pixel.x > 0u ? pixel.x - 1u : 0u, pixel.y);
    uint2 rightPixel = min(pixel + uint2(1u, 0u), constants.extent - 1u);
    uint2 downPixel = uint2(pixel.x, pixel.y > 0u ? pixel.y - 1u : 0u);
    uint2 upPixel = min(pixel + uint2(0u, 1u), constants.extent - 1u);
    float left = motionDepth.Load(int3(leftPixel, 0)).w;
    float right = motionDepth.Load(int3(rightPixel, 0)).w;
    float down = motionDepth.Load(int3(downPixel, 0)).w;
    float up = motionDepth.Load(int3(upPixel, 0)).w;
    return float2(abs(right - linearDepth) < abs(linearDepth - left) ? right - linearDepth : linearDepth - left,
        abs(up - linearDepth) < abs(linearDepth - down) ? up - linearDepth : linearDepth - down);
}

float sigmaWeight(int2 tap, int2 offset, float linearDepth, float2 gradient)
{
    if (any(tap < 0) || any(tap >= int2(constants.extent)))
        return 0.0;
    float sampleDepth = motionDepth.Load(int3(tap, 0)).w;
    if (sampleDepth <= 0.0)
        return 0.0;
    float slope = gradient.x * float(offset.x) + gradient.y * float(offset.y);
    float difference = abs(sampleDepth - linearDepth - slope);
    if (abs(slope) > 4.0 * linearDepth)
        difference = sampleDepth > linearDepth ? abs(sampleDepth - linearDepth) * 0.1 : abs(sampleDepth - linearDepth);
    return exp(-difference / max(0.03 * linearDepth, 1e-4));
}

float shadowTap(int2 tap, int2 offset, float depth, float2 gradient, float3 normal)
{
    float weight = sigmaWeight(tap, offset, depth, gradient);
    if (weight < 1e-4)
        return 0.0;
    float3 tapNormal = currentNormals.Load(int3(tap, 0)).xyz;
    return dot(tapNormal, tapNormal) > 1e-6 && dot(normal, normalize(tapNormal)) >= constants.normalThreshold ? weight : 0.0;
}

float2 shadowFilter(uint2 pixel, float4 current, float4 normal)
{
    float depth = motionDepth.Load(int3(pixel, 0)).w;
    if (depth <= 0.0 || dot(normal.xyz, normal.xyz) < 1e-6)
        return float2(1.0);
    float2 gradient = sigmaDepthGradient(pixel, depth);
    float3 centerNormal = normalize(normal.xyz);
    float shadowSum = 0.0;
    float total = 0.0;
    float penumbraSum = 0.0;
    float penumbraWeight = 0.0;
    float2 range = float2(1.0, 0.0);
    float centerRadius = current.b < 60000.0 ? current.b : 2.0;
    int2 center = int2(sourcePixel(pixel));
    float pointVisibility = 1.0;
    float bestPointWeight = 0.0;
    for (int y = -2; y <= 2; y++)
    for (int x = -2; x <= 2; x++)
    {
        int2 inputPixel = center + int2(x, y);
        if (any(inputPixel < 0) || any(inputPixel >= int2(constants.sourceExtent)) || validityInput.Load(int3(inputPixel, 0)).x <= 0.5)
            continue;
        int2 tap = int2(sourceSurfacePixel(uint2(inputPixel)));
        int2 offset = tap - int2(pixel);
        float weight = shadowTap(tap, offset, depth, gradient, centerNormal);
        if (weight <= 0.0)
            continue;
        float4 value = currentEffect.Load(int3(inputPixel, 0));
        float distanceSquared = dot(float2(offset), float2(offset));
        float pointWeight = weight / (1.0 + distanceSquared);
        if (pointWeight > bestPointWeight)
        {
            bestPointWeight = pointWeight;
            pointVisibility = value.g;
        }
        weight *= exp(-0.5 * distanceSquared / max(centerRadius * centerRadius, 1.0));
        shadowSum += value.r * weight;
        total += weight;
        range = float2(min(range.x, value.r), max(range.y, value.r));
        if (value.b < 60000.0)
        {
            float w = weight / (1.0 + value.b);
            penumbraSum += value.b * w;
            penumbraWeight += w;
        }
    }
    if (total <= 1e-6)
        return float2(1.0, pointVisibility);
    float visibility = shadowSum / total;
    if (range.x >= 0.98 || range.y <= 0.02 || penumbraWeight <= 0.0)
        return float2(visibility, pointVisibility);
    float radius = clamp(0.65 * penumbraSum / penumbraWeight, 0.0, 16.0);
    if (radius < 1.0)
        return float2(visibility, pointVisibility);
    float angle = 6.2831853 * frac(float(constants.padding) * 0.61803399);
    float2x2 rotation = float2x2(cos(angle), -sin(angle), sin(angle), cos(angle));
    for (uint sample = 0u; sample < 12u; sample++)
    {
        int2 offset = int2(round(mul(rotation, SigmaPoisson[sample]) * radius));
        int2 tap = int2(pixel) + offset;
        if (any(tap < 0) || any(tap >= int2(constants.extent)) || !isValid(uint2(tap)))
            continue;
        uint2 inputPixel = sourcePixel(uint2(tap));
        tap = int2(sourceSurfacePixel(inputPixel));
        float weight = shadowTap(tap, tap - int2(pixel), depth, gradient, centerNormal);
        if (weight <= 0.0)
            continue;
        float4 value = currentEffect.Load(int3(inputPixel, 0));
        weight *= exp(-1.5 * dot(SigmaPoisson[sample], SigmaPoisson[sample])) * saturate(value.b / radius);
        shadowSum += value.r * weight;
        total += weight;
    }
    return float2(shadowSum / total, pointVisibility);
}

float inputNoiseVariance(uint2 pixel, float4 normal, float linearDepth, bool diffuse, float depthScale, float3 center)
{
    float sum = 0.0;
    float sumSquared = 0.0;
    float samples = 0.0;
    for (int y = -1; y <= 1; y++)
    for (int x = -1; x <= 1; x++)
    {
        int2 samplePixel = int2(pixel) + int2(x, y) * (diffuse ? 2 : 1);
        if (any(samplePixel < 0) || any(samplePixel >= int2(constants.extent)))
            continue;
        float4 sampleValue = currentEffect.Load(int3(sourcePixel(uint2(samplePixel)), 0));
        float4 sampleNormal = currentNormals.Load(int3(surfacePixel(uint2(samplePixel)), 0));
        if (dot(sampleNormal.xyz, sampleNormal.xyz) < 1e-6 || dot(normal.xyz, normalize(sampleNormal.xyz)) < constants.normalThreshold)
            continue;
        float sampleDepth = constants.useSceneDepth != 0u
            ? currentDepth.Load(int3(surfacePixel(uint2(samplePixel)), 0))
            : motionDepth.Load(int3(surfacePixel(uint2(samplePixel)), 0)).w;
        if (sampleDepth <= 0.0 || (constants.useSceneDepth != 0u && sampleDepth >= 1.0) || abs(sampleDepth - linearDepth) > depthScale * 2.0)
            continue;
        float value = dot(sampleValue.rgb - center, float3(0.2126, 0.7152, 0.0722));
        sum += value;
        sumSquared += value * value;
        samples += 1.0;
    }
    return samples > 1.0 ? max(sumSquared / samples - (sum / samples) * (sum / samples), 0.0) : 0.0;
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
    if (constants.mode == 4u)
    {
        filteredOutput[pixel] = float4(shadowFilter(pixel, currentSample, normal), 0.0, currentSample.a);
        return;
    }
    bool reconstructed = false;
    float noise = 0.0;
    float3 result = valid && (constants.mode == 3u || (constants.mode == 1u && normal.w < 0.15)) ? current
        : valid && constants.mode == 0u ? temporalFilter(pixel, current, normal)
        : spatialFilter(pixel, current, normal, reconstructed, noise);
    float auxiliary = currentSample.a;
    if (constants.mode == 2u)
        auxiliary = !reconstructed ? -1.0 : constants.validityEnabled != 0u ? noise : currentDepth.Load(int3(surfacePixel(pixel), 0));
    filteredOutput[pixel] = float4(result, auxiliary);
}
