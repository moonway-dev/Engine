using System.Reflection;
using Engine.Editor.DragDrop;
using Engine.Editor.Models;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;
using NumericsVector4 = System.Numerics.Vector4;

namespace Engine.Editor.Inspection;

public static class ComponentInspectorHooks
{
    private static readonly Dictionary<Type, List<Func<EditorContext, Component, bool>>> Hooks = [];

    public static void Register<TComponent>(Func<EditorContext, TComponent, bool> drawer)
        where TComponent : Component
    {
        if (!Hooks.TryGetValue(typeof(TComponent), out List<Func<EditorContext, Component, bool>>? list))
        {
            list = [];
            Hooks[typeof(TComponent)] = list;
        }
        list.Add((context, component) => drawer(context, (TComponent)component));
    }

    internal static bool TryDraw(EditorContext context, Component component)
    {
        if (!Hooks.TryGetValue(component.GetType(), out List<Func<EditorContext, Component, bool>>? list))
            return false;
        foreach (Func<EditorContext, Component, bool> hook in list)
        {
            if (hook(context, component))
                return true;
        }
        return false;
    }
}

public static class ComponentPropertyInspector
{
    private static readonly Dictionary<Type, IReadOnlyList<PropertyInfo>> PropertyCache = [];

    public static void Draw(EditorContext context, Component component)
    {
        if (ComponentInspectorHooks.TryDraw(context, component))
            return;
        ImGui.PushID(component.Id.ToString("N"));
        foreach (PropertyInfo property in GetProperties(component.GetType()))
            DrawProperty(context, component, property);
        ImGui.PopID();
    }

    private static IReadOnlyList<PropertyInfo> GetProperties(Type type)
    {
        if (PropertyCache.TryGetValue(type, out IReadOnlyList<PropertyInfo>? cached))
            return cached;
        List<PropertyInfo> properties = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead
                && property.CanWrite
                && property.GetIndexParameters().Length == 0
                && property.DeclaringType != typeof(Component)
                && property.GetCustomAttribute<InspectorHiddenAttribute>() is null
                && IsSupported(property.PropertyType))
            .OrderBy(property => property.MetadataToken)
            .ToList();
        IReadOnlyList<PropertyInfo> result = properties;
        PropertyCache[type] = result;
        return result;
    }

    private static bool IsSupported(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsEnum)
            return true;
        return type == typeof(bool)
            || type == typeof(int)
            || type == typeof(float)
            || type == typeof(double)
            || type == typeof(string)
            || type == typeof(Vector2)
            || type == typeof(Vector3)
            || type == typeof(Vector4)
            || type == typeof(Quaternion)
            || type == typeof(AssetReference);
    }

    public static void DrawProperty(EditorContext context, Component component, PropertyInfo property) =>
        DrawMember(context, component, component, property);

    public static void DrawProperty(EditorContext context, Component component, object target, PropertyInfo property) =>
        DrawMember(context, component, target, property);

    public static void DrawMember(EditorContext context, Component component, object target, MemberInfo member)
    {
        Type type = Nullable.GetUnderlyingType(MemberType(member)) ?? MemberType(member);
        string label = member.GetCustomAttribute<InspectorLabelAttribute>()?.Label ?? Humanize(member.Name);
        string? tooltip = member.GetCustomAttribute<InspectorTooltipAttribute>()?.Tooltip;
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        if (type != typeof(bool))
            ImGui.SetNextItemWidth(-1f);
        string id = $"##{member.Name}";
        Func<Component, object> read = ReferenceEquals(target, component)
            ? current => MemberValue(current, member)!
            : current => MemberValue(ScriptTarget(current), member)!;
        Action<Component, object> write = ReferenceEquals(target, component)
            ? (current, value) => SetMemberValue(current, member, value)
            : (current, value) => SetMemberValue(ScriptTarget(current), member, value);
        if (type == typeof(bool))
            DrawBool(context, component, member, id, read, write);
        else if (type == typeof(int))
            DrawInt(context, component, member, id, read, write);
        else if (type == typeof(float) || type == typeof(double))
            DrawFloat(context, component, member, id, type, read, write);
        else if (type == typeof(string))
            DrawString(context, component, member, id, read, write);
        else if (type.IsEnum)
            DrawEnum(context, component, member, id, read, write);
        else if (type == typeof(Vector2))
            DrawVector2(context, component, member, id, read, write);
        else if (type == typeof(Vector3))
            DrawVector3(context, component, member, id, read, write);
        else if (type == typeof(Vector4))
            DrawVector4(context, component, member, id, read, write);
        else if (type == typeof(Quaternion))
            DrawQuaternion(context, component, member, id, read, write);
        else if (type == typeof(AssetReference))
            DrawAssetReference(context, component, member, id, read, write);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip ?? $"{label} ({type.Name})");
    }

    internal static Type MemberType(MemberInfo member) => member switch
    {
        PropertyInfo property => property.PropertyType,
        FieldInfo field => field.FieldType,
        _ => throw new ArgumentException($"Unsupported member '{member.Name}'.", nameof(member)),
    };

    private static object? MemberValue(object target, MemberInfo member) => member switch
    {
        PropertyInfo property => property.GetValue(target),
        FieldInfo field => field.GetValue(target),
        _ => throw new ArgumentException($"Unsupported member '{member.Name}'.", nameof(member)),
    };

    private static void SetMemberValue(object target, MemberInfo member, object? value)
    {
        switch (member)
        {
            case PropertyInfo property:
                property.SetValue(target, value);
                break;
            case FieldInfo field:
                field.SetValue(target, value);
                break;
            default:
                throw new ArgumentException($"Unsupported member '{member.Name}'.", nameof(member));
        }
    }

    public static bool IsSupportedType(Type type) => IsSupported(type);

    private static object ScriptTarget(Component component) =>
        component is ScriptComponent script
            ? script.Instance ?? throw new InvalidOperationException("The script is not loaded.")
            : component;

    private static void DrawBool(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        bool value = (bool)read(component)!;
        if (ImGui.Checkbox(id, ref value))
            Execute(context, component, member, read, write, value, false);
    }

    private static void DrawInt(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        int value = (int)read(component)!;
        InspectorRangeAttribute? range = member.GetCustomAttribute<InspectorRangeAttribute>();
        bool changed = range is { } r
            ? ImGui.SliderInt(id, ref value, (int)r.Min, (int)r.Max)
            : ImGui.DragInt(id, ref value, 0.2f);
        if (changed)
            Execute(context, component, member, read, write, value, false);
    }

    private static void DrawFloat(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Type type,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        float value = Convert.ToSingle(read(component));
        InspectorRangeAttribute? range = member.GetCustomAttribute<InspectorRangeAttribute>();
        bool changed = range is { } r
            ? ImGui.SliderFloat(id, ref value, r.Min, r.Max)
            : ImGui.DragFloat(id, ref value, 0.05f);
        if (!changed)
            return;
        Execute(context, component, member, read, write, ConvertFloatValue(value, type));
    }

    internal static object ConvertFloatValue(float value, Type type) => type == typeof(double) ? (object)(double)value : value;

    private static void DrawString(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        string value = (string)read(component)!;
        if (ImGui.InputText(id, ref value, 1024))
            Execute(context, component, member, read, write, value);
        InspectorEditing.EndMergeAfterEdit(context);
    }

    private static void DrawEnum(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        Type type = MemberType(member);
        object value = read(component)!;
        string[] names = Enum.GetNames(type);
        int index = Array.IndexOf(names, Enum.GetName(type, value));
        if (index < 0)
            index = 0;
        if (ImGui.Combo(id, ref index, names, names.Length))
            Execute(context, component, member, read, write, Enum.ToObject(type, index), false);
    }

    private static void DrawVector2(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        Vector2 value = (Vector2)read(component)!;
        NumericsVector2 numerics = new(value.X, value.Y);
        if (ImGui.DragFloat2(id, ref numerics, 0.02f))
            Execute(context, component, member, read, write, new Vector2(numerics.X, numerics.Y));
        InspectorEditing.EndMergeAfterEdit(context);
    }

    private static void DrawVector3(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        Vector3 value = (Vector3)read(component)!;
        NumericsVector3 numerics = new(value.X, value.Y, value.Z);
        bool changed = member.GetCustomAttribute<InspectorColorAttribute>() is not null
            ? ImGui.ColorEdit3(id, ref numerics)
            : ImGui.DragFloat3(id, ref numerics, 0.05f);
        if (changed)
            Execute(context, component, member, read, write, new Vector3(numerics.X, numerics.Y, numerics.Z));
        InspectorEditing.EndMergeAfterEdit(context);
    }

    private static void DrawVector4(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        Vector4 value = (Vector4)read(component)!;
        NumericsVector4 numerics = new(value.X, value.Y, value.Z, value.W);
        if (ImGui.DragFloat4(id, ref numerics, 0.05f))
            Execute(context, component, member, read, write, new Vector4(numerics.X, numerics.Y, numerics.Z, numerics.W));
        InspectorEditing.EndMergeAfterEdit(context);
    }

    private static void DrawQuaternion(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        Quaternion value = (Quaternion)read(component)!;
        Vector3 ypr = value.ToYawPitchRoll();
        NumericsVector3 rotation = new(
            ypr.Y * MathUtil.RadToDeg,
            ypr.X * MathUtil.RadToDeg,
            ypr.Z * MathUtil.RadToDeg);
        if (ImGui.DragFloat3(id, ref rotation, 0.5f))
        {
            Quaternion next = Quaternion.FromYawPitchRoll(
                rotation.Y * MathUtil.DegToRad,
                rotation.X * MathUtil.DegToRad,
                rotation.Z * MathUtil.DegToRad);
            Execute(context, component, member, read, write, next);
        }
        InspectorEditing.EndMergeAfterEdit(context);
    }

    private static void DrawAssetReference(
        EditorContext context,
        Component component,
        MemberInfo member,
        string id,
        Func<Component, object> read,
        Action<Component, object> write)
    {
        AssetReference value = (AssetReference)read(component)!;
        AssetKind kind = InferAssetKind(member.Name);
        string display = ResolveDisplay(context, value, kind);
        AssetDragPayload? drop = AssetDropField.Draw(member.Name, display, kind);
        if (drop is { } payload)
            Execute(context, component, member, read, write, new AssetReference(payload.RelativePath), false);
        if (value.IsEmpty)
            return;
        if (kind == AssetKind.Material && ImGui.SmallButton($"Open{id}"))
            InspectorEditing.TryMaterialAction(context, () => context.SelectMaterial(value));
        ImGui.SameLine();
        if (ImGui.SmallButton($"Clear{id}"))
            Execute(context, component, member, read, write, default(AssetReference), false);
    }

    private static void Execute(
        EditorContext context,
        Component component,
        MemberInfo member,
        Func<Component, object> read,
        Action<Component, object> write,
        object value,
        bool merge = true)
    {
        InspectorEditing.SetComponentProperty(
            context,
            component.Id,
            $"{component.GetType().Name}.{member.Name}",
            current => read(current),
            (current, next) => write(current, next),
            value,
            merge);
    }

    private static string ResolveDisplay(EditorContext context, AssetReference value, AssetKind kind)
    {
        if (value.IsEmpty)
            return string.Empty;
        if (kind != AssetKind.Material)
            return value.Key;
        try
        {
            return Path.GetFileNameWithoutExtension(context.Materials.ResolvePath(value));
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            return "Missing Material";
        }
    }

    private static AssetKind InferAssetKind(string propertyName)
    {
        if (propertyName.Contains("Material", StringComparison.OrdinalIgnoreCase))
            return AssetKind.Material;
        if (propertyName.Contains("Texture", StringComparison.OrdinalIgnoreCase))
            return AssetKind.Texture;
        if (propertyName.Contains("Mesh", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Model", StringComparison.OrdinalIgnoreCase))
            return AssetKind.Mesh;
        return AssetKind.Unknown;
    }

    private static string Humanize(string name)
    {
        System.Text.StringBuilder builder = new(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];
            if (i > 0
                && char.IsUpper(current)
                && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                builder.Append(' ');
            builder.Append(current);
        }
        return builder.ToString();
    }
}
