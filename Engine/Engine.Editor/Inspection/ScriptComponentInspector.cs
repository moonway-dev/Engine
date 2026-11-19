namespace Engine.Editor.Inspection;

using System.Reflection;
using Engine.Scene;
using ImGuiNET;

internal static class ScriptComponentInspector
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
            return;
        _registered = true;
        ComponentInspectorHooks.Register<ScriptComponent>(Draw);
    }

    private static bool Draw(EditorContext context, ScriptComponent component)
    {
        ImGui.PushID(component.Id.ToString("N"));
        DrawEnabled(context, component);
        DrawScriptSelector(context, component);
        if (component.Instance is { } instance)
        {
            ImGui.Separator();
            foreach (MemberInfo member in GetInstanceMembers(instance.GetType()))
                ComponentPropertyInspector.DrawMember(context, component, instance, member);
        }
        else if (!string.IsNullOrWhiteSpace(component.Script))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
            ImGui.TextUnformatted("Script is not loaded.");
            ImGui.PopStyleColor();
        }
        ImGui.PopID();
        return true;
    }

    private static void DrawEnabled(EditorContext context, ScriptComponent component)
    {
        bool enabled = component.Enabled;
        if (ImGui.Checkbox("##scriptEnabled", ref enabled))
            InspectorEditing.SetComponentProperty(
                context,
                component.Id,
                nameof(Component.Enabled),
                current => current.Enabled,
                (current, value) => current.Enabled = value,
                enabled,
                false);
        ImGui.SameLine();
    }

    private static void DrawScriptSelector(EditorContext context, ScriptComponent component)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted("Script");
        ImGui.PopStyleColor();
        ImGui.SetNextItemWidth(-1f);
        string current = component.Script;
        string preview = string.IsNullOrWhiteSpace(current) ? "(none)" : current;
        if (!ImGui.BeginCombo("##scriptSelector", preview))
            return;
        if (ImGui.Selectable("(none)", string.IsNullOrWhiteSpace(current))
            && !string.IsNullOrWhiteSpace(current))
            SetScript(context, component, string.Empty);
        foreach (ScriptDescription description in ScriptHost.AvailableScripts)
        {
            bool selected = description.Key.Equals(current, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(description.Key, selected) && !selected)
                SetScript(context, component, description.Key);
        }
        ImGui.EndCombo();
    }

    private static void SetScript(EditorContext context, ScriptComponent component, string value)
    {
        InspectorEditing.SetComponentProperty<string>(
            context,
            component.Id,
            nameof(ScriptComponent.Script),
            current => ((ScriptComponent)current).Script,
            (current, next) =>
            {
                ScriptComponent script = (ScriptComponent)current;
                script.Script = next;
                script.RecreateInstance();
            },
            value,
            false);
    }

    internal static IReadOnlyList<MemberInfo> GetInstanceMembers(Type type)
    {
        List<MemberInfo> members = [];
        HashSet<string> names = [];
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
                continue;
            if (property.GetCustomAttribute<InspectorHiddenAttribute>() is not null)
                continue;
            if (property.Name == nameof(ScriptComponent.Script))
                continue;
            if (!ComponentPropertyInspector.IsSupportedType(property.PropertyType))
                continue;
            members.Add(property);
            names.Add(property.Name);
        }
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsInitOnly || field.IsLiteral)
                continue;
            if (field.GetCustomAttribute<InspectorHiddenAttribute>() is not null)
                continue;
            if (!ComponentPropertyInspector.IsSupportedType(field.FieldType))
                continue;
            if (!names.Add(field.Name))
                continue;
            members.Add(field);
        }
        return members.OrderBy(member => member.MetadataToken).ToList();
    }
}
