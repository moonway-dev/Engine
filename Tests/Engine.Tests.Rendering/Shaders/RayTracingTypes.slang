struct RadiancePayload
{
    float3 color;
    uint depth;
};

struct ShadowPayload
{
    uint occluded;
};

struct SphereAttributes
{
    float3 normal;
};

struct MaterialCallData
{
    float3 color;
    float roughness;
};

struct RayConstants
{
    uint2 extent;
    uint frameIndex;
    float exposure;
};
