using Engine.Mathematics;
using Engine.Scene;
using Engine.RHI;

namespace Engine.Rendering;

[InspectorTooltip("Renders a mesh from the project assets with a PBR material.")]
public sealed class MeshRendererComponent : Component
{
    [InspectorTooltip("Material asset used to shade the mesh.")]
    public AssetReference MaterialAsset { get; set; }
    private string _resourceKey = string.Empty;

    [InspectorTooltip("Path to the model file (OBJ or FBX) inside the project assets.")]
    public string ResourceKey
    {
        get => _resourceKey;
        set
        {
            if (_resourceKey == value)
                return;
            _resourceKey = value;
            Mesh = null;
        }
    }
    private string _partId = string.Empty;

    [InspectorTooltip("Name of the model part to render when the file contains multiple meshes. Leave empty to use the whole model.")]
    public string PartId
    {
        get => _partId;
        set
        {
            if (_partId == value)
                return;
            _partId = value;
            Mesh = null;
        }
    }

    [InspectorTooltip("Base color used when no albedo texture is assigned.")]
    [InspectorColor]
    public Vector3 AlbedoColor { get; set; } = new(0.8f, 0.8f, 0.8f);
    [InspectorTooltip("Auto picks a level of detail from screen size, Forced renders a specific LOD.")]
    public MeshLodMode LodMode { get; set; } = MeshLodMode.Auto;
    [InspectorTooltip("Level of detail rendered when LOD Mode is Forced.")]
    [InspectorRange(0, 8)]
    public int ForcedLodLevel { get; set; }
    [InspectorTooltip("Multiplier applied to the automatic LOD selection distance. Above 1 switches to lower detail earlier.")]
    [InspectorRange(0.05f, 4f)]
    public float LodBias { get; set; } = 1f;
    [InspectorTooltip("How metallic the surface is: 0 is dielectric (plastic, wood), 1 is full metal.")]
    [InspectorRange(0f, 1f)]
    public float Metallic { get; set; }
    [InspectorTooltip("Surface roughness: 0 is a sharp mirror, 1 is fully diffuse.")]
    [InspectorRange(0.045f, 1f)]
    public float Roughness { get; set; } = 0.5f;
    [InspectorTooltip("Strength of the self-emission. Above 0 the object glows regardless of lighting.")]
    [InspectorRange(0f, 10f)]
    public float EmissiveIntensity { get; set; }
    [InspectorTooltip("UV tiling of all material textures.")]
    public Vector2 UvScale { get; set; } = new(1f, 1f);
    [InspectorTooltip("Intensity of the normal map detail.")]
    [InspectorRange(0f, 4f)]
    public float NormalScale { get; set; } = 1f;
    [InspectorTooltip("How strongly the ambient occlusion texture darkens indirect lighting.")]
    [InspectorRange(0f, 1f)]
    public float AoStrength { get; set; } = 1f;
    [InspectorTooltip("Base color texture (albedo).")]
    public AssetReference AlbedoTexture { get; set; }
    [InspectorTooltip("Metallic mask texture. Uses the channel selected by the material.")]
    public AssetReference MetallicTexture { get; set; }
    [InspectorTooltip("Roughness mask texture. Uses the channel selected by the material.")]
    public AssetReference RoughnessTexture { get; set; }
    [InspectorTooltip("Tangent-space normal map.")]
    public AssetReference NormalTexture { get; set; }
    [InspectorTooltip("Ambient occlusion texture baked for the mesh.")]
    public AssetReference AmbientOcclusionTexture { get; set; }

    internal Mesh? Mesh { get; set; }
    internal Material? Material { get; set; }
}
