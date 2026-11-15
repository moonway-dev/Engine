using Silk.NET.Vulkan;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanPipelineCache : IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly string _path;
    private bool _disposed;

    public PipelineCache Handle { get; }

    public VulkanPipelineCache(Vk vk, VulkanDevice device, VulkanPhysicalDevice physical)
    {
        _vk = vk;
        _device = device;
        string key = $"{physical.Properties.VendorID:x8}-{physical.Properties.DeviceID:x8}-{physical.Properties.DriverVersion:x8}";
        _path = Path.Combine("Cache", "Vulkan", key + ".bin");
        byte[] data = File.Exists(_path) ? File.ReadAllBytes(_path) : [];
        fixed (byte* dataPointer = data)
        {
            PipelineCacheCreateInfo createInfo = new()
            {
                SType = StructureType.PipelineCacheCreateInfo,
                InitialDataSize = (nuint)data.Length,
                PInitialData = data.Length == 0 ? null : dataPointer,
            };
            Result result = vk.CreatePipelineCache(device.Handle, &createInfo, null, out PipelineCache cache);
            if (result != Result.Success && data.Length > 0)
            {
                createInfo.InitialDataSize = 0;
                createInfo.PInitialData = null;
                VulkanHelpers.Throw(vk.CreatePipelineCache(device.Handle, &createInfo, null, out cache), "vkCreatePipelineCache");
            }
            else
            {
                VulkanHelpers.Throw(result, "vkCreatePipelineCache");
            }
            Handle = cache;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        nuint size = 0;
        if (_vk.GetPipelineCacheData(_device.Handle, Handle, &size, null) == Result.Success && size > 0)
        {
            byte[] data = new byte[(int)size];
            fixed (byte* pointer = data)
            {
                if (_vk.GetPipelineCacheData(_device.Handle, Handle, &size, pointer) == Result.Success)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                    File.WriteAllBytes(_path, data.AsSpan(0, (int)size).ToArray());
                }
            }
        }
        _vk.DestroyPipelineCache(_device.Handle, Handle, null);
    }
}
