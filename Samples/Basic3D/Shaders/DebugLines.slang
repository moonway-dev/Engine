struct DebugLineVertexInput
{
    float3 position : POSITION;
    float4 color : COLOR0;
};

struct DebugLineVertexOutput
{
    float4 position : SV_Position;
    float4 color : COLOR0;
};

struct DebugPushConstants
{
    float4x4 viewProjection;
};
[[vk::push_constant]] DebugPushConstants pushConstants;

[shader("vertex")]
DebugLineVertexOutput vsMain(DebugLineVertexInput input)
{
    DebugLineVertexOutput output;
    output.position = mul(pushConstants.viewProjection, float4(input.position, 1.0));
    output.color = input.color;
    return output;
}

[shader("fragment")]
float4 psMain(DebugLineVertexOutput input) : SV_Target
{
    return input.color;
}
