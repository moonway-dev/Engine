import Common;

struct RtViewData
{
    float4x4 viewProjection;
    float4x4 inverseViewProjection;
    float4x4 previousViewProjection;
    float4 cameraPositionAndMaxDistance;
    float4 extentAndFrame;
    float4 lightDirectionAndAngularRadius;
    float4 settings;
    float4 pointLightPositionAndRange;
    float4 shadowQuality;
    float4 reflectionQuality;
    float4 giQuality;
};

struct RayTracingVertexData
{
    float4 position;
    float4 normal;
    float4 tangent;
    float4 uv;
};

struct RayTracingSurfaceData
{
    float4x4 model;
    float4x4 inverseModel;
    uint vertexOffset;
    uint indexOffset;
    uint textureOffset;
    uint padding;
    MaterialData material;
    float2 padding1;
    float2 padding2;
};

#ifndef RT_MATERIAL_TEXTURE_COUNT
#define RT_MATERIAL_TEXTURE_COUNT 96
#endif
static const uint RtMaterialTextureCount = RT_MATERIAL_TEXTURE_COUNT;
static const uint RtSurfaceRayMask = 1u;
static const uint RtShadowRayMask = 2u;
static const uint RtMaximumSamples = 8;
static const float RtDistanceOffsetScale = 0.0025f;

uint rtHash(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    return value ^ (value >> 16);
}

float rtRandom(inout uint state)
{
    state = state * 747796405u + 2891336453u;
    return float(state >> 8) * (1.0 / 16777216.0);
}

float3 rtSafeNormal(float3 value, float3 fallback)
{
    return dot(value, value) > 1e-12 ? normalize(value) : fallback;
}

void rtBasis(float3 normal, out float3 tangent, out float3 bitangent)
{
    float3 axis = abs(normal.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    tangent = normalize(cross(axis, normal));
    bitangent = cross(normal, tangent);
}

float3 rtWorldPosition(float2 uv, float depth, RtViewData view)
{
    float4 world = mul(view.inverseViewProjection, float4(uv * 2.0 - 1.0, depth, 1.0));
    return world.xyz / world.w;
}

float rtSurfaceOffset(float3 position, float3 normal, RtViewData view)
{
    // Self-intersection of grazing rays on large triangles grows with the
    // distance to the camera, so scale the origin offset with both.
    float3 toPosition = position - view.cameraPositionAndMaxDistance.xyz;
    float distance = length(toPosition);
    float3 viewDirection = rtSafeNormal(toPosition, normal);
    float grazing = 1.0 - clamp(abs(dot(normal, viewDirection)), 0.0, 1.0);
    return view.settings.y + distance * RtDistanceOffsetScale * grazing;
}

uint rtSampleCount(RtViewData view, uint effect)
{
    float4 quality = effect == 0u ? view.shadowQuality : effect == 1u ? view.reflectionQuality : view.giQuality;
    return uint(clamp(quality.x, 1.0, float(RtMaximumSamples)));
}

uint rtTraceRate(RtViewData view, uint effect, float3 position)
{
    return 1u;
}

bool rtTracePixel(uint2 pixel, uint rate, uint effect)
{
    uint phase = rtHash(effect * 0x9e3779b9u);
    uint2 offset = uint2(phase & (rate - 1u), (phase >> 8) & (rate - 1u));
    return all((pixel & (rate - 1u)) == offset);
}

float2 rtSequence(uint2 pixel, uint frame, uint sample)
{
    uint seed = rtHash(pixel.x ^ rtHash(pixel.y));
    float2 rotation = float2(float(seed >> 8), float(rtHash(seed) >> 8)) / 16777216.0;
    uint index = (frame & 1023u) * RtMaximumSamples + sample;
    return frac(rotation + float2(0.754877666, 0.569840296) * float(index + 1u));
}

float3 rtCosineDirection(float3 normal, float2 random)
{
    float3 tangent;
    float3 bitangent;
    rtBasis(normal, tangent, bitangent);
    float radius = sqrt(random.x);
    float angle = 6.2831853 * random.y;
    return tangent * (radius * cos(angle)) + bitangent * (radius * sin(angle)) + normal * sqrt(1.0 - random.x);
}

float3 rtConeDirection(float3 direction, float angularRadius, float2 random)
{
    float3 tangent;
    float3 bitangent;
    rtBasis(direction, tangent, bitangent);
    float cosTheta = lerp(1.0, cos(clamp(angularRadius, 0.0, 1.5707963)), random.x);
    float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
    float phi = 6.2831853 * random.y;
    return normalize(direction * cosTheta + (tangent * cos(phi) + bitangent * sin(phi)) * sinTheta);
}

float3 rtReflectionDirection(float3 normal, float3 viewDirection, float roughness, float2 random)
{
    float3 tangent;
    float3 bitangent;
    rtBasis(normal, tangent, bitangent);
    float3 localView = float3(dot(viewDirection, tangent), dot(viewDirection, bitangent), max(dot(viewDirection, normal), 1e-4));
    float alpha = max(roughness * roughness, 0.002);
    float3 stretchedView = normalize(float3(alpha * localView.xy, localView.z));
    float lengthSquared = dot(stretchedView.xy, stretchedView.xy);
    float3 basis1 = lengthSquared > 1e-8 ? float3(-stretchedView.y, stretchedView.x, 0.0) / sqrt(lengthSquared) : float3(1.0, 0.0, 0.0);
    float3 basis2 = cross(stretchedView, basis1);
    float radius = sqrt(random.x);
    float phi = 6.2831853 * random.y;
    float diskX = radius * cos(phi);
    float diskY = radius * sin(phi);
    float blend = 0.5 * (1.0 + stretchedView.z);
    diskY = lerp(sqrt(saturate(1.0 - diskX * diskX)), diskY, blend);
    float3 hemisphereNormal = diskX * basis1 + diskY * basis2 + sqrt(saturate(1.0 - diskX * diskX - diskY * diskY)) * stretchedView;
    float3 localHalf = normalize(float3(alpha * hemisphereNormal.xy, max(hemisphereNormal.z, 0.0)));
    float3 halfVector = tangent * localHalf.x + bitangent * localHalf.y + normal * localHalf.z;
    float3 direction = reflect(-viewDirection, halfVector);
    return dot(direction, normal) > 1e-4 ? normalize(direction) : normalize(reflect(-viewDirection, normal));
}
