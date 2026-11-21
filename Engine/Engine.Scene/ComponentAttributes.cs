namespace Engine.Scene;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InspectorRangeAttribute(float min, float max) : Attribute
{
    public float Min { get; } = min;

    public float Max { get; } = max;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InspectorLabelAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class)]
public sealed class InspectorTooltipAttribute(string tooltip) : Attribute
{
    public string Tooltip { get; } = tooltip;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InspectorHiddenAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InspectorColorAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InspectorLayerAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InspectorGroupAttribute(string name) : Attribute
{
    public string Name { get; } = name;
}

[AttributeUsage(AttributeTargets.Method)]
public sealed class InspectorButtonAttribute(string label) : Attribute
{
    public string Label { get; } = label;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
public sealed class InspectorAssetDropAttribute(string kind) : Attribute
{
    public string Kind { get; } = kind;
}
