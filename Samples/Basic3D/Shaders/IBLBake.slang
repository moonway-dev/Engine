[[vk::binding(0, 0)]] TextureCube<float4> environmentMap;
[[vk::binding(1, 0)]] SamplerState environmentSampler;
[[vk::binding(2, 0)]] RWTexture2DArray<float4> cubeOutput;
[[vk::binding(3, 0)]] RWTexture2D<float4> brdfOutput;

struct BakeConstants
{
    uint mode;
    uint face;
    uint extent;
    float roughness;
};

[[vk::push_constant]] BakeConstants constants;

uint bakeSampleCount()
{
    return constants.mode == 2 ? 256u : 128u;
}

static const float PI = 3.14159265359;

float3 faceDirection(uint face, float2 uv)
{
    float2 p = uv * 2.0 - 1.0;
    float3 direction = face == 0 ? float3(1.0, -p.y, -p.x) :
        face == 1 ? float3(-1.0, -p.y, p.x) :
        face == 2 ? float3(p.x, 1.0, p.y) :
        face == 3 ? float3(p.x, -1.0, -p.y) :
        face == 4 ? float3(p.x, -p.y, 1.0) : float3(-p.x, -p.y, -1.0);
    return normalize(direction);
}

float radicalInverse(uint bits)
{
    bits = (bits << 16) | (bits >> 16);
    bits = ((bits & 0x55555555) << 1) | ((bits & 0xAAAAAAAA) >> 1);
    bits = ((bits & 0x33333333) << 2) | ((bits & 0xCCCCCCCC) >> 2);
    bits = ((bits & 0x0F0F0F0F) << 4) | ((bits & 0xF0F0F0F0) >> 4);
    bits = ((bits & 0x00FF00FF) << 8) | ((bits & 0xFF00FF00) >> 8);
    return float(bits) * 2.3283064365386963e-10;
}

float3 importanceSampleGgx(uint index, uint sampleCount, float roughness, float3 normal)
{
    float2 xi = float2(float(index) / float(sampleCount), radicalInverse(index));
    float a = max(roughness * roughness, 0.001);
    float phi = 2.0 * PI * xi.x;
    float cosTheta = sqrt((1.0 - xi.y) / max(1.0 + (a * a - 1.0) * xi.y, 1e-5));
    float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
    float3 halfTangent = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
    float3 up = abs(normal.z) < 0.999 ? float3(0.0, 0.0, 1.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    return normalize(tangent * halfTangent.x + bitangent * halfTangent.y + normal * halfTangent.z);
}

float3 sampleIrradiance(float3 normal)
{
    float3 up = abs(normal.y) < 0.999 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    float3 tangent = normalize(cross(up, normal));
    float3 bitangent = cross(normal, tangent);
    float3 irradiance = 0.0;
    uint sampleCount = bakeSampleCount();
    for (uint i = 0; i < sampleCount; i++)
    {
        float2 xi = float2(float(i) / float(sampleCount), radicalInverse(i));
        float phi = 2.0 * PI * xi.x;
        float cosTheta = sqrt(1.0 - xi.y);
        float sinTheta = sqrt(xi.y);
        float3 localDirection = float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
        float3 direction = normalize(tangent * localDirection.x + bitangent * localDirection.y + normal * localDirection.z);
        irradiance += environmentMap.SampleLevel(environmentSampler, direction, 0).rgb;
    }
    return irradiance * (PI / float(sampleCount));
}

float2 integrateBrdf(float nDotV, float roughness)
{
    float3 viewDirection = float3(sqrt(saturate(1.0 - nDotV * nDotV)), 0.0, nDotV);
    float2 result = 0.0;
    uint sampleCount = bakeSampleCount();
    for (uint i = 0; i < sampleCount; i++)
    {
        float3 halfVector = importanceSampleGgx(i, sampleCount, roughness, float3(0.0, 0.0, 1.0));
        float3 lightDirection = normalize(2.0 * dot(viewDirection, halfVector) * halfVector - viewDirection);
        float nDotL = saturate(lightDirection.z);
        float nDotH = saturate(halfVector.z);
        float vDotH = saturate(dot(viewDirection, halfVector));
        if (nDotL <= 0.0)
            continue;
        float k = roughness * roughness * 0.5;
        float visibilityV = nDotV / max(nDotV * (1.0 - k) + k, 1e-4);
        float visibilityL = nDotL / max(nDotL * (1.0 - k) + k, 1e-4);
        float visibility = visibilityV * visibilityL * vDotH / max(nDotH * nDotV, 1e-4);
        float fresnel = pow(1.0 - vDotH, 5.0);
        result += float2((1.0 - fresnel) * visibility, fresnel * visibility);
    }
    return result / float(sampleCount);
}

[shader("compute")]
[numthreads(8, 8, 1)]
void csMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 pixel = dispatchThreadId.xy;
    if (any(pixel >= constants.extent))
        return;
    float2 uv = (float2(pixel) + 0.5) / float(constants.extent);
    if (constants.mode == 2)
    {
        float2 integrated = integrateBrdf(max(uv.x, 0.001), max(uv.y, 0.001));
        brdfOutput[pixel] = float4(integrated, 0.0, 1.0);
        return;
    }

    float3 normal = faceDirection(constants.face, uv);
    if (constants.mode == 0)
    {
        cubeOutput[uint3(pixel, 0)] = float4(sampleIrradiance(normal), 1.0);
        return;
    }

    float3 viewDirection = normal;
    float3 prefiltered = 0.0;
    float totalWeight = 0.0;
    uint sampleCount = bakeSampleCount();
    for (uint i = 0; i < sampleCount; i++)
    {
        float3 halfVector = importanceSampleGgx(i, sampleCount, constants.roughness, normal);
        float3 lightDirection = normalize(2.0 * dot(viewDirection, halfVector) * halfVector - viewDirection);
        float nDotL = saturate(dot(normal, lightDirection));
        if (nDotL > 0.0)
        {
            prefiltered += environmentMap.SampleLevel(environmentSampler, lightDirection, 0).rgb * nDotL;
            totalWeight += nDotL;
        }
    }
    cubeOutput[uint3(pixel, 0)] = float4(prefiltered / max(totalWeight, 1e-4), 1.0);
}
