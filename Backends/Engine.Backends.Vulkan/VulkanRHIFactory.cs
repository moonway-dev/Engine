using Engine.Core;
using Silk.NET.Vulkan;
using Engine.RHI;

namespace Engine.Backends.Vulkan;

public sealed class VulkanRHIFactory : IRhiFactory
{
    public string Name => "Vulkan Renderer";

    public GraphicsBackend Backend => GraphicsBackend.Vulkan;

    public bool IsSupported()
    {
        try
        {
            return Vk.GetApi() is not null;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch (BadImageFormatException)
        {
            return false;
        }
    }

    public IRHI Create(ILogger logger) => new VulkanRHI(logger);
}
