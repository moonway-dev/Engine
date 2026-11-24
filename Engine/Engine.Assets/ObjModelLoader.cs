using System.Globalization;
using Engine.Mathematics;
using Engine.Rendering;

namespace Engine.Assets;

public static class ObjModelLoader
{
    public static (Vertex[] Vertices, uint[] Indices) Load(string path, CancellationToken cancellationToken = default)
    {
        ModelData model = LoadModel(path, cancellationToken);
        return (model.Vertices, model.Indices);
    }

    public static ModelData LoadModel(string path, CancellationToken cancellationToken = default)
    {
        List<Vector3> positions = [];
        List<Vector3> normals = [];
        List<Vector2> uvs = [];
        List<Vertex> vertices = [];
        List<bool> missingNormals = [];
        bool needsNormals = false;
        List<int> positionVertices = [];
        Dictionary<(int Position, int Uv, int Normal), uint> vertexMap = [];
        List<uint> indices = [];
        Dictionary<string, List<(int Start, int Count)>> ranges = new(StringComparer.Ordinal);
        string objectName = string.Empty;
        string groupName = string.Empty;
        string activePart = Path.GetFileNameWithoutExtension(path);
        int partStart = 0;
        void FinishPart()
        {
            if (indices.Count == partStart)
                return;
            if (!ranges.TryGetValue(activePart, out var spans))
                ranges.Add(activePart, spans = []);
            spans.Add((partStart, indices.Count - partStart));
            partStart = indices.Count;
        }
        int lineNumber = 0;
        string contents = Core.VirtualFileSystem.ReadAllText(path);
        foreach (ReadOnlySpan<char> lineRaw in contents.AsSpan().EnumerateLines())
        {
            if ((lineNumber++ & 4095) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            ReadOnlySpan<char> line = lineRaw;
            ReadOnlySpan<char> kind = NextToken(ref line);
            if (kind.SequenceEqual("v"))
            {
                positions.Add(ReadVector3(ref line));
                positionVertices.Add(-1);
            }
            else if (kind.SequenceEqual("vn"))
                normals.Add(ReadVector3(ref line));
            else if (kind.SequenceEqual("vt"))
                uvs.Add(new Vector2(ReadFloat(NextToken(ref line)), ReadFloat(NextToken(ref line))));
            else if (kind.SequenceEqual("o") || kind.SequenceEqual("g"))
            {
                FinishPart();
                string name = line.Trim().ToString();
                if (kind.SequenceEqual("o"))
                {
                    objectName = name;
                    groupName = string.Empty;
                }
                else
                    groupName = name;
                activePart = string.IsNullOrEmpty(objectName) ? groupName
                    : string.IsNullOrEmpty(groupName) ? objectName : objectName + "/" + groupName;
                if (string.IsNullOrEmpty(activePart))
                    activePart = Path.GetFileNameWithoutExtension(path);
            }
            else if (kind.SequenceEqual("f"))
            {
                uint first = 0;
                uint previous = 0;
                int count = 0;
                while (true)
                {
                    ReadOnlySpan<char> token = NextToken(ref line);
                    if (token.IsEmpty || token[0] == '#')
                        break;
                    int slash = token.IndexOf('/');
                    int position = ReadIndex(slash < 0 ? token : token[..slash], positions.Count);
                    int uv = -1;
                    int normal = -1;
                    if (slash >= 0)
                    {
                        ReadOnlySpan<char> rest = token[(slash + 1)..];
                        int secondSlash = rest.IndexOf('/');
                        ReadOnlySpan<char> uvToken = secondSlash < 0 ? rest : rest[..secondSlash];
                        if (!uvToken.IsEmpty)
                            uv = ReadIndex(uvToken, uvs.Count);
                        if (secondSlash >= 0 && secondSlash + 1 < rest.Length)
                            normal = ReadIndex(rest[(secondSlash + 1)..], normals.Count);
                    }
                    uint index;
                    bool plain = uv < 0 && normal < 0;
                    bool found;
                    if (plain)
                    {
                        int existing = positionVertices[position];
                        found = existing >= 0;
                        index = (uint)existing;
                    }
                    else
                        found = vertexMap.TryGetValue((position, uv, normal), out index);
                    if (!found)
                    {
                        index = (uint)vertices.Count;
                        vertices.Add(new Vertex
                        {
                            Position = positions[position],
                            UV = uv >= 0 ? new Vector2(uvs[uv].X, 1f - uvs[uv].Y) : Vector2.Zero,
                            Normal = normal >= 0 ? normals[normal] : Vector3.Up,
                        });
                        missingNormals.Add(normal < 0);
                        needsNormals |= normal < 0;
                        if (plain)
                            positionVertices[position] = (int)index;
                        else
                            vertexMap.Add((position, uv, normal), index);
                    }
                    if (count == 0)
                        first = index;
                    else if (count >= 2)
                    {
                        indices.Add(first);
                        indices.Add(previous);
                        indices.Add(index);
                    }
                    previous = index;
                    count++;
                }
                if (count < 3)
                    throw new InvalidDataException($"OBJ face at line {lineNumber} has fewer than three vertices.");
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (needsNormals)
            FillSmoothNormals(vertices, indices, missingNormals, cancellationToken);
        Vertex[] result = [.. vertices];
        uint[] resultIndices = [.. indices];
        MeshTangents.Generate(result, resultIndices, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        FinishPart();
        List<ModelPart> parts = [];
        if (ranges.Count <= 1)
        {
            if (ranges.Count == 1)
            {
                string name = ranges.Keys.First();
                parts.Add(new ModelPart(name, name, 0, resultIndices.Length));
            }
        }
        else
        {
            int offset = 0;
            foreach (var entry in ranges)
            {
                int start = offset;
                foreach (var range in entry.Value)
                {
                    indices.CopyTo(range.Start, resultIndices, offset, range.Count);
                    offset += range.Count;
                }
                parts.Add(new ModelPart(entry.Key, entry.Key, start, offset - start));
            }
        }
        return new ModelData(result, resultIndices, [.. parts]);
    }

    private static ReadOnlySpan<char> NextToken(ref ReadOnlySpan<char> line)
    {
        line = line.TrimStart();
        int length = 0;
        while (length < line.Length && !char.IsWhiteSpace(line[length]))
            length++;
        ReadOnlySpan<char> token = line[..length];
        line = line[length..];
        return token;
    }

    private static int ReadIndex(ReadOnlySpan<char> text, int count)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value == 0)
            throw new InvalidDataException("Invalid OBJ index.");
        long index = value > 0 ? (long)value - 1 : (long)count + value;
        if (index < 0 || index >= count)
            throw new InvalidDataException("OBJ index is outside the referenced array.");
        return (int)index;
    }

    private static Vector3 ReadVector3(ref ReadOnlySpan<char> line) => new(
        ReadFloat(NextToken(ref line)), ReadFloat(NextToken(ref line)), ReadFloat(NextToken(ref line)));

    private static float ReadFloat(ReadOnlySpan<char> text)
    {
        float value = float.Parse(text, CultureInfo.InvariantCulture);
        if (!float.IsFinite(value))
            throw new InvalidDataException("OBJ attributes must be finite.");
        return value;
    }

    private static void FillSmoothNormals(List<Vertex> vertices, List<uint> indices, List<bool> missingNormals, CancellationToken cancellationToken)
    {
        Vector3[] accumulated = new Vector3[vertices.Count];
        for (int i = 0; i < indices.Count; i += 3)
        {
            if ((i & 16383) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int a = (int)indices[i];
            int b = (int)indices[i + 1];
            int c = (int)indices[i + 2];
            Vector3 normal = Vector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
            accumulated[a] += normal;
            accumulated[b] += normal;
            accumulated[c] += normal;
        }
        for (int i = 0; i < vertices.Count; i++)
        {
            if ((i & 16383) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            if (!missingNormals[i])
                continue;
            Vector3 normal = accumulated[i];
            if (normal.LengthSquared > MathUtil.Epsilon * MathUtil.Epsilon)
            {
                Vertex updated = vertices[i];
                updated.Normal = normal.Normalized;
                vertices[i] = updated;
            }
        }
    }
}
