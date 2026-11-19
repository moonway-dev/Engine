using Engine.Editor.DragDrop;
using Engine.Editor.Inspection;
using Engine.Editor.Models;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;
using NumericsVector3 = System.Numerics.Vector3;

namespace Engine.Editor.Panels;

internal static class MeshRendererInspector
{
    internal static void Register() =>
        ComponentInspectorHooks.Register<MeshRendererComponent>((context, mesh) =>
        {
            Draw(context, mesh);
            return true;
        });

    private static void Draw(EditorContext context, MeshRendererComponent mesh)
    {
        Guid id = mesh.Id;
        InspectorEditing.PrepareField("Rendering Rules");
        int renderingMode = (int)mesh.RenderingMode;
        string[] renderingModes = ["Full (Default)", "Off", "Shadows Only", "Render Only"];
        if (ImGui.Combo("##RenderingMode", ref renderingMode, renderingModes, renderingModes.Length))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.RenderingMode), current => ((MeshRendererComponent)current).RenderingMode, (current, value) => ((MeshRendererComponent)current).RenderingMode = value, (MeshRenderingMode)renderingMode, false);
        InspectorEditing.DrawItemTooltip("Full renders in cameras and casts shadows. Off disables rendering. Shadows Only hides the mesh from cameras but keeps shadows and reflections. Render Only renders without casting shadows.");

        string resourceKey = mesh.ResourceKey;
        InspectorEditing.PrepareField("Resource");
        if (ImGui.InputText("##Resource", ref resourceKey, 1024))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.ResourceKey), current => ((MeshRendererComponent)current).ResourceKey, (current, value) => ((MeshRendererComponent)current).ResourceKey = value, resourceKey);
        InspectorEditing.EndMergeAfterEdit(context);
        if (ImGui.BeginDragDropTarget())
        {
            AssetDragPayload? payload = ImGuiDragPayloads.AcceptAsset();
            if (payload is { Kind: AssetKind.Mesh } meshPayload)
                InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.ResourceKey), current => ((MeshRendererComponent)current).ResourceKey, (current, value) => ((MeshRendererComponent)current).ResourceKey = value, meshPayload.RelativePath, false);
            ImGui.EndDragDropTarget();
        }
        InspectorEditing.DrawItemTooltip("Path to the model file (OBJ or FBX) inside the project assets. You can also drag a model here.");

        InspectorEditing.PrepareField("Model Part");
        string partId = mesh.PartId;
        if (ImGui.InputText("##ModelPart", ref partId, 1024))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.PartId), current => ((MeshRendererComponent)current).PartId, (current, value) => ((MeshRendererComponent)current).PartId = value, partId);
        InspectorEditing.EndMergeAfterEdit(context);

        int lodMode = (int)mesh.LodMode;
        InspectorEditing.DrawItemTooltip("Name of the model part to render when the file contains multiple meshes. Leave empty to use the whole model.");
        string[] lodModes = Enum.GetNames<MeshLodMode>();
        InspectorEditing.PrepareField("LOD Mode");
        if (ImGui.Combo("##LodMode", ref lodMode, lodModes, lodModes.Length))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.LodMode), current => ((MeshRendererComponent)current).LodMode, (current, value) => ((MeshRendererComponent)current).LodMode = value, (MeshLodMode)lodMode, false);
        InspectorEditing.DrawItemTooltip("Auto picks a level of detail from screen size, Forced renders a specific LOD.");
        int lodCount = context.ModelAssets?.RequestMesh(mesh.ResourceKey, mesh.PartId)?.LodCount ?? 1;
        int forcedLod = Math.Clamp(mesh.ForcedLodLevel, 0, Math.Max(0, lodCount - 1));
        InspectorEditing.PrepareField("Forced LOD");
        ImGui.BeginDisabled(lodCount <= 1);
        if (ImGui.SliderInt("##ForcedLod", ref forcedLod, 0, Math.Max(0, lodCount - 1)))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.ForcedLodLevel), current => ((MeshRendererComponent)current).ForcedLodLevel, (current, value) => ((MeshRendererComponent)current).ForcedLodLevel = value, forcedLod);
        ImGui.EndDisabled();
        InspectorEditing.DrawItemTooltip("Level of detail rendered when LOD Mode is Forced.");
        float lodBias = mesh.LodBias;
        InspectorEditing.PrepareField("LOD Bias");
        if (ImGui.DragFloat("##LodBias", ref lodBias, 0.02f, 0.05f, 4f))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.LodBias), current => ((MeshRendererComponent)current).LodBias, (current, value) => ((MeshRendererComponent)current).LodBias = value, lodBias);
        InspectorEditing.DrawItemTooltip("Multiplier applied to the automatic LOD selection distance. Above 1 switches to lower detail earlier.");
        InspectorEditing.EndMergeAfterEdit(context);
        if (context.ModelAssets is { } assets && !string.IsNullOrWhiteSpace(mesh.ResourceKey)
            && (mesh.ResourceKey.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
                || mesh.ResourceKey.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            && assets.IsMeshImportFailed(mesh.ResourceKey))
        {
            ImGui.TextUnformatted("Model import failed");
            if (ImGui.Button("Retry Import"))
                assets.RetryMeshImport(mesh.ResourceKey);
        }

        InspectorEditing.PrepareField("Material Asset");
        string materialName = string.Empty;
        if (!mesh.MaterialAsset.IsEmpty)
        {
            try
            {
                materialName = Path.GetFileNameWithoutExtension(context.Materials.ResolvePath(mesh.MaterialAsset));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                materialName = "Missing Material";
            }
        }
        AssetDragPayload? materialDrop = AssetDropField.Draw("MaterialAsset", materialName, AssetKind.Material);
        InspectorEditing.DrawItemTooltip("Material asset used to shade the mesh. Drag a material here.");
        if (materialDrop is { } materialPayload)
            InspectorEditing.TryMaterialAction(context, () => context.AssignMaterial(id, materialPayload.RelativePath));
        if (!mesh.MaterialAsset.IsEmpty)
        {
            if (ImGui.SmallButton("Open Material"))
                InspectorEditing.TryMaterialAction(context, () => context.SelectMaterial(mesh.MaterialAsset));
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear Material"))
                context.AssignMaterial(id, default);
            ImGui.TextWrapped("Shared material asset. Edit it to update all referencing objects.");
            return;
        }
        Vector3 albedo = mesh.AlbedoColor;
        NumericsVector3 color = new(albedo.X, albedo.Y, albedo.Z);
        InspectorEditing.PrepareField("Albedo");
        if (ImGui.ColorEdit3("##Albedo", ref color))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.AlbedoColor), current => ((MeshRendererComponent)current).AlbedoColor, (current, value) => ((MeshRendererComponent)current).AlbedoColor = value, new Vector3(color.X, color.Y, color.Z));
        InspectorEditing.DrawItemTooltip("Base color used when no albedo texture is assigned.");
        InspectorEditing.EndMergeAfterEdit(context);
        DrawComponentFloat(context, id, "Metallic", nameof(MeshRendererComponent.Metallic), current => ((MeshRendererComponent)current).Metallic, (current, value) => ((MeshRendererComponent)current).Metallic = value, 0.02f, 0f, 1f);
        DrawComponentFloat(context, id, "Roughness", nameof(MeshRendererComponent.Roughness), current => ((MeshRendererComponent)current).Roughness, (current, value) => ((MeshRendererComponent)current).Roughness = value, 0.02f, 0f, 1f);
        DrawComponentFloat(context, id, "Emissive", nameof(MeshRendererComponent.EmissiveIntensity), current => ((MeshRendererComponent)current).EmissiveIntensity, (current, value) => ((MeshRendererComponent)current).EmissiveIntensity = value, 0.05f, 0f, 100000f);

        Vector2 uv = mesh.UvScale;
        NumericsVector2 uvValue = new(uv.X, uv.Y);
        InspectorEditing.PrepareField("UV Scale");
        if (ImGui.DragFloat2("##UV Scale", ref uvValue, 0.02f, 0.001f, 1000f))
            InspectorEditing.SetComponentProperty(context, id, nameof(MeshRendererComponent.UvScale), current => ((MeshRendererComponent)current).UvScale, (current, value) => ((MeshRendererComponent)current).UvScale = value, new Vector2(uvValue.X, uvValue.Y));
        InspectorEditing.DrawItemTooltip("UV tiling of all material textures.");
        InspectorEditing.EndMergeAfterEdit(context);
    }

    private static void DrawComponentFloat(
        EditorContext context,
        Guid id,
        string label,
        string property,
        Func<Component, float> read,
        Action<Component, float> write,
        float speed,
        float min,
        float max)
    {
        Component? component = context.Document.Scene.FindComponentById(id);
        if (component is null)
            return;
        float value = read(component);
        InspectorEditing.PrepareField(label);
        if (ImGui.DragFloat($"##{label}", ref value, speed, min, max))
            InspectorEditing.SetComponentProperty(context, id, property, read, write, value);
        InspectorEditing.EndMergeAfterEdit(context);
    }
}
