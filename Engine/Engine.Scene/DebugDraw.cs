using System.Runtime.InteropServices;
using Engine.Mathematics;

namespace Engine.Scene;

public struct DebugLineVertex
{
    public Vector3 Position;
    public Vector4 Color;
}

public static class DebugDraw
{
    private static readonly List<DebugLineVertex> Vertices = [];
    private static readonly Vector4 DefaultColor = new(0.2f, 1f, 0.35f, 1f);

    public static bool HasLines => Vertices.Count > 0;

    public static void Clear() => Vertices.Clear();

    public static void DrawLine(Vector3 from, Vector3 to, Vector4? color = null)
    {
        Vector4 lineColor = color ?? DefaultColor;
        Vertices.Add(new DebugLineVertex { Position = from, Color = lineColor });
        Vertices.Add(new DebugLineVertex { Position = to, Color = lineColor });
    }

    public static void DrawWireBox(Vector3 center, Vector3 size, Quaternion rotation, Vector4? color = null)
    {
        Vector4 lineColor = color ?? new Vector4(0.2f, 1f, 0.35f, 1f);
        Vector3 half = size * 0.5f;
        Span<Vector3> corners = stackalloc Vector3[8];
        int index = 0;
        for (int z = -1; z <= 1; z += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int x = -1; x <= 1; x += 2)
                    corners[index++] = center + rotation.Rotate(new Vector3(half.X * x, half.Y * y, half.Z * z));
        ReadOnlySpan<(int From, int To)> edges =
        [
            (0, 1), (2, 3), (4, 5), (6, 7),
            (0, 2), (1, 3), (4, 6), (5, 7),
            (0, 4), (1, 5), (2, 6), (3, 7),
        ];
        foreach ((int from, int to) in edges)
            DrawLine(corners[from], corners[to], lineColor);
    }

    public static void DrawWireSphere(Vector3 center, float radius, Vector4? color = null, int segments = 24)
    {
        Vector4 lineColor = color ?? new Vector4(0.35f, 0.8f, 1f, 1f);
        DrawCircle(center, rotation: Quaternion.Identity, plane: CirclePlane.XZ, radius, lineColor, segments);
        DrawCircle(center, rotation: Quaternion.Identity, plane: CirclePlane.XY, radius, lineColor, segments);
        DrawCircle(center, rotation: Quaternion.Identity, plane: CirclePlane.YZ, radius, lineColor, segments);
    }

    public static void DrawWireCylinder(Vector3 center, Quaternion rotation, float radius, float length, Vector4? color = null, int segments = 20)
    {
        Vector4 lineColor = color ?? new Vector4(1f, 0.6f, 0.2f, 1f);
        Vector3 axis = rotation.Rotate(Vector3.Up);
        Vector3 half = axis * (length * 0.5f);
        Vector3 top = center + half;
        Vector3 bottom = center - half;
        (Vector3 first, Vector3 second) = PerpendicularBasis(axis);
        for (int i = 0; i < segments; i++)
        {
            float angleA = MathF.PI * 2f * i / segments;
            float angleB = MathF.PI * 2f * (i + 1) / segments;
            Vector3 radialA = first * MathF.Cos(angleA) + second * MathF.Sin(angleA);
            Vector3 radialB = first * MathF.Cos(angleB) + second * MathF.Sin(angleB);
            DrawLine(top + radialA * radius, top + radialB * radius, lineColor);
            DrawLine(bottom + radialA * radius, bottom + radialB * radius, lineColor);
            if (i % Math.Max(1, segments / 4) == 0)
                DrawLine(bottom + radialA * radius, top + radialA * radius, lineColor);
        }
    }

    public static void DrawWireCapsule(Vector3 center, Quaternion rotation, float radius, float length, Vector4? color = null, int segments = 20)
    {
        Vector4 lineColor = color ?? new Vector4(0.3f, 0.55f, 1f, 1f);
        Vector3 axis = rotation.Rotate(Vector3.Up);
        Vector3 half = axis * (length * 0.5f);
        Vector3 top = center + half;
        Vector3 bottom = center - half;
        (Vector3 first, Vector3 second) = PerpendicularBasis(axis);
        for (int i = 0; i < segments; i++)
        {
            float angleA = MathF.PI * 2f * i / segments;
            float angleB = MathF.PI * 2f * (i + 1) / segments;
            Vector3 radialA = first * MathF.Cos(angleA) + second * MathF.Sin(angleA);
            Vector3 radialB = first * MathF.Cos(angleB) + second * MathF.Sin(angleB);
            DrawLine(top + radialA * radius, top + radialB * radius, lineColor);
            DrawLine(bottom + radialA * radius, bottom + radialB * radius, lineColor);
            Vector3 equatorA = center + radialA * radius;
            Vector3 equatorB = center + radialB * radius;
            if (MathF.Abs(radialA.Y) < 0.9f)
                DrawLine(equatorA, equatorB, lineColor);
            DrawLine(bottom + radialA * radius, top + radialA * radius, lineColor);
        }
    }

    private static void DrawCircle(Vector3 center, Quaternion rotation, CirclePlane plane, float radius, Vector4 color, int segments)
    {
        for (int i = 0; i < segments; i++)
        {
            float angleA = MathF.PI * 2f * i / segments;
            float angleB = MathF.PI * 2f * (i + 1) / segments;
            Vector3 offsetA = CircleOffset(plane, angleA, radius);
            Vector3 offsetB = CircleOffset(plane, angleB, radius);
            DrawLine(center + rotation.Rotate(offsetA), center + rotation.Rotate(offsetB), color);
        }
    }

    private static Vector3 CircleOffset(CirclePlane plane, float angle, float radius)
    {
        float x = MathF.Cos(angle) * radius;
        float y = MathF.Sin(angle) * radius;
        return plane switch
        {
            CirclePlane.XY => new Vector3(x, y, 0f),
            CirclePlane.YZ => new Vector3(0f, x, y),
            _ => new Vector3(x, 0f, y),
        };
    }

    private static (Vector3 First, Vector3 Second) PerpendicularBasis(Vector3 axis)
    {
        Vector3 reference = MathF.Abs(axis.Y) > 0.95f ? new Vector3(1f, 0f, 0f) : new Vector3(0f, 1f, 0f);
        Vector3 first = Vector3.Cross(axis, reference).Normalized;
        Vector3 second = Vector3.Cross(axis, first).Normalized;
        return (first, second);
    }

    public static Span<DebugLineVertex> GetSpan() => CollectionsMarshal.AsSpan(Vertices);

    private enum CirclePlane
    {
        XY,
        YZ,
        XZ,
    }
}
