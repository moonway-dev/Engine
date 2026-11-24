using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
using System.Reflection;
using Engine.Assets;
using Engine.Editor.Documents;
using Engine.Editor.DragDrop;
using Engine.Editor.Models;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.Scene;
using ImGuiNET;

namespace Engine.Editor.Panels;

internal static class MaterialInspector
{
    public static void Draw(EditorContext context, MaterialDocument document)
    {
        ImGui.TextUnformatted(document.Title);
        ImGui.TextDisabled(document.Effective.Transparent ? "PBR Lit / Transparent" : "PBR Lit / Opaque");
        if (document.HasExternalConflict)
        {
            ImGui.TextWrapped("Material changed on disk. Your unsaved edits are preserved.");
            if (ImGui.Button("Reload from disk"))
                document.Reload();
            ImGui.SameLine();
            if (ImGui.Button("Overwrite"))
                document.Save(overwriteExternal: true);
        }
        else if (ImGui.Button("Save Material"))
            document.Save();
        MaterialAsset asset = document.Asset;
        AssetReference parent = asset.Parent;
        ImGui.SeparatorText("Parent");
        string parentName = string.Empty;
        if (!parent.IsEmpty)
        {
            try
            {
                parentName = Path.GetFileNameWithoutExtension(context.Materials.ResolvePath(parent));
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
            {
                parentName = "Missing Material";
            }
        }
        AssetDragPayload? parentDrop = AssetDropField.Draw("Parent", parentName, AssetKind.Material);
        if (parentDrop is { } parentPayload)
            document.Edit("Parent", value => value.Parent = Canonical(context, parentPayload.RelativePath), false);
        if (!parent.IsEmpty)
        {
            if (ImGui.SmallButton("Open Parent"))
                context.SelectMaterial(parent);
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear Parent"))
                document.Edit("Parent", value => value.Parent = default, false);
        }
        MaterialProperties effective = document.Effective;
        ImGui.SeparatorText("Surface Parameters");
        foreach (PropertyInfo property in typeof(MaterialProperties).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == nameof(MaterialProperties.LegacyEmission))
                continue;
            ImGui.PushID(property.Name);
            bool overridden = asset.Overrides.ContainsKey(property.Name);
            bool inherited = !parent.IsEmpty && !overridden;
            object current = property.GetValue(effective)!;
            ImGui.TextUnformatted(Label(property.Name));
            if (!parent.IsEmpty)
            {
                if (ImGui.SmallButton(overridden ? "Revert" : "Override"))
                {
                    if (overridden)
                        document.Revert(property.Name);
                    else
                        document.Edit(property.Name, value => value.SetOverride(property.Name, current), false);
                }
                ImGui.SameLine();
                ImGui.TextDisabled(overridden ? "Overridden" : "Inherited");
            }
            ImGui.BeginDisabled(inherited);
            ImGui.SetNextItemWidth(-1);
            object? next = DrawValue(context, property, current);
            if (next is not null)
                document.Edit(property.Name, value => value.SetOverride(property.Name, next));
            if (ImGui.IsItemDeactivatedAfterEdit())
                document.History.EndMergeGroup();
            ImGui.EndDisabled();
            ImGui.Spacing();
            ImGui.PopID();
        }
        foreach (string diagnostic in context.Materials.Diagnostics)
            ImGui.TextWrapped(diagnostic);
    }

    private static object? DrawValue(EditorContext context, PropertyInfo property, object current)
    {
        if (current is AssetReference texture)
        {
            string textureName = string.Empty;
            if (!texture.IsEmpty)
            {
                try
                {
                    textureName = Path.GetFileNameWithoutExtension(context.Materials.ResolvePath(texture));
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException)
                {
                    textureName = "Missing Texture";
                }
            }
            object? result = null;
            if (AssetDropField.Draw(property.Name, textureName, AssetKind.Texture) is { } textureDrop)
                result = Canonical(context, textureDrop.RelativePath);
            if (!texture.IsEmpty && ImGui.SmallButton("Clear Texture"))
                result = AssetReference.Empty;
            return result;
        }
        if (current is Vector3 vector)
        {
            System.Numerics.Vector3 value = new(vector.X, vector.Y, vector.Z);
            if (ImGui.ColorEdit3("##Value", ref value, ImGuiColorEditFlags.Float))
                return new Vector3(value.X, value.Y, value.Z);
        }
        else if (current is Vector2 uv)
        {
            System.Numerics.Vector2 value = new(uv.X, uv.Y);
            if (ImGui.DragFloat2("##Value", ref value, 0.02f))
                return new Vector2(value.X, value.Y);
        }
        else if (current is float scalar)
        {
            float max = property.Name is "Metallic" or "Roughness" or "AoStrength" or "Opacity" ? 1f : 100000f;
            if (ImGui.DragFloat("##Value", ref scalar, 0.01f, 0f, max))
                return scalar;
        }
        else if (current is bool flag)
        {
            if (ImGui.Checkbox("##Value", ref flag))
                return flag;
        }
        else if (current is TextureChannel channel)
        {
            int value = (int)channel;
            if (ImGui.Combo("##Value", ref value, "R\0G\0B\0A\0"))
                return (TextureChannel)value;
        }
        return null;
    }

    private static AssetReference Canonical(EditorContext context, string value) =>
        string.IsNullOrWhiteSpace(value) ? default : context.Materials.GetReference(context.Materials.ResolvePath(value));

    private static string Label(string name) => System.Text.RegularExpressions.Regex.Replace(name, "([a-z])([A-Z])", "$1 $2");
}
