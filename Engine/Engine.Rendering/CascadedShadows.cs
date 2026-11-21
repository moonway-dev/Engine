using Engine.Mathematics;

namespace Engine.Rendering;

public enum ShadowQuality
{
    Low,
    Medium,
    High,
    Soft,
}

public readonly record struct ShadowCasterBounds(Vector3 Minimum, Vector3 Maximum);

public sealed record CascadedShadowSettings
{
    public int Resolution { get; init; } = 1024;
    public float Distance { get; init; } = 80f;
    public float SplitLambda { get; init; } = 0.7f;
    public float BlendFraction { get; init; } = 0.1f;
    public float FadeFraction { get; init; } = 0.1f;
    public float Strength { get; init; } = 1f;
    public float ReceiverBias { get; init; } = 0.2f;
    public float NormalBias { get; init; } = 0.75f;
    public float AngularRadius { get; init; } = 0.00465f;
    public float FilterRadius { get; init; } = 6f;
    public ShadowQuality Quality { get; init; } = ShadowQuality.Medium;
    public bool DebugCascades { get; init; }

    public void Validate()
    {
        if (Resolution < 128 || Resolution > 2048 || (Resolution & (Resolution - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(Resolution));
        if (!float.IsFinite(Distance) || Distance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(Distance));
        ValidateRange(SplitLambda, 0f, 1f, nameof(SplitLambda));
        ValidateRange(BlendFraction, 0f, 0.25f, nameof(BlendFraction));
        ValidateRange(FadeFraction, 0f, 0.5f, nameof(FadeFraction));
        ValidateRange(Strength, 0f, 3f, nameof(Strength));
        ValidateRange(ReceiverBias, 0f, 8f, nameof(ReceiverBias));
        ValidateRange(NormalBias, 0f, 8f, nameof(NormalBias));
        ValidateRange(AngularRadius, 0f, 0.1f, nameof(AngularRadius));
        ValidateRange(FilterRadius, 1f, CascadedShadows.Gutter - 2f, nameof(FilterRadius));
        if (!Enum.IsDefined(Quality))
            throw new ArgumentOutOfRangeException(nameof(Quality));
    }

    private static void ValidateRange(float value, float minimum, float maximum, string name)
    {
        if (!float.IsFinite(value) || value < minimum || value > maximum)
            throw new ArgumentOutOfRangeException(name);
    }
}

public static class CascadedShadows
{
    public const int CascadeCount = 4;
    public const int Gutter = 10;

    public static int AtlasSize(int resolution) => checked((resolution + Gutter * 2) * 2);

    public static Rect Tile(int cascade, int resolution)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(cascade);
        if (cascade >= CascadeCount)
            throw new ArgumentOutOfRangeException(nameof(cascade));
        int tileSize = resolution + Gutter * 2;
        return new Rect((cascade % 2) * tileSize + Gutter, (cascade / 2) * tileSize + Gutter, resolution, resolution);
    }

    public static float[] SplitDepths(float near, float far, float lambda)
    {
        if (!float.IsFinite(near) || near <= 0f || !float.IsFinite(far) || far <= near)
            throw new ArgumentOutOfRangeException(nameof(far));
        if (!float.IsFinite(lambda) || lambda < 0f || lambda > 1f)
            throw new ArgumentOutOfRangeException(nameof(lambda));
        float[] splits = new float[CascadeCount];
        for (int index = 0; index < CascadeCount; index++)
        {
            float fraction = (index + 1f) / CascadeCount;
            float logarithmic = near * MathF.Pow(far / near, fraction);
            float uniform = near + (far - near) * fraction;
            splits[index] = lambda * logarithmic + (1f - lambda) * uniform;
        }
        splits[^1] = far;
        return splits;
    }

    public static ShadowData Build(Camera camera, Light? light, int lightIndex, CascadedShadowSettings settings, IReadOnlyList<ShadowCasterBounds> casters)
    {
        ArgumentNullException.ThrowIfNull(camera);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(casters);
        settings.Validate();
        if (!float.IsFinite(camera.NearPlane) || camera.NearPlane <= 0f || !float.IsFinite(camera.FarPlane) || camera.FarPlane <= camera.NearPlane
            || !float.IsFinite(camera.AspectRatio) || camera.AspectRatio <= 0f || !IsFinite(camera.Position))
            throw new ArgumentOutOfRangeException(nameof(camera));
        float near = camera.NearPlane;
        float far = MathF.Max(near + 0.01f, MathF.Min(camera.FarPlane, settings.Distance));
        float[] splits = SplitDepths(near, far, settings.SplitLambda);
        Vector3 direction = light?.DirectionOrPosition.Normalized ?? Vector3.Down;
        bool enabled = light is not null && lightIndex >= 0 && IsFinite(direction) && direction.LengthSquared > 1e-8f;
        if (!enabled)
            direction = Vector3.Down;
        ShadowData data = new()
        {
            SplitDepths = new Vector4(splits[0], splits[1], splits[2], splits[3]),
            CameraPositionAndNear = new Vector4(camera.Position, near),
            CameraForwardAndDistance = new Vector4(camera.Forward, far),
            LightDirectionAndStrength = new Vector4(direction, enabled ? settings.Strength : 0f),
            FilterParameters = new Vector4(settings.BlendFraction, settings.FadeFraction, settings.AngularRadius, settings.FilterRadius),
            BiasParameters = new Vector4(settings.ReceiverBias, settings.NormalBias, 1f / AtlasSize(settings.Resolution), settings.Resolution),
            LightIndex = enabled ? lightIndex : -1,
            Quality = (uint)settings.Quality,
            DebugCascades = settings.DebugCascades ? 1u : 0u,
        };
        Vector3 up = MathF.Abs(Vector3.Dot(direction, Vector3.Up)) > 0.98f ? Vector3.Right : Vector3.Up;
        Matrix4x4 lightRotation = Matrix4x4.CreateLookAt(Vector3.Zero, direction, up);
        for (int index = 0; index < CascadeCount; index++)
        {
            float nominalNear = index == 0 ? near : splits[index - 1];
            float previousNear = index < 2 ? near : splits[index - 2];
            float cascadeNear = index == 0 ? near : nominalNear - (nominalNear - previousNear) * settings.BlendFraction;
            float cascadeFar = splits[index];
            Vector3[] corners = FrustumCorners(camera, cascadeNear, cascadeFar);
            Vector3 center = camera.Position + camera.Forward * ((cascadeNear + cascadeFar) * 0.5f);
            float radius = corners.Max(corner => Vector3.Distance(corner, center));
            radius = MathF.Ceiling(radius * 16f) / 16f;
            radius *= settings.Resolution / (settings.Resolution - 2f);
            float texelSize = radius * 2f / settings.Resolution;
            Vector3 lightCenter = Vector3.TransformPoint(center, lightRotation);
            lightCenter.X = MathF.Floor(lightCenter.X / texelSize + 0.5f) * texelSize;
            lightCenter.Y = MathF.Floor(lightCenter.Y / texelSize + 0.5f) * texelSize;
            float minimumZ = float.PositiveInfinity;
            float maximumZ = float.NegativeInfinity;
            foreach (Vector3 corner in corners)
            {
                float z = Vector3.TransformPoint(corner, lightRotation).Z;
                minimumZ = MathF.Min(minimumZ, z);
                maximumZ = MathF.Max(maximumZ, z);
            }
            foreach (ShadowCasterBounds caster in casters)
            {
                if (!IsFinite(caster.Minimum) || !IsFinite(caster.Maximum))
                    continue;
                Vector3 minimum = new(float.PositiveInfinity);
                Vector3 maximum = new(float.NegativeInfinity);
                foreach (Vector3 corner in BoundsCorners(caster))
                {
                    Vector3 position = Vector3.TransformPoint(corner, lightRotation);
                    minimum = Vector3.Min(minimum, position);
                    maximum = Vector3.Max(maximum, position);
                }
                if (maximum.X < lightCenter.X - radius || minimum.X > lightCenter.X + radius
                    || maximum.Y < lightCenter.Y - radius || minimum.Y > lightCenter.Y + radius)
                    continue;
                maximumZ = MathF.Max(maximumZ, maximum.Z);
            }
            float padding = MathF.Max(0.1f, texelSize * 4f);
            maximumZ += padding;
            minimumZ -= padding;
            float depthRange = MathF.Max(maximumZ - minimumZ, 0.01f);
            Matrix4x4 view = lightRotation;
            view.M14 = -lightCenter.X;
            view.M24 = -lightCenter.Y;
            view.M34 = -maximumZ - 0.01f;
            Matrix4x4 projection = Matrix4x4.CreateOrthographic(radius * 2f, radius * 2f, 0.01f, depthRange + 0.01f);
            projection.M22 = -projection.M22;
            data.LightViewProjections[index] = projection * view;
            Rect tile = Tile(index, settings.Resolution);
            float atlasSize = AtlasSize(settings.Resolution);
            data.AtlasTiles[index] = new Vector4(tile.X / atlasSize, tile.Y / atlasSize, tile.Width / atlasSize, tile.Height / atlasSize);
            data.CascadeMetrics[index] = new Vector4(texelSize, depthRange, radius, cascadeNear);
        }
        return data;
    }

    public static Vector3[] FrustumCorners(Camera camera, float near, float far)
    {
        Vector3[] corners = new Vector3[8];
        float tangent = MathF.Tan(camera.FieldOfViewY * 0.5f);
        for (int plane = 0; plane < 2; plane++)
        {
            float distance = plane == 0 ? near : far;
            float halfHeight = tangent * distance;
            float halfWidth = halfHeight * camera.AspectRatio;
            Vector3 center = camera.Position + camera.Forward * distance;
            for (int corner = 0; corner < 4; corner++)
                corners[plane * 4 + corner] = center + camera.Right * ((corner & 1) == 0 ? -halfWidth : halfWidth)
                    + camera.Up * ((corner & 2) == 0 ? -halfHeight : halfHeight);
        }
        return corners;
    }

    public static ShadowCasterBounds TransformBounds(Vector3 minimum, Vector3 maximum, Matrix4x4 model)
    {
        Vector3 worldMinimum = new(float.PositiveInfinity);
        Vector3 worldMaximum = new(float.NegativeInfinity);
        foreach (Vector3 corner in BoundsCorners(new ShadowCasterBounds(minimum, maximum)))
        {
            Vector3 world = Vector3.TransformPoint(corner, model);
            worldMinimum = Vector3.Min(worldMinimum, world);
            worldMaximum = Vector3.Max(worldMaximum, world);
        }
        return new ShadowCasterBounds(worldMinimum, worldMaximum);
    }

    private static IEnumerable<Vector3> BoundsCorners(ShadowCasterBounds bounds)
    {
        for (int corner = 0; corner < 8; corner++)
            yield return new Vector3((corner & 1) == 0 ? bounds.Minimum.X : bounds.Maximum.X,
                (corner & 2) == 0 ? bounds.Minimum.Y : bounds.Maximum.Y,
                (corner & 4) == 0 ? bounds.Minimum.Z : bounds.Maximum.Z);
    }

    private static bool IsFinite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
