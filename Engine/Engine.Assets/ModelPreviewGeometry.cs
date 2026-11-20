using Engine.Mathematics;

namespace Engine.Assets;

public static class ModelPreviewGeometry
{
    public static ModelData Reduce(ModelData source, CancellationToken token = default)
    {
        long bytes = (long)source.Vertices.Length * 48 + (long)source.Indices.Length * sizeof(uint);
        if (source.Vertices.Length == 0 || bytes <= 16L * 1024 * 1024)
            return source;
        Vector3 minimum = source.Vertices[0].Position;
        Vector3 maximum = minimum;
        foreach (var vertex in source.Vertices)
        {
            minimum = Vector3.Min(minimum, vertex.Position);
            maximum = Vector3.Max(maximum, vertex.Position);
        }
        Vector3 extent = maximum - minimum;
        float size = MathF.Max(extent.X, MathF.Max(extent.Y, extent.Z));
        float scale = size > MathUtil.Epsilon ? 64f / size : 1f;
        Dictionary<(int X, int Y, int Z), uint> clusters = [];
        List<Engine.Rendering.Vertex> vertices = [];
        int[] counts = new int[source.Vertices.Length];
        uint[] remap = new uint[source.Vertices.Length];
        for (int i = 0; i < source.Vertices.Length; i++)
        {
            if ((i & 65535) == 0)
                token.ThrowIfCancellationRequested();
            var vertex = source.Vertices[i];
            Vector3 point = (vertex.Position - minimum) * scale;
            var key = ((int)MathF.Round(point.X), (int)MathF.Round(point.Y), (int)MathF.Round(point.Z));
            if (!clusters.TryGetValue(key, out uint index))
            {
                index = (uint)vertices.Count;
                clusters.Add(key, index);
                vertices.Add(vertex);
            }
            else
            {
                var previous = vertices[(int)index];
                previous.Position += vertex.Position;
                previous.Normal += vertex.Normal;
                vertices[(int)index] = previous;
            }
            counts[index]++;
            remap[i] = index;
        }
        for (int i = 0; i < vertices.Count; i++)
        {
            var vertex = vertices[i];
            vertex.Position /= counts[i];
            vertex.Normal = vertex.Normal.Normalized;
            vertices[i] = vertex;
        }
        HashSet<(uint A, uint B, uint C)> triangles = [];
        List<uint> indices = [];
        for (int i = 0; i < source.Indices.Length; i += 3)
        {
            if ((i & 65535) == 0)
                token.ThrowIfCancellationRequested();
            uint a = remap[source.Indices[i]];
            uint b = remap[source.Indices[i + 1]];
            uint c = remap[source.Indices[i + 2]];
            if (a == b || b == c || c == a)
                continue;
            if (b < a && b < c)
                (a, b, c) = (b, c, a);
            else if (c < a && c < b)
                (a, b, c) = (c, a, b);
            if (triangles.Add((a, b, c)))
                indices.AddRange([a, b, c]);
        }
        return new ModelData([.. vertices], [.. indices], []);
    }
}
