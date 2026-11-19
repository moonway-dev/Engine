namespace Engine.Rendering;

public enum MeshRenderingMode
{
    Full,
    Off,
    ShadowsOnly,
    RenderOnly,
}

public static class MeshRenderingRules
{
    public const byte SurfaceRayMask = 1;
    public const byte ShadowRayMask = 2;

    public static bool RendersCamera(this MeshRenderingMode mode) =>
        mode is MeshRenderingMode.Full or MeshRenderingMode.RenderOnly;

    public static bool CastsShadows(this MeshRenderingMode mode) =>
        mode is MeshRenderingMode.Full or MeshRenderingMode.ShadowsOnly;

    public static byte RayTracingMask(this MeshRenderingMode mode) => mode switch
    {
        MeshRenderingMode.Full or MeshRenderingMode.ShadowsOnly => SurfaceRayMask | ShadowRayMask,
        MeshRenderingMode.RenderOnly => SurfaceRayMask,
        _ => 0,
    };
}
