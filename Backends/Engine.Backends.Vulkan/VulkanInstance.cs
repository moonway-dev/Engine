using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Engine.Core;
using Engine.RHI;
using Silk.NET.Core;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.EXT;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanInstance : IDisposable
{
    private readonly Vk _vk;
    private readonly ILogger _log;
    private ExtDebugUtils? _debugUtils;
    private DebugUtilsMessengerEXT _messenger;

    public Instance Handle { get; }

    public bool ValidationEnabled { get; }

    private VulkanInstance(Vk vk, Instance handle, bool validation, ILogger log)
    {
        _vk = vk;
        Handle = handle;
        ValidationEnabled = validation;
        _log = log;
    }

    public static VulkanInstance Create(Vk vk, RHIConfiguration configuration, IReadOnlyList<string>? surfaceExtensions, ILogger log)
    {
        string appName = configuration.ApplicationName;
        List<string> extensions = [];
        if (surfaceExtensions is not null)
            extensions.AddRange(surfaceExtensions);

        bool validation = configuration.EnableValidation && IsLayerAvailable(vk, VulkanHelpers.LayerKhronosValidation);
        if (configuration.EnableValidation && !validation)
            log.Warning($"Validation layer '{VulkanHelpers.LayerKhronosValidation}' is not available, validation disabled");
        if (validation && IsInstanceExtensionAvailable(vk, VulkanHelpers.ExtensionDebugUtils))
            extensions.Add(VulkanHelpers.ExtensionDebugUtils);
        else if (validation)
            log.Warning("VK_EXT_debug_utils is not available, debug messenger disabled");

        ApplicationInfo applicationInfo = new()
        {
            SType = StructureType.ApplicationInfo,
            PApplicationName = (byte*)VulkanHelpers.AllocUtf8(appName),
            PEngineName = (byte*)VulkanHelpers.AllocUtf8(EngineInfo.EngineName),
            ApplicationVersion = VulkanHelpers.MakeVersion(0, 1, 0),
            EngineVersion = VulkanHelpers.MakeVersion(0, 1, 0),
            ApiVersion = VulkanHelpers.MakeVersion(1, 3, 0),
        };

        uint layerCount = 0;
        byte** layers = null;
        uint extensionCount = 0;
        byte** extensionPointers = null;
        if (validation)
            layers = VulkanHelpers.AllocUtf8Array([VulkanHelpers.LayerKhronosValidation], out layerCount);
        if (extensions.Count > 0)
            extensionPointers = VulkanHelpers.AllocUtf8Array(extensions, out extensionCount);

        InstanceCreateInfo createInfo = new()
        {
            SType = StructureType.InstanceCreateInfo,
            PApplicationInfo = &applicationInfo,
            EnabledLayerCount = layerCount,
            PpEnabledLayerNames = layers,
            EnabledExtensionCount = extensionCount,
            PpEnabledExtensionNames = extensionPointers,
        };

        try
        {
            vk.CreateInstance(&createInfo, null, out Instance instance)
                .ThrowIfNotSuccess(log, "vkCreateInstance");
            VulkanInstance result = new(vk, instance, validation, log);
            if (validation)
                result.AttachDebugMessenger();
            log.Information(
                $"Vulkan instance created (API 1.3, validation {(validation ? "enabled" : "disabled")}, " +
                $"{extensionCount} extension(s))");
            return result;
        }
        finally
        {
            VulkanHelpers.FreeUtf8((nint)applicationInfo.PApplicationName);
            VulkanHelpers.FreeUtf8((nint)applicationInfo.PEngineName);
            if (layers is not null)
                VulkanHelpers.FreeUtf8Array(layers, (int)layerCount);
            if (extensionPointers is not null)
                VulkanHelpers.FreeUtf8Array(extensionPointers, (int)extensionCount);
        }
    }

    private static bool IsLayerAvailable(Vk vk, string layerName)
    {
        uint count = 0;
        vk.EnumerateInstanceLayerProperties(&count, null);
        LayerProperties[] layers = new LayerProperties[count];
        fixed (LayerProperties* pointer = layers)
            vk.EnumerateInstanceLayerProperties(&count, pointer);
        for (uint i = 0; i < count; i++)
        {
            fixed (LayerProperties* layer = &layers[i])
            {
                if (VulkanHelpers.ReadFixedString(layer->LayerName, 256) == layerName)
                    return true;
            }
        }
        return false;
    }

    private static bool IsInstanceExtensionAvailable(Vk vk, string extensionName)
    {
        uint count = 0;
        vk.EnumerateInstanceExtensionProperties((byte*)null, &count, null);
        ExtensionProperties[] extensions = new ExtensionProperties[count];
        fixed (ExtensionProperties* pointer = extensions)
            vk.EnumerateInstanceExtensionProperties((byte*)null, &count, pointer);
        for (uint i = 0; i < count; i++)
        {
            fixed (ExtensionProperties* extension = &extensions[i])
            {
                if (VulkanHelpers.ReadFixedString(extension->ExtensionName, 256) == extensionName)
                    return true;
            }
        }
        return false;
    }

    private void AttachDebugMessenger()
    {
        if (!_vk.TryGetInstanceExtension(Handle, out ExtDebugUtils? debugUtils) || debugUtils is null)
        {
            _log.Debug("ExtDebugUtils extension could not be loaded");
            return;
        }
        _debugUtils = debugUtils;
        DebugUtilsMessengerCreateInfoEXT createInfo = new()
        {
            SType = StructureType.DebugUtilsMessengerCreateInfoExt,
            MessageSeverity = DebugUtilsMessageSeverityFlagsEXT.WarningBitExt
                | DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt,
            MessageType = DebugUtilsMessageTypeFlagsEXT.GeneralBitExt
                | DebugUtilsMessageTypeFlagsEXT.ValidationBitExt
                | DebugUtilsMessageTypeFlagsEXT.PerformanceBitExt,
            PfnUserCallback = new PfnDebugUtilsMessengerCallbackEXT(&OnDebugMessage),
        };
        VulkanHelpers.Throw(
            _debugUtils.CreateDebugUtilsMessenger(Handle, &createInfo, null, out _messenger),
            "vkCreateDebugUtilsMessengerKHR");
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static Bool32 OnDebugMessage(
        DebugUtilsMessageSeverityFlagsEXT severity,
        DebugUtilsMessageTypeFlagsEXT messageType,
        DebugUtilsMessengerCallbackDataEXT* data,
        void* userData)
    {
        string? id = data->PMessageIdName is not null ? Marshal.PtrToStringUTF8((nint)data->PMessageIdName) : null;
        string message = data->PMessage is not null ? Marshal.PtrToStringUTF8((nint)data->PMessage) ?? string.Empty : string.Empty;
        string text = string.IsNullOrEmpty(id) ? message : $"[{id}] {message}";
        if (severity >= DebugUtilsMessageSeverityFlagsEXT.ErrorBitExt)
            Console.Error.WriteLine($"Vulkan validation error: {text}");
        else
            Console.Out.WriteLine($"Vulkan validation warning: {text}");
        return new Bool32(true);
    }

    public void Dispose()
    {
        if (_debugUtils is not null && _messenger.Handle != 0)
            _debugUtils.DestroyDebugUtilsMessenger(Handle, _messenger, null);
        _debugUtils = null;
        _vk.DestroyInstance(Handle, null);
    }
}

internal static class VulkanResultExtensions
{
    public static void ThrowIfNotSuccess(this Result result, ILogger log, string context)
    {
        if (result != Result.Success)
        {
            log.Error($"{context} failed: {VulkanException.ResultToString(result)}");
            throw new VulkanException(result, context);
        }
    }
}
