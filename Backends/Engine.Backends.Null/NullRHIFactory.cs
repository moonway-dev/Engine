using Engine.Core;
using Engine.RHI;

namespace Engine.Backends.Null;

public sealed class NullRHIFactory : IRhiFactory
{
    public string Name => "Null Renderer";

    public GraphicsBackend Backend => GraphicsBackend.Null;

    public bool IsSupported() => true;

    public IRHI Create(ILogger logger) => new NullRHI(logger);
}
