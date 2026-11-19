import RtData;

[[vk::binding(0, 0)]] ConstantBuffer<RtViewData> view;
[[vk::binding(1, 0)]] RaytracingAccelerationStructure scene;
[[vk::binding(2, 0)]] Texture2D<float> sceneDepth;
[[vk::binding(3, 0)]] Texture2D<float4> normalRoughness;
[[vk::binding(5, 0)]] RWTexture2D<float4> shadowOutput;
[[vk::binding(6, 0)]] RWTexture2D<float4> validityOutput;

struct ShadowPayload
{
    float visibility;
};

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
    float3 normal = normalRoughness.Load(int3(sourcePixel, 0)).xyz;
    float3 direction = -view.lightDirectionAndAngularRadius.xyz;
    if (depth >= view.extentAndFrame.w || dot(normal, normal) < 1e-6)
    {
        shadowOutput[pixel] = float4(1.0, 1.0, 1.0, depth);
        validityOutput[pixel] = 1.0;
        return;
    }
    normal = normalize(normal);
    float3 position = rtWorldPosition(uv, depth, view);
    uint rate = rtTraceRate(view, 0u, position);
    if (!rtTracePixel(sourcePixel, rate, 0u))
    {
        shadowOutput[pixel] = float4(1.0, 1.0, 1.0, depth);
        validityOutput[pixel] = 0.0;
        return;
    }
    bool directionalActive = dot(direction, direction) > 1e-6;
    direction = rtSafeNormal(direction, normal);
    float normalOffset = rtSurfaceOffset(position, normal, view);
    uint state = rtHash((pixel.y * extent.x + pixel.x) ^ rtHash(uint(view.extentAndFrame.z)));
    uint sampleCount = rtSampleCount(view, 0u);
    float visibility = 0.0;
    uint eligibleSamples = 0;
    for (uint sample = 0; sample < (directionalActive ? sampleCount : 0u); sample++)
    {
        float3 rayDirection = rtConeDirection(direction, view.lightDirectionAndAngularRadius.w, float2(rtRandom(state), rtRandom(state)));
        if (dot(normal, rayDirection) <= 0.0)
            continue;
        eligibleSamples++;
        RayDesc ray;
        ray.Origin = position + normal * normalOffset;
        ray.Direction = rayDirection;
        ray.TMin = normalOffset;
        ray.TMax = max(view.cameraPositionAndMaxDistance.w, ray.TMin * 2.0);
        ShadowPayload payload;
        payload.visibility = 0.0;
        TraceRay(scene, RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH, RtShadowRayMask, 0, 0, 0, ray, payload);
        visibility += payload.visibility;
    }
    visibility = eligibleSamples == 0 ? 1.0 : visibility / float(eligibleSamples);
    float pointVisibility = 1.0;
    float4 pointLight = view.pointLightPositionAndRange;
    if (pointLight.w > 0.0)
    {
        float3 origin = position + normal * normalOffset;
        float3 delta = pointLight.xyz - origin;
        float distance = length(delta);
        if (distance > normalOffset * 0.5 && distance < pointLight.w && dot(normal, delta) > 0.0)
        {
            RayDesc pointRay;
            pointRay.Origin = origin;
            pointRay.Direction = delta / distance;
            pointRay.TMin = normalOffset;
            pointRay.TMax = distance - normalOffset * 0.25;
            ShadowPayload pointPayload;
            pointPayload.visibility = 0.0;
            TraceRay(scene, RAY_FLAG_FORCE_OPAQUE | RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH, RtShadowRayMask, 0, 0, 0, pointRay, pointPayload);
            pointVisibility = pointPayload.visibility;
        }
    }
    shadowOutput[pixel] = float4(visibility, pointVisibility, visibility, depth);
    validityOutput[pixel] = 1.0;
}

[shader("miss")]
void missMain(inout ShadowPayload payload)
{
    payload.visibility = 1.0;
}

[shader("closesthit")]
void closestHitMain(inout ShadowPayload payload, BuiltInTriangleIntersectionAttributes attributes)
{
    payload.visibility = 0.0;
}
