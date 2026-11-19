import Common;
import RtData;

[[vk::binding(0, 0)]] ConstantBuffer<RtViewData> view;
[[vk::binding(1, 0)]] RaytracingAccelerationStructure scene;
[[vk::binding(2, 0)]] Texture2D<float> sceneDepth;
[[vk::binding(3, 0)]] Texture2D<float4> normalRoughness;
[[vk::binding(4, 0)]] RWTexture2D<float4> giOutput;
[[vk::binding(7, 0)]] RWTexture2D<float4> validityOutput;
[[vk::binding(5, 0)]] TextureCube irradianceMap;
[[vk::binding(6, 0)]] SamplerState iblSampler;
[[vk::binding(0, 1)]] StructuredBuffer<RayTracingSurfaceData> surfaces;
[[vk::binding(1, 1)]] StructuredBuffer<RayTracingVertexData> vertices;
[[vk::binding(2, 1)]] StructuredBuffer<uint> indices;
[[vk::binding(3, 1)]] Texture2D materialTextures[RtMaterialTextureCount];
[[vk::binding(4, 1)]] SamplerState materialSampler;

struct GiPayload
{
    float3 radiance;
    float padding;
};

static const float GiHemisphereRadius = 1.5358897;

[shader("raygeneration")]
void rayGen()
{
    uint2 pixel = DispatchRaysIndex().xy;
    uint2 extent = DispatchRaysDimensions().xy;
    float2 uv = (float2(pixel) + 0.5) / float2(extent);
    uint2 sourcePixel = min(uint2(uv * view.extentAndFrame.xy), uint2(view.extentAndFrame.xy) - 1u);
    if (any(pixel >= extent))
        return;
    float depth = sceneDepth.Load(int3(sourcePixel, 0));
    float4 surface = normalRoughness.Load(int3(sourcePixel, 0));
    if (depth >= view.extentAndFrame.w || dot(surface.xyz, surface.xyz) < 1e-6)
    {
        giOutput[pixel] = float4(0.0, 0.0, 0.0, depth);
        validityOutput[pixel] = 1.0;
        return;
    }
    float3 position = rtWorldPosition(uv, depth, view);
    uint rate = rtTraceRate(view, 2u, position);
    if (!rtTracePixel(sourcePixel, rate, 2u))
    {
        giOutput[pixel] = float4(0.0, 0.0, 0.0, depth);
        validityOutput[pixel] = 0.0;
        return;
    }
    float3 normal = rtSafeNormal(surface.xyz, float3(0.0, 1.0, 0.0));
    float normalOffset = rtSurfaceOffset(position, normal, view);
    uint state = rtHash((pixel.y * extent.x + pixel.x) ^ rtHash(uint(view.extentAndFrame.z)));
    uint sampleCount = rtSampleCount(view, 2u);
    float3 radiance = float3(0.0);
    for (uint sample = 0; sample < sampleCount; sample++)
    {
        RayDesc ray;
        ray.Origin = position + normal * normalOffset;
        ray.Direction = rtConeDirection(normal, GiHemisphereRadius, float2(rtRandom(state), rtRandom(state)));
        ray.TMin = normalOffset;
        ray.TMax = max(view.cameraPositionAndMaxDistance.w, ray.TMin * 2.0);
        GiPayload payload;
        payload.radiance = float3(0.0);
        payload.padding = 0.0;
        TraceRay(scene, RAY_FLAG_FORCE_OPAQUE, RtSurfaceRayMask, 0, 0, 0, ray, payload);
        radiance += payload.radiance;
    }
    giOutput[pixel] = float4(max(radiance / float(sampleCount), 0.0), depth);
    validityOutput[pixel] = 1.0;
}

[shader("miss")]
void missMain(inout GiPayload payload)
{
    payload.radiance = float3(0.0);
}

[shader("closesthit")]
void closestHitMain(inout GiPayload payload, BuiltInTriangleIntersectionAttributes attributes)
{
    RayTracingSurfaceData surface = surfaces[InstanceID()];
    uint indexBase = surface.indexOffset + PrimitiveIndex() * 3u;
    RayTracingVertexData v0 = vertices[surface.vertexOffset + indices[indexBase]];
    RayTracingVertexData v1 = vertices[surface.vertexOffset + indices[indexBase + 1u]];
    RayTracingVertexData v2 = vertices[surface.vertexOffset + indices[indexBase + 2u]];
    float3 barycentric = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics);
    float2 uv = (v0.uv.xy * barycentric.x + v1.uv.xy * barycentric.y + v2.uv.xy * barycentric.z) * surface.material.uvScale + surface.material.uvOffset;
    float3 p0 = mul(surface.model, float4(v0.position.xyz, 1.0)).xyz;
    float3 p1 = mul(surface.model, float4(v1.position.xyz, 1.0)).xyz;
    float3 p2 = mul(surface.model, float4(v2.position.xyz, 1.0)).xyz;
    float3 faceNormal = rtSafeNormal(cross(p1 - p0, p2 - p0), -WorldRayDirection());
    float3 localNormal = v0.normal.xyz * barycentric.x + v1.normal.xyz * barycentric.y + v2.normal.xyz * barycentric.z;
    float3 geometryNormal = rtSafeNormal(mul(transpose((float3x3)surface.inverseModel), localNormal), faceNormal);
    float4 localTangent = v0.tangent * barycentric.x + v1.tangent * barycentric.y + v2.tangent * barycentric.z;
    float3 transformedTangent = mul((float3x3)surface.model, localTangent.xyz);
    float3 fallbackTangent;
    float3 fallbackBitangent;
    rtBasis(geometryNormal, fallbackTangent, fallbackBitangent);
    float3 tangent = rtSafeNormal(transformedTangent - geometryNormal * dot(geometryNormal, transformedTangent), fallbackTangent);
    float handedness = localTangent.w < 0.0 ? -1.0 : 1.0;
    float3 bitangent = normalize(cross(geometryNormal, tangent)) * handedness;
    float3 tangentNormal = decodeNormalMap(materialTextures[NonUniformResourceIndex(surface.textureOffset + 3u)].SampleLevel(materialSampler, uv, 0).xyz, surface.material.normalScale);
    float3 normal = rtSafeNormal(tangent * tangentNormal.x + bitangent * tangentNormal.y + geometryNormal * tangentNormal.z, geometryNormal);
    float3 viewDirection = -WorldRayDirection();
    if (dot(normal, viewDirection) < 0.0)
        normal = -normal;

    float3 albedo = surface.material.albedoColor * materialTextures[NonUniformResourceIndex(surface.textureOffset)].SampleLevel(materialSampler, uv, 0).rgb;
    float metallic = saturate(surface.material.metallic * materialTextures[NonUniformResourceIndex(surface.textureOffset + 1u)].SampleLevel(materialSampler, uv, 0)[min(surface.material.metallicChannel, 3u)]);
    float3 diffuseAlbedo = albedo * (1.0 - metallic);
    float3 irradiance = irradianceMap.SampleLevel(iblSampler, normal, 0).rgb;
    payload.radiance = diffuseAlbedo * irradiance / 3.14159265;
}
