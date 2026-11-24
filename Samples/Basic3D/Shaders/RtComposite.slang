[[vk::binding(0, 0)]] Texture2D<float4> sceneColor;
[[vk::binding(1, 0)]] Texture2D<float4> filteredReflection;
[[vk::binding(2, 0)]] Texture2D<float4> originalSpecularIndirect;
[[vk::binding(3, 0)]] Texture2D<float4> rawReflection;
[[vk::binding(4, 0)]] RWTexture2D<float4> compositeOutput;

struct CompositeConstants
{
    uint2 extent;
    uint2 sourceExtent;
    uint reflectionsEnabled;
    float strength;
};

[[vk::push_constant]] CompositeConstants constants;

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    if (any(pixel >= constants.extent))
        return;
    float4 color = sceneColor.Load(int3(pixel, 0));
    if (constants.reflectionsEnabled != 0u)
    {
        uint reflectionWidth;
        uint reflectionHeight;
        filteredReflection.GetDimensions(reflectionWidth, reflectionHeight);
        uint2 reflectionPixel = min(uint2((float2(pixel) + 0.5) * float2(constants.sourceExtent) / float2(constants.extent)), constants.sourceExtent - 1u);
        uint2 filteredPixel = min(uint2((float2(pixel) + 0.5) * float2(reflectionWidth, reflectionHeight) / float2(constants.extent)), uint2(reflectionWidth, reflectionHeight) - 1u);
        float replacement = saturate(rawReflection.Load(int3(reflectionPixel, 0)).w * constants.strength);
        replacement *= step(0.999, color.a);
        float3 original = originalSpecularIndirect.Load(int3(pixel, 0)).rgb;
        float3 reflected = filteredReflection.Load(int3(filteredPixel, 0)).rgb;
        color.rgb = max(color.rgb + (reflected - original) * replacement, float3(0.0));
    }
    compositeOutput[pixel] = color;
}
