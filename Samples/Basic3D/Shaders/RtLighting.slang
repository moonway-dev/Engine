import Common;
import RtData;

[[vk::binding(0, 1)]] StructuredBuffer<RayTracingSurfaceData> surfaces;
[[vk::binding(1, 1)]] StructuredBuffer<RayTracingVertexData> vertices;
[[vk::binding(2, 1)]] StructuredBuffer<uint> indices;
[[vk::binding(3, 1)]] Texture2D materialTextures[RtMaterialTextureCount];
[[vk::binding(4, 1)]] SamplerState materialSampler;

struct RtHitPayload
{
    float3 position;
    float distance;
    float3 normal;
    float roughness;
    float3 geometryNormal;
    float metallic;
    float3 albedo;
    float ambientOcclusion;
    float3 emission;
    float padding;
};

float rtVisibility(RtHitPayload hit, float3 direction, float maximumDistance)
{
    float offset = max(view.settings.y, 1e-4) + max(max(abs(hit.position.x), abs(hit.position.y)), abs(hit.position.z)) * 2e-7;
    float3 normal = dot(hit.geometryNormal, direction) >= 0.0 ? hit.geometryNormal : -hit.geometryNormal;
    RayDesc ray;
    ray.Origin = hit.position + normal * offset;
    ray.Direction = direction;
    ray.TMin = offset;
    ray.TMax = max(maximumDistance - offset * 2.0, offset);
    RtHitPayload payload = (RtHitPayload)0;
    TraceRay(scene, RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER,
        RtShadowRayMask, 0, 0, 0, ray, payload);
    return payload.distance < 0.0 ? 1.0 : 0.0;
}

float3 rtDirectLighting(RtHitPayload hit, float3 viewDirection, inout uint state, bool diffuseOnly)
{
    float3 result = float3(0.0);
    float3 fresnelBase = lerp(float3(0.04), hit.albedo, hit.metallic);
    for (uint index = 0; index < min(lights.count, 8u); index++)
    {
        LightInput light = lights.lights[index];
        if (light.intensity <= 0.0)
            continue;
        float3 direction;
        float attenuation = 1.0;
        float distance = view.cameraPositionAndMaxDistance.w;
        if (light.type < 0.5)
            direction = rtConeDirection(rtSafeNormal(-light.directionOrPosition, hit.normal), view.lightDirectionAndAngularRadius.w,
                float2(rtRandom(state), rtRandom(state)));
        else
        {
            distance = length(light.directionOrPosition - hit.position);
            computePointLightRadiance(light, hit.position, direction, attenuation);
        }
        float nDotL = saturate(dot(hit.normal, direction));
        if (nDotL <= 0.0 || attenuation <= 0.0)
            continue;
        float visibility = rtVisibility(hit, direction, distance);
        if (visibility <= 0.0)
            continue;
        float3 halfVector = rtSafeNormal(viewDirection + direction, hit.normal);
        float nDotV = saturate(dot(hit.normal, viewDirection));
        float3 fresnel = fresnelSchlick(saturate(dot(viewDirection, halfVector)), fresnelBase);
        float3 diffuse = (1.0 - fresnel) * (1.0 - hit.metallic) * hit.albedo / 3.14159265;
        float3 specular = diffuseOnly ? float3(0.0)
            : distributionGGX(hit.normal, halfVector, hit.roughness) * geometrySmith(nDotV, nDotL, hit.roughness)
                * fresnel / max(4.0 * nDotV * nDotL, 1e-4);
        result += (diffuse + specular) * light.color * light.intensity * attenuation * nDotL * visibility;
    }
    return result;
}

[shader("closesthit")]
void closestHitMain(inout RtHitPayload payload, BuiltInTriangleIntersectionAttributes attributes)
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
    if (dot(normal, -WorldRayDirection()) < 0.0)
        normal = -normal;
    if (dot(faceNormal, -WorldRayDirection()) < 0.0)
        faceNormal = -faceNormal;
    payload.position = WorldRayOrigin() + WorldRayDirection() * RayTCurrent();
    payload.distance = RayTCurrent();
    payload.normal = normal;
    payload.geometryNormal = faceNormal;
    payload.albedo = surface.material.albedoColor * materialTextures[NonUniformResourceIndex(surface.textureOffset)].SampleLevel(materialSampler, uv, 0).rgb;
    payload.metallic = saturate(surface.material.metallic * materialTextures[NonUniformResourceIndex(surface.textureOffset + 1u)].SampleLevel(materialSampler, uv, 0)[min(surface.material.metallicChannel, 3u)]);
    payload.roughness = clamp(surface.material.roughness * materialTextures[NonUniformResourceIndex(surface.textureOffset + 2u)].SampleLevel(materialSampler, uv, 0)[min(surface.material.roughnessChannel, 3u)], 0.045, 1.0);
    float sampledAo = materialTextures[NonUniformResourceIndex(surface.textureOffset + 4u)].SampleLevel(materialSampler, uv, 0)[min(surface.material.ambientOcclusionChannel, 3u)];
    payload.ambientOcclusion = lerp(1.0, sampledAo, saturate(surface.material.aoStrength));
    float3 emission = surface.material.legacyEmission != 0u ? payload.albedo
        : surface.material.emissionColor * materialTextures[NonUniformResourceIndex(surface.textureOffset + 5u)].SampleLevel(materialSampler, uv, 0).rgb;
    payload.emission = emission * surface.material.emissiveIntensity;
}
