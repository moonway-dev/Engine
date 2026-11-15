import Common;

[[vk::binding(0, 0)]] ConstantBuffer<CameraData> camera;
[[vk::binding(1, 0)]] ConstantBuffer<ShadowData> shadow;
[[vk::binding(2, 0)]] Texture2D shadowMap;
[[vk::binding(3, 0)]] SamplerComparisonState shadowSampler;
[[vk::binding(4, 0)]] ConstantBuffer<LightsInput> lights;

[[vk::binding(0, 1)]] ConstantBuffer<MaterialData> material;
[[vk::binding(1, 1)]] Texture2D albedoTexture;
[[vk::binding(2, 1)]] Texture2D metallicTexture;
[[vk::binding(3, 1)]] Texture2D roughnessTexture;
[[vk::binding(4, 1)]] Texture2D normalTexture;
[[vk::binding(5, 1)]] Texture2D ambientOcclusionTexture;
[[vk::binding(6, 1)]] SamplerState materialSampler;
[[vk::binding(7, 1)]] Texture2D emissionTexture;

[[vk::push_constant]] ModelConstants pushConstants;

struct FragmentInput
{
    float4 position : SV_Position;
    float3 worldPosition : WORLD_POSITION;
    float3 normal : NORMAL;
    float3 tangent : TANGENT;
    float handedness : TANGENT_HANDEDNESS;
    float2 uv : TEXCOORD;
    float4 lightSpacePosition : LIGHT_SPACE_POSITION;
};

[shader("vertex")]
FragmentInput vsMain(VertexInput input)
{
    FragmentInput output;
    float4 worldPosition = mul(pushConstants.model, float4(input.position, 1.0));
    output.worldPosition = worldPosition.xyz;
    output.position = mul(camera.viewProjection, worldPosition);
    output.normal = normalize(mul((float3x3)pushConstants.normalMatrix, input.normal));
    float3 worldTangent = mul((float3x3)pushConstants.model, input.tangent.xyz);
    float3 orthogonalTangent = worldTangent - output.normal * dot(output.normal, worldTangent);
    if (dot(orthogonalTangent, orthogonalTangent) < 1e-8)
    {
        float3 tangentAxis = abs(output.normal.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
        orthogonalTangent = cross(tangentAxis, output.normal);
    }
    output.tangent = normalize(orthogonalTangent);
    output.handedness = input.tangent.w;
    output.uv = input.uv * material.uvScale + material.uvOffset;
    output.lightSpacePosition = mul(shadow.lightViewProjection, worldPosition);
    return output;
}

float sampleShadowMap(float4 lightSpacePosition)
{
    float3 projected = lightSpacePosition.xyz / lightSpacePosition.w;
    if (lightSpacePosition.w <= 0.0 || projected.z < 0.0 || projected.z > 1.0)
        return 1.0;
    float2 uv = projected.xy * 0.5 + 0.5;
    if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
        return 1.0;
    float bias = 0.0015;
    float visibility = 0.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y) * shadow.texelSize;
            visibility += shadowMap.SampleCmp(shadowSampler, uv + offset, projected.z - bias);
        }
    }
    visibility /= 9.0;
    return lerp(1.0, visibility, shadow.strength);
}

[shader("fragment")]
float4 psMain(FragmentInput input) : SV_Target
{
    float3 geometryNormal = normalize(input.normal);
    float3 tangent = normalize(input.tangent - geometryNormal * dot(geometryNormal, input.tangent));
    float3 bitangent = normalize(cross(geometryNormal, tangent)) * input.handedness;
    float3 tangentNormal = decodeNormalMap(normalTexture.Sample(materialSampler, input.uv).xyz, material.normalScale);
    float3 normal = normalize(tangent * tangentNormal.x + bitangent * tangentNormal.y + geometryNormal * tangentNormal.z);
    float3 viewDirection = normalize(camera.cameraPosition - input.worldPosition);

    float3 albedo = material.albedoColor * albedoTexture.Sample(materialSampler, input.uv).rgb;
    float metallic = saturate(material.metallic * metallicTexture.Sample(materialSampler, input.uv)[min(material.metallicChannel, 3u)]);
    float roughness = clamp(material.roughness * roughnessTexture.Sample(materialSampler, input.uv)[min(material.roughnessChannel, 3u)], 0.045, 1.0);
    float sampledAo = ambientOcclusionTexture.Sample(materialSampler, input.uv)[min(material.ambientOcclusionChannel, 3u)];
    float ambientOcclusion = lerp(1.0, sampledAo, saturate(material.aoStrength));
    float3 fresnelBase = lerp(float3(0.04, 0.04, 0.04), albedo, metallic);

    float shadowVisibility = sampleShadowMap(input.lightSpacePosition);

    float3 ambient = albedo * 0.14 * ambientOcclusion;
    float3 emission = material.legacyEmission != 0 ? albedo : material.emissionColor * emissionTexture.Sample(materialSampler, input.uv).rgb;
    float3 emissive = emission * material.emissiveIntensity;
    float3 direct = applyLighting(
        input.worldPosition,
        normal,
        viewDirection,
        albedo,
        metallic,
        roughness,
        fresnelBase,
        lights,
        shadow.lightIndex,
        shadowVisibility);

    float3 color = ambient + emissive + direct;
    return float4(color, 1.0);
}
