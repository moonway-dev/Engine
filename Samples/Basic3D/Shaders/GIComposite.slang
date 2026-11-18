[[vk::binding(0, 0)]] Texture2D<float4> sceneColor;
[[vk::binding(1, 0)]] Texture2D<float4> giColor;
[[vk::binding(2, 0)]] RWTexture2D<float4> compositeOutput;

struct GiCompositeConstants
{
    uint2 extent;
    float intensity;
    float padding;
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
    float4 gi = giColor.Load(int3(pixel, 0));
    compositeOutput[pixel] = float4(color.rgb + gi.rgb * constants.intensity, color.a);
}
