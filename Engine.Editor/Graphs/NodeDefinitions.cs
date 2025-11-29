using System.Numerics;

namespace Engine.Editor.Graphs;

public static class NodeDefinitions
{
    public static NodeDefinition GetDefinition(string nodeType)
    {
        return nodeType switch
        {
            "TexCoord" => new NodeDefinition
            {
                Title = "TexCoord",
                Subtitle = "UV Source",
                Size = new Vector2(200f, 280f),
                HeaderColor = new Vector4(0.74f, 0.18f, 0.12f, 1f),
                Inputs = new[]
                {
                    ("Coordinates U", NodeValueKind.Scalar),
                    ("Coordinates V", NodeValueKind.Scalar),
                    ("Scale U", NodeValueKind.Scalar),
                    ("Scale V", NodeValueKind.Scalar),
                    ("Offset U", NodeValueKind.Scalar),
                    ("Offset V", NodeValueKind.Scalar)
                },
                Outputs = new[] { ("UV", NodeValueKind.UV) }
            },
            "Texture Sample" => new NodeDefinition
            {
                Title = "Texture Sample",
                Subtitle = null,
                Size = new Vector2(230f, 190f),
                HeaderColor = new Vector4(0.1f, 0.42f, 0.66f, 1f),
                Inputs = new[]
                {
                    ("UVs", NodeValueKind.UV),
                    ("RGBA", NodeValueKind.Color)
                },
                Outputs = new[]
                {
                    ("RGBA", NodeValueKind.Color)
                }
            },
            "Constant3Vector" => new NodeDefinition
            {
                Title = "Constant3Vector",
                Subtitle = null,
                Size = new Vector2(210f, 140f),
                HeaderColor = new Vector4(0.3f, 0.7f, 0.4f, 0.95f),
                Inputs = Array.Empty<(string, NodeValueKind)>(),
                Outputs = new[] { ("Color", NodeValueKind.Color) }
            },
            "Constant4Vector" => new NodeDefinition
            {
                Title = "Constant4Vector",
                Subtitle = null,
                Size = new Vector2(210f, 140f),
                HeaderColor = new Vector4(0.3f, 0.7f, 0.4f, 0.95f),
                Inputs = Array.Empty<(string, NodeValueKind)>(),
                Outputs = new[] { ("Color", NodeValueKind.Color) }
            },
            "Scalar Parameter" => new NodeDefinition
            {
                Title = "Scalar Parameter",
                Subtitle = null,
                Size = new Vector2(200f, 120f),
                HeaderColor = new Vector4(0.3f, 0.7f, 0.4f, 0.95f),
                Inputs = Array.Empty<(string, NodeValueKind)>(),
                Outputs = new[] { ("Value", NodeValueKind.Scalar) }
            },
            "Multiply" => new NodeDefinition
            {
                Title = "Multiply",
                Subtitle = null,
                Size = new Vector2(220f, 150f),
                HeaderColor = new Vector4(0.24f, 0.48f, 0.23f, 1f),
                Inputs = new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color) },
                Outputs = new[] { ("Result", NodeValueKind.Color) }
            },
            "Add" => new NodeDefinition
            {
                Title = "Add",
                Subtitle = null,
                Size = new Vector2(200f, 150f),
                HeaderColor = new Vector4(0.24f, 0.48f, 0.23f, 1f),
                Inputs = new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color) },
                Outputs = new[] { ("Result", NodeValueKind.Color) }
            },
            "Lerp" => new NodeDefinition
            {
                Title = "Lerp",
                Subtitle = null,
                Size = new Vector2(240f, 170f),
                HeaderColor = new Vector4(0.18f, 0.35f, 0.45f, 1f),
                Inputs = new[] { ("A", NodeValueKind.Color), ("B", NodeValueKind.Color), ("Alpha", NodeValueKind.Scalar) },
                Outputs = new[] { ("Result", NodeValueKind.Color) }
            },
            "Power" => new NodeDefinition
            {
                Title = "Power",
                Subtitle = null,
                Size = new Vector2(200f, 150f),
                HeaderColor = new Vector4(0.24f, 0.48f, 0.23f, 1f),
                Inputs = new[] { ("Base", NodeValueKind.Scalar), ("Exp", NodeValueKind.Scalar) },
                Outputs = new[] { ("Result", NodeValueKind.Scalar) }
            },
            "Material Output" => new NodeDefinition
            {
                Title = "Material Output",
                Subtitle = null,
                Size = new Vector2(260f, 320f),
                HeaderColor = new Vector4(0.18f, 0.18f, 0.18f, 1f),
                Inputs = new[]
                {
                    ("Base Color", NodeValueKind.Color),
                    ("Metallic", NodeValueKind.Scalar),
                    ("Specular", NodeValueKind.Scalar),
                    ("Roughness", NodeValueKind.Scalar),
                    ("Normal", NodeValueKind.Normal),
                    ("Ambient Occlusion", NodeValueKind.Scalar)
                },
                Outputs = Array.Empty<(string, NodeValueKind)>()
            },
            _ => throw new ArgumentException($"Unknown node type: {nodeType}")
        };
    }
}


public class NodeDefinition
{
    public string Title { get; set; } = string.Empty;
    public string? Subtitle { get; set; }
    public Vector2 Size { get; set; }
    public Vector4 HeaderColor { get; set; }
    public (string label, NodeValueKind kind)[] Inputs { get; set; } = Array.Empty<(string, NodeValueKind)>();
    public (string label, NodeValueKind kind)[] Outputs { get; set; } = Array.Empty<(string, NodeValueKind)>();
}

