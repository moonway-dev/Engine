import Common;

[[vk::binding(0, 0)]] ConstantBuffer<CameraData> camera;
[[vk::binding(0, 1)]] ConstantBuffer<MaterialData> material;
[[vk::binding(3, 1)]] Texture2D roughnessTexture;
[[vk::binding(4, 1)]] Texture2D normalTexture;
[[vk::binding(6, 1)]] SamplerState materialSampler;
[[vk::push_constant]] ModelConstants pushConstants;

struct FragmentInput
{
    float4 position : SV_Position;
    float3 normal : NORMAL;
    float3 tangent : TANGENT;
    float handedness : TANGENT_HANDEDNESS;
    float2 uv : TEXCOORD;
};

struct FragmentOutput
{
    float4 normalRoughness : SV_Target0;
};

[shader("vertex")]
FragmentInput vsMain(VertexInput input)
{
    FragmentInput output;
    float4 worldPosition = mul(pushConstants.model, float4(input.position, 1.0));
    output.position = mul(camera.viewProjection, worldPosition);
    float3x3 modelBasis = (float3x3)pushConstants.model;
    float3 basisX = float3(modelBasis[0][0], modelBasis[1][0], modelBasis[2][0]);
    float3 basisY = float3(modelBasis[0][1], modelBasis[1][1], modelBasis[2][1]);
    float3 basisZ = float3(modelBasis[0][2], modelBasis[1][2], modelBasis[2][2]);
    float3 transformedNormal = cross(basisY, basisZ) * input.normal.x + cross(basisZ, basisX) * input.normal.y + cross(basisX, basisY) * input.normal.z;
    transformedNormal *= determinant(modelBasis) < 0.0 ? -1.0 : 1.0;
    output.normal = dot(transformedNormal, transformedNormal) > 1e-12 ? normalize(transformedNormal) : input.normal;
    float3 worldTangent = mul(modelBasis, input.tangent.xyz);
    float3 orthogonalTangent = worldTangent - output.normal * dot(output.normal, worldTangent);
    if (dot(orthogonalTangent, orthogonalTangent) < 1e-8)
    {
        float3 tangentAxis = abs(output.normal.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
        orthogonalTangent = cross(tangentAxis, output.normal);
    }
    output.tangent = normalize(orthogonalTangent);
    output.handedness = input.tangent.w;
    output.uv = input.uv * material.uvScale + material.uvOffset;
    return output;
}

[shader("fragment")]
FragmentOutput psMain(FragmentInput input)
{
    float3 geometryNormal = normalize(input.normal);
    float3 tangent = normalize(input.tangent - geometryNormal * dot(geometryNormal, input.tangent));
    float3 bitangent = normalize(cross(geometryNormal, tangent)) * input.handedness;
    float3 tangentNormal = decodeNormalMap(normalTexture.Sample(materialSampler, input.uv).xyz, material.normalScale);
    float3 normal = normalize(tangent * tangentNormal.x + bitangent * tangentNormal.y + geometryNormal * tangentNormal.z);
    float roughness = clamp(material.roughness * roughnessTexture.Sample(materialSampler, input.uv)[min(material.roughnessChannel, 3u)], 0.045, 1.0);
    FragmentOutput output;
    output.normalRoughness = float4(normal, roughness);
    return output;
}
