using System.Runtime.InteropServices;
using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector2 UV;
    public Vector4 Tangent;

    public Vector3 TangentDirection
    {
        readonly get => Tangent.XYZ;
        set => Tangent = new Vector4(value, Tangent.W == 0f ? 1f : Tangent.W);
    }

    public static VertexLayoutDescription Layout { get; } = new()
    {
        Stride = (uint)Marshal.SizeOf<Vertex>(),
        Attributes =
        [
            new VertexAttributeDescription { Offset = 0, Format = VertexFormat.Float3 },
            new VertexAttributeDescription { Offset = 12, Format = VertexFormat.Float3 },
            new VertexAttributeDescription { Offset = 24, Format = VertexFormat.Float2 },
            new VertexAttributeDescription { Offset = 32, Format = VertexFormat.Float4 },
        ],
    };
}

public static class MeshTangents
{
    public static void Generate(Span<Vertex> vertices, ReadOnlySpan<uint> indices, CancellationToken cancellationToken = default)
    {
        if (indices.Length % 3 != 0)
            throw new ArgumentException("Triangle index count must be divisible by three.", nameof(indices));

        cancellationToken.ThrowIfCancellationRequested();
        Vector3[] tangents = new Vector3[vertices.Length];
        Vector3[] bitangents = new Vector3[vertices.Length];
        for (int triangle = 0; triangle < indices.Length; triangle += 3)
        {
            if ((triangle & 16383) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int index0 = CheckedIndex(indices[triangle], vertices.Length);
            int index1 = CheckedIndex(indices[triangle + 1], vertices.Length);
            int index2 = CheckedIndex(indices[triangle + 2], vertices.Length);
            Vertex vertex0 = vertices[index0];
            Vertex vertex1 = vertices[index1];
            Vertex vertex2 = vertices[index2];
            Vector3 edge1 = vertex1.Position - vertex0.Position;
            Vector3 edge2 = vertex2.Position - vertex0.Position;
            Vector2 uvEdge1 = vertex1.UV - vertex0.UV;
            Vector2 uvEdge2 = vertex2.UV - vertex0.UV;
            float determinant = uvEdge1.X * uvEdge2.Y - uvEdge1.Y * uvEdge2.X;
            if (!float.IsFinite(determinant) || MathF.Abs(determinant) <= MathUtil.Epsilon)
                continue;

            float inverseDeterminant = 1f / determinant;
            Vector3 tangent = (edge1 * uvEdge2.Y - edge2 * uvEdge1.Y) * inverseDeterminant;
            Vector3 bitangent = (edge2 * uvEdge1.X - edge1 * uvEdge2.X) * inverseDeterminant;
            if (!IsFinite(tangent) || !IsFinite(bitangent))
                continue;

            tangents[index0] += tangent;
            tangents[index1] += tangent;
            tangents[index2] += tangent;
            bitangents[index0] += bitangent;
            bitangents[index1] += bitangent;
            bitangents[index2] += bitangent;
        }

        for (int index = 0; index < vertices.Length; index++)
        {
            if ((index & 16383) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            Vertex vertex = vertices[index];
            Vector3 normal = IsFinite(vertex.Normal) && vertex.Normal.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon
                ? vertex.Normal.Normalized
                : Vector3.Up;
            Vector3 tangent = tangents[index] - normal * Vector3.Dot(normal, tangents[index]);
            if (!IsFinite(tangent) || tangent.LengthSquared <= MathUtil.Epsilon * MathUtil.Epsilon)
                tangent = CreateFallbackTangent(normal);
            else
                tangent = tangent.Normalized;

            Vector3 bitangent = bitangents[index];
            float handedness = IsFinite(bitangent) && bitangent.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon
                && Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0f
                    ? -1f
                    : 1f;
            vertex.Normal = normal;
            vertex.Tangent = new Vector4(tangent, handedness);
            vertices[index] = vertex;
        }
    }

    private static int CheckedIndex(uint index, int vertexCount)
    {
        if (index >= (uint)vertexCount)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Mesh index is outside the vertex array.");
        return (int)index;
    }

    private static Vector3 CreateFallbackTangent(Vector3 normal)
    {
        Vector3 axis = MathF.Abs(normal.Y) < 0.999f ? Vector3.Up : Vector3.Right;
        Vector3 tangent = Vector3.Cross(axis, normal);
        if (tangent.LengthSquared <= MathUtil.Epsilon * MathUtil.Epsilon)
            tangent = Vector3.Cross(Vector3.Forward, normal);
        return tangent.Normalized;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public sealed class Mesh : IDisposable
{
    private readonly IDevice _device;
    private readonly List<(Mesh Mesh, float Error)> _lods = [];
    private float[]? _lodErrors;
    private bool _disposed;

    public int LodCount => _lods.Count + 1;

    public ReadOnlySpan<float> LodErrors => _lodErrors ??= BuildLodErrors();

    private float[] BuildLodErrors()
    {
        float[] errors = new float[_lods.Count + 1];
        for (int index = 0; index < _lods.Count; index++)
            errors[index + 1] = _lods[index].Error;
        return errors;
    }

    public float GetLodError(int level) => level == 0 ? 0 : _lods[level - 1].Error;

    public Mesh GetLod(int level) => level == 0 ? this : _lods[level - 1].Mesh;

    public void AddLod(Vertex[] vertices, uint[] indices, float error)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        float previousError = _lods.Count == 0 ? 0 : _lods[^1].Error;
        uint previousCount = _lods.Count == 0 ? IndexCount : _lods[^1].Mesh.IndexCount;
        if (!float.IsFinite(error) || error < previousError || indices.Length >= previousCount)
            throw new ArgumentException("LOD must reduce geometry with a finite nondecreasing error.");
        Mesh level = new(_device, vertices, indices, "LOD");
        _lods.Add((level, error));
        _lodErrors = null;
    }

    public IBuffer VertexBuffer { get; }

    public IBuffer IndexBuffer { get; }

    public uint IndexCount { get; }

    public int VertexCount { get; }

    public Vector3 BoundsMin { get; }

    public Vector3 BoundsMax { get; }

    public Mesh(IDevice device, Vertex[] vertices, uint[] indices, string debugName)
    {
        _device = device;
        IndexCount = (uint)indices.Length;
        VertexCount = vertices.Length;
        if (vertices.Length > 0)
        {
            Vector3 minimum = vertices[0].Position;
            Vector3 maximum = vertices[0].Position;
            foreach (Vertex vertex in vertices)
            {
                minimum = Vector3.Min(minimum, vertex.Position);
                maximum = Vector3.Max(maximum, vertex.Position);
            }
            BoundsMin = minimum;
            BoundsMax = maximum;
        }
        VertexBuffer = device.CreateBuffer(new BufferDescription
        {
            Size = checked((ulong)vertices.Length * (ulong)Marshal.SizeOf<Vertex>()),
            Usage = BufferUsage.Vertex | BufferUsage.TransferDestination,
            Memory = MemoryUsage.GpuOnly,
        });
        try
        {
            IndexBuffer = device.CreateBuffer(new BufferDescription
            {
                Size = checked((ulong)indices.Length * sizeof(uint)),
                Usage = BufferUsage.Index | BufferUsage.TransferDestination,
                Memory = MemoryUsage.GpuOnly,
            });
            try
            {
                device.UploadBufferData(VertexBuffer, MemoryMarshal.AsBytes(vertices.AsSpan()));
                device.UploadBufferData(IndexBuffer, MemoryMarshal.AsBytes(indices.AsSpan()));
            }
            catch
            {
                IndexBuffer.Dispose();
                throw;
            }
        }
        catch
        {
            VertexBuffer.Dispose();
            throw;
        }
    }

    public static unsafe byte[] BytesOf<T>(T[] array) where T : unmanaged
    {
        byte[] bytes = new byte[array.Length * sizeof(T)];
        fixed (T* source = array)
            fixed (byte* destination = bytes)
                Buffer.MemoryCopy(source, destination, bytes.Length, bytes.Length);
        return bytes;
    }

    public void Draw(ICommandBuffer commandBuffer)
    {
        commandBuffer.BindVertexBuffer(0, VertexBuffer);
        commandBuffer.BindIndexBuffer(IndexBuffer, IndexType.UInt32);
        commandBuffer.DrawIndexed(IndexCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var level in _lods)
            level.Mesh.Dispose();
        _lods.Clear();
        VertexBuffer.Dispose();
        IndexBuffer.Dispose();
    }
}
