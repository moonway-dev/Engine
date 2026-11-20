using System.Reflection;
using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Editor.Inspection;
using Engine.Editor.Models;
using Engine.Mathematics;
using Engine.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace Engine.Editor.Panels;

public sealed class InspectorPanel : EditorPanel
{
    private readonly EditorContext _context;

    public override string Title => "Inspector";

    public InspectorPanel(EditorContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public override void Draw()
    {
        if (!IsOpen)
            return;
        bool open = IsOpen;
        if (!ImGui.Begin(Title, ref open))
        {
            IsOpen = open;
            ImGui.End();
            return;
        }
        IsOpen = open;
        if (_context.SelectedMaterial is { } materialDocument)
        {
            try
            {
                MaterialInspector.Draw(_context, materialDocument);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException)
            {
                _context.Error = exception.Message;
            }
            ImGui.End();
            return;
        }
        GameObject? gameObject = _context.Document.SelectedObject;
        if (gameObject is null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
            ImGui.TextWrapped("Select an object in the hierarchy.");
            ImGui.PopStyleColor();
            ImGui.End();
            return;
        }
        DrawObject(gameObject);
        ImGui.End();
    }

    private void DrawObject(GameObject gameObject)
    {
        Guid objectId = gameObject.Id;
        string name = gameObject.Name;
        InspectorEditing.PrepareField("Name");
        if (ImGui.InputText("##Name", ref name, 256))
            InspectorEditing.SetObjectProperty(_context, objectId, nameof(GameObject.Name), current => current.Name, (current, value) => current.Name = value, name);
        InspectorEditing.EndMergeAfterEdit(_context);

        bool active = gameObject.Active;
        if (ImGui.Checkbox("Active", ref active))
            InspectorEditing.SetObjectProperty(_context, objectId, nameof(GameObject.Active), current => current.Active, (current, value) => current.Active = value, active, false);

        if (ImGui.CollapsingHeader("Transform", ImGuiTreeNodeFlags.DefaultOpen))
            DrawTransform(gameObject);

        foreach (Component component in gameObject.Components.ToArray())
        {
            ImGui.PushID(component.Id.ToString("N"));
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            bool enabled = component.Enabled;
            if (ImGui.Checkbox("##Enabled", ref enabled))
                InspectorEditing.SetComponentProperty(_context, component.Id, nameof(Component.Enabled), current => current.Enabled, (current, value) => current.Enabled = value, enabled, false);
            ImGui.SameLine(0f, 8f);
            bool expanded = ImGui.CollapsingHeader(component.ComponentTypeName, ImGuiTreeNodeFlags.DefaultOpen);
            if (ImGui.IsItemHovered())
            {
                string? componentTooltip = component.GetType().GetCustomAttribute<InspectorTooltipAttribute>(inherit: true)?.Tooltip;
                if (componentTooltip is not null)
                    ImGui.SetTooltip(componentTooltip);
            }
            bool remove = false;
            if (ImGui.BeginPopupContextItem())
            {
                remove = ImGui.MenuItem("Remove Component");
                ImGui.EndPopup();
            }
            if (remove)
            {
                RemoveComponent(objectId, component.Id);
                ImGui.PopID();
                break;
            }
            if (expanded)
            {
                ImGui.Indent(8f);
                ComponentPropertyInspector.Draw(_context, component);
                ImGui.Unindent(8f);
            }
            ImGui.PopID();
        }
        ImGui.Separator();
        DrawAddComponent(gameObject);
    }

    private void DrawTransform(GameObject gameObject)
    {
        Guid id = gameObject.Id;
        Vector3 position = gameObject.Transform.LocalPosition;
        if (DragVector3("Position", ref position, 0.05f))
            InspectorEditing.SetObjectProperty(_context, id, nameof(Transform.LocalPosition), current => current.Transform.LocalPosition, (current, value) => current.Transform.LocalPosition = value, position);
        InspectorEditing.EndMergeAfterEdit(_context);

        Vector3 ypr = gameObject.Transform.LocalRotation.ToYawPitchRoll();
        Vector3 rotation = new(
            ypr.Y * MathUtil.RadToDeg,
            ypr.X * MathUtil.RadToDeg,
            ypr.Z * MathUtil.RadToDeg);
        if (DragVector3("Rotation", ref rotation, 0.5f))
        {
            Quaternion value = Quaternion.FromYawPitchRoll(
                rotation.Y * MathUtil.DegToRad,
                rotation.X * MathUtil.DegToRad,
                rotation.Z * MathUtil.DegToRad);
            InspectorEditing.SetObjectProperty(_context, id, nameof(Transform.LocalRotation), current => current.Transform.LocalRotation, (current, next) => current.Transform.LocalRotation = next, value);
        }
        InspectorEditing.EndMergeAfterEdit(_context);

        Vector3 scale = gameObject.Transform.LocalScale;
        if (DragVector3("Scale", ref scale, 0.05f))
        {
            scale = EditorContext.ClampScale(scale);
            InspectorEditing.SetObjectProperty(_context, id, nameof(Transform.LocalScale), current => current.Transform.LocalScale, (current, value) => current.Transform.LocalScale = value, scale);
        }
        InspectorEditing.EndMergeAfterEdit(_context);
    }

    private void DrawAddComponent(GameObject gameObject)
    {
        if (ImGui.Button("Add Component", new NumericsVector2(-1f, 0f)))
            ImGui.OpenPopup("AddComponentPopup");
        if (!ImGui.BeginPopup("AddComponentPopup"))
            return;
        foreach (string key in _context.Document.Scene.ComponentRegistry.RegisteredKeys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
        {
            Type? type = _context.Document.Scene.ComponentRegistry.GetRegisteredType(key);
            if (type is null || gameObject.GetComponent(type) is not null)
                continue;
            if (ImGui.MenuItem(key))
                AddComponent(gameObject.Id, key);
        }
        ImGui.EndPopup();
    }

    private void AddComponent(Guid objectId, string componentKey)
    {
        SceneDocument document = _context.Document;
        document.Execute(new SnapshotCommand("Add Component", document, () =>
        {
            GameObject current = document.Scene.FindById(objectId)
                ?? throw new InvalidOperationException("The GameObject no longer exists.");
            current.AddComponent(componentKey);
        }), false);
    }

    private void RemoveComponent(Guid objectId, Guid componentId)
    {
        SceneDocument document = _context.Document;
        document.Execute(new SnapshotCommand("Remove Component", document, () =>
        {
            GameObject current = document.Scene.FindById(objectId)
                ?? throw new InvalidOperationException("The GameObject no longer exists.");
            Component? component = document.Scene.FindComponentById(componentId);
            if (component is not null)
                current.RemoveComponent(component);
        }), false);
    }

    private static bool DragVector3(string label, ref Vector3 value, float speed)
    {
        NumericsVector3 vector = new(value.X, value.Y, value.Z);
        InspectorEditing.PrepareField(label);
        bool changed = ImGui.DragFloat3($"##{label}", ref vector, speed);
        if (changed)
            value = new Vector3(vector.X, vector.Y, vector.Z);
        return changed;
    }
}
