using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Rendering;

public enum TextureChannel
{
    R,
    G,
    B,
    A,
}

public sealed record MaterialProperties
{
    public Vector3 AlbedoColor { get; init; } = new(0.8f);
    public float Metallic { get; init; }
    public float Roughness { get; init; } = 0.5f;
    public float EmissiveIntensity { get; init; }
    public Vector3 EmissionColor { get; init; } = Vector3.One;
    public Vector2 UvScale { get; init; } = Vector2.One;
    public Vector2 UvOffset { get; init; } = Vector2.Zero;
    public float NormalScale { get; init; } = 1f;
    public float AoStrength { get; init; } = 1f;
    public TextureChannel MetallicChannel { get; init; }
    public TextureChannel RoughnessChannel { get; init; }
    public TextureChannel AmbientOcclusionChannel { get; init; }
    public AssetReference AlbedoTexture { get; init; }
    public AssetReference MetallicTexture { get; init; }
    public AssetReference RoughnessTexture { get; init; }
    public AssetReference NormalTexture { get; init; }
    public AssetReference AmbientOcclusionTexture { get; init; }
    public AssetReference EmissionTexture { get; init; }
    public bool LegacyEmission { get; init; }

    public static MaterialProperties FromComponent(MeshRendererComponent component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return new MaterialProperties
        {
            AlbedoColor = component.AlbedoColor,
            Metallic = component.Metallic,
            Roughness = component.Roughness,
            EmissiveIntensity = component.EmissiveIntensity,
            UvScale = component.UvScale,
            NormalScale = component.NormalScale,
            AoStrength = component.AoStrength,
            AlbedoTexture = component.AlbedoTexture,
            MetallicTexture = component.MetallicTexture,
            RoughnessTexture = component.RoughnessTexture,
            NormalTexture = component.NormalTexture,
            AmbientOcclusionTexture = component.AmbientOcclusionTexture,
            LegacyEmission = true,
        };
    }
}

public record ResolvedMaterial(MaterialProperties Properties, long Revision);
