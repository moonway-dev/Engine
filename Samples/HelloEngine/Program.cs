using Engine.Backends.Null;
using Engine.Backends.Vulkan;
using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;

namespace HelloEngine;

internal static class Program
{
    internal static int Main(string[] args)
    {
        SampleOptions options = SampleOptions.Parse(args);
        RhiModule rhiModule = CreateRhiModule(options);
        Application application = ApplicationBuilder
            .Create()
            .AddLoggerSink(new ConsoleLogSink(LogLevel.Information))
            .AddLoggerSink(new FileLogSink(Path.Combine("Logs"), "hello-engine"))
            .AddModule(new PlatformModule(CreateWindowDescription(options)))
            .AddModule(rhiModule)
            .AddModule(new RhiRenderModule(rhiModule) { ClearColor = new Engine.Mathematics.Vector4(0.25f, 0.5f, 0.9f, 1f) })
            .AddModule(new HelloEngineModule(options, rhiModule))
            .Build();
        application.Run();
        return application.Context.ExitCode;
    }

    private static WindowDescription CreateWindowDescription(SampleOptions options) => new()
    {
        Title = "Engine — Hello Vulkan",
        Width = options.Width,
        Height = options.Height,
    };

    private static RhiModule CreateRhiModule(SampleOptions options)
    {
        RHIConfiguration configuration = new()
        {
            ApplicationName = "HelloEngine",
            EnableValidation = options.Validation,
            VSync = options.VSync,
        };
        if (options.RequestedBackend == GraphicsBackend.Null)
            return new RhiModule(new NullRHIFactory(), configuration);
        VulkanRHIFactory vulkanFactory = new();
        if (!vulkanFactory.IsSupported())
            return new RhiModule(new NullRHIFactory(), configuration);
        return new RhiModule(vulkanFactory, configuration);
    }
}

internal sealed class SampleOptions
{
    public int Width { get; private set; } = 1280;
    public int Height { get; private set; } = 720;
    public int Frames { get; private set; }
    public string? ScreenshotPath { get; private set; }
    public GraphicsBackend RequestedBackend { get; private set; } = GraphicsBackend.Vulkan;
    public bool Validation { get; private set; }
    public bool VSync { get; private set; } = true;

    public static SampleOptions Parse(string[] args)
    {
        SampleOptions options = new();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--width":
                    options.Width = ParseInt(args, ref i, options.Width);
                    break;
                case "--height":
                    options.Height = ParseInt(args, ref i, options.Height);
                    break;
                case "--frames":
                    options.Frames = ParseInt(args, ref i, options.Frames);
                    break;
                case "--screenshot":
                    if (i + 1 < args.Length)
                        options.ScreenshotPath = args[++i];
                    break;
                case "--backend":
                    if (i + 1 < args.Length && args[++i].Equals("null", StringComparison.OrdinalIgnoreCase))
                        options.RequestedBackend = GraphicsBackend.Null;
                    break;
                case "--validation":
                    options.Validation = true;
                    break;
                case "--no-vsync":
                    options.VSync = false;
                    break;
            }
        }
        return options;
    }

    private static int ParseInt(string[] args, ref int index, int fallback)
    {
        if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int value))
            return fallback;
        index++;
        return value;
    }
}

internal sealed class HelloEngineModule(SampleOptions options, RhiModule rhiModule) : IUpdatableModule
{
    private IEngineContext? _context;
    private IWindow? _window;
    private IRHI? _rhi;
    private ILogger? _log;
    private string? _screenshotPath;

    public string Name => "HelloEngine";

    public void Initialize(IEngineContext context)
    {
        _context = context;
        _log = context.Logging.CreateLogger(Name);
        _window = context.Services.Resolve<IWindow>();
        _screenshotPath = options.ScreenshotPath;
    }

    public void Update(in FrameTime frameTime)
    {
        if (_rhi is null)
        {
            _rhi = rhiModule.Instance;
            if (_rhi is null)
                return;
            if (_rhi.Backend == GraphicsBackend.Null && options.RequestedBackend != GraphicsBackend.Null)
                _log!.Warning("Vulkan is not available on this machine, running with the Null renderer (no output)");
            _log!.Information($"Sample started: {_rhi.Device.Features.DeviceName}, backend {_rhi.Backend}");
        }

        if (_window!.Keyboard.IsPressed(KeyCode.Escape))
        {
            _context!.RequestExit();
            return;
        }

        if (_rhi!.Backend != GraphicsBackend.Null)
        {
            float hue = (frameTime.TotalTime * 0.25f) % 1f;
            rhiModule.ClearColor = ColorFromHue(hue).ToVector4();
        }

        if (options.Frames > 0 && frameTime.FrameIndex >= (ulong)options.Frames)
            _context!.RequestExit();

        if (_screenshotPath is not null && frameTime.FrameIndex >= 15)
        {
            _rhi!.WaitIdle();
            _log!.Information($"DBG currentImageIndex={_rhi.CurrentImageIndex}");
            byte[]? pixels = _rhi.ReadSwapchainPixels();
            if (pixels is not null)
            {
                WritePpm(_screenshotPath, (int)_rhi.Swapchain.Extent.Width, (int)_rhi.Swapchain.Extent.Height, pixels);
                _log!.Information($"Screenshot saved to {Path.GetFullPath(_screenshotPath)}");
            }
            if (_rhi.ReadAllSwapchainPixels() is byte[] allPixels)
            {
                int imageBytes = (int)_rhi.Swapchain.Extent.Width * (int)_rhi.Swapchain.Extent.Height * 3;
                int imageCount = (int)_rhi.SwapchainImageCount;
                for (int i = 0; i < imageCount; i++)
                {
                    int r = 0, g = 0, b = 0, n = 0;
                    int offset = i * imageBytes;
                    for (int p = offset; p < offset + imageBytes; p += 997)
                    {
                        r += allPixels[p]; g += allPixels[p + 1]; b += allPixels[p + 2]; n++;
                    }
                    _log!.Information($"DBG image {i}: avg=({r / n}, {g / n}, {b / n})");
                }
            }
            _screenshotPath = null;
            _context!.RequestExit();
        }
    }

    private static Color ColorFromHue(float hue)
    {
        float sector = hue * 6f;
        float fraction = sector - MathF.Floor(sector);
        float section = MathF.Floor(sector) % 6f;
        return section switch
        {
            0 => new Color(1f, fraction, 0f),
            1 => new Color(1f - fraction, 1f, 0f),
            2 => new Color(0f, 1f, fraction),
            3 => new Color(0f, 1f - fraction, 1f),
            4 => new Color(fraction, 0f, 1f),
            _ => new Color(1f, 0f, 1f - fraction),
        };
    }

    private static void WritePpm(string path, int width, int height, byte[] bgraPixels)
    {
        using FileStream stream = File.Create(path);
        using BinaryWriter writer = new(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n"));
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * width + x) * 4;
                writer.Write(bgraPixels[index + 2]);
                writer.Write(bgraPixels[index + 1]);
                writer.Write(bgraPixels[index]);
            }
        }
    }

    public void Shutdown()
    {
        _log?.Information("Sample stopped");
    }
}
