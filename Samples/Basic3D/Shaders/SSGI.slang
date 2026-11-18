[[vk::binding(0, 0)]] ConstantBuffer<SsgiData> ssgi;
[[vk::binding(1, 0)]] Texture2D<float4> sceneColor;
[[vk::binding(2, 0)]] Texture2D<float> sceneDepth;
[[vk::binding(3, 0)]] RWTexture2D<float4> giOutput;

struct SsgiData
{
    float4x4 viewProjection;
    float4x4 inverseViewProjection;
    float4 cameraPositionAndRadius;
    float4 extentAndFrame;
};

static const uint RayCount = 4;
static const uint StepCount = 8;
static const float Thickness = 0.15;
static const float MaxHitDistanceScale = 2.0;

uint hashUInt(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    value ^= value >> 16;
    return value;
}

float randomFloat(inout uint state)
{
    state = state * 747796405u + 2891336453u;
    return float(state >> 8) * (1.0 / 16777216.0);
}

float3 reconstructWorldPosition(float2 uv, float depth)
{
    float4 ndc = float4(uv * 2.0 - 1.0, depth, 1.0);
    float4 world = mul(ssgi.inverseViewProjection, ndc);
    return world.xyz / world.w;
}

float2 projectToUv(float3 worldPosition, out bool onScreen)
{
    float4 clip = mul(ssgi.viewProjection, float4(worldPosition, 1.0));
    onScreen = clip.w > 1e-6;
    float2 uv = clip.xy / max(clip.w, 1e-6) * 0.5 + 0.5;
    onScreen = onScreen && all(uv >= 0.0) && all(uv <= 1.0);
    return uv;
}

float3 reconstructNormal(uint2 pixel, float3 centerPosition)
{
    uint2 extent = uint2(ssgi.extentAndFrame.xy);
    uint2 rightPixel = min(pixel + uint2(1, 0), extent - 1);
    uint2 downPixel = min(pixel + uint2(0, 1), extent - 1);
    float3 rightPosition = reconstructWorldPosition((float2(rightPixel) + 0.5) / float2(extent), sceneDepth.Load(int3(rightPixel, 0)));
    float3 downPosition = reconstructWorldPosition((float2(downPixel) + 0.5) / float2(extent), sceneDepth.Load(int3(downPixel, 0)));
    float3 normal = cross(rightPosition - centerPosition, downPosition - centerPosition);
    if (dot(normal, normal) < 1e-12)
        return float3(0.0, 0.0, 0.0);
    normal = normalize(normal);
    float3 viewDirection = ssgi.cameraPositionAndRadius.xyz - centerPosition;
    return dot(normal, viewDirection) < 0.0 ? -normal : normal;
}

float3 cosineHemisphereDirection(float3 normal, float2 xi)
{
    float radius = sqrt(xi.x);
    float phi = 6.2831853 * xi.y;
    float3 tangentAxis = abs(normal.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(tangentAxis, normal));
    float3 bitangent = cross(normal, tangent);
    return normalize(tangent * (radius * cos(phi)) + bitangent * (radius * sin(phi)) + normal * sqrt(max(0.0, 1.0 - xi.x)));
}

float3 traceSample(float3 position, float3 normal, float radius, float skyDepthThreshold, inout uint noiseState)
{
    float3 direction = cosineHemisphereDirection(normal, float2(randomFloat(noiseState), randomFloat(noiseState)));

    float3 contribution = float3(0.0, 0.0, 0.0);
    for (uint step = 1; step <= StepCount; step++)
    {
        float t = float(step) / float(StepCount);
        float3 rayPosition = position + direction * radius * t;
        bool onScreen;
        float2 uv = projectToUv(rayPosition, onScreen);
        if (!onScreen)
            break;
        uint2 pixel = uint2(uv * float2(ssgi.extentAndFrame.xy));
        float sampleDepth = sceneDepth.Load(int3(pixel, 0));
        if (sampleDepth >= skyDepthThreshold)
            continue;
        float4 clip = mul(ssgi.viewProjection, float4(rayPosition, 1.0));
        float rayNdcDepth = clip.z / max(clip.w, 1e-6);
        if (sampleDepth < rayNdcDepth - 0.0001 && rayNdcDepth - sampleDepth < Thickness)
        {
            float3 hitPosition = reconstructWorldPosition(uv, sampleDepth);
            float distance = length(hitPosition - position);
            if (distance < radius * MaxHitDistanceScale)
            {
                float3 radiance = sceneColor.Load(int3(pixel, 0)).rgb;
                contribution += radiance * saturate(1.0 - distance / (radius * MaxHitDistanceScale));
            }
            break;
        }
    }
    return contribution;
}

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    uint2 extent = uint2(ssgi.extentAndFrame.xy);
    if (any(pixel >= extent))
        return;

    float depth = sceneDepth.Load(int3(pixel, 0));
    float skyDepthThreshold = ssgi.extentAndFrame.w;
    if (depth >= skyDepthThreshold)
    {
        giOutput[pixel] = float4(0.0, 0.0, 0.0, depth);
        return;
    }

    float2 uv = (float2(pixel) + 0.5) / float2(extent);
    float3 position = reconstructWorldPosition(uv, depth);
    float3 normal = reconstructNormal(pixel, position);
    if (dot(normal, normal) < 1e-6)
    {
        giOutput[pixel] = float4(0.0, 0.0, 0.0, depth);
        return;
    }

    float radius = ssgi.cameraPositionAndRadius.w;
    uint noiseState = hashUInt((pixel.y * extent.x + pixel.x) ^ hashUInt(uint(ssgi.extentAndFrame.z)));
    float3 sum = float3(0.0, 0.0, 0.0);
    for (uint ray = 0; ray < RayCount; ray++)
        sum += traceSample(position, normal, radius, skyDepthThreshold, noiseState);

    giOutput[pixel] = float4(sum / float(RayCount), depth);
}
