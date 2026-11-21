struct SkyPushConstants
{
    float4x4 inverseViewProjection;
    float3 cameraPosition;
    float padding0;
    float3 sunDirection;
    float padding1;
    float2 extent;
    float sunDiscIntensity;
    float padding2;
};
[[vk::push_constant]] SkyPushConstants pushConstants;

static const float PlanetRadius = 6360.0;
static const float AtmosphereRadius = 6420.0;
static const float3 RayleighScattering = float3(5.802e-3, 13.558e-3, 33.1e-3);
static const float3 MieScattering = float3(3.996e-3, 3.996e-3, 3.996e-3);
static const float3 MieAbsorption = float3(4.4e-3, 4.4e-3, 4.4e-3);
static const float3 OzoneAbsorption = float3(1.625e-3, 4.7025e-3, 0.2125e-3);
static const float3 MultipleScatteringBias = float3(0.25, 0.45, 0.90);
static const float RayleighScaleHeight = 8.0;
static const float MieScaleHeight = 1.2;
static const float MieAsymmetry = 0.76;
static const float SunAngularRadius = 0.025;
static const float SunEdgeSoftness = 0.004;
static const float SunIntensity = 22.0;
static const float SkyScale = 3.0;
static const float DiscIntensityMultiplier = 14.0;
static const float HorizonBlendStart = 0.012;
static const float HorizonBlendEnd = 0.09;

struct SkyVertexOutput
{
    float4 position : SV_Position;
};

[shader("vertex")]
SkyVertexOutput vsMain(uint vertexId : SV_VertexID)
{
    SkyVertexOutput output;
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    output.position = float4(uv * 2.0 - 1.0, 1.0, 1.0);
    return output;
}

float ozoneDensity(float altitude)
{
    return saturate(1.0 - abs(altitude - 25.0) / 15.0);
}

float2 raySphere(float3 origin, float3 direction, float radius)
{
    float projection = dot(origin, direction);
    float discriminant = projection * projection - (dot(origin, origin) - radius * radius);
    if (discriminant < 0.0)
        return float2(1e9, -1e9);
    float sqrtDiscriminant = sqrt(discriminant);
    return float2(-projection - sqrtDiscriminant, -projection + sqrtDiscriminant);
}

float3 opticalDepthToSun(float3 samplePosition, float3 sunDirection)
{
    float2 atmosphereHit = raySphere(samplePosition, sunDirection, AtmosphereRadius);
    float distance = max(atmosphereHit.y, 0.0);
    const int lightSteps = 4;
    float stepSize = distance / lightSteps;
    float3 opticalDepth = float3(0.0, 0.0, 0.0);
    for (int i = 0; i < lightSteps; i++)
    {
        float3 point = samplePosition + sunDirection * ((i + 0.5) * stepSize);
        float altitude = length(point) - PlanetRadius;
        opticalDepth += float3(
            exp(-altitude / RayleighScaleHeight),
            exp(-altitude / MieScaleHeight),
            ozoneDensity(altitude)) * stepSize;
    }
    return opticalDepth;
}

float sunVisibility(float3 samplePosition, float3 sunDirection)
{
    float radius = length(samplePosition);
    float sunCosine = dot(samplePosition / max(radius, 1e-4), sunDirection);
    float horizonCosine = radius > PlanetRadius
        ? -sqrt(max(1.0 - (PlanetRadius * PlanetRadius) / (radius * radius), 0.0))
        : -1.0;
    return smoothstep(horizonCosine - 0.035, horizonCosine + 0.12, sunCosine);
}

float rayleighPhase(float cosTheta)
{
    return 3.0 / (16.0 * 3.14159265) * (1.0 + cosTheta * cosTheta);
}

float miePhase(float cosTheta)
{
    float g = MieAsymmetry;
    float g2 = g * g;
    return (1.0 - g2) * (1.0 + cosTheta * cosTheta)
        / ((4.0 * 3.14159265) * (2.0 + g2) * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
}

void marchScatter(
    float3 origin,
    float3 rayDirection,
    float tStart,
    float tEnd,
    float3 sunDirection,
    out float3 viewOpticalDepth,
    out float3 scattered)
{
    const int viewSteps = 16;
    float stepSize = (tEnd - tStart) / viewSteps;
    viewOpticalDepth = float3(0.0, 0.0, 0.0);
    scattered = float3(0.0, 0.0, 0.0);
    float cosTheta = dot(rayDirection, sunDirection);
    float rayleighPhaseValue = rayleighPhase(cosTheta);
    float miePhaseValue = miePhase(cosTheta);
    for (int i = 0; i < viewSteps; i++)
    {
        float3 samplePosition = origin + rayDirection * (tStart + (i + 0.5) * stepSize);
        float altitude = length(samplePosition) - PlanetRadius;
        float3 stepOpticalDepth = float3(
            exp(-altitude / RayleighScaleHeight),
            exp(-altitude / MieScaleHeight),
            ozoneDensity(altitude)) * stepSize;
        viewOpticalDepth += stepOpticalDepth;
        float visibility = sunVisibility(samplePosition, sunDirection);
        if (visibility > 0.0)
        {
            float3 sunOpticalDepth = opticalDepthToSun(samplePosition, sunDirection);
            float3 transmittance = exp(-(
                RayleighScattering * (viewOpticalDepth.x + sunOpticalDepth.x)
                + (MieScattering + MieAbsorption) * (viewOpticalDepth.y + sunOpticalDepth.y)
                + OzoneAbsorption * (viewOpticalDepth.z + sunOpticalDepth.z)));
            scattered += transmittance * visibility
                * (rayleighPhaseValue * RayleighScattering * stepOpticalDepth.x
                    + miePhaseValue * MieScattering * stepOpticalDepth.y);
        }
    }
    scattered += scattered * MultipleScatteringBias;
}

float hash13(float3 p)
{
    p = fract(p * 0.3183099 + 0.1);
    p *= 17.0;
    return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float3 hash33(float3 p)
{
    float n = hash13(p);
    return fract(float3(n * 137.31, n * 269.73, n * 113.47));
}

[shader("fragment")]
float4 psMain(SkyVertexOutput input) : SV_Target
{
    float2 ndc = (input.position.xy / pushConstants.extent) * 2.0 - 1.0;
    float4 farPoint = mul(pushConstants.inverseViewProjection, float4(ndc, 1.0, 1.0));
    float3 rayDirection = normalize(farPoint.xyz / farPoint.w - pushConstants.cameraPosition);

    float cameraHeight = max(pushConstants.cameraPosition.y, -10.0) * 0.001;
    float3 origin = float3(0.0, PlanetRadius + cameraHeight, 0.0);
    float3 sunDirection = normalize(pushConstants.sunDirection);

    float2 atmosphereHit = raySphere(origin, rayDirection, AtmosphereRadius);
    if (atmosphereHit.y < 0.0)
        return float4(0.0, 0.0, 0.0, 1.0);

    float tStart = max(atmosphereHit.x, 0.0);
    float tEnd = atmosphereHit.y;
    bool groundHit = false;
    float2 planetHit = raySphere(origin, rayDirection, PlanetRadius);
    if (planetHit.x > 0.0 && planetHit.x < tEnd)
    {
        tEnd = planetHit.x;
        groundHit = true;
    }

    float3 viewOpticalDepth;
    float3 scattered;
    marchScatter(origin, rayDirection, tStart, tEnd, sunDirection, viewOpticalDepth, scattered);

    float3 viewTransmittance = exp(-(
        RayleighScattering * viewOpticalDepth.x
        + (MieScattering + MieAbsorption) * viewOpticalDepth.y
        + OzoneAbsorption * viewOpticalDepth.z));

    float3 color;
    float nightFade = 1.0 - smoothstep(-0.12, 0.05, sunDirection.y);
    float horizonBoost = pow(1.0 - saturate(rayDirection.y), 3.0);
    float3 airglow = float3(0.0022, 0.0042, 0.0080)
        * (0.4 + 1.6 * horizonBoost) * (0.35 + 0.65 * nightFade);
    if (!groundHit)
    {
        color = scattered * SkyScale * SunIntensity + airglow;

        float sunCosine = dot(rayDirection, sunDirection);
        float discCosine = cos(SunAngularRadius);
        float edgeCosine = cos(SunAngularRadius + SunEdgeSoftness);
        float disc = smoothstep(edgeCosine, discCosine, sunCosine);
        if (disc > 0.0)
        {
            float angle = acos(clamp(sunCosine, -1.0, 1.0));
            float limbCosine = sqrt(max(1.0 - pow(angle / SunAngularRadius, 2.0), 0.0));
            float limbDarkening = 0.4 + 0.6 * limbCosine;
            color += viewTransmittance * (SunIntensity * DiscIntensityMultiplier * pushConstants.sunDiscIntensity)
                * disc * limbDarkening;
        }

        if (nightFade > 0.0)
        {
            float3 cellCoordinate = rayDirection * 240.0;
            float3 cell = floor(cellCoordinate);
            float3 cellFraction = fract(cellCoordinate) - 0.5;
            float cellHash = hash13(cell);
            if (cellHash > 0.92)
            {
                float3 jitter = (hash33(cell) - 0.5) * 0.72;
                float starDistance = length(cellFraction - jitter);
                float brightness = pow(hash13(cell + 17.7), 9.0) * 1.6 + 0.06;
                float3 tint = normalize(lerp(
                    float3(0.72, 0.82, 1.0),
                    float3(1.0, 0.86, 0.72),
                    hash13(cell + 5.3)));
                color += tint * (brightness * exp(-starDistance * starDistance * 42.0)) * nightFade;
            }
        }
    }
    else
    {
        float belowHorizon = -rayDirection.y;
        float horizonBlend = 1.0 - smoothstep(HorizonBlendStart, HorizonBlendEnd, belowHorizon);
        float3 hazeScattered = scattered;
        if (horizonBlend > 0.0)
        {
            float3 grazingDirection = normalize(float3(rayDirection.x, HorizonBlendStart, rayDirection.z));
            float2 grazingAtmosphere = raySphere(origin, grazingDirection, AtmosphereRadius);
            float3 grazingOpticalDepth;
            float3 grazingScattered;
            marchScatter(
                origin,
                grazingDirection,
                max(grazingAtmosphere.x, 0.0),
                grazingAtmosphere.y,
                sunDirection,
                grazingOpticalDepth,
                grazingScattered);
            hazeScattered = lerp(scattered, grazingScattered, horizonBlend);
        }

        float3 groundPoint = origin + rayDirection * planetHit.x;
        float3 sunOpticalDepth = opticalDepthToSun(groundPoint, sunDirection);
        float3 sunTransmittance = exp(-(
            RayleighScattering * sunOpticalDepth.x
            + (MieScattering + MieAbsorption) * sunOpticalDepth.y
            + OzoneAbsorption * sunOpticalDepth.z));
        float3 groundAlbedo = float3(0.028, 0.028, 0.029);
        float3 groundColor = groundAlbedo * SunIntensity * sunTransmittance
            * saturate(sunDirection.y * 1.2 + 0.02);
        groundColor += groundAlbedo * SunIntensity * 0.015 * saturate(sunDirection.y + 0.1);
        float3 groundTerm = groundColor * viewTransmittance + airglow * horizonBlend;
        float3 hazeTerm = hazeScattered * SkyScale * SunIntensity + airglow;
        color = lerp(groundTerm, hazeTerm, horizonBlend);
    }

    return float4(color, 1.0);
}
