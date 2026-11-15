using Silk.NET.Vulkan;

namespace Engine.Backends.Vulkan;

public sealed class VulkanException : Exception
{
    public Result Result { get; }

    public VulkanException(Result result, string context)
        : base($"{context} failed with VkResult {result} ({ResultToString(result)}).")
    {
        Result = result;
    }

    public static string ResultToString(Result result) => result switch
    {
        Result.Success => "VK_SUCCESS",
        Result.NotReady => "VK_NOT_READY",
        Result.Timeout => "VK_TIMEOUT",
        Result.EventSet => "VK_EVENT_SET",
        Result.EventReset => "VK_EVENT_RESET",
        Result.Incomplete => "VK_INCOMPLETE",
        Result.ErrorOutOfHostMemory => "VK_ERROR_OUT_OF_HOST_MEMORY",
        Result.ErrorOutOfDeviceMemory => "VK_ERROR_OUT_OF_DEVICE_MEMORY",
        Result.ErrorInitializationFailed => "VK_ERROR_INITIALIZATION_FAILED",
        Result.ErrorDeviceLost => "VK_ERROR_DEVICE_LOST",
        Result.ErrorMemoryMapFailed => "VK_ERROR_MEMORY_MAP_FAILED",
        Result.ErrorLayerNotPresent => "VK_ERROR_LAYER_NOT_PRESENT",
        Result.ErrorExtensionNotPresent => "VK_ERROR_EXTENSION_NOT_PRESENT",
        Result.ErrorFeatureNotPresent => "VK_ERROR_FEATURE_NOT_PRESENT",
        Result.ErrorIncompatibleDriver => "VK_ERROR_INCOMPATIBLE_DRIVER",
        Result.ErrorTooManyObjects => "VK_ERROR_TOO_MANY_OBJECTS",
        Result.ErrorFormatNotSupported => "VK_ERROR_FORMAT_NOT_SUPPORTED",
        Result.ErrorFragmentedPool => "VK_ERROR_FRAGMENTED_POOL",
        Result.ErrorUnknown => "VK_ERROR_UNKNOWN",
        Result.SuboptimalKhr => "VK_SUBOPTIMAL_KHR",
        Result.ErrorOutOfDateKhr => "VK_ERROR_OUT_OF_DATE_KHR",
        _ => $"VkResult {result}",
    };
}
