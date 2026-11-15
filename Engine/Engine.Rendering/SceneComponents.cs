using Engine.Mathematics;
using Engine.Scene;
using Engine.RHI;

namespace Engine.Rendering;

public sealed class MeshRendererComponent : Component
{
    public AssetReference MaterialAsset { get; set; }
    private string _resourceKey = string.Empty;

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

    public Vector3 AlbedoColor { get; set; } = new(0.8f, 0.8f, 0.8f);
    public MeshLodMode LodMode { get; set; } = MeshLodMode.Auto;
    public int ForcedLodLevel { get; set; }
    public float LodBias { get; set; } = 1f;
    public float Metallic { get; set; }
    public float Roughness { get; set; } = 0.5f;
    public float EmissiveIntensity { get; set; }
    public Vector2 UvScale { get; set; } = new(1f, 1f);
    public float NormalScale { get; set; } = 1f;
    public float AoStrength { get; set; } = 1f;
    public AssetReference AlbedoTexture { get; set; }
    public AssetReference MetallicTexture { get; set; }
    public AssetReference RoughnessTexture { get; set; }
    public AssetReference NormalTexture { get; set; }
    public AssetReference AmbientOcclusionTexture { get; set; }

    internal Mesh? Mesh { get; set; }
    internal Material? Material { get; set; }
}
