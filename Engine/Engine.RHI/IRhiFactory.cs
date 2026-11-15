using Engine.Core;

namespace Engine.RHI;

public interface IRhiFactory
{
    string Name { get; }

    GraphicsBackend Backend { get; }

    bool IsSupported();

    IRHI Create(ILogger logger);
}
