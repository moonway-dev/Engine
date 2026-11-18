import RayTracingTypes;

[[vk::binding(0, 0)]] RaytracingAccelerationStructure scenes[2];
[[vk::binding(1, 2)]] RWTexture2D<float4> outputs[2];
[[vk::binding(0, 4)]] Texture2D<float4> textures[3];
[[vk::binding(1, 4)]] SamplerState samplers[2];
[[vk::binding(2, 4)]] ConstantBuffer<RayConstants> frameData[2];
[[vk::binding(3, 4)]] StructuredBuffer<float4> materials[2];

[shader("raygeneration")]
void arrayRayGeneration()
{
    uint2 pixel = DispatchRaysIndex().xy;
    RayDesc ray = { float3(0, 0, -3), 0.001, float3(0, 0, 1), 1000.0 };
    RadiancePayload payload = { float3(0), 0 };
    TraceRay(scenes[0], RAY_FLAG_NONE, 0xff, 0, 1, 0, ray, payload);
    float4 color = textures[2].SampleLevel(samplers[1], float2(0.5), 0);
    outputs[1][pixel] = float4(payload.color, 1.0) + color * frameData[1].exposure + materials[1][0];
}
