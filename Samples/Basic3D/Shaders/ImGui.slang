[[vk::binding(0, 0)]] Texture2D uiTexture;
[[vk::binding(1, 0)]] SamplerState uiSampler;

struct ImGuiFrameData
{
    float2 displaySize;
    float2 padding;
};
[[vk::binding(2, 0)]] ConstantBuffer<ImGuiFrameData> frameData;

struct ImGuiVertexInput
{
    float2 position : POSITION;
    float2 uv : TEXCOORD;
    float4 color : COLOR;
};

struct ImGuiVertexOutput
{
    float4 position : SV_Position;
    float4 color : COLOR;
    float2 uv : TEXCOORD;
};

[shader("vertex")]
ImGuiVertexOutput vsMain(ImGuiVertexInput input)
{
    ImGuiVertexOutput output;
    output.position = float4(input.position * 2.0 / frameData.displaySize - 1.0, 0.0, 1.0);
    output.color = input.color;
    output.uv = input.uv;
    return output;
}

[shader("fragment")]
float4 psMain(ImGuiVertexOutput input) : SV_Target
{
    float4 color = input.color * uiTexture.Sample(uiSampler, input.uv);
    return color;
}
