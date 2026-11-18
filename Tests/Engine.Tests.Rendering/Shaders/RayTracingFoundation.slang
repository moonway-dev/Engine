import RayTracingTypes;

// Sparse sets exercise RT reflection without coupling to the sample raster ABI.
[[vk::binding(2, 0)]] RaytracingAccelerationStructure scene;
[[vk::binding(3, 2)]] RWTexture2D<float4> outputImage;
[[vk::binding(1, 4)]] StructuredBuffer<float4> materialColors;
[[vk::binding(6, 4)]] Texture2D<float4> alphaTextures[2];
[[vk::binding(7, 4)]] SamplerState materialSamplers[2];
[[vk::push_constant]] RayConstants constants;

[shader("raygeneration")]
void rayGeneration()
{
    uint2 pixel = DispatchRaysIndex().xy;
    float2 uv = (float2(pixel) + 0.5) / float2(constants.extent);
    RayDesc ray;
    ray.Origin = float3(0.0, 0.0, -3.0);
    ray.Direction = normalize(float3(uv * 2.0 - 1.0, 1.0));
    ray.TMin = 0.001;
    ray.TMax = 1000.0;
    RadiancePayload radiance = { float3(0.0), 0 };
    // Hit-group index 0, one ray type per geometry, radiance miss at SBT index 0.
    TraceRay(scene, RAY_FLAG_NONE, 0xff, 0, 1, 0, ray, radiance);
    ShadowPayload shadow = { 1 };
    // A second miss entry uses SBT index 1, not a second stage-keyed dictionary slot.
    TraceRay(scene, RAY_FLAG_ACCEPT_FIRST_HIT_AND_END_SEARCH | RAY_FLAG_SKIP_CLOSEST_HIT_SHADER,
        0xff, 0, 1, 1, ray, shadow);
    outputImage[pixel] = float4(radiance.color * constants.exposure * (shadow.occluded == 0 ? 1.0 : 0.5), 1.0);
}

[shader("miss")]
void radianceMiss(inout RadiancePayload payload)
{
    payload.color = float3(0.1, 0.2, 0.4) * (1.0 + 0.01 * constants.frameIndex);
}

[shader("miss")]
void shadowMiss(inout ShadowPayload payload)
{
    payload.occluded = 0;
}

[shader("closesthit")]
void triangleClosestHit(inout RadiancePayload payload, BuiltInTriangleIntersectionAttributes attributes)
{
    float3 barycentrics = float3(1.0 - attributes.barycentrics.x - attributes.barycentrics.y, attributes.barycentrics);
    MaterialCallData material = { materialColors[PrimitiveIndex()].xyz * barycentrics, 0.5 };
    CallShader(0, material);
    payload.color = material.color;
}

[shader("anyhit")]
void triangleAnyHit(inout RadiancePayload payload, BuiltInTriangleIntersectionAttributes attributes)
{
    float alpha = alphaTextures[0].SampleLevel(materialSamplers[1], attributes.barycentrics, 0).a;
    if (alpha < 0.5)
        IgnoreHit();
}

[shader("intersection")]
void sphereIntersection()
{
    float3 origin = ObjectRayOrigin();
    float3 direction = ObjectRayDirection();
    float a = dot(direction, direction);
    float b = dot(origin, direction);
    float c = dot(origin, origin) - 1.0;
    float discriminant = b * b - a * c;
    if (discriminant >= 0.0)
    {
        float distance = (-b - sqrt(discriminant)) / a;
        if (distance >= RayTMin() && distance <= RayTCurrent())
        {
            SphereAttributes attributes = { normalize(origin + distance * direction) };
            ReportHit(distance, 0, attributes);
        }
    }
}

[shader("closesthit")]
void sphereClosestHit(inout RadiancePayload payload, SphereAttributes attributes)
{
    payload.color = materialColors[0].xyz * (0.5 + 0.5 * attributes.normal);
}

[shader("anyhit")]
void sphereAnyHit(inout RadiancePayload payload, SphereAttributes attributes)
{
    if (attributes.normal.y < -0.75)
        IgnoreHit();
}

[shader("callable")]
void shadeMaterial(inout MaterialCallData material)
{
    material.color *= materialColors[0].xyz * (1.0 - material.roughness * 0.5);
}
