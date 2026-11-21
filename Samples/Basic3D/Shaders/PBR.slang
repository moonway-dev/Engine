import Common;

[[vk::binding(0, 0)]] ConstantBuffer<CameraData> camera;
[[vk::binding(1, 0)]] ConstantBuffer<ShadowData> shadow;
[[vk::binding(2, 0)]] Texture2D shadowMap;
[[vk::binding(3, 0)]] SamplerComparisonState shadowSampler;
[[vk::binding(4, 0)]] ConstantBuffer<LightsInput> lights;
[[vk::binding(5, 0)]] TextureCube irradianceMap;
[[vk::binding(6, 0)]] TextureCube prefilteredEnvironment;
[[vk::binding(7, 0)]] Texture2D brdfLut;
[[vk::binding(8, 0)]] SamplerState iblSampler;
#if defined(RT_SHADOWS) && RT_SHADOWS
[[vk::binding(9, 0)]] Texture2D<float4> rtShadowVisibility;
[[vk::binding(10, 0)]] ConstantBuffer<RtShadowControl> rtShadowControl;
#endif

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
    float4 currentClipPosition : CURRENT_CLIP_POSITION;
    float4 previousClipPosition : PREVIOUS_CLIP_POSITION;
};

[shader("vertex")]
FragmentInput vsMain(VertexInput input)
{
    FragmentInput output;
    float4 worldPosition = mul(pushConstants.model, float4(input.position, 1.0));
    output.worldPosition = worldPosition.xyz;
    output.position = mul(camera.viewProjection, worldPosition);
    output.currentClipPosition = output.position;
    output.previousClipPosition = mul(camera.previousViewProjection, mul(pushConstants.previousModel, float4(input.position, 1.0)));
    float3x3 modelBasis = (float3x3)pushConstants.model;
    float3 basisX = float3(modelBasis[0][0], modelBasis[1][0], modelBasis[2][0]);
    float3 basisY = float3(modelBasis[0][1], modelBasis[1][1], modelBasis[2][1]);
    float3 basisZ = float3(modelBasis[0][2], modelBasis[1][2], modelBasis[2][2]);
    float3 transformedNormal = cross(basisY, basisZ) * input.normal.x + cross(basisZ, basisX) * input.normal.y + cross(basisX, basisY) * input.normal.z;
    transformedNormal *= determinant(modelBasis) < 0.0 ? -1.0 : 1.0;
    output.normal = dot(transformedNormal, transformedNormal) > 1e-12 ? normalize(transformedNormal) : input.normal;
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
    return output;
}

float shadowViewDepth(float3 worldPosition)
{
    return dot(worldPosition - shadow.cameraPositionAndNear.xyz, shadow.cameraForwardAndDistance.xyz);
}

uint shadowCascade(float depth)
{
    return depth > shadow.splitDepths.z ? 3u : depth > shadow.splitDepths.y ? 2u : depth > shadow.splitDepths.x ? 1u : 0u;
}

float2 shadowAtlasUv(uint cascade, float2 localUv, float2 offset)
{
    float4 tile = shadow.atlasTiles[cascade];
    float texel = shadow.biasParameters.z;
    return clamp(tile.xy + localUv * tile.zw + offset * texel, tile.xy + texel * 0.5, tile.xy + tile.zw - texel * 0.5);
}

float sampleShadowCascade(uint cascade, float3 worldPosition, float3 geometryNormal)
{
    float4 metrics = shadow.cascadeMetrics[cascade];
    float nDotL = saturate(dot(geometryNormal, -shadow.lightDirectionAndStrength.xyz));
    float normalOffset = shadow.biasParameters.y * metrics.x * sqrt(saturate(1.0 - nDotL * nDotL));
    float4 lightPosition = mul(shadow.lightViewProjections[cascade], float4(worldPosition + geometryNormal * normalOffset, 1.0));
    float3 projected = lightPosition.xyz / lightPosition.w;
    float2 localUv = projected.xy * 0.5 + 0.5;
    if (any(localUv < 0.0) || any(localUv > 1.0) || projected.z < 0.0 || projected.z > 1.0)
        return 1.0;
    float receiverDepth = projected.z - shadow.biasParameters.x * metrics.x / metrics.y;
    float radius = shadow.quality == 0u ? 1.0 : shadow.quality == 1u ? 2.0 : 3.0;
    if (shadow.quality == 3u)
    {
        float blockerDepth = 0.0;
        float blockers = 0.0;
        float searchRadius = min(shadow.filterParameters.w, max(1.0, tan(shadow.filterParameters.z) * metrics.y * receiverDepth / metrics.x));
        for (uint index = 0u; index < 16u; index++)
        {
            float angle = float(index) * 2.39996323;
            float2 offset = float2(cos(angle), sin(angle)) * sqrt((float(index) + 0.5) / 16.0) * searchRadius;
            float2 atlasUv = shadowAtlasUv(cascade, localUv, offset);
            uint2 pixel = uint2(atlasUv / shadow.biasParameters.z);
            float sampleDepth = shadowMap.Load(int3(pixel, 0)).x;
            if (sampleDepth < receiverDepth)
            {
                blockerDepth += sampleDepth;
                blockers += 1.0;
            }
        }
        if (blockers == 0.0)
            return 1.0;
        float separation = max(0.0, receiverDepth - blockerDepth / blockers) * metrics.y;
        radius = clamp(tan(shadow.filterParameters.z) * separation / metrics.x, 1.0, shadow.filterParameters.w);
    }
    int kernel = shadow.quality == 0u ? 1 : shadow.quality == 1u ? 2 : 3;
    float visibility = 0.0;
    float totalWeight = 0.0;
    for (int y = -kernel; y <= kernel; y++)
    for (int x = -kernel; x <= kernel; x++)
    {
        float weight = float((kernel + 1 - abs(x)) * (kernel + 1 - abs(y)));
        float2 offset = float2(x, y) * radius / float(kernel);
        visibility += shadowMap.SampleCmpLevelZero(shadowSampler, shadowAtlasUv(cascade, localUv, offset), receiverDepth) * weight;
        totalWeight += weight;
    }
    return visibility / totalWeight;
}

struct RtShadowControl
{
    uint enabled;
    uint extentX;
    uint extentY;
    uint pointLightEnabled;
    uint pointLightIndex;
    uint rasterDisabled;
    uint padding1;
    uint padding2;
};

float sampleShadowMap(float3 worldPosition, float3 geometryNormal)
{
    if (shadow.lightIndex < 0 || shadow.lightDirectionAndStrength.w <= 0.0)
        return 1.0;
    float depth = shadowViewDepth(worldPosition);
    if (depth < shadow.cameraPositionAndNear.w || depth >= shadow.cameraForwardAndDistance.w)
        return 1.0;
    uint cascade = shadowCascade(depth);
    float visibility = sampleShadowCascade(cascade, worldPosition, geometryNormal);
    if (cascade < 3u && shadow.filterParameters.x > 0.0)
    {
        float nearDepth = cascade == 0u ? shadow.cameraPositionAndNear.w : shadow.splitDepths[cascade - 1u];
        float farDepth = shadow.splitDepths[cascade];
        float blend = saturate((depth - farDepth + (farDepth - nearDepth) * shadow.filterParameters.x) / ((farDepth - nearDepth) * shadow.filterParameters.x));
        if (blend > 0.0)
            visibility = lerp(visibility, sampleShadowCascade(cascade + 1u, worldPosition, geometryNormal), blend);
    }
    float fadeWidth = max(0.001, shadow.cameraForwardAndDistance.w * shadow.filterParameters.y);
    float fade = saturate((shadow.cameraForwardAndDistance.w - depth) / fadeWidth);
    return saturate(1.0 - (1.0 - visibility) * shadow.lightDirectionAndStrength.w * fade);
}

struct FragmentOutput
{
    float4 color : SV_Target0;
#if defined(TEMPORAL_MRT) && TEMPORAL_MRT
    float4 motion : SV_Target1;
#endif
#if defined(SPECULAR_MRT) && SPECULAR_MRT
#if defined(TEMPORAL_MRT) && TEMPORAL_MRT
    float4 specular : SV_Target2;
    float4 specularResponse : SV_Target3;
#else
    float4 specular : SV_Target1;
    float4 specularResponse : SV_Target2;
#endif
#endif
};

[shader("fragment")]
FragmentOutput psMain(FragmentInput input)
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

    float shadowVisibility = 1.0;
#if defined(RT_SHADOWS) && RT_SHADOWS
    if (rtShadowControl.rasterDisabled == 0u)
#endif
        shadowVisibility = sampleShadowMap(input.worldPosition, geometryNormal);
    float pointShadowVisibility = 1.0;
    uint2 rtPixel = uint2(0u);
#if defined(RT_SHADOWS) && RT_SHADOWS
    if (rtShadowControl.extentX > 0u && rtShadowControl.extentY > 0u)
    {
        uint visibilityWidth;
        uint visibilityHeight;
        rtShadowVisibility.GetDimensions(visibilityWidth, visibilityHeight);
        rtPixel = min(uint2(input.position.xy * float2(visibilityWidth, visibilityHeight) / float2(rtShadowControl.extentX, rtShadowControl.extentY)), uint2(visibilityWidth, visibilityHeight) - 1u);
    }
    if (rtShadowControl.enabled != 0u && shadow.lightIndex >= 0)
    {
        float rtVisibility = rtShadowVisibility.Load(int3(rtPixel, 0)).r;
        shadowVisibility = saturate(1.0 - (1.0 - rtVisibility) * shadow.lightDirectionAndStrength.w);
    }
    if (rtShadowControl.pointLightEnabled != 0u)
        pointShadowVisibility = rtShadowVisibility.Load(int3(rtPixel, 0)).g;
#endif

    float nDotV = saturate(dot(normal, viewDirection));
    float3 irradiance = irradianceMap.SampleLevel(iblSampler, normal, 0).rgb;
    float3 diffuseIndirect = irradiance * albedo * (1.0 - metallic) / 3.14159265;
    float3 reflectionDirection = reflect(-viewDirection, normal);
    float3 prefiltered = prefilteredEnvironment.SampleLevel(iblSampler, reflectionDirection, roughness * 6.0).rgb;
    float2 brdf = brdfLut.SampleLevel(iblSampler, float2(nDotV, roughness), 0).rg;
    float3 specularIndirect = prefiltered * (fresnelBase * brdf.x + brdf.y);
    float3 indirect = diffuseIndirect * ambientOcclusion + specularIndirect;
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
        shadowVisibility,
#if defined(RT_SHADOWS) && RT_SHADOWS
        rtShadowControl.pointLightEnabled != 0u ? int(rtShadowControl.pointLightIndex) : -1,
        pointShadowVisibility);
#else
        -1,
        1.0);
#endif

    float3 color = indirect + emissive + direct;
    float shadowAmount = shadow.lightDirectionAndStrength.w;
    if (shadow.lightIndex >= 0 && shadowAmount > 1.0)
    {
        float ambientShadowFactor = saturate(1.0 - (shadowAmount - 1.0) * (1.0 - shadowVisibility) * 0.6);
        color = indirect * ambientShadowFactor + emissive + direct;
    }
    if (shadow.debugCascades != 0u && shadowViewDepth(input.worldPosition) < shadow.cameraForwardAndDistance.w)
    {
        uint cascade = shadowCascade(shadowViewDepth(input.worldPosition));
        float3 cascadeColor = cascade == 0u ? float3(1.0, 0.15, 0.15) : cascade == 1u ? float3(0.15, 1.0, 0.15) : cascade == 2u ? float3(0.15, 0.3, 1.0) : float3(1.0, 0.8, 0.15);
        color = lerp(color, cascadeColor, 0.35);
    }
    float2 currentNdc = input.currentClipPosition.xy / max(abs(input.currentClipPosition.w), 1e-6);
    float2 previousNdc = input.previousClipPosition.xy / max(abs(input.previousClipPosition.w), 1e-6);
    FragmentOutput output;
    output.color = float4(color, 1.0);
    float linearDepth = length(camera.cameraPosition - input.worldPosition);
#if defined(TEMPORAL_MRT) && TEMPORAL_MRT
    float previousDepth = input.previousClipPosition.z / max(abs(input.previousClipPosition.w), 1e-6);
    output.motion = float4((currentNdc - previousNdc) * 0.5, previousDepth, linearDepth);
#endif
#if defined(SPECULAR_MRT) && SPECULAR_MRT
    output.specular = float4(specularIndirect, linearDepth);
    output.specularResponse = float4(fresnelBase * brdf.x + brdf.y, 1.0);
#endif
    return output;
}
