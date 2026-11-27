import Common;
import RtData;

[[vk::binding(0, 0)]] ConstantBuffer<RtViewData> view;
[[vk::binding(1, 0)]] RaytracingAccelerationStructure scene;
[[vk::binding(2, 0)]] Texture2D<float> sceneDepth;
[[vk::binding(3, 0)]] Texture2D<float4> normalRoughness;
[[vk::binding(4, 0)]] RWTexture2D<float4> reflectionOutput;
[[vk::binding(11, 0)]] RWTexture2D<float4> validityOutput;
[[vk::binding(5, 0)]] ConstantBuffer<LightsInput> lights;
[[vk::binding(6, 0)]] TextureCube irradianceMap;
[[vk::binding(7, 0)]] TextureCube prefilteredEnvironment;
[[vk::binding(8, 0)]] Texture2D brdfLut;
[[vk::binding(9, 0)]] SamplerState iblSampler;
[[vk::binding(10, 0)]] Texture2D<float4> primarySpecularResponse;

#include "RtLighting.slang"

[shader("raygeneration")]
void rayGen()
{
    uint2 pixel = DispatchRaysIndex().xy;
    uint2 extent = DispatchRaysDimensions().xy;
    float2 uv = (float2(pixel) + 0.5) / float2(extent);
    uint2 sourcePixel = min(uint2(uv * view.extentAndFrame.xy), uint2(view.extentAndFrame.xy) - 1u);
    float depth = sceneDepth.Load(int3(sourcePixel, 0));
    float4 surfaceNormal = normalRoughness.Load(int3(sourcePixel, 0));
    float roughness = clamp(surfaceNormal.w, 0.045, 1.0);
    float roughnessLimit = clamp(view.settings.x, 0.045, 1.0);
    float4 response = primarySpecularResponse.Load(int3(sourcePixel, 0));
    float replacement = (1.0 - smoothstep(roughnessLimit * 0.75, roughnessLimit, roughness)) * step(0.999, response.a);
    if (depth >= view.extentAndFrame.w || dot(surfaceNormal.xyz, surfaceNormal.xyz) < 1e-6 || replacement <= 0.0)
    {
        reflectionOutput[pixel] = float4(0.0);
        validityOutput[pixel] = 1.0;
        return;
    }
    float3 position = rtWorldPosition(uv, depth, view);
    float3 normal = normalize(surfaceNormal.xyz);
    float3 viewDirection = rtSafeNormal(view.cameraPositionAndMaxDistance.xyz - position, normal);
    float normalOffset = rtSurfaceOffset(position, normal, view);
    uint frame = uint(view.extentAndFrame.z);
    uint state = rtHash(pixel.y * extent.x + pixel.x + frame * 0x9e3779b9u);
    uint sampleCount = rtSampleCount(view, 1u);
    uint maximumBounces = view.reflectionQuality.w >= 0.75 ? 2u : 1u;
    float3 radiance = float3(0.0);
    for (uint sample = 0; sample < sampleCount; sample++)
    {
        RayDesc ray;
        ray.Origin = position + normal * normalOffset;
        ray.Direction = rtReflectionDirection(normal, viewDirection, roughness, rtSequence(pixel, frame, sample));
        ray.TMin = normalOffset;
        ray.TMax = max(view.cameraPositionAndMaxDistance.w, ray.TMin * 2.0);
        float3 throughput = float3(1.0);
        for (uint bounce = 0; bounce < maximumBounces; bounce++)
        {
            RtHitPayload hit = (RtHitPayload)0;
            TraceRay(scene, RAY_FLAG_FORCE_OPAQUE, RtSurfaceRayMask, 0, 0, 0, ray, hit);
            if (hit.distance < 0.0)
            {
                radiance += throughput * prefilteredEnvironment.SampleLevel(iblSampler, ray.Direction, 0).rgb;
                break;
            }
            float3 hitView = -ray.Direction;
            float3 fresnelBase = lerp(float3(0.04), hit.albedo, hit.metallic);
            float2 brdf = brdfLut.SampleLevel(iblSampler, float2(saturate(dot(hit.normal, hitView)), hit.roughness), 0).rg;
            float3 specularResponse = fresnelBase * brdf.x + brdf.y;
            float3 diffuse = irradianceMap.SampleLevel(iblSampler, hit.normal, 0).rgb
                * hit.albedo * (1.0 - hit.metallic) * hit.ambientOcclusion / 3.14159265;
            radiance += throughput * (rtDirectLighting(hit, hitView, state, false) + diffuse + hit.emission);
            if (bounce + 1u >= maximumBounces || hit.roughness > 0.2)
            {
                radiance += throughput * specularResponse * prefilteredEnvironment.SampleLevel(iblSampler,
                    reflect(-hitView, hit.normal), hit.roughness * 6.0).rgb;
                break;
            }
            throughput *= specularResponse;
            if (max(max(throughput.x, throughput.y), throughput.z) < 0.001)
                break;
            float offset = max(view.settings.y, 1e-4);
            ray.Direction = rtReflectionDirection(hit.normal, hitView, hit.roughness, float2(rtRandom(state), rtRandom(state)));
            ray.Origin = hit.position + hit.geometryNormal * offset;
            ray.TMin = offset;
        }
    }
    reflectionOutput[pixel] = float4(max(radiance / float(sampleCount), float3(0.0)) * response.rgb, replacement);
    validityOutput[pixel] = 1.0;
}

[shader("miss")]
void missMain(inout RtHitPayload payload)
{
    payload.distance = -1.0;
}
