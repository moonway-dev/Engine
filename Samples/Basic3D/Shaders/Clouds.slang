struct CloudsPushConstants
{
    float4x4 inverseViewProjection;
    float3 cameraPosition;
    float padding0;
    float3 sunDirection;
    float jitterSeed;
    float2 extent;
    float coverage;
    float density;
    float2 windOffset;
    float cloudBottom;
    float cloudTop;
};
[[vk::push_constant]] CloudsPushConstants pushConstants;

[[vk::binding(0, 0)]] Texture3D<float4> shapeNoise;
[[vk::binding(1, 0)]] Texture3D<float4> detailNoise;
[[vk::binding(2, 0)]] Texture2D<float4> weatherMap;
[[vk::binding(3, 0)]] SamplerState linearSampler;
[[vk::binding(4, 0)]] Texture2D<float> depthTexture;

static const float PlanetRadius = 6360.0;
static const float AtmosphereRadius = 6420.0;
static const float3 RayleighScattering = float3(5.802e-3, 13.558e-3, 33.1e-3);
static const float3 OzoneAbsorption = float3(1.625e-3, 4.7025e-3, 0.2125e-3);
static const float MieTotal = 3.996e-3 + 4.4e-3;
static const float SunIntensity = 22.0;
static const float SkyScale = 3.0;
static const float ShapeFrequency = 0.045;
static const float DetailFrequency = 1.8;
static const float WeatherFrequency = 1.0 / 60.0;
static const float Extinction = 1.2;
static const int ViewSteps = 96;
static const int LightSteps = 5;

struct CloudsVertexOutput
{
    float4 position : SV_Position;
};

[shader("vertex")]
CloudsVertexOutput vsMain(uint vertexId : SV_VertexID)
{
    CloudsVertexOutput output;
    float2 uv = float2((vertexId << 1) & 2, vertexId & 2);
    output.position = float4(uv * 2.0 - 1.0, 1.0, 1.0);
    return output;
}

float remap(float value, float sourceMin, float sourceMax, float targetMin, float targetMax)
{
    return targetMin + saturate((value - sourceMin) / max(sourceMax - sourceMin, 1e-5)) * (targetMax - targetMin);
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

float sunVisibility(float3 samplePosition, float3 sunDirection)
{
    float radius = length(samplePosition);
    float sunCosine = dot(samplePosition / max(radius, 1e-4), sunDirection);
    float horizonCosine = radius > PlanetRadius
        ? -sqrt(max(1.0 - (PlanetRadius * PlanetRadius) / (radius * radius), 0.0))
        : -1.0;
    return smoothstep(horizonCosine - 0.035, horizonCosine + 0.12, sunCosine);
}

float3 sunTransmittance(float3 samplePosition, float3 sunDirection)
{
    float2 hit = raySphere(samplePosition, sunDirection, AtmosphereRadius);
    float distance = max(hit.y, 0.0);
    float3 rayleigh = 0.0;
    float mie = 0.0;
    float3 ozone = 0.0;
    const int steps = 4;
    float stepSize = distance / steps;
    for (int i = 0; i < steps; i++)
    {
        float3 samplePoint = samplePosition + sunDirection * ((i + 0.5) * stepSize);
        float altitude = length(samplePoint) - PlanetRadius;
        rayleigh += exp(-altitude / 8.0) * stepSize;
        mie += exp(-altitude / 1.2) * stepSize;
        ozone += saturate(1.0 - abs(altitude - 25.0) / 15.0) * stepSize;
    }
    return exp(-(RayleighScattering * rayleigh + MieTotal * mie + OzoneAbsorption * ozone));
}

float hgPhase(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * 3.14159265 * pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
}

float cloudPhase(float cosTheta)
{
    return lerp(hgPhase(cosTheta, -0.18), hgPhase(cosTheta, 0.72), 0.62) * 3.14159265;
}

float2 weatherCoordinates(float3 position)
{
    return position.xz * WeatherFrequency
        + float2(position.y * 0.0006, -position.y * 0.0004)
        + pushConstants.windOffset * 0.02;
}

float sampleDensity(float3 position, out float heightFraction)
{
    float altitude = length(position) - PlanetRadius;
    heightFraction = saturate((altitude - pushConstants.cloudBottom)
        / max(pushConstants.cloudTop - pushConstants.cloudBottom, 0.001));

    float2 weather = weatherMap.SampleLevel(linearSampler, weatherCoordinates(position), 0).rg;
    float heightGradient = smoothstep(0.0, 0.3, heightFraction) * smoothstep(1.0, 0.55, heightFraction);
    float4 shape = shapeNoise.SampleLevel(linearSampler, position * ShapeFrequency + float3(pushConstants.windOffset * 0.05, 0.0), 0);
    float shapeFbm = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
    float localCoverage = saturate(remap(weather.r, 1.0 - pushConstants.coverage, 1.0 - pushConstants.coverage * 0.4, 0.0, 1.0));
    if (localCoverage <= 0.004)
        return 0.0;
    float boundary = 1.0 - localCoverage;
    float baseDensity = saturate(remap(shape.r, boundary, boundary + 0.07, 0.0, 1.0)) * heightGradient;
    baseDensity *= saturate(remap(shapeFbm, boundary * 0.8, boundary * 0.8 + 0.3, 0.2, 1.0));
    if (baseDensity <= 0.001)
        return 0.0;

    float3 detail = detailNoise.SampleLevel(linearSampler, position * DetailFrequency + float3(pushConstants.windOffset * 0.11, 0.0), 0).rgb;
    float3 detailFine = detailNoise.SampleLevel(linearSampler, position * (DetailFrequency * 2.7) + float3(pushConstants.windOffset * 0.17, 0.0), 0).rgb;
    float detailErosion = lerp(detail.r, 1.0 - detail.g, saturate(heightFraction * 1.5));
    float fineErosion = lerp(detailFine.r, 1.0 - detailFine.b, saturate(heightFraction * 1.8));
    float erosion = detailErosion * 0.42 + fineErosion * 0.26;
    float density = remap(baseDensity, erosion * (1.0 - 0.45 * heightFraction), 1.0, 0.0, 1.0);
    return saturate(density) * pushConstants.density * 0.85;
}

float3 integrateClouds(
    float3 origin,
    float3 rayDirection,
    float tStart,
    float tEnd,
    float3 sunDirection,
    float2 pixel,
    float jitterSeed,
    out float transmittance)
{
    float stepSize = (tEnd - tStart) / ViewSteps;
    float jitter = frac(sin(dot(pixel, float2(12.9898, 78.233))) * 43758.5453 + jitterSeed);
    float t = tStart + stepSize * jitter;

    transmittance = 1.0;
    float3 scattered = float3(0.0, 0.0, 0.0);
    float cosTheta = dot(rayDirection, sunDirection);
    float phase = cloudPhase(cosTheta);

    for (int i = 0; i < ViewSteps; i++)
    {
        float3 position = origin + rayDirection * (t + 0.5 * stepSize);
        t += stepSize;

        float distanceFade = saturate(1.0 - (t - tStart) / 28.0 * 1.33);
        if (distanceFade <= 0.002)
            break;

        float heightFraction;
        float density = sampleDensity(position, heightFraction) * distanceFade;
        if (density > 0.004)
        {
            float lightOpticalDepth = 0.0;
            float lightStepSize = (pushConstants.cloudTop - pushConstants.cloudBottom) / LightSteps;
            for (int j = 0; j < LightSteps; j++)
            {
                float3 lightPosition = position + sunDirection * ((j + 0.6) * lightStepSize);
                float lightHeightFraction;
                lightOpticalDepth += sampleDensity(lightPosition, lightHeightFraction) * lightStepSize;
            }
            lightOpticalDepth *= Extinction;

            float3 sunLight = sunTransmittance(position, sunDirection) * SunIntensity;
            float visibility = sunVisibility(position, sunDirection);
            float multipleScatter = 0.0;
            float attenuation = 1.0;
            float extinctionScale = 1.0;
            for (int octave = 0; octave < 3; octave++)
            {
                multipleScatter += attenuation * exp(-lightOpticalDepth * extinctionScale)
                    * hgPhase(cosTheta, 0.72 - 0.18 * octave);
                attenuation *= 0.42;
                extinctionScale *= 0.32;
            }
            multipleScatter *= 3.14159265 * 0.22;
            float powder = 1.0 - exp(-density * 14.0);
            float3 sunTerm = sunLight * visibility * (multipleScatter * phase + powder * 0.28)
                * saturate(heightFraction * 0.8 + 0.2);

            float dayFactor = smoothstep(-0.15, 0.2, sunDirection.y);
            float3 skyTop = float3(0.38, 0.5, 0.72) * SunIntensity * 0.045;
            float3 skyBottom = float3(0.18, 0.22, 0.34) * SunIntensity * 0.045;
            float3 ambient = lerp(skyBottom, skyTop, heightFraction) * (0.12 + 0.88 * dayFactor);

            float extinction = density * Extinction;
            scattered += transmittance * (sunTerm + ambient) * density * Extinction * stepSize;
            transmittance *= exp(-extinction * stepSize);
            if (transmittance < 0.015)
                break;
        }
    }
    return scattered * SkyScale;
}

[shader("fragment")]
float4 psMain(CloudsVertexOutput input) : SV_Target
{
    float2 ndc = (input.position.xy / pushConstants.extent) * 2.0 - 1.0;
    float4 farPoint = mul(pushConstants.inverseViewProjection, float4(ndc, 1.0, 1.0));
    float3 rayDirection = normalize(farPoint.xyz / farPoint.w - pushConstants.cameraPosition);

    float cameraHeight = max(pushConstants.cameraPosition.y, -10.0) * 0.001;
    float3 origin = float3(0.0, PlanetRadius + cameraHeight, 0.0);
    float3 sunDirection = normalize(pushConstants.sunDirection);

    float layerBottom = PlanetRadius + pushConstants.cloudBottom;
    float layerTop = PlanetRadius + pushConstants.cloudTop;
    float2 outerHit = raySphere(origin, rayDirection, layerTop);
    float2 innerHit = raySphere(origin, rayDirection, layerBottom);
    float cameraRadius = length(origin);

    float tStart = max(outerHit.x, 0.0);
    float tEnd = outerHit.y;
    if (innerHit.x > 0.0 && innerHit.x < tEnd)
        tEnd = innerHit.x;
    if (cameraRadius >= layerBottom && cameraRadius <= layerTop)
        tStart = 0.0;
    if (tEnd <= tStart)
        return float4(0.0, 0.0, 0.0, 0.0);

    float2 atmosphereHit = raySphere(origin, rayDirection, AtmosphereRadius);
    tEnd = min(tEnd, max(atmosphereHit.y, tStart));
    if (tEnd <= tStart)
        return float4(0.0, 0.0, 0.0, 0.0);

    float2 planetHit = raySphere(origin, rayDirection, PlanetRadius);
    if (planetHit.x > 0.0 && planetHit.x < tEnd)
        tEnd = planetHit.x;
    tEnd = min(tEnd, tStart + 28.0);
    if (tEnd <= tStart)
        return float4(0.0, 0.0, 0.0, 0.0);

    float sceneDepth = depthTexture.SampleLevel(linearSampler, input.position.xy / pushConstants.extent, 0);
    if (sceneDepth < 0.99999)
        return float4(0.0, 0.0, 0.0, 0.0);

    float transmittance;
    float3 color = integrateClouds(origin, rayDirection, tStart, tEnd, sunDirection, input.position.xy, pushConstants.jitterSeed, transmittance);
    return float4(color, saturate(1.0 - transmittance));
}
