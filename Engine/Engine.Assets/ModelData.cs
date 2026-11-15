using Engine.Rendering;

namespace Engine.Assets;

public sealed record ModelPart(string Id, string Name, int FirstIndex, int IndexCount);

public sealed record ModelData(Vertex[] Vertices, uint[] Indices, ModelPart[] Parts)
{
    public (Vertex[] Vertices, uint[] Indices) ExtractPart(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return (Vertices, Indices);
        ModelPart part = Parts.FirstOrDefault(value => value.Id == id)
            ?? throw new InvalidDataException($"Model part '{id}' does not exist.");
        if (part.FirstIndex == 0 && part.IndexCount == Indices.Length)
            return (Vertices, Indices);
        Dictionary<uint, uint> remap = [];
        List<Vertex> vertices = [];
        uint[] indices = new uint[part.IndexCount];
        for (int i = 0; i < indices.Length; i++)
        {
            uint source = Indices[part.FirstIndex + i];
            if (!remap.TryGetValue(source, out uint destination))
            {
                destination = (uint)vertices.Count;
                remap.Add(source, destination);
                vertices.Add(Vertices[source]);
            }
            indices[i] = destination;
        }
        return ([.. vertices], indices);
    }
}
