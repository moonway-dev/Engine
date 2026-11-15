using VkImageLayout = Silk.NET.Vulkan.ImageLayout;
using VkSemaphore = Silk.NET.Vulkan.Semaphore;
using Engine.Core;
using Engine.Mathematics;
using Engine.RHI;
using VkFormat = Silk.NET.Vulkan.Format;
using Silk.NET.Vulkan;
using Silk.NET.Vulkan.Extensions.KHR;

namespace Engine.Backends.Vulkan;

internal unsafe sealed class VulkanSwapchain : ISwapchain, IDisposable
{
    private readonly Vk _vk;
    private readonly VulkanDevice _device;
    private readonly KhrSurface _khrSurface;
    private readonly SurfaceKHR _surface;
    private readonly ILogger _log;
    private SwapchainKHR _handle;
    private ImageView[] _imageViews = [];
    private VulkanSwapchainImage[] _renderTargets = [];
    private Image[] _images = [];
    private Size2D _requestedExtent;
    private bool _dirty;

    public bool IsSuspended { get; private set; }

    public ulong Generation { get; private set; }

    public TextureFormat Format { get; private set; } = TextureFormat.Unknown;

    public VkFormat VulkanFormat { get; private set; }

    public Size2D Extent { get; private set; }

    public uint ImageCount => (uint)_imageViews.Length;

    public ITexture GetRenderTarget(uint imageIndex) => _renderTargets[imageIndex];

    public SwapchainKHR Handle => _handle;

    public bool VSync { get; set; }

    public void RequestRecreation() => _dirty = true;

    public VulkanSwapchain(Vk vk, VulkanDevice device, KhrSurface khrSurface, SurfaceKHR surface, bool vsync, Size2D fallbackExtent, ILogger log)
    {
        _vk = vk;
        _device = device;
        _khrSurface = khrSurface;
        _surface = surface;
        _log = log;
        VSync = vsync;
        Extent = fallbackExtent;
        _requestedExtent = fallbackExtent;
        CreateOrRecreate(fallbackExtent);
        _supportedUsage = _lastSupportedUsage;
    }

    private ImageUsageFlags _lastSupportedUsage;

    public void Resize(Size2D newExtent)
    {
        _requestedExtent = newExtent;
        if (newExtent.Width == 0 || newExtent.Height == 0)
        {
            IsSuspended = true;
            return;
        }
        IsSuspended = false;
        if (newExtent != Extent || _dirty)
        {
            Extent = newExtent;
            _dirty = true;
        }
    }

    public void EnsureValid()
    {
        if (IsSuspended || _requestedExtent.Width == 0 || _requestedExtent.Height == 0)
            return;
        if (_dirty)
        {
            _device.WaitForPresentation();
            CreateOrRecreate(_requestedExtent);
            _dirty = false;
        }
    }

    private void CreateOrRecreate(Size2D requestedExtent)
    {
        SwapchainKHR oldSwapchain = _handle;
        VulkanSwapchainImage[] oldRenderTargets = _renderTargets;
        Image[] oldImages = _images;
        ImageView[] oldImageViews = _imageViews;
        _handle = default;
        _renderTargets = [];
        _images = [];
        _imageViews = [];
        SurfaceCapabilitiesKHR capabilities = GetCapabilities();
        Extent = SelectExtent(capabilities, requestedExtent);
        VulkanFormat = SelectFormat();
        Format = VulkanHelpers.FromVulkanFormat(VulkanFormat);
        PresentModeKHR presentMode = SelectPresentMode();

        uint imageCount = presentMode == PresentModeKHR.MailboxKhr
            ? capabilities.MinImageCount + 1
            : Math.Max(capabilities.MinImageCount, 2);
        if (capabilities.MaxImageCount > 0 && imageCount > capabilities.MaxImageCount)
            imageCount = capabilities.MaxImageCount;

        uint graphicsFamily = _device.Physical.GraphicsFamilyIndex;
        uint presentFamily = _device.Physical.PresentFamilyIndex;
        SharingMode sharingMode = graphicsFamily == presentFamily ? SharingMode.Exclusive : SharingMode.Concurrent;
        uint[] familyIndices = [graphicsFamily, presentFamily];
        fixed (uint* familyPointer = sharingMode == SharingMode.Concurrent ? familyIndices : null)
        {
            SwapchainCreateInfoKHR createInfo = new()
            {
                SType = StructureType.SwapchainCreateInfoKhr,
                Surface = _surface,
                MinImageCount = imageCount,
                ImageFormat = VulkanFormat,
                ImageColorSpace = ColorSpaceKHR.SpaceSrgbNonlinearKhr,
                ImageExtent = new Extent2D((uint)Extent.Width, (uint)Extent.Height),
                ImageArrayLayers = 1,
                ImageUsage = (capabilities.SupportedUsageFlags & ImageUsageFlags.TransferSrcBit) != 0
                    ? ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit
                    : ImageUsageFlags.ColorAttachmentBit | ImageUsageFlags.TransferDstBit,
                ImageSharingMode = sharingMode,
                QueueFamilyIndexCount = sharingMode == SharingMode.Concurrent ? 2u : 0u,
                PQueueFamilyIndices = familyPointer,
                PreTransform = capabilities.CurrentTransform,
                CompositeAlpha = CompositeAlphaFlagsKHR.OpaqueBitKhr,
                PresentMode = presentMode,
                Clipped = true,
                OldSwapchain = oldSwapchain,
            };
            VulkanHelpers.Throw(
                _device.SwapchainExtension.CreateSwapchain(_device.Handle, &createInfo, null, out SwapchainKHR swapchain),
                "vkCreateSwapchainKHR");
            _handle = swapchain;
            _lastSupportedUsage = createInfo.ImageUsage;
        }

        _images = GetSwapchainImages();
        _imageViews = CreateImageViews();
        _renderTargets = new VulkanSwapchainImage[_images.Length];
        for (int i = 0; i < _images.Length; i++)
        {
            _renderTargets[i] = new VulkanSwapchainImage(
                _vk, _device, _images[i], _imageViews[i], Format,
                (uint)Extent.Width, (uint)Extent.Height);
        }
        foreach (VulkanSwapchainImage renderTarget in oldRenderTargets)
            renderTarget.Dispose();
        _ = oldImages;
        _ = oldImageViews;
        if (oldSwapchain.Handle != 0)
            _device.SwapchainExtension.DestroySwapchain(_device.Handle, oldSwapchain, null);
        _supportedUsage = _lastSupportedUsage;
        Generation++;
        _log.Debug($"Vulkan swapchain created: {Extent.Width}x{Extent.Height}, {_imageViews.Length} images, format {VulkanFormat}, vsync {VSync}, present {presentMode}");
    }

    private SurfaceCapabilitiesKHR GetCapabilities()
    {
        VulkanHelpers.Throw(
            _khrSurface.GetPhysicalDeviceSurfaceCapabilities(_device.Physical.Handle, _surface, out SurfaceCapabilitiesKHR capabilities),
            "vkGetPhysicalDeviceSurfaceCapabilitiesKHR");
        return capabilities;
    }

    private Size2D SelectExtent(SurfaceCapabilitiesKHR capabilities, Size2D requested)
    {
        if (capabilities.CurrentExtent.Width != uint.MaxValue && capabilities.CurrentExtent.Height != uint.MaxValue)
            return new Size2D((int)capabilities.CurrentExtent.Width, (int)capabilities.CurrentExtent.Height);
        int width = Math.Clamp(requested.Width, (int)capabilities.MinImageExtent.Width, (int)capabilities.MaxImageExtent.Width);
        int height = Math.Clamp(requested.Height, (int)capabilities.MinImageExtent.Height, (int)capabilities.MaxImageExtent.Height);
        return new Size2D(width, height);
    }

    private VkFormat SelectFormat()
    {
        uint count = 0;
        VulkanHelpers.Throw(
            _khrSurface.GetPhysicalDeviceSurfaceFormats(_device.Physical.Handle, _surface, &count, null),
            "vkGetPhysicalDeviceSurfaceFormatsKHR");
        SurfaceFormatKHR[] formats = new SurfaceFormatKHR[count];
        fixed (SurfaceFormatKHR* pointer = formats)
            VulkanHelpers.Throw(
                _khrSurface.GetPhysicalDeviceSurfaceFormats(_device.Physical.Handle, _surface, &count, pointer),
                "vkGetPhysicalDeviceSurfaceFormatsKHR");

        foreach (SurfaceFormatKHR format in formats)
        {
            if (format.Format == VkFormat.B8G8R8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return format.Format;
        }
        foreach (SurfaceFormatKHR format in formats)
        {
            if (format.Format == VkFormat.R8G8B8A8Srgb && format.ColorSpace == ColorSpaceKHR.SpaceSrgbNonlinearKhr)
                return format.Format;
        }
        return formats[0].Format;
    }

    private PresentModeKHR SelectPresentMode()
    {
        uint count = 0;
        VulkanHelpers.Throw(
            _khrSurface.GetPhysicalDeviceSurfacePresentModes(_device.Physical.Handle, _surface, &count, null),
            "vkGetPhysicalDeviceSurfacePresentModesKHR");
        PresentModeKHR[] modes = new PresentModeKHR[count];
        fixed (PresentModeKHR* pointer = modes)
            VulkanHelpers.Throw(
                _khrSurface.GetPhysicalDeviceSurfacePresentModes(_device.Physical.Handle, _surface, &count, pointer),
                "vkGetPhysicalDeviceSurfacePresentModesKHR");

        if (!VSync)
        {
            foreach (PresentModeKHR mode in modes)
            {
                if (mode == PresentModeKHR.ImmediateKhr)
                    return mode;
            }
            foreach (PresentModeKHR mode in modes)
            {
                if (mode == PresentModeKHR.MailboxKhr)
                    return mode;
            }
        }
        return PresentModeKHR.FifoKhr;
    }

    private Image[] GetSwapchainImages()
    {
        uint count = 0;
        VulkanHelpers.Throw(
            _device.SwapchainExtension.GetSwapchainImages(_device.Handle, _handle, &count, null),
            "vkGetSwapchainImagesKHR");
        Image[] images = new Image[count];
        fixed (Image* pointer = images)
            VulkanHelpers.Throw(
                _device.SwapchainExtension.GetSwapchainImages(_device.Handle, _handle, &count, pointer),
                "vkGetSwapchainImagesKHR");
        return images;
    }

    private ImageView[] CreateImageViews()
    {
        ImageView[] views = new ImageView[_images.Length];
        for (int i = 0; i < _images.Length; i++)
        {
            ImageViewCreateInfo createInfo = new()
            {
                SType = StructureType.ImageViewCreateInfo,
                Image = _images[i],
                ViewType = ImageViewType.Type2D,
                Format = VulkanFormat,
                SubresourceRange = new ImageSubresourceRange
                {
                    AspectMask = ImageAspectFlags.ColorBit,
                    BaseMipLevel = 0,
                    LevelCount = 1,
                    BaseArrayLayer = 0,
                    LayerCount = 1,
                },
            };
            VulkanHelpers.Throw(
                _vk.CreateImageView(_device.Handle, &createInfo, null, out ImageView view),
                "vkCreateImageView");
            views[i] = view;
        }
        return views;
    }

    public AcquireResult AcquireImage(VkSemaphore signalSemaphore)
    {
        EnsureValid();
        uint imageIndex = 0;
        Result result = _device.SwapchainExtension.AcquireNextImage(
            _device.Handle, _handle, ulong.MaxValue, signalSemaphore, default, ref imageIndex);
        if (result == Result.ErrorOutOfDateKhr)
        {
            _dirty = true;
            return AcquireResult.OutOfDateValue;
        }
        if (result == Result.SuboptimalKhr)
            _dirty = true;
        else
            VulkanHelpers.Throw(result, "vkAcquireNextImageKHR");
        return AcquireResult.Acquired(imageIndex);
    }

    public Result Present(Queue queue, VkSemaphore waitSemaphore, uint imageIndex)
    {
        VkSemaphore wait = waitSemaphore;
        SwapchainKHR swapchain = _handle;
        uint imageIndexValue = imageIndex;
        Result* results = stackalloc Result[1];
        PresentInfoKHR presentInfo = new()
        {
            SType = StructureType.PresentInfoKhr,
            WaitSemaphoreCount = 1,
            PWaitSemaphores = &wait,
            SwapchainCount = 1,
            PSwapchains = &swapchain,
            PImageIndices = &imageIndexValue,
            PResults = results,
        };
        Result result = _device.SwapchainExtension.QueuePresent(queue, &presentInfo);
        if (result == Result.ErrorOutOfDateKhr)
        {
            _dirty = true;
            return result;
        }
        if (result == Result.SuboptimalKhr)
        {
            _dirty = true;
            return Result.Success;
        }
        VulkanHelpers.Throw(result, "vkQueuePresentKHR");
        return result;
    }

    public ImageView GetImageView(uint imageIndex) => _imageViews[imageIndex];

    public Image GetImage(uint imageIndex) => _images[imageIndex];

    public byte[]? ReadPixels(uint imageIndex, VulkanReadback readback)
    {
        if ((_supportedUsage & ImageUsageFlags.TransferSrcBit) == 0 || imageIndex >= (uint)_images.Length)
            return null;
        VulkanSwapchainImage target = _renderTargets[imageIndex];
        return readback.ReadTexturePixels(target, VkImageLayout.PresentSrcKhr);
    }

    public byte[]? ReadAllPixels(VulkanReadback readback)
    {
        if ((_supportedUsage & ImageUsageFlags.TransferSrcBit) == 0)
            return null;
        List<byte> combined = [];
        for (uint i = 0; i < (uint)_images.Length; i++)
        {
            byte[] pixels = readback.ReadTexturePixels(_renderTargets[i], VkImageLayout.PresentSrcKhr);
            combined.AddRange(pixels);
        }
        return [.. combined];
    }

    public uint SwapchainImageCount => (uint)_images.Length;

    private ImageUsageFlags _supportedUsage;

    private void DestroySwapchainObjects()
    {
        foreach (VulkanSwapchainImage renderTarget in _renderTargets)
            renderTarget.Dispose();
        _renderTargets = [];
        _imageViews = [];
        _images = [];
        if (_handle.Handle != 0)
            _device.SwapchainExtension.DestroySwapchain(_device.Handle, _handle, null);
        _handle = default;
    }

    public void Dispose()
    {
        DestroySwapchainObjects();
    }

    public readonly struct AcquireResult
    {
        public bool OutOfDate { get; private init; }

        public uint ImageIndex { get; private init; }

        public static AcquireResult OutOfDateValue => new() { OutOfDate = true };

        public static AcquireResult Acquired(uint imageIndex) => new() { ImageIndex = imageIndex };
    }
}
