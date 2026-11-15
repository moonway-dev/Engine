import Common;

struct ModelPushConstants
{
    float4x4 model;
};
[[vk::push_constant]] ModelPushConstants pushConstants;

struct ShadowVertexOutput
{
    float4 position : SV_Position;
};

[shader("vertex")]
ShadowVertexOutput vsMain(VertexInput input)
{
    ShadowVertexOutput output;
    output.position = mul(pushConstants.model, float4(input.position, 1.0));
    return output;
}

[shader("fragment")]
void psMain(ShadowVertexOutput input)
{
}
