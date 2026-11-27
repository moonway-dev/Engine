import Common;
import RtData;

[[vk::binding(0, 0)]] ConstantBuffer<RtViewData> view;
[[vk::binding(1, 0)]] RaytracingAccelerationStructure scene;
[[vk::binding(2, 0)]] Texture2D<float> sceneDepth;
[[vk::binding(3, 0)]] Texture2D<float4> normalRoughness;
[[vk::binding(4, 0)]] RWTexture2D<float4> giOutput;
[[vk::binding(7, 0)]] RWTexture2D<float4> validityOutput;
[[vk::binding(6, 0)]] SamplerState iblSampler;
[[vk::binding(8, 0)]] ConstantBuffer<LightsInput> lights;
[[vk::binding(9, 0)]] TextureCube environmentMap;

#include "RtLighting.slang"

[shader("raygeneration")]
void rayGen()
{
    uint2 pixel = DispatchRaysIndex().xy;
    uint2 extent = DispatchRaysDimensions().xy;
    uint2 sourceExtent = uint2(view.extentAndFrame.xy);
    uint2 sourcePixel = min(uint2((float2(pixel) + 0.5) * float2(sourceExtent) / float2(extent)), sourceExtent - 1u);
    float2 uv = (float2(sourcePixel) + 0.5) / float2(sourceExtent);
    float depth = sceneDepth.Load(int3(sourcePixel, 0));
    float4 surface = normalRoughness.Load(int3(sourcePixel, 0));
    if (depth >= view.extentAndFrame.w || dot(surface.xyz, surface.xyz) < 1e-6)
    {
        giOutput[pixel] = float4(0.0, 0.0, 0.0, -1.0);
        validityOutput[pixel] = 0.0;
        return;
    }
    float3 position = rtWorldPosition(uv, depth, view);
    float3 normal = normalize(surface.xyz);
    float normalOffset = rtSurfaceOffset(position, normal, view);
    uint frame = uint(view.extentAndFrame.z);
    uint state = rtHash(pixel.y * extent.x + pixel.x + frame * 0x9e3779b9u);
    uint sampleCount = rtSampleCount(view, 2u);
    uint maximumBounces = view.giQuality.w >= 0.75 ? 2u : 1u;
    float3 radiance = float3(0.0);
    for (uint sample = 0; sample < sampleCount; sample++)
    {
        RayDesc ray;
        ray.Origin = position + normal * normalOffset;
        ray.Direction = rtCosineDirection(normal, rtSequence(pixel ^ uint2(15823u, 9733u), frame, sample));
        ray.TMin = normalOffset;
        ray.TMax = max(view.cameraPositionAndMaxDistance.w, ray.TMin * 2.0);
        float3 throughput = float3(1.0);
        for (uint bounce = 0; bounce < maximumBounces; bounce++)
        {
            RtHitPayload hit = (RtHitPayload)0;
            TraceRay(scene, RAY_FLAG_FORCE_OPAQUE, RtSurfaceRayMask, 0, 0, 0, ray, hit);
            if (hit.distance < 0.0)
            {
                if (bounce != 0u)
                    radiance += throughput * environmentMap.SampleLevel(iblSampler, ray.Direction, 0).rgb;
                break;
            }
            if (bounce == 0u)
                radiance -= environmentMap.SampleLevel(iblSampler, ray.Direction, 0).rgb;
            radiance += throughput * (rtDirectLighting(hit, -ray.Direction, state, true) + hit.emission);
            throughput *= hit.albedo * (1.0 - hit.metallic) * hit.ambientOcclusion;
            if (max(max(throughput.x, throughput.y), throughput.z) < 0.001)
                break;
            float offset = max(view.settings.y, 1e-4);
            ray.Origin = hit.position + hit.geometryNormal * offset;
            ray.Direction = rtCosineDirection(hit.normal, float2(rtRandom(state), rtRandom(state)));
            ray.TMin = offset;
        }
    }
    giOutput[pixel] = float4(radiance / float(sampleCount), depth);
    validityOutput[pixel] = 1.0;
}

[shader("miss")]
void missMain(inout RtHitPayload payload)
{
    payload.distance = -1.0;
}
