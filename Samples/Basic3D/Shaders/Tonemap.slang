import Common;

[[vk::binding(0, 0)]] Texture2D hdrColor;
[[vk::binding(1, 0)]] SamplerState linearSampler;
[[vk::binding(2, 0)]] Texture2D bloomColor;

struct TonemapPushConstants
{
    float exposure;
    float3 padding;
};
[[vk::push_constant]] TonemapPushConstants pushConstants;

struct FullscreenVertexOutput
{
    float4 position : SV_Position;
    float2 uv : TEXCOORD;
};

[shader("vertex")]
FullscreenVertexOutput vsMain(uint vertexId : SV_VertexID)
{
    FullscreenVertexOutput output;
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    output.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
    output.uv = uv;
    return output;
}

[shader("fragment")]
float4 psMain(FullscreenVertexOutput input) : SV_Target
{
    float3 color = hdrColor.Sample(linearSampler, input.uv).rgb;
    color += bloomColor.Sample(linearSampler, input.uv).rgb;
    color *= pushConstants.exposure;
    color = acesTonemap(color);
    return float4(color, 1.0);
}
