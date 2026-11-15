[[vk::binding(0, 0)]] Texture2D<float4> sourceTexture;
[[vk::binding(1, 0)]] RWTexture2D<float4> destinationTexture;
[[vk::binding(2, 0)]] SamplerState linearSampler;

struct BloomConstants
{
    uint2 sourceSize;
    uint2 destinationSize;
    float threshold;
    float intensity;
    uint mode;
};
[[vk::push_constant]] BloomConstants constants;

float3 thresholdColor(float3 color)
{
    float brightness = max(color.r, max(color.g, color.b));
    float contribution = max(brightness - constants.threshold, 0.0) / max(brightness, 1e-4);
    return color * contribution;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    if (any(pixel >= constants.destinationSize))
        return;
    float2 texel = 1.0 / float2(constants.sourceSize);
    float2 uv = (float2(pixel) + 0.5) / float2(constants.destinationSize);
    float3 color = float3(0.0);
    color += sourceTexture.SampleLevel(linearSampler, uv + texel * float2(-0.5, -0.5), 0).rgb;
    color += sourceTexture.SampleLevel(linearSampler, uv + texel * float2(0.5, -0.5), 0).rgb;
    color += sourceTexture.SampleLevel(linearSampler, uv + texel * float2(-0.5, 0.5), 0).rgb;
    color += sourceTexture.SampleLevel(linearSampler, uv + texel * float2(0.5, 0.5), 0).rgb;
    color *= 0.25;
    if (constants.mode == 0)
        color = thresholdColor(color);
    destinationTexture[pixel] = float4(color * constants.intensity, 1.0);
}
