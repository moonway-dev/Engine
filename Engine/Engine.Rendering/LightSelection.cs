using Engine.Mathematics;

namespace Engine.Rendering;

public static class LightSelection
{
    public static IReadOnlyList<Light> SelectImportant(
        IReadOnlyList<Light> lights,
        IReadOnlyList<Vector3> samplePositions,
        Vector3 fallbackPosition,
        int maximumCount)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(samplePositions);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
        if (maximumCount == 0 || lights.Count == 0)
            return [];

        List<(Light Light, int SourceIndex, float Score)> ranked = [];
        for (int index = 0; index < lights.Count; index++)
        {
            Light light = lights[index];
            float score = EstimateContribution(light, samplePositions, fallbackPosition);
            if (score > 0f && float.IsFinite(score))
                ranked.Add((light, index, score));
        }

        return ranked
            .OrderByDescending(entry => entry.Score)
            .ThenBy(entry => entry.SourceIndex)
            .Take(maximumCount)
            .Select(entry => entry.Light)
            .ToArray();
    }

    public static int FindShadowLightIndex(IReadOnlyList<Light> activeLights)
    {
        ArgumentNullException.ThrowIfNull(activeLights);
        for (int index = 0; index < activeLights.Count; index++)
        {
            if (activeLights[index].Type == LightType.Directional)
                return index;
        }
        return -1;
    }

    public static float PointAttenuation(float distance, float range)
    {
        if (!float.IsFinite(distance) || !float.IsFinite(range) || range <= 0f)
            return 0f;

        distance = MathF.Max(distance, 0f);
        float normalizedDistance = distance / range;
        float rangeFade = Math.Clamp(1f - MathF.Pow(normalizedDistance, 4f), 0f, 1f);
        return rangeFade * rangeFade / MathF.Max(distance * distance, 1f);
    }

    public static Matrix4x4 CreateDirectionalShadowMatrix(Camera camera, Light light, int shadowMapSize, float maximumDistance = 40f) =>
        CreateDirectionalShadowMatrix([camera], light, shadowMapSize, maximumDistance);

    public static Matrix4x4 CreateDirectionalShadowMatrix(
        IReadOnlyList<Camera> cameras,
        Light light,
        int shadowMapSize,
        float maximumDistance = 40f)
    {
        ArgumentNullException.ThrowIfNull(cameras);
        ArgumentNullException.ThrowIfNull(light);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(shadowMapSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDistance);
        if (cameras.Count == 0)
            return Matrix4x4.Identity;

        Vector3 direction = light.DirectionOrPosition.Normalized;
        if (direction.LengthSquared < 1e-8f)
            return Matrix4x4.Identity;

        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up;
        Matrix4x4 lightRotation = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);
        Vector3 minimum = new(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        Vector3 maximum = new(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity);
        foreach (Camera camera in cameras)
        {
            ArgumentNullException.ThrowIfNull(camera);
            float nearDistance = MathF.Max(camera.NearPlane, 0.01f);
            float farDistance = MathF.Max(nearDistance + 0.01f, MathF.Min(camera.FarPlane, maximumDistance));
            float tangent = MathF.Tan(camera.FieldOfViewY * 0.5f);
            foreach (float distance in new[] { nearDistance, farDistance })
            {
                float halfHeight = tangent * distance;
                float halfWidth = halfHeight * camera.AspectRatio;
                Vector3 planeCenter = camera.Position + camera.Forward * distance;
                foreach (float x in new[] { -1f, 1f })
                foreach (float y in new[] { -1f, 1f })
                {
                    Vector3 corner = planeCenter + camera.Right * (x * halfWidth) + camera.Up * (y * halfHeight);
                    Vector3 lightSpace = Vector3.TransformPoint(corner, lightRotation);
                    minimum = Vector3.Min(minimum, lightSpace);
                    maximum = Vector3.Max(maximum, lightSpace);
                }
            }
        }

        float centerX = (minimum.X + maximum.X) * 0.5f;
        float centerY = (minimum.Y + maximum.Y) * 0.5f;
        float centerZ = (minimum.Z + maximum.Z) * 0.5f;
        float extent = MathF.Max(12f, MathF.Max(maximum.X - minimum.X, maximum.Y - minimum.Y) * 0.5f);
        extent += extent * 2f / shadowMapSize;
        float texelSize = extent * 2f / shadowMapSize;
        centerX = MathF.Floor(centerX / texelSize + 0.5f) * texelSize;
        centerY = MathF.Floor(centerY / texelSize + 0.5f) * texelSize;

        Matrix4x4 view = lightRotation;
        view.M14 = -centerX;
        view.M24 = -centerY;
        float depthPadding = 5f;
        float depthRange = maximum.Z - minimum.Z;
        view.M34 = -maximum.Z - depthPadding;
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(
            extent * 2f,
            extent * 2f,
            depthPadding,
            depthRange + depthPadding);
        projection.M22 = -projection.M22;
        return projection * view;
    }

    private static float EstimateContribution(Light light, IReadOnlyList<Vector3> samplePositions, Vector3 fallbackPosition)
    {
        if (!float.IsFinite(light.Intensity) || light.Intensity <= 0f || !IsFinite(light.Color))
            return 0f;

        float luminance = MathF.Max(0f,
            0.2126f * light.Color.X + 0.7152f * light.Color.Y + 0.0722f * light.Color.Z);
        if (luminance <= 0f)
            return 0f;

        if (light.Type == LightType.Directional)
            return light.DirectionOrPosition.LengthSquared > 1e-8f && IsFinite(light.DirectionOrPosition)
                ? luminance * light.Intensity
                : 0f;
        if (light.Type != LightType.Point || !IsFinite(light.DirectionOrPosition) || !float.IsFinite(light.Range) || light.Range <= 0f)
            return 0f;

        float attenuation = samplePositions.Count == 0
            ? PointAttenuation(Vector3.Distance(light.DirectionOrPosition, fallbackPosition), light.Range)
            : samplePositions.Max(position => IsFinite(position)
                ? PointAttenuation(Vector3.Distance(light.DirectionOrPosition, position), light.Range)
                : 0f);
        return luminance * light.Intensity * attenuation;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
