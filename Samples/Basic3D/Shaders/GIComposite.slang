[[vk::binding(0, 0)]] Texture2D<float4> sceneColor;
[[vk::binding(1, 0)]] Texture2D<float4> giColor;
[[vk::binding(2, 0)]] RWTexture2D<float4> compositeOutput;
[[vk::binding(3, 0)]] Texture2D<float4> diffuseResponse;

struct GiCompositeConstants
{
    uint2 extent;
    float intensity;
    uint mode;
};
[[vk::push_constant]] GiCompositeConstants constants;

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    if (any(pixel >= constants.extent))
        return;
    float4 color = sceneColor.Load(int3(pixel, 0));
    if (constants.mode == 2u)
    {
        compositeOutput[pixel] = color;
        return;
    }
    float4 gi = giColor.Load(int3(pixel, 0));
    if (constants.mode == 1u)
    {
        if (gi.a < 0.0)
        {
            compositeOutput[pixel] = color;
            return;
        }
        gi.rgb *= diffuseResponse.Load(int3(pixel, 0)).rgb;
    }
    compositeOutput[pixel] = float4(max(color.rgb + gi.rgb * constants.intensity, float3(0.0)), color.a);
}
