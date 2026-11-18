using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Scene;
using ImGuiNET;

namespace Engine.Editor.Inspection;

internal static class InspectorEditing
{
    internal static void SetObjectProperty<T>(
        EditorContext context,
        Guid objectId,
        string property,
        Func<GameObject, T> read,
        Action<GameObject, T> write,
        T value,
        bool allowMerge = true)
    {
        SceneDocument document = context.Document;
        GameObject current = document.Scene.FindById(objectId)
            ?? throw new InvalidOperationException("The GameObject no longer exists.");
        T before = read(current);
        if (EqualityComparer<T>.Default.Equals(before, value))
            return;
        document.Execute(new PropertyCommand<T>(
            $"Set {property}",
            (objectId, property),
            () => read(document.Scene.FindById(objectId)
                ?? throw new InvalidOperationException("The GameObject no longer exists.")),
            next => write(document.Scene.FindById(objectId)
                ?? throw new InvalidOperationException("The GameObject no longer exists."), next),
            before,
            value), allowMerge);
    }

    internal static void SetComponentProperty<T>(
        EditorContext context,
        Guid componentId,
        string property,
        Func<Component, T> read,
        Action<Component, T> write,
        T value,
        bool allowMerge = true)
    {
        SceneDocument document = context.Document;
        Component current = document.Scene.FindComponentById(componentId)
            ?? throw new InvalidOperationException("The component no longer exists.");
        T before = read(current);
        if (EqualityComparer<T>.Default.Equals(before, value))
            return;
        document.Execute(new PropertyCommand<T>(
            $"Set {property}",
            (componentId, property),
            () => read(document.Scene.FindComponentById(componentId)
                ?? throw new InvalidOperationException("The component no longer exists.")),
            next => write(document.Scene.FindComponentById(componentId)
                ?? throw new InvalidOperationException("The component no longer exists."), next),
            before,
            value), allowMerge);
    }

    internal static void EndMergeAfterEdit(EditorContext context)
    {
        if (ImGui.IsItemDeactivatedAfterEdit())
            context.History.EndMergeGroup();
    }

    internal static void PrepareField(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.SetNextItemWidth(-1f);
    }

    internal static void DrawItemTooltip(string? tooltip)
    {
        if (tooltip is not null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    internal static void TryMaterialAction(EditorContext context, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            context.Error = exception.Message;
        }
    }
}
