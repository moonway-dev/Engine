struct DebugVertexOutput
{
    float4 position : SV_Position;
};

[shader("vertex")]
DebugVertexOutput vsMain(uint vertexId : SV_VertexID)
{
    DebugVertexOutput output;
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    output.position = float4(uv * 2.0 - 1.0, 0.0, 1.0);
    return output;
}

[shader("fragment")]
float4 psMain(DebugVertexOutput input) : SV_Target
{
    return float4(1.0, 0.0, 0.0, 1.0);
}
