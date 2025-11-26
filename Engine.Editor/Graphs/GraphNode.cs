using System;
using System.Collections.Generic;
using System.Numerics;
using EngineVec2 = Engine.Math.Vector2;
using EngineVec4 = Engine.Math.Vector4;

namespace Engine.Editor.Graphs;

public abstract class GraphNode
{
    public int Id { get; }
    public string Title { get; protected set; }
    public string? Subtitle { get; protected set; }
    public Vector2 Position { get; set; }
    public Vector2 Size { get; set; }
    public Vector4 HeaderColor { get; protected set; }
    public List<NodePin> InputPins { get; }
    public List<NodePin> OutputPins { get; }

    protected GraphNode(int id, string title, string? subtitle, Vector2 position, Vector2 size, Vector4 headerColor, IEnumerable<(string label, NodeValueKind kind)> inputs, IEnumerable<(string label, NodeValueKind kind)> outputs)
    {
        Id = id;
        Title = title;
        Subtitle = subtitle;
        Position = position;
        Size = size;
        HeaderColor = headerColor;
        InputPins = new List<NodePin>();
        OutputPins = new List<NodePin>();

        if (inputs != null)
        {
            foreach (var (label, kind) in inputs)
            {
                InputPins.Add(new NodePin(label, kind));
            }
        }

        if (outputs != null)
        {
            foreach (var (label, kind) in outputs)
            {
                OutputPins.Add(new NodePin(label, kind));
            }
        }
    }

    public abstract object? EvaluateOutput(int outputIndex, NodeEvaluationContext context);
    public abstract void DrawInspector();
}

public class NodePin
{
    public NodePin(string label, NodeValueKind kind)
    {
        Label = label;
        Kind = kind;
    }

    public string Label { get; }
    public NodeValueKind Kind { get; }
}

public class NodeEvaluationContext
{
    public Dictionary<(int nodeId, int outputIndex), object> Cache { get; } = new Dictionary<(int, int), object>();
    public Func<int, int, Type, object?> GetInputValueFunc { get; set; } = null!;
    
    public T? GetInputValue<T>(int nodeId, int inputIndex)
    {
        var result = GetInputValueFunc(nodeId, inputIndex, typeof(T));
        if (result is T t)
            return t;
        return default;
    }
}

