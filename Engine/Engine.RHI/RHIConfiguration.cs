using Engine.Core;
using Engine.Mathematics;

namespace Engine.RHI;

public sealed class RHIConfiguration
{
    public GraphicsBackend Backend { get; set; } = GraphicsBackend.Vulkan;
    public string ApplicationName { get; set; } = "Engine Application";
    public string EngineVersion { get; set; } = EngineInfo.EngineVersion;
    public bool EnableValidation { get; set; }
    public bool VSync { get; set; } = true;
    public uint FramesInFlight { get; set; } = 2;
    public int? PreferredDeviceIndex { get; set; }
    public RayTracingMode RayTracing { get; set; } = RayTracingMode.Auto;
    public Size2D HeadlessExtent { get; set; } = new(1280, 720);
}
