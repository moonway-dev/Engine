using System;
using System.Collections.Generic;
using System.Linq;
using EngineVec2 = Engine.Math.Vector2;
using EngineVec4 = Engine.Math.Vector4;

namespace Engine.Editor.Graphs;

public enum NodeValueKind
{
    Color,
    Scalar,
    UV,
    Normal,
    Vector,
    Emission
}

public class NodeValueTypeDefinition
{
    public NodeValueKind Kind { get; }
    public string Name { get; }
    public Type RuntimeType { get; }
    public int ComponentCount { get; }
    public string[] ComponentNames { get; }
    public bool CanSplit { get; }
    public bool CanPromote { get; }
    public NodeValueKind[] CompatibleTypes { get; }
    public Func<object, object>? DefaultConverter { get; }

    public NodeValueTypeDefinition(
        NodeValueKind kind,
        string name,
        Type runtimeType,
        int componentCount,
        string[] componentNames,
        bool canSplit,
        bool canPromote,
        NodeValueKind[] compatibleTypes,
        Func<object, object>? defaultConverter = null)
    {
        Kind = kind;
        Name = name;
        RuntimeType = runtimeType;
        ComponentCount = componentCount;
        ComponentNames = componentNames;
        CanSplit = canSplit;
        CanPromote = canPromote;
        CompatibleTypes = compatibleTypes;
        DefaultConverter = defaultConverter;
    }
}

public static class NodeValueTypeSystem
{
    private static readonly Dictionary<NodeValueKind, NodeValueTypeDefinition> _definitions = new();
    private static readonly Dictionary<(NodeValueKind, NodeValueKind), Func<object, object>> _converters = new();

    static NodeValueTypeSystem()
    {
        RegisterType(NodeValueKind.Color, "Color", typeof(EngineVec4), 4, new[] { "R", "G", "B", "A" }, true, false,
            new[] { NodeValueKind.Scalar, NodeValueKind.Vector, NodeValueKind.Emission },
            value => value is float f ? new EngineVec4(f, f, f, 1f) : value);

        RegisterType(NodeValueKind.Scalar, "Scalar", typeof(float), 1, new[] { "Value" }, false, true,
            new[] { NodeValueKind.Color, NodeValueKind.Vector },
            value => value is EngineVec4 v ? (v.X + v.Y + v.Z) / 3f : value);

        RegisterType(NodeValueKind.UV, "UV", typeof(EngineVec2), 2, new[] { "U", "V" }, true, false,
            new[] { NodeValueKind.Vector },
            value => value is EngineVec4 v ? new EngineVec2(v.X, v.Y) : value);

        RegisterType(NodeValueKind.Normal, "Normal", typeof(EngineVec4), 3, new[] { "X", "Y", "Z" }, true, false,
            Array.Empty<NodeValueKind>(),
            null);

        RegisterType(NodeValueKind.Vector, "Vector", typeof(EngineVec4), 4, new[] { "X", "Y", "Z", "W" }, true, false,
            new[] { NodeValueKind.Color, NodeValueKind.Scalar, NodeValueKind.UV },
            value => value is float f ? new EngineVec4(f, f, f, 1f) : value);

        RegisterType(NodeValueKind.Emission, "Emission", typeof(EngineVec4), 4, new[] { "R", "G", "B", "A" }, true, false,
            new[] { NodeValueKind.Color },
            value => value is float f ? new EngineVec4(f, f, f, 1f) : value);

        RegisterConverter(NodeValueKind.Scalar, NodeValueKind.Color, value =>
            value is float f ? new EngineVec4(f, f, f, 1f) : value);

        RegisterConverter(NodeValueKind.Color, NodeValueKind.Scalar, value =>
            value is EngineVec4 v ? (v.X + v.Y + v.Z) / 3f : 0.5f);

        RegisterConverter(NodeValueKind.Color, NodeValueKind.Vector, value =>
            value is EngineVec4 v ? v : new EngineVec4(0.5f, 0.5f, 0.5f, 1f));

        RegisterConverter(NodeValueKind.Vector, NodeValueKind.Color, value =>
            value is EngineVec4 v ? v : new EngineVec4(0.5f, 0.5f, 0.5f, 1f));

        RegisterConverter(NodeValueKind.UV, NodeValueKind.Vector, value =>
            value is EngineVec2 v ? new EngineVec4(v.X, v.Y, 0f, 1f) : new EngineVec4(0.5f, 0.5f, 0f, 1f));

        RegisterConverter(NodeValueKind.Vector, NodeValueKind.UV, value =>
            value is EngineVec4 v ? new EngineVec2(v.X, v.Y) : new EngineVec2(0.5f, 0.5f));
    }

    private static void RegisterType(NodeValueKind kind, string name, Type runtimeType, int componentCount,
        string[] componentNames, bool canSplit, bool canPromote, NodeValueKind[] compatibleTypes,
        Func<object, object>? defaultConverter)
    {
        _definitions[kind] = new NodeValueTypeDefinition(kind, name, runtimeType, componentCount, componentNames,
            canSplit, canPromote, compatibleTypes, defaultConverter);
    }

    private static void RegisterConverter(NodeValueKind from, NodeValueKind to, Func<object, object> converter)
    {
        _converters[(from, to)] = converter;
    }

    public static NodeValueTypeDefinition? GetDefinition(NodeValueKind kind)
    {
        return _definitions.TryGetValue(kind, out var def) ? def : null;
    }

    public static bool AreTypesCompatible(NodeValueKind outputKind, NodeValueKind inputKind)
    {
        if (outputKind == inputKind)
            return true;

        var outputDef = GetDefinition(outputKind);
        if (outputDef == null)
            return false;

        return outputDef.CompatibleTypes.Contains(inputKind);
    }

    public static object? ConvertValue(object? value, NodeValueKind fromKind, NodeValueKind toKind)
    {
        if (value == null)
            return null;

        if (fromKind == toKind)
            return value;

        if (_converters.TryGetValue((fromKind, toKind), out var converter))
        {
            return converter(value);
        }

        var fromDef = GetDefinition(fromKind);
        if (fromDef?.DefaultConverter != null)
        {
            return fromDef.DefaultConverter(value);
        }

        return value;
    }

    public static object? ExtractComponent(object? value, NodeValueKind kind, int componentIndex)
    {
        if (value == null)
            return null;

        var def = GetDefinition(kind);
        if (def == null || componentIndex < 0 || componentIndex >= def.ComponentCount)
            return null;

        return kind switch
        {
            NodeValueKind.Color when value is EngineVec4 v => componentIndex switch
            {
                0 => v.X,
                1 => v.Y,
                2 => v.Z,
                3 => v.W,
                _ => null
            },
            NodeValueKind.Scalar when value is float f => f,
            NodeValueKind.UV when value is EngineVec2 v => componentIndex switch
            {
                0 => v.X,
                1 => v.Y,
                _ => null
            },
            NodeValueKind.Normal when value is EngineVec4 v => componentIndex switch
            {
                0 => v.X,
                1 => v.Y,
                2 => v.Z,
                _ => null
            },
            NodeValueKind.Vector when value is EngineVec4 v => componentIndex switch
            {
                0 => v.X,
                1 => v.Y,
                2 => v.Z,
                3 => v.W,
                _ => null
            },
            NodeValueKind.Emission when value is EngineVec4 v => componentIndex switch
            {
                0 => v.X,
                1 => v.Y,
                2 => v.Z,
                3 => v.W,
                _ => null
            },
            _ => null
        };
    }

    public static object? CombineComponents(object?[] components, NodeValueKind targetKind)
    {
        if (components == null || components.Length == 0)
            return null;

        var def = GetDefinition(targetKind);
        if (def == null)
            return null;

        return targetKind switch
        {
            NodeValueKind.Color => new EngineVec4(
                components.Length > 0 && components[0] is float r ? r : 0f,
                components.Length > 1 && components[1] is float g ? g : 0f,
                components.Length > 2 && components[2] is float b ? b : 0f,
                components.Length > 3 && components[3] is float a ? a : 1f),
            NodeValueKind.Scalar => components[0] is float f ? f : 0f,
            NodeValueKind.UV => new EngineVec2(
                components.Length > 0 && components[0] is float u ? u : 0.5f,
                components.Length > 1 && components[1] is float v ? v : 0.5f),
            NodeValueKind.Normal => new EngineVec4(
                components.Length > 0 && components[0] is float x ? x : 0f,
                components.Length > 1 && components[1] is float y ? y : 0f,
                components.Length > 2 && components[2] is float z ? z : 0f,
                0f),
            NodeValueKind.Vector => new EngineVec4(
                components.Length > 0 && components[0] is float x ? x : 0f,
                components.Length > 1 && components[1] is float y ? y : 0f,
                components.Length > 2 && components[2] is float z ? z : 0f,
                components.Length > 3 && components[3] is float w ? w : 0f),
            NodeValueKind.Emission => new EngineVec4(
                components.Length > 0 && components[0] is float r ? r : 0f,
                components.Length > 1 && components[1] is float g ? g : 0f,
                components.Length > 2 && components[2] is float b ? b : 0f,
                components.Length > 3 && components[3] is float a ? a : 1f),
            _ => null
        };
    }

    public static bool CanSplit(NodeValueKind kind)
    {
        return GetDefinition(kind)?.CanSplit ?? false;
    }

    public static bool CanPromote(NodeValueKind kind)
    {
        return GetDefinition(kind)?.CanPromote ?? false;
    }

    public static string[] GetComponentNames(NodeValueKind kind)
    {
        return GetDefinition(kind)?.ComponentNames ?? Array.Empty<string>();
    }

    public static int GetComponentCount(NodeValueKind kind)
    {
        return GetDefinition(kind)?.ComponentCount ?? 0;
    }

    public static object GetDefaultValue(NodeValueKind kind)
    {
        return kind switch
        {
            NodeValueKind.Color => new EngineVec4(0.5f, 0.5f, 0.5f, 1f),
            NodeValueKind.Scalar => 0.5f,
            NodeValueKind.UV => new EngineVec2(0f, 0f),
            NodeValueKind.Normal => new EngineVec4(0f, 0f, 1f, 0f),
            NodeValueKind.Vector => new EngineVec4(0f, 0f, 0f, 0f),
            NodeValueKind.Emission => new EngineVec4(0f, 0f, 0f, 1f),
            _ => new EngineVec4(0.5f, 0.5f, 0.5f, 1f)
        };
    }
}

