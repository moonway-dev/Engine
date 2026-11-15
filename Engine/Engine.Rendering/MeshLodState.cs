using Engine.Mathematics;

namespace Engine.Rendering;

public sealed record MeshLodSettings(MeshLodMode Mode = MeshLodMode.Auto, int ForcedLevel = 0, float Bias = 1f);

public sealed class MeshLodState
{
    private int _previous;

    public int PreviousLevel => _previous;

    public int Select(Mesh mesh, in MeshLodSettings settings, float pixelsPerUnit, float scale, float pixelBudget)
    {
        if (mesh.LodCount <= 1)
        {
            _previous = 0;
            return 0;
        }
        int level = settings.Mode switch
        {
            MeshLodMode.Off => 0,
            MeshLodMode.Forced => Math.Clamp(settings.ForcedLevel, 0, mesh.LodCount - 1),
            _ => MeshLodSelection.Select(mesh.LodErrors, pixelsPerUnit, scale, PixelBudget(settings, pixelBudget), _previous),
        };
        _previous = level;
        return Math.Min(level, mesh.LodCount - 1);
    }

    private static float PixelBudget(in MeshLodSettings settings, float pixelBudget) =>
        MathF.Max(1f, pixelBudget * (float.IsFinite(settings.Bias) && settings.Bias > 0f ? settings.Bias : 1f));
}

public static class MeshLodProjection
{
    public static float PixelsPerUnit(Camera camera, Size2D extent, Vector3 position)
    {
        if (extent.Height <= 0 || !float.IsFinite(camera.FieldOfViewY) || camera.FieldOfViewY <= 0f)
            return 0f;
        float distance = Vector3.Distance(camera.Position, position);
        float worldHeight = 2f * MathF.Tan(camera.FieldOfViewY * 0.5f) * MathF.Max(distance, 1e-4f);
        return worldHeight <= MathUtil.Epsilon ? 0f : extent.Height / worldHeight;
    }

    public static float PixelsPerUnit(Camera camera, Size2D extent, Mesh mesh, in Matrix4x4 modelMatrix)
    {
        if (extent.Height <= 0 || !float.IsFinite(camera.FieldOfViewY) || camera.FieldOfViewY <= 0f)
            return 0f;
        Vector3 center = (mesh.BoundsMin + mesh.BoundsMax) * 0.5f;
        Vector3 halfExtent = (mesh.BoundsMax - mesh.BoundsMin) * 0.5f;
        float radius = halfExtent.Length * ScaleOf(modelMatrix);
        Vector3 worldCenter = Vector3.TransformPoint(center, modelMatrix);
        float centerDepth = Vector3.Dot(worldCenter - camera.Position, camera.Forward);
        float nearestDepth = MathF.Max(camera.NearPlane, centerDepth - radius);
        if (nearestDepth <= MathF.Max(camera.NearPlane, 0f))
            return float.MaxValue;
        float worldHeight = 2f * MathF.Tan(camera.FieldOfViewY * 0.5f) * nearestDepth;
        return worldHeight <= MathUtil.Epsilon ? float.MaxValue : extent.Height / worldHeight;
    }

    public static float ScaleOf(in Matrix4x4 matrix)
    {
        float x = MathF.Sqrt(matrix.M11 * matrix.M11 + matrix.M21 * matrix.M21 + matrix.M31 * matrix.M31);
        float y = MathF.Sqrt(matrix.M12 * matrix.M12 + matrix.M22 * matrix.M22 + matrix.M32 * matrix.M32);
        float z = MathF.Sqrt(matrix.M13 * matrix.M13 + matrix.M23 * matrix.M23 + matrix.M33 * matrix.M33);
        return MathF.Max(x, MathF.Max(y, z));
    }

    public static Vector3 CenterOf(Mesh mesh, in Matrix4x4 matrix) =>
        Vector3.TransformPoint((mesh.BoundsMin + mesh.BoundsMax) * 0.5f, matrix);
}
