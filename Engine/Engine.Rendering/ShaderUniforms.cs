using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Engine.Mathematics;

namespace Engine.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct CameraData
{
    public Matrix4x4 ViewProjection;
    public Matrix4x4 PreviousViewProjection;
    public Vector3 CameraPosition;
    public float Padding0;
    public Vector2 Jitter;
    public Vector2 PreviousJitter;

    public static int SizeInBytes => Marshal.SizeOf<CameraData>();
}

[InlineArray(4)]
public struct ShadowMatrixArray
{
    private Matrix4x4 _element0;
}

[InlineArray(4)]
public struct ShadowVectorArray
{
    private Vector4 _element0;
}

[StructLayout(LayoutKind.Sequential)]
public struct ShadowData
{
    public ShadowMatrixArray LightViewProjections;
    public ShadowVectorArray AtlasTiles;
    public ShadowVectorArray CascadeMetrics;
    public Vector4 SplitDepths;
    public Vector4 CameraPositionAndNear;
    public Vector4 CameraForwardAndDistance;
    public Vector4 LightDirectionAndStrength;
    public Vector4 FilterParameters;
    public Vector4 BiasParameters;
    public int LightIndex;
    public uint Quality;
    public uint DebugCascades;
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
    public float Opacity;
    public uint Transparent;
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
    public Matrix4x4 PreviousModel;

    public static ModelConstants FromModel(Matrix4x4 model, Matrix4x4? previousModel = null) => new()
    {
        Model = model,
        PreviousModel = previousModel ?? model,
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
public struct TemporalConstants
{
    public uint Width;
    public uint Height;
    public uint SourceWidth;
    public uint SourceHeight;
    public uint HistoryValid;
    public float HistoryWeight;
    public float DepthThreshold;
}

[StructLayout(LayoutKind.Sequential)]
public struct TonemapConstants
{
    public float Exposure;
    public float Padding0;
    public float Padding1;
    public float Padding2;
}

[StructLayout(LayoutKind.Sequential)]
public struct SkyConstants
{
    public Matrix4x4 InverseViewProjection;
    public Vector3 CameraPosition;
    private float Padding0;
    public Vector3 SunDirection;
    private float Padding1;
    public Vector2 Extent;
    public float SunDiscIntensity;
    private float Padding2;
}

[StructLayout(LayoutKind.Sequential)]
public struct CloudsConstants
{
    public Matrix4x4 InverseViewProjection;
    public Vector3 CameraPosition;
    private float Padding0;
    public Vector3 SunDirection;
    public float JitterSeed;
    public Vector2 Extent;
    public float Coverage;
    public float Density;
    public Vector2 WindOffset;
    public float CloudBottom;
    public float CloudTop;
}

[StructLayout(LayoutKind.Sequential)]
public struct RtShadowControl
{
    public uint Enabled;
    public uint ExtentWidth;
    public uint ExtentHeight;
    public uint PointLightEnabled;
    public uint PointLightIndex;
    public uint RasterDisabled;
    public uint Padding1;
    public uint Padding2;

    public static int SizeInBytes => Marshal.SizeOf<RtShadowControl>();
}

[StructLayout(LayoutKind.Sequential)]
public struct RtViewData
{
    public Matrix4x4 ViewProjection;
    public Matrix4x4 InverseViewProjection;
    public Matrix4x4 PreviousViewProjection;
    public Vector4 CameraPositionAndMaxDistance;
    public Vector4 ExtentAndFrame;
    public Vector4 LightDirectionAndAngularRadius;
    public Vector4 Settings;
    public Vector4 PointLightPositionAndRange;
    public Vector4 ShadowQuality;
    public Vector4 ReflectionQuality;
    public Vector4 GiQuality;

    public static int SizeInBytes => Marshal.SizeOf<RtViewData>();
}

[StructLayout(LayoutKind.Sequential)]
public struct RtFilterConstants
{
    public uint ExtentWidth;
    public uint ExtentHeight;
    public uint SourceWidth;
    public uint SourceHeight;
    public uint HistoryValid;
    public uint Mode;
    public float HistoryWeight;
    public float DepthThreshold;
    public float NormalThreshold;
    public uint StepWidth;
    public uint ValidityEnabled;
    public uint UseSceneDepth;
    public uint Padding1;
    public uint Padding2;

    public static int SizeInBytes => Marshal.SizeOf<RtFilterConstants>();
}

[StructLayout(LayoutKind.Sequential)]
public struct RtDenoiseConstants
{
    public uint ExtentWidth;
    public uint ExtentHeight;
    public uint SourceWidth;
    public uint SourceHeight;
    public uint HistoryValid;
    public uint Effect;

    public static int SizeInBytes => Marshal.SizeOf<RtDenoiseConstants>();
}

[StructLayout(LayoutKind.Sequential)]
public struct RtCompositeConstants
{
    public uint ExtentWidth;
    public uint ExtentHeight;
    public uint SourceWidth;
    public uint SourceHeight;
    public uint ReflectionsEnabled;
    public float Strength;

    public static int SizeInBytes => Marshal.SizeOf<RtCompositeConstants>();
}

[StructLayout(LayoutKind.Sequential)]
public struct SsgiData
{
    public Matrix4x4 ViewProjection;
    public Matrix4x4 InverseViewProjection;
    public Vector4 CameraPositionAndRadius;
    public Vector4 ExtentAndFrame;

    public static int SizeInBytes => Marshal.SizeOf<SsgiData>();
}

[StructLayout(LayoutKind.Sequential)]
public struct GiCompositeConstants
{
    public uint Width;
    public uint Height;
    public float Intensity;
    public uint Mode;
}
