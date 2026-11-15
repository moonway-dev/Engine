using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Editor.DragDrop;
using Engine.Editor.Models;
using Engine.Mathematics;
using Engine.Rendering;
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
        PrepareField("Name");
        if (ImGui.InputText("##Name", ref name, 256))
            SetObjectProperty(objectId, nameof(GameObject.Name), current => current.Name, (current, value) => current.Name = value, name);
        EndMergeAfterEdit();

        bool active = gameObject.Active;
        if (ImGui.Checkbox("Active", ref active))
            SetObjectProperty(objectId, nameof(GameObject.Active), current => current.Active, (current, value) => current.Active = value, active, false);

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
                SetComponentProperty(component.Id, nameof(Component.Enabled), current => current.Enabled, (current, value) => current.Enabled = value, enabled, false);
            ImGui.SameLine(0f, 8f);
            bool expanded = ImGui.CollapsingHeader(component.ComponentTypeName, ImGuiTreeNodeFlags.DefaultOpen);
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
                DrawComponent(component);
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
            SetObjectProperty(id, nameof(Transform.LocalPosition), current => current.Transform.LocalPosition, (current, value) => current.Transform.LocalPosition = value, position);
        EndMergeAfterEdit();

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
            SetObjectProperty(id, nameof(Transform.LocalRotation), current => current.Transform.LocalRotation, (current, next) => current.Transform.LocalRotation = next, value);
        }
        EndMergeAfterEdit();

        Vector3 scale = gameObject.Transform.LocalScale;
        if (DragVector3("Scale", ref scale, 0.05f))
            SetObjectProperty(id, nameof(Transform.LocalScale), current => current.Transform.LocalScale, (current, value) => current.Transform.LocalScale = value, scale);
        EndMergeAfterEdit();
    }

    private void DrawComponent(Component component)
    {
        switch (component)
        {
            case CameraComponent camera:
                DrawCamera(camera);
                break;
            case LightComponent light:
                DrawLight(light);
                break;
            case MeshRendererComponent mesh:
                DrawMesh(mesh);
                break;
            default:
                ImGui.TextDisabled("No editor binding is registered.");
                break;
        }
    }

    private void DrawCamera(CameraComponent camera)
    {
        Guid id = camera.Id;
        float fov = camera.FieldOfViewY * MathUtil.RadToDeg;
        PrepareField("Field of View");
        if (ImGui.DragFloat("##Field of View", ref fov, 0.5f, 10f, 170f))
            SetComponentProperty(id, nameof(CameraComponent.FieldOfViewY), current => ((CameraComponent)current).FieldOfViewY, (current, value) => ((CameraComponent)current).FieldOfViewY = value, fov * MathUtil.DegToRad);
        EndMergeAfterEdit();
        DrawComponentFloat(id, "Near Plane", nameof(CameraComponent.NearPlane), current => ((CameraComponent)current).NearPlane, (current, value) => ((CameraComponent)current).NearPlane = value, 0.02f, 0.001f, 100f);
        DrawComponentFloat(id, "Far Plane", nameof(CameraComponent.FarPlane), current => ((CameraComponent)current).FarPlane, (current, value) => ((CameraComponent)current).FarPlane = value, 1f, 0.01f, 100000f);
    }

    private void DrawLight(LightComponent light)
    {
        Guid id = light.Id;
        int kind = (int)light.Kind;
        string[] kinds = Enum.GetNames<LightKind>();
        PrepareField("Kind");
        if (ImGui.Combo("##Kind", ref kind, kinds, kinds.Length))
            SetComponentProperty(id, nameof(LightComponent.Kind), current => ((LightComponent)current).Kind, (current, value) => ((LightComponent)current).Kind = value, (LightKind)kind, false);

        Vector3 color = light.Color;
        NumericsVector3 colorValue = new(color.X, color.Y, color.Z);
        PrepareField("Color");
        if (ImGui.ColorEdit3("##Color", ref colorValue))
            SetComponentProperty(id, nameof(LightComponent.Color), current => ((LightComponent)current).Color, (current, value) => ((LightComponent)current).Color = value, new Vector3(colorValue.X, colorValue.Y, colorValue.Z));
        EndMergeAfterEdit();
        DrawComponentFloat(id, "Intensity", nameof(LightComponent.Intensity), current => ((LightComponent)current).Intensity, (current, value) => ((LightComponent)current).Intensity = value, 0.2f, 0f, 100000f);
        if (light.Kind == LightKind.Point)
            DrawComponentFloat(id, "Range", nameof(LightComponent.Range), current => ((LightComponent)current).Range, (current, value) => ((LightComponent)current).Range = value, 0.2f, 0f, 100000f);
    }

    private void DrawMesh(MeshRendererComponent mesh)
    {
        Guid id = mesh.Id;
        string resourceKey = mesh.ResourceKey;
        PrepareField("Resource");
        if (ImGui.InputText("##Resource", ref resourceKey, 1024))
            SetComponentProperty(id, nameof(MeshRendererComponent.ResourceKey), current => ((MeshRendererComponent)current).ResourceKey, (current, value) => ((MeshRendererComponent)current).ResourceKey = value, resourceKey);
        EndMergeAfterEdit();
        if (ImGui.BeginDragDropTarget())
        {
            AssetDragPayload? payload = ImGuiDragPayloads.AcceptAsset();
            if (payload is { Kind: AssetKind.Mesh } meshPayload)
                SetComponentProperty(id, nameof(MeshRendererComponent.ResourceKey), current => ((MeshRendererComponent)current).ResourceKey, (current, value) => ((MeshRendererComponent)current).ResourceKey = value, meshPayload.RelativePath, false);
            ImGui.EndDragDropTarget();
        }

        PrepareField("Model Part");
        string partId = mesh.PartId;
        if (ImGui.InputText("##ModelPart", ref partId, 1024))
            SetComponentProperty(id, nameof(MeshRendererComponent.PartId), current => ((MeshRendererComponent)current).PartId, (current, value) => ((MeshRendererComponent)current).PartId = value, partId);
        EndMergeAfterEdit();

        int lodMode = (int)mesh.LodMode;
        string[] lodModes = Enum.GetNames<MeshLodMode>();
        PrepareField("LOD Mode");
        if (ImGui.Combo("##LodMode", ref lodMode, lodModes, lodModes.Length))
            SetComponentProperty(id, nameof(MeshRendererComponent.LodMode), current => ((MeshRendererComponent)current).LodMode, (current, value) => ((MeshRendererComponent)current).LodMode = value, (MeshLodMode)lodMode, false);
        int lodCount = _context.ModelAssets?.RequestMesh(mesh.ResourceKey, mesh.PartId)?.LodCount ?? 1;
        int forcedLod = Math.Clamp(mesh.ForcedLodLevel, 0, Math.Max(0, lodCount - 1));
        PrepareField("Forced LOD");
        ImGui.BeginDisabled(lodCount <= 1);
        if (ImGui.SliderInt("##ForcedLod", ref forcedLod, 0, Math.Max(0, lodCount - 1)))
            SetComponentProperty(id, nameof(MeshRendererComponent.ForcedLodLevel), current => ((MeshRendererComponent)current).ForcedLodLevel, (current, value) => ((MeshRendererComponent)current).ForcedLodLevel = value, forcedLod);
        ImGui.EndDisabled();
        float lodBias = mesh.LodBias;
        PrepareField("LOD Bias");
        if (ImGui.DragFloat("##LodBias", ref lodBias, 0.02f, 0.05f, 4f))
            SetComponentProperty(id, nameof(MeshRendererComponent.LodBias), current => ((MeshRendererComponent)current).LodBias, (current, value) => ((MeshRendererComponent)current).LodBias = value, lodBias);
        EndMergeAfterEdit();
        if (_context.ModelAssets is { } assets && !string.IsNullOrWhiteSpace(mesh.ResourceKey)
            && (mesh.ResourceKey.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
                || mesh.ResourceKey.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            && assets.IsMeshImportFailed(mesh.ResourceKey))
        {
            ImGui.TextUnformatted("Model import failed");
            if (ImGui.Button("Retry Import"))
                assets.RetryMeshImport(mesh.ResourceKey);
        }

        PrepareField("Material Asset");
        string materialName = "None — drop material here";
        if (!mesh.MaterialAsset.IsEmpty)
        {
            try
            {
                materialName = Path.GetFileNameWithoutExtension(_context.Materials.ResolvePath(mesh.MaterialAsset));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                materialName = "Missing Material";
            }
        }
        if (ImGui.Button(materialName + "##MaterialAsset", new NumericsVector2(-1f, 0f)) && !mesh.MaterialAsset.IsEmpty)
            TryMaterialAction(() => _context.SelectMaterial(mesh.MaterialAsset));
        if (ImGui.BeginDragDropTarget())
        {
            if (ImGuiDragPayloads.AcceptAsset() is { Kind: AssetKind.Material } payload)
                TryMaterialAction(() => _context.AssignMaterial(id, payload.RelativePath));
            ImGui.EndDragDropTarget();
        }
        if (!mesh.MaterialAsset.IsEmpty)
        {
            if (ImGui.SmallButton("Open Material"))
                TryMaterialAction(() => _context.SelectMaterial(mesh.MaterialAsset));
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear Material"))
                _context.AssignMaterial(id, default);
            ImGui.TextWrapped("Shared material asset. Edit it to update all referencing objects.");
            return;
        }
        if (ImGui.SmallButton("Create Material from Inline"))
            TryMaterialAction(() =>
            {
                string path = "Materials/Material_" + Guid.NewGuid().ToString("N")[..8] + ".mat";
                AssetReference reference = _context.CreateMaterial(path, properties: MaterialProperties.FromComponent(mesh));
                _context.AssignMaterial(id, reference);
            });
        Vector3 albedo = mesh.AlbedoColor;
        NumericsVector3 color = new(albedo.X, albedo.Y, albedo.Z);
        PrepareField("Albedo");
        if (ImGui.ColorEdit3("##Albedo", ref color))
            SetComponentProperty(id, nameof(MeshRendererComponent.AlbedoColor), current => ((MeshRendererComponent)current).AlbedoColor, (current, value) => ((MeshRendererComponent)current).AlbedoColor = value, new Vector3(color.X, color.Y, color.Z));
        EndMergeAfterEdit();
        DrawComponentFloat(id, "Metallic", nameof(MeshRendererComponent.Metallic), current => ((MeshRendererComponent)current).Metallic, (current, value) => ((MeshRendererComponent)current).Metallic = value, 0.02f, 0f, 1f);
        DrawComponentFloat(id, "Roughness", nameof(MeshRendererComponent.Roughness), current => ((MeshRendererComponent)current).Roughness, (current, value) => ((MeshRendererComponent)current).Roughness = value, 0.02f, 0f, 1f);
        DrawComponentFloat(id, "Emissive", nameof(MeshRendererComponent.EmissiveIntensity), current => ((MeshRendererComponent)current).EmissiveIntensity, (current, value) => ((MeshRendererComponent)current).EmissiveIntensity = value, 0.05f, 0f, 100000f);

        Vector2 uv = mesh.UvScale;
        NumericsVector2 uvValue = new(uv.X, uv.Y);
        PrepareField("UV Scale");
        if (ImGui.DragFloat2("##UV Scale", ref uvValue, 0.02f, 0.001f, 1000f))
            SetComponentProperty(id, nameof(MeshRendererComponent.UvScale), current => ((MeshRendererComponent)current).UvScale, (current, value) => ((MeshRendererComponent)current).UvScale = value, new Vector2(uvValue.X, uvValue.Y));
        EndMergeAfterEdit();
    }

    private void TryMaterialAction(Action action)
    {
        try { action(); }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
        {
            _context.Error = exception.Message;
        }
    }

    private void DrawComponentFloat(
        Guid id,
        string label,
        string property,
        Func<Component, float> read,
        Action<Component, float> write,
        float speed,
        float min,
        float max)
    {
        Component? component = _context.Document.Scene.FindComponentById(id);
        if (component is null)
            return;
        float value = read(component);
        PrepareField(label);
        if (ImGui.DragFloat($"##{label}", ref value, speed, min, max))
            SetComponentProperty(id, property, read, write, value);
        EndMergeAfterEdit();
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

    private void SetObjectProperty<T>(
        Guid objectId,
        string property,
        Func<GameObject, T> read,
        Action<GameObject, T> write,
        T value,
        bool allowMerge = true)
    {
        SceneDocument document = _context.Document;
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

    private void SetComponentProperty<T>(
        Guid componentId,
        string property,
        Func<Component, T> read,
        Action<Component, T> write,
        T value,
        bool allowMerge = true)
    {
        SceneDocument document = _context.Document;
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

    private void EndMergeAfterEdit()
    {
        if (ImGui.IsItemDeactivatedAfterEdit())
            _context.History.EndMergeGroup();
    }

    private static void PrepareField(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, EditorTheme.MutedText);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();
        ImGui.SetNextItemWidth(-1f);
    }

    private static bool DragVector3(string label, ref Vector3 value, float speed)
    {
        NumericsVector3 vector = new(value.X, value.Y, value.Z);
        PrepareField(label);
        bool changed = ImGui.DragFloat3($"##{label}", ref vector, speed);
        if (changed)
            value = new Vector3(vector.X, vector.Y, vector.Z);
        return changed;
    }
}
