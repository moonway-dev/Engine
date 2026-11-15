using Engine.Mathematics;
using Engine.Scene;
using Engine.RHI;

namespace Engine.Rendering;

public enum LightType
{
    Directional,
    Point,
}

public sealed class Light
{
    public LightType Type { get; set; } = LightType.Directional;
    public Vector3 DirectionOrPosition { get; set; } = new(0.5f, -1f, 0.3f);
    public Vector3 Color { get; set; } = new(1f, 1f, 1f);
    public float Intensity { get; set; } = 1f;
    public float Range { get; set; } = 20f;

    public static Light Directional(Vector3 direction, Vector3 color, float intensity = 1f) => new()
    {
        Type = LightType.Directional,
        DirectionOrPosition = direction.Normalized,
        Color = color,
        Intensity = intensity,
    };

    public static Light Point(Vector3 position, Vector3 color, float intensity = 1f, float range = 20f) => new()
    {
        Type = LightType.Point,
        DirectionOrPosition = position,
        Color = color,
        Intensity = intensity,
        Range = range,
    };
}

public enum MaterialTextureSlot
{
    Albedo,
    Metallic,
    Roughness,
    Normal,
    AmbientOcclusion,
    Emission,
}

public readonly record struct MaterialTextureSet(
    ITexture Albedo,
    ITexture Metallic,
    ITexture Roughness,
    ITexture Normal,
    ITexture AmbientOcclusion,
    ITexture? Emission = null)
{
    public ITexture this[MaterialTextureSlot slot] => slot switch
    {
        MaterialTextureSlot.Albedo => Albedo,
        MaterialTextureSlot.Metallic => Metallic,
        MaterialTextureSlot.Roughness => Roughness,
        MaterialTextureSlot.Normal => Normal,
        MaterialTextureSlot.AmbientOcclusion => AmbientOcclusion,
        MaterialTextureSlot.Emission => Emission ?? Albedo,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };
}

public sealed class MaterialDefaultTextures : IDisposable
{
    private readonly ITexture _whiteLinear;
    private bool _disposed;

    public ITexture WhiteSrgb { get; }
    public ITexture WhiteLinear => _whiteLinear;
    public ITexture BlackLinear { get; }
    public ITexture FlatNormal { get; }

    public MaterialTextureSet TextureSet => new(
        WhiteSrgb,
        _whiteLinear,
        _whiteLinear,
        FlatNormal,
        _whiteLinear,
        WhiteSrgb);

    private MaterialDefaultTextures(
        ITexture whiteSrgb,
        ITexture whiteLinear,
        ITexture blackLinear,
        ITexture flatNormal)
    {
        WhiteSrgb = whiteSrgb;
        _whiteLinear = whiteLinear;
        BlackLinear = blackLinear;
        FlatNormal = flatNormal;
    }

    public static MaterialDefaultTextures Create(IDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        ITexture? whiteSrgb = null;
        ITexture? whiteLinear = null;
        ITexture? blackLinear = null;
        ITexture? flatNormal = null;
        try
        {
            whiteSrgb = CreateTexture(device, TextureFormat.R8G8B8A8Srgb, [255, 255, 255, 255]);
            whiteLinear = CreateTexture(device, TextureFormat.R8G8B8A8UNorm, [255, 255, 255, 255]);
            blackLinear = CreateTexture(device, TextureFormat.R8G8B8A8UNorm, [0, 0, 0, 255]);
            flatNormal = CreateTexture(device, TextureFormat.R8G8B8A8UNorm, [128, 128, 255, 255]);
            return new MaterialDefaultTextures(whiteSrgb, whiteLinear, blackLinear, flatNormal);
        }
        catch
        {
            whiteSrgb?.Dispose();
            whiteLinear?.Dispose();
            blackLinear?.Dispose();
            flatNormal?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        WhiteSrgb.Dispose();
        _whiteLinear.Dispose();
        BlackLinear.Dispose();
        FlatNormal.Dispose();
    }

    private static ITexture CreateTexture(IDevice device, TextureFormat format, byte[] pixels)
    {
        ITexture texture = device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(1, 1),
            Format = format,
        });
        try
        {
            device.UploadTextureData(texture, pixels);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }
}

public sealed class Material : IDisposable
{
    public Vector3 AlbedoColor { get; set; } = new(0.8f, 0.8f, 0.8f);
    public float Metallic { get; set; }
    public float Roughness { get; set; } = 0.5f;
    public float EmissiveIntensity { get; set; }
    public Vector3 EmissionColor { get; set; } = Vector3.One;
    public bool LegacyEmission { get; set; } = true;
    public Vector2 UvOffset { get; set; }
    public TextureChannel MetallicChannel { get; set; }
    public TextureChannel RoughnessChannel { get; set; }
    public TextureChannel AmbientOcclusionChannel { get; set; }
    public Vector2 UvScale { get; set; } = new(1f, 1f);
    public float NormalScale { get; set; } = 1f;
    public float AoStrength { get; set; } = 1f;
    public ITexture? EmissionTexture { get; set; }
    public ITexture? AlbedoTexture { get; set; }
    public ITexture? MetallicTexture { get; set; }
    public ITexture? RoughnessTexture { get; set; }
    public ITexture? NormalTexture { get; set; }
    public ITexture? AmbientOcclusionTexture { get; set; }

    public MaterialTextureSet ResolveTextures(MaterialDefaultTextures defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        MaterialTextureSet fallback = defaults.TextureSet;
        return new MaterialTextureSet(
            AlbedoTexture ?? fallback.Albedo,
            MetallicTexture ?? fallback.Metallic,
            RoughnessTexture ?? fallback.Roughness,
            NormalTexture ?? fallback.Normal,
            AmbientOcclusionTexture ?? fallback.AmbientOcclusion,
            EmissionTexture ?? fallback.Emission);
    }

    public ITexture? GetTexture(MaterialTextureSlot slot) => slot switch
    {
        MaterialTextureSlot.Albedo => AlbedoTexture,
        MaterialTextureSlot.Metallic => MetallicTexture,
        MaterialTextureSlot.Roughness => RoughnessTexture,
        MaterialTextureSlot.Normal => NormalTexture,
        MaterialTextureSlot.AmbientOcclusion => AmbientOcclusionTexture,
        MaterialTextureSlot.Emission => EmissionTexture,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    public void SetTexture(MaterialTextureSlot slot, ITexture? texture)
    {
        switch (slot)
        {
            case MaterialTextureSlot.Albedo:
                AlbedoTexture = texture;
                break;
            case MaterialTextureSlot.Metallic:
                MetallicTexture = texture;
                break;
            case MaterialTextureSlot.Roughness:
                RoughnessTexture = texture;
                break;
            case MaterialTextureSlot.Normal:
                NormalTexture = texture;
                break;
            case MaterialTextureSlot.AmbientOcclusion:
                AmbientOcclusionTexture = texture;
                break;
            case MaterialTextureSlot.Emission:
                EmissionTexture = texture;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(slot), slot, null);
        }
    }

    internal IBuffer ParamsBuffer { get; set; } = null!;
    internal IDescriptorSet? Set { get; set; }
    internal bool ParamsDirty { get; set; } = true;

    public MaterialData ToData()
    {
        return new MaterialData
        {
            AlbedoColor = AlbedoColor,
            Metallic = Metallic,
            Roughness = Roughness,
            UvScale = UvScale,
            NormalScale = NormalScale,
            AoStrength = AoStrength,
            EmissiveIntensity = EmissiveIntensity,
            EmissionColor = EmissionColor,
            UvOffset = UvOffset,
            MetallicChannel = (uint)MetallicChannel,
            RoughnessChannel = (uint)RoughnessChannel,
            AmbientOcclusionChannel = (uint)AmbientOcclusionChannel,
            LegacyEmission = LegacyEmission ? 1u : 0u,
        };
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Set?.Dispose();
        ParamsBuffer?.Dispose();
    }
}

public sealed class RenderItem
{
    public required Mesh Mesh { get; init; }
    public required Material Material { get; init; }
    public Vector3 Position { get; set; } = Vector3.Zero;
    public Quaternion Rotation { get; set; } = Quaternion.Identity;
    public Vector3 Scale { get; set; } = Vector3.One;
    public MeshLodSettings Lod { get; set; } = new();

    internal readonly MeshLodState SceneLodState = new();
    internal readonly MeshLodState GameLodState = new();
    internal readonly MeshLodState ShadowLodState = new();

    internal Transform? BoundTransform { get; set; }

    public Matrix4x4 ModelMatrix => BoundTransform?.WorldMatrix ?? Matrix4x4.CreateTRS(Position, Rotation, Scale);
}
