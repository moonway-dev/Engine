using Assimp;
using Engine.Mathematics;
using Engine.Rendering;
using AssimpScene = Assimp.Scene;
using AssimpMesh = Assimp.Mesh;
using AssimpMatrix4x4 = Assimp.Matrix4x4;
using EngineVector2 = Engine.Mathematics.Vector2;
using EngineVector3 = Engine.Mathematics.Vector3;

namespace Engine.Assets;

public static class FbxModelLoader
{
    public static ModelData LoadModel(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using AssimpContext context = new();
        using Stream sourceStream = Engine.Core.VirtualFileSystem.OpenRead(path);
        AssimpScene scene = context.ImportFileFromStream(
            sourceStream,
            PostProcessSteps.Triangulate | PostProcessSteps.SortByPrimitiveType,
            Path.GetExtension(path).TrimStart('.'));
        if (scene is null || scene.MeshCount == 0 || scene.RootNode is null)
            throw new InvalidDataException($"FBX file '{path}' contains no meshes.");

        List<Vertex> vertices = [];
        List<uint> indices = [];
        List<ModelPart> parts = [];
        ImportNode(scene.RootNode, AssimpMatrix4x4.Identity, scene, vertices, indices, parts, cancellationToken);
        if (vertices.Count == 0 || indices.Count == 0)
            throw new InvalidDataException($"FBX file '{path}' contains no triangle geometry.");

        Vertex[] result = [.. vertices];
        uint[] resultIndices = [.. indices];
        GenerateMissingNormals(result, resultIndices, cancellationToken);
        MeshTangents.Generate(result, resultIndices, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new ModelData(result, resultIndices, [.. parts]);
    }

    private static void ImportNode(
        Node node,
        AssimpMatrix4x4 parentTransform,
        AssimpScene scene,
        List<Vertex> vertices,
        List<uint> indices,
        List<ModelPart> parts,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AssimpMatrix4x4 transform = node.Transform * parentTransform;
        foreach (int meshIndex in node.MeshIndices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (meshIndex < 0 || meshIndex >= scene.MeshCount)
                continue;
            AssimpMesh mesh = scene.Meshes[meshIndex];
            if (mesh.VertexCount == 0 || mesh.FaceCount == 0)
                continue;
            int firstIndex = indices.Count;
            uint vertexBase = checked((uint)vertices.Count);
            bool hasNormals = mesh.HasNormals;
            bool hasUv = mesh.TextureCoordinateChannelCount > 0;
            AssimpMatrix4x4 normalTransform = transform;
            normalTransform.Inverse();
            normalTransform.Transpose();
            for (int vertexIndex = 0; vertexIndex < mesh.VertexCount; vertexIndex++)
            {
                if ((vertexIndex & 4095) == 0)
                    cancellationToken.ThrowIfCancellationRequested();
                Assimp.Vector3D position = mesh.Vertices[vertexIndex];
                Assimp.Vector3D normal = hasNormals ? mesh.Normals[vertexIndex] : default;
                Assimp.Vector3D uv = hasUv ? mesh.TextureCoordinateChannels[0][vertexIndex] : default;
                EngineVector3 transformedPosition = TransformPosition(transform, position);
                EngineVector3 transformedNormal = hasNormals
                    ? TransformDirection(normalTransform, normal).Normalized
                    : EngineVector3.Zero;
                vertices.Add(new Vertex
                {
                    Position = transformedPosition,
                    Normal = transformedNormal,
                    UV = hasUv ? new EngineVector2(uv.X, uv.Y) : EngineVector2.Zero,
                });
            }
            foreach (Face face in mesh.Faces)
            {
                if (face.IndexCount != 3)
                    continue;
                if (transform.Determinant() < 0f)
                {
                    indices.Add(checked(vertexBase + (uint)face.Indices[0]));
                    indices.Add(checked(vertexBase + (uint)face.Indices[2]));
                    indices.Add(checked(vertexBase + (uint)face.Indices[1]));
                }
                else
                {
                    indices.Add(checked(vertexBase + (uint)face.Indices[0]));
                    indices.Add(checked(vertexBase + (uint)face.Indices[1]));
                    indices.Add(checked(vertexBase + (uint)face.Indices[2]));
                }
            }
            int indexCount = indices.Count - firstIndex;
            if (indexCount == 0)
                continue;
            string name = string.IsNullOrWhiteSpace(node.Name)
                ? string.IsNullOrWhiteSpace(mesh.Name) ? $"Mesh_{meshIndex}" : mesh.Name
                : node.Name;
            string id = UniqueId($"{name}#{meshIndex}", parts);
            parts.Add(new ModelPart(id, name, firstIndex, indexCount));
        }
        foreach (Node child in node.Children)
            ImportNode(child, transform, scene, vertices, indices, parts, cancellationToken);
    }

    private static EngineVector3 TransformPosition(AssimpMatrix4x4 matrix, Assimp.Vector3D value) => new(
        matrix.A1 * value.X + matrix.A2 * value.Y + matrix.A3 * value.Z + matrix.A4,
        matrix.B1 * value.X + matrix.B2 * value.Y + matrix.B3 * value.Z + matrix.B4,
        matrix.C1 * value.X + matrix.C2 * value.Y + matrix.C3 * value.Z + matrix.C4);

    private static EngineVector3 TransformDirection(AssimpMatrix4x4 matrix, Assimp.Vector3D value) => new(
        matrix.A1 * value.X + matrix.A2 * value.Y + matrix.A3 * value.Z,
        matrix.B1 * value.X + matrix.B2 * value.Y + matrix.B3 * value.Z,
        matrix.C1 * value.X + matrix.C2 * value.Y + matrix.C3 * value.Z);

    private static string UniqueId(string name, List<ModelPart> parts)
    {
        string id = name;
        int suffix = 1;
        while (parts.Any(part => string.Equals(part.Id, id, StringComparison.Ordinal)))
            id = $"{name}_{suffix++}";
        return id;
    }

    private static void GenerateMissingNormals(Vertex[] vertices, uint[] indices, CancellationToken cancellationToken)
    {
        bool needsNormals = vertices.Any(vertex => vertex.Normal.LengthSquared <= 1e-20f);
        if (!needsNormals)
            return;
        EngineVector3[] accumulated = new EngineVector3[vertices.Length];
        for (int i = 0; i < indices.Length; i += 3)
        {
            if ((i & 16383) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            int a = checked((int)indices[i]);
            int b = checked((int)indices[i + 1]);
            int c = checked((int)indices[i + 2]);
            EngineVector3 normal = EngineVector3.Cross(vertices[b].Position - vertices[a].Position, vertices[c].Position - vertices[a].Position);
            accumulated[a] += normal;
            accumulated[b] += normal;
            accumulated[c] += normal;
        }
        for (int i = 0; i < vertices.Length; i++)
        {
            if (vertices[i].Normal.LengthSquared <= 1e-20f)
                vertices[i].Normal = accumulated[i].Normalized;
        }
    }
}
