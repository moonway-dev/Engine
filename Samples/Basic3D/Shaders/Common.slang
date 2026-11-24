struct CameraData
{
    float4x4 viewProjection;
    float4x4 previousViewProjection;
    float3 cameraPosition;
    float padding;
    float2 jitter;
    float2 previousJitter;
};

struct ShadowData
{
    float4x4 lightViewProjections[4];
    float4 atlasTiles[4];
    float4 cascadeMetrics[4];
    float4 splitDepths;
    float4 cameraPositionAndNear;
    float4 cameraForwardAndDistance;
    float4 lightDirectionAndStrength;
    float4 filterParameters;
    float4 biasParameters;
    int lightIndex;
    uint quality;
    uint debugCascades;
    float padding0;
};

struct MaterialData
{
    float3 albedoColor;
    float metallic;
    float roughness;
    float emissiveIntensity;
    float2 uvScale;
    float normalScale;
    float aoStrength;
    float2 uvOffset;
    float3 emissionColor;
    uint legacyEmission;
    uint metallicChannel;
    uint roughnessChannel;
    uint ambientOcclusionChannel;
    float opacity;
    uint transparent;
    float padding0;
};

struct LightInput
{
    float3 directionOrPosition;
    float type;
    float3 color;
    float intensity;
    float range;
    float3 padding;
};

struct LightsInput
{
    LightInput lights[8];
    uint count;
    float padding0;
    float padding1;
    float padding2;
};

struct ModelConstants
{
    float4x4 model;
    float4x4 previousModel;
};

struct TonemapInput
{
    float exposure;
    float3 padding;
};

struct VertexInput
{
    float3 position : POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD;
    float4 tangent : TANGENT;
};

struct SurfaceVertexOutput
{
    float4 position : SV_Position;
    float3 worldPosition : WORLD_POSITION;
    float3 normal : NORMAL;
    float2 uv : TEXCOORD;
};

float3 decodeNormalMap(float3 encodedNormal, float normalScale)
{
    float3 normal = encodedNormal * 2.0 - 1.0;
    normal.xy *= normalScale;
    return normalize(normal);
}

float3 fresnelSchlick(float cosTheta, float3 F0)
{
    return F0 + (1.0 - F0) * pow(saturate(1.0 - cosTheta), 5.0);
}

float distributionGGX(float3 normal, float3 halfVector, float roughness)
{
    float a = roughness * roughness;
    float a2 = a * a;
    float nDotH = saturate(dot(normal, halfVector));
    float d = nDotH * nDotH * (a2 - 1.0) + 1.0;
    return a2 / (3.14159265 * d * d + 1e-7);
}

float geometrySmith(float nDotV, float nDotL, float roughness)
{
    float r = roughness + 1.0;
    float k = r * r / 8.0;
    float ggxV = nDotV / (nDotV * (1.0 - k) + k);
    float ggxL = nDotL / (nDotL * (1.0 - k) + k);
    return ggxV * ggxL;
}

void computePointLightRadiance(LightInput light, float3 worldPosition, out float3 lightDirection, out float attenuation)
{
    float3 delta = light.directionOrPosition - worldPosition;
    float distance = length(delta);
    lightDirection = delta / max(distance, 1e-4);
    float normalizedDistance = distance / max(light.range, 1e-4);
    float rangeFactor = saturate(1.0 - pow(normalizedDistance, 4.0));
    attenuation = rangeFactor * rangeFactor / max(distance * distance, 1.0);
}

float3 applyLighting(
    float3 worldPosition,
    float3 normal,
    float3 viewDirection,
    float3 albedo,
    float metallic,
    float roughness,
    float3 fresnelBase,
    LightsInput lights,
    int shadowLightIndex,
    float shadowVisibility,
    int pointShadowLightIndex,
    float pointShadowVisibility)
{
    float3 result = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < int(lights.count); i++)
    {
        LightInput light = lights.lights[i];
        float3 lightDirection;
        float attenuation = 1.0;
        if (light.type < 0.5)
        {
            lightDirection = -normalize(light.directionOrPosition);
        }
        else
        {
            computePointLightRadiance(light, worldPosition, lightDirection, attenuation);
        }

        float3 halfVector = normalize(viewDirection + lightDirection);
        float nDotL = saturate(dot(normal, lightDirection));
        float nDotV = saturate(dot(normal, viewDirection));
        float nDotH = saturate(dot(normal, halfVector));
        float vDotH = saturate(dot(viewDirection, halfVector));

        float distribution = distributionGGX(normal, halfVector, roughness);
        float geometry = geometrySmith(nDotV, nDotL, roughness);
        float3 fresnel = fresnelSchlick(vDotH, fresnelBase);

        float3 specular = distribution * geometry * fresnel / max(4.0 * nDotV * nDotL, 1e-4);
        float3 diffuse = (1.0 - fresnel) * (1.0 - metallic) * albedo / 3.14159265;

        float3 radiance = light.color * light.intensity * attenuation;
        float visibility = i == shadowLightIndex ? shadowVisibility
            : i == pointShadowLightIndex ? pointShadowVisibility
            : 1.0;
        result += (diffuse + specular) * radiance * nDotL * visibility;
    }
    return result;
}

float3 acesTonemap(float3 color)
{
    float a = 2.51;
    float b = 0.03;
    float c = 2.43;
    float d = 0.59;
    float e = 0.14;
    return saturate((color * (a * color + b)) / (color * (c * color + d) + e));
}
