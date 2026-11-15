using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Engine.Mathematics;

namespace Engine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct CameraData
{
    public Matrix4x4 ViewProjection;
    public Vector3 CameraPosition;
    public float Padding0;

    public static int SizeInBytes => Marshal.SizeOf<CameraData>();
}

[StructLayout(LayoutKind.Sequential)]
public struct ShadowData
{
    public Matrix4x4 LightViewProjection;
    public float TexelSize;
    public float Strength;
    public int LightIndex;
    public float Padding0;
}

[StructLayout(LayoutKind.Sequential)]
public struct MaterialData
{
    public Vector3 AlbedoColor;
    public float Metallic;
    public float Roughness;
    public float EmissiveIntensity;
    public Vector2 UvScale;
    public float NormalScale;
    public float AoStrength;
    public Vector2 UvOffset;
    public Vector3 EmissionColor;
    public uint LegacyEmission;
    public uint MetallicChannel;
    public uint RoughnessChannel;
    public uint AmbientOcclusionChannel;
    public float Padding0;
}

[StructLayout(LayoutKind.Sequential)]
public struct LightData
{
    public Vector3 DirectionOrPosition;
    public float Type;
    public Vector3 Color;
    public float Intensity;
    public float Range;
    public float Padding0;
    public float Padding1;
    public float Padding2;
    public Vector4 Padding3;

    public const float Directional = 0f;
    public const float Point = 1f;
}

[InlineArray(8)]
public struct LightDataArray
{
    private LightData _element0;
}

[StructLayout(LayoutKind.Sequential)]
public struct LightsData
{
    public const int MaxLights = 8;

    public LightDataArray Lights;
    public uint Count;
    public float Padding0;
    public float Padding1;
    public float Padding2;
}

[StructLayout(LayoutKind.Sequential)]
public struct ShadowModelConstants
{
    public Matrix4x4 ModelViewProjection;
}

[StructLayout(LayoutKind.Sequential)]
public struct ModelConstants
{
    public Matrix4x4 Model;
    public Matrix4x4 NormalMatrix;

    public static ModelConstants FromModel(Matrix4x4 model) => new()
    {
        Model = model,
        NormalMatrix = model.Invert().Transpose(),
    };
}

[StructLayout(LayoutKind.Sequential)]
public struct BloomConstants
{
    public uint SourceWidth;
    public uint SourceHeight;
    public uint DestinationWidth;
    public uint DestinationHeight;
    public float Threshold;
    public float Intensity;
    public uint Mode;
}

[StructLayout(LayoutKind.Sequential)]
public struct TonemapConstants
{
    public float Exposure;
    public float Padding0;
    public float Padding1;
    public float Padding2;
}
