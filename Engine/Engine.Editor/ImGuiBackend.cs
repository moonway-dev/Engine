using System.Reflection;
using System.Runtime.InteropServices;
using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;
using Engine.RHI;
using Engine.Rendering;
using Engine.Shaders;
using ImGuiNET;

namespace Engine.Editor;

public sealed class ImGuiBackend : IDisposable
{
    private const int FrameBufferCount = 4;

    private readonly IDevice _device;
    private readonly IWindow _window;
    private readonly ILogger _log;
    private IGraphicsPipeline? _pipeline;
    private IShaderProgram? _program;
    private ISampler? _sampler;
    private ITexture? _fontTexture;
    private IDescriptorSet? _fontSet;
    private IBuffer? _frameDataBuffer;
    private readonly List<BufferPair> _frameBuffers = [];
    private int _frameIndex;
    private readonly Dictionary<ITexture, IntPtr> _textureIds = [];
    private readonly Dictionary<IntPtr, IDescriptorSet> _descriptorSets = [];
    private int _nextTextureHandle;
    private IntPtr _iniFilename;
    private string? _iniPath;
    private bool _disposed;

    public ImGuiBackend(
        IDevice device,
        IWindow window,
        ILogger log,
        string shaderDirectory,
        TextureFormat overlayFormat,
        string iniPath,
        string? shaderCacheDirectory = null)
    {
        _device = device;
        _window = window;
        _log = log;
        ImGui.CreateContext();
        EditorTheme.Apply();
        _window.TextInput += OnTextInput;
        ImGuiIOPtr io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;
        ConfigureIniFile(io, iniPath);
        CreateDeviceObjects(shaderDirectory, overlayFormat, shaderCacheDirectory);
        for (int i = 0; i < FrameBufferCount; i++)
            _frameBuffers.Add(new BufferPair());
    }

    private unsafe void ConfigureIniFile(ImGuiIOPtr io, string iniPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(iniPath);
        string fullPath = Path.GetFullPath(iniPath);
        _iniPath = fullPath;
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(fullPath + '\0');
        _iniFilename = Marshal.AllocHGlobal(utf8.Length);
        Marshal.Copy(utf8, 0, _iniFilename, utf8.Length);
        io.NativePtr->IniFilename = (byte*)_iniFilename;
        if (File.Exists(fullPath))
            ImGui.LoadIniSettingsFromDisk(fullPath);
    }

    public IntPtr GetTextureId(ITexture texture)
    {
        if (_textureIds.TryGetValue(texture, out IntPtr handle))
            return handle;
        IDescriptorSet set = _program!.CreateDescriptorSet(0);
        set.WriteTexture(0, texture);
        set.WriteSampler(1, _sampler!);
        set.WriteUniformBuffer(2, _frameDataBuffer!);
        _nextTextureHandle++;
        IntPtr id = new(_nextTextureHandle);
        _textureIds[texture] = id;
        _descriptorSets[id] = set;
        return id;
    }

    public Action<IDisposable>? RetireResource { get; set; }

    private void Retire(IDisposable resource)
    {
        if (RetireResource is { } retire)
            retire(resource);
        else
            resource.Dispose();
    }

    public void RemoveTexture(ITexture texture)
    {
        if (!_textureIds.Remove(texture, out IntPtr id))
            return;
        if (_descriptorSets.TryGetValue(id, out IDescriptorSet? set))
            Retire(new RetiredDescriptor(this, id, set));
    }

    private sealed class RetiredDescriptor(ImGuiBackend backend, IntPtr id, IDescriptorSet set) : IDisposable
    {
        public void Dispose()
        {
            if (backend._descriptorSets.Remove(id))
                set.Dispose();
        }
    }

    private void CreateDeviceObjects(string shaderDirectory, TextureFormat overlayFormat, string? shaderCacheDirectory)
    {
        ShaderProgramDescription program = new SlangShaderCompiler(_log, cacheDirectory: shaderCacheDirectory).CompileProgram(
            Path.Combine(shaderDirectory, "ImGui.slang"),
            [(ShaderStage.Vertex, "vsMain"), (ShaderStage.Fragment, "psMain")]);
        _program = _device.CreateShaderProgram(program);
        _pipeline = _device.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            Name = "ImGui",
            ShaderProgram = _program,
            VertexLayouts = [ImGuiVertexLayout],
            CullMode = CullMode.None,
            DepthTest = false,
            DepthWrite = false,
            BlendMode = BlendMode.Alpha,
            ColorAttachmentFormats = [overlayFormat],
        });
        _sampler = _device.CreateSampler(new SamplerDescription
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            AddressMode = AddressMode.Repeat,
        });
        _frameDataBuffer = _device.CreateBuffer(new BufferDescription
        {
            Size = 16,
            Usage = BufferUsage.Uniform | BufferUsage.TransferDestination,
            Memory = MemoryUsage.CpuToGpu,
        });
        CreateFontTexture();
    }

    private void WriteFrameData()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        float[] data =
        [
            io.DisplaySize.X,
            io.DisplaySize.Y,
            0f,
            0f,
        ];
        _device.UploadBufferData(_frameDataBuffer!, [.. data.SelectMany(static value => BitConverter.GetBytes(value))]);
    }

    private void CreateFontTexture()
    {
        ImFontAtlasPtr atlas = ImGui.GetIO().Fonts;
        string? fontPath = ResolveFontPath();
        if (fontPath is null)
            atlas.AddFontDefault();
        else
            atlas.AddFontFromFileTTF(fontPath, 14f);
        atlas.GetTexDataAsRGBA32(out IntPtr pixels, out int width, out int height, out int bytesPerPixel);
        byte[] data = new byte[width * height * bytesPerPixel];
        Marshal.Copy(pixels, data, 0, data.Length);
        _fontTexture = _device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(width, height),
            Format = TextureFormat.R8G8B8A8UNorm,
        });
        _device.UploadTextureData(_fontTexture, data);
        _fontSet = _program!.CreateDescriptorSet(0);
        _fontSet.WriteTexture(0, _fontTexture);
        _fontSet.WriteSampler(1, _sampler!);
        _fontSet.WriteUniformBuffer(2, _frameDataBuffer!);
        _nextTextureHandle++;
        FontTextureId = new(_nextTextureHandle);
        _descriptorSets[FontTextureId] = _fontSet;
        atlas.SetTexID(FontTextureId);
    }

    private readonly Dictionary<string, IntPtr> _iconTextureIds = [];

    public IntPtr GetIconTexture(string name)
    {
        if (_iconTextureIds.TryGetValue(name, out IntPtr id))
            return id;
        byte[] bytes = LoadIconResource(name);
        Engine.Assets.ImageData image = Engine.Assets.ImageLoader.Load(bytes);
        IntPtr textureId = CreateFileTexture(image.Pixels, image.Width, image.Height);
        _iconTextureIds[name] = textureId;
        return textureId;
    }

    private readonly Dictionary<string, (IntPtr Texture, long Revision)> _fileTextureIds = [];
    private const int MaxThumbnailDimension = 256;

    public IntPtr GetFileTexture(string path)
    {
        long revision;
        try
        {
            revision = File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (IOException)
        {
            return IntPtr.Zero;
        }
        if (_fileTextureIds.TryGetValue(path, out (IntPtr Texture, long Revision) cached))
        {
            if (cached.Revision == revision)
                return cached.Texture;
            ITexture previous = _textureKeys[cached.Texture];
            RemoveTexture(previous);
            Retire(previous);
            _textureKeys.Remove(cached.Texture);
            _fileTextureIds.Remove(path);
        }
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return IntPtr.Zero;
        }
        Engine.Assets.ImageData image;
        try
        {
            image = Engine.Assets.ImageLoader.Load(bytes);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return IntPtr.Zero;
        }
        if (Math.Max(image.Width, image.Height) > MaxThumbnailDimension)
            image = Downsample(image, MaxThumbnailDimension);
        IntPtr textureId = CreateFileTexture(image.Pixels, image.Width, image.Height);
        _fileTextureIds[path] = (textureId, revision);
        return textureId;
    }

    private readonly Dictionary<IntPtr, ITexture> _textureKeys = [];

    private IntPtr CreateFileTexture(byte[] pixels, int width = 0, int height = 0)
    {
        if (width == 0 || height == 0)
            (width, height) = (pixels.Length / 4, 1);
        ITexture texture = _device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(width, height),
            Format = TextureFormat.R8G8B8A8UNorm,
        });
        _device.UploadTextureData(texture, pixels);
        IntPtr textureId = GetTextureId(texture);
        _textureKeys[textureId] = texture;
        return textureId;
    }

    private static Engine.Assets.ImageData Downsample(Engine.Assets.ImageData image, int maximum)
    {
        float scale = (float)maximum / Math.Max(image.Width, image.Height);
        int width = Math.Max(1, (int)MathF.Round(image.Width * scale));
        int height = Math.Max(1, (int)MathF.Round(image.Height * scale));
        byte[] result = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(image.Height - 1, (int)((y + 0.5f) / scale));
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(image.Width - 1, (int)((x + 0.5f) / scale));
                int sourceIndex = (sourceY * image.Width + sourceX) * 4;
                int targetIndex = (y * width + x) * 4;
                result[targetIndex] = image.Pixels[sourceIndex];
                result[targetIndex + 1] = image.Pixels[sourceIndex + 1];
                result[targetIndex + 2] = image.Pixels[sourceIndex + 2];
                result[targetIndex + 3] = image.Pixels[sourceIndex + 3];
            }
        }
        return new Engine.Assets.ImageData(result, width, height, 4);
    }

    private static byte[] LoadIconResource(string name)
    {
        Assembly assembly = typeof(ImGuiBackend).Assembly;
        string resourceName = $"Engine.Editor.Icons.{name}.png";
        using Stream? stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Icon '{name}' is not embedded.");
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string? ResolveFontPath()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ?
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "segoeui.ttf"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "segoeuib.ttf"),
            ]
            : OperatingSystem.IsMacOS()
                ? ["/System/Library/Fonts/SFNS.ttf", "/System/Library/Fonts/Helvetica.ttc"]
                : ["/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf"];
        return candidates.FirstOrDefault(File.Exists);
    }

    public IntPtr FontTextureId { get; private set; }

    public System.Numerics.Vector2? MousePositionOverride { get; set; }

    public static VertexLayoutDescription ImGuiVertexLayout { get; } = new()
    {
        Stride = 20,
        Attributes =
        [
            new VertexAttributeDescription { Offset = 0, Format = VertexFormat.Float2 },
            new VertexAttributeDescription { Offset = 8, Format = VertexFormat.Float2 },
            new VertexAttributeDescription { Offset = 16, Format = VertexFormat.UByte4Norm },
        ],
    };

    public void NewFrame(FrameTime frameTime)
    {
        ImGuiIOPtr io = ImGui.GetIO();
        float framebufferWidth = MathF.Max(1f, _window.FramebufferSize.Width);
        float framebufferHeight = MathF.Max(1f, _window.FramebufferSize.Height);
        float clientWidth = MathF.Max(1f, _window.ClientSize.Width);
        float clientHeight = MathF.Max(1f, _window.ClientSize.Height);
        io.DisplaySize = new System.Numerics.Vector2(framebufferWidth, framebufferHeight);
        io.DisplayFramebufferScale = new System.Numerics.Vector2(1f, 1f);
        io.DeltaTime = MathF.Max(frameTime.DeltaTime, 1f / 1000f);
        UpdateInput(io, framebufferWidth / clientWidth, framebufferHeight / clientHeight);
        ImGui.NewFrame();
    }

    private void UpdateInput(ImGuiIOPtr io, float scaleX, float scaleY)
    {
        KeyboardState keyboard = _window.Keyboard;
        bool shift = keyboard.IsDown(KeyCode.LeftShift) || keyboard.IsDown(KeyCode.RightShift);
        bool ctrl = keyboard.IsDown(KeyCode.LeftControl) || keyboard.IsDown(KeyCode.RightControl);
        bool alt = keyboard.IsDown(KeyCode.LeftAlt) || keyboard.IsDown(KeyCode.RightAlt);
        io.AddKeyEvent(ImGuiKey.ModShift, shift);
        io.AddKeyEvent(ImGuiKey.ModCtrl, ctrl);
        io.AddKeyEvent(ImGuiKey.ModAlt, alt);
        foreach ((KeyCode keyCode, ImGuiKey imguiKey) in KeyMap)
            io.AddKeyEvent(imguiKey, keyboard.IsDown(keyCode));
        MouseState mouse = _window.Mouse;
        if (MousePositionOverride is { } overridePosition)
            io.AddMousePosEvent(overridePosition.X, overridePosition.Y);
        else
            io.AddMousePosEvent(mouse.Position.X * scaleX, mouse.Position.Y * scaleY);
        io.AddMouseButtonEvent(0, mouse.IsDown(MouseButton.Left));
        io.AddMouseButtonEvent(1, mouse.IsDown(MouseButton.Right));
        io.AddMouseButtonEvent(2, mouse.IsDown(MouseButton.Middle));
        io.AddMouseWheelEvent(0f, mouse.ScrollDelta.Y);
    }

    private void OnTextInput(string text)
    {
        if (!_disposed && ImGui.GetCurrentContext() != IntPtr.Zero)
            ImGui.GetIO().AddInputCharactersUTF8(text);
    }

    private static readonly Dictionary<KeyCode, ImGuiKey> KeyMap = new()
    {
        [KeyCode.Space] = ImGuiKey.Space,
        [KeyCode.Tab] = ImGuiKey.Tab,
        [KeyCode.Backspace] = ImGuiKey.Backspace,
        [KeyCode.Enter] = ImGuiKey.Enter,
        [KeyCode.Escape] = ImGuiKey.Escape,
        [KeyCode.Delete] = ImGuiKey.Delete,
        [KeyCode.Home] = ImGuiKey.Home,
        [KeyCode.End] = ImGuiKey.End,
        [KeyCode.PageUp] = ImGuiKey.PageUp,
        [KeyCode.PageDown] = ImGuiKey.PageDown,
        [KeyCode.Insert] = ImGuiKey.Insert,
        [KeyCode.Left] = ImGuiKey.LeftArrow,
        [KeyCode.Right] = ImGuiKey.RightArrow,
        [KeyCode.Up] = ImGuiKey.UpArrow,
        [KeyCode.Down] = ImGuiKey.DownArrow,
        [KeyCode.A] = ImGuiKey.A,
        [KeyCode.B] = ImGuiKey.B,
        [KeyCode.C] = ImGuiKey.C,
        [KeyCode.D] = ImGuiKey.D,
        [KeyCode.E] = ImGuiKey.E,
        [KeyCode.F] = ImGuiKey.F,
        [KeyCode.G] = ImGuiKey.G,
        [KeyCode.H] = ImGuiKey.H,
        [KeyCode.I] = ImGuiKey.I,
        [KeyCode.J] = ImGuiKey.J,
        [KeyCode.K] = ImGuiKey.K,
        [KeyCode.L] = ImGuiKey.L,
        [KeyCode.M] = ImGuiKey.M,
        [KeyCode.N] = ImGuiKey.N,
        [KeyCode.O] = ImGuiKey.O,
        [KeyCode.P] = ImGuiKey.P,
        [KeyCode.Q] = ImGuiKey.Q,
        [KeyCode.R] = ImGuiKey.R,
        [KeyCode.S] = ImGuiKey.S,
        [KeyCode.T] = ImGuiKey.T,
        [KeyCode.U] = ImGuiKey.U,
        [KeyCode.V] = ImGuiKey.V,
        [KeyCode.W] = ImGuiKey.W,
        [KeyCode.X] = ImGuiKey.X,
        [KeyCode.Y] = ImGuiKey.Y,
        [KeyCode.Z] = ImGuiKey.Z,
        [KeyCode.F2] = ImGuiKey.F2,
        [KeyCode.LeftControl] = ImGuiKey.ModCtrl,
        [KeyCode.RightControl] = ImGuiKey.ModCtrl,
        [KeyCode.LeftShift] = ImGuiKey.ModShift,
        [KeyCode.RightShift] = ImGuiKey.ModShift,
        [KeyCode.LeftAlt] = ImGuiKey.ModAlt,
        [KeyCode.RightAlt] = ImGuiKey.ModAlt,
    };

    public void Render(ICommandBuffer commandBuffer)
    {
        if (_pipeline is null || _descriptorSets.Count == 0)
            return;
        ImDrawDataPtr drawData = ImGui.GetDrawData();
        float width = drawData.DisplaySize.X;
        float height = drawData.DisplaySize.Y;
        if (width <= 0f || height <= 0f)
            return;

        WriteFrameData();
        BufferPair buffers = _frameBuffers[_frameIndex];
        _frameIndex = (_frameIndex + 1) % FrameBufferCount;

        int vertexCount = 0;
        int indexCount = 0;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            vertexCount += drawData.CmdLists[i].VtxBuffer.Size;
            indexCount += drawData.CmdLists[i].IdxBuffer.Size;
        }
        if (indexCount == 0)
            return;

        int vertexBytes = vertexCount * Marshal.SizeOf<ImDrawVert>();
        int indexBytes = indexCount * sizeof(ushort);
        byte[] vertices = new byte[vertexBytes];
        byte[] indices = new byte[indexBytes];
        int vertexOffset = 0;
        int indexOffset = 0;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];
            unsafe
            {
                fixed (byte* vertexDestination = vertices.AsSpan(vertexOffset))
                    Buffer.MemoryCopy((void*)cmdList.VtxBuffer.Data, vertexDestination, cmdList.VtxBuffer.Size * Marshal.SizeOf<ImDrawVert>(), cmdList.VtxBuffer.Size * Marshal.SizeOf<ImDrawVert>());
                fixed (byte* indexDestination = indices.AsSpan(indexOffset))
                    Buffer.MemoryCopy((void*)cmdList.IdxBuffer.Data, indexDestination, cmdList.IdxBuffer.Size * sizeof(ushort), cmdList.IdxBuffer.Size * sizeof(ushort));
            }
            vertexOffset += cmdList.VtxBuffer.Size * Marshal.SizeOf<ImDrawVert>();
            indexOffset += cmdList.IdxBuffer.Size * sizeof(ushort);
        }

        IBuffer vertexBuffer = EnsureBuffer(buffers, vertexBytes, BufferUsage.Vertex, isVertex: true);
        IBuffer indexBuffer = EnsureBuffer(buffers, indexBytes, BufferUsage.Index, isVertex: false);
        _device.UploadBufferData(vertexBuffer, vertices);
        _device.UploadBufferData(indexBuffer, indices);

        commandBuffer.BindGraphicsPipeline(_pipeline);
        commandBuffer.SetViewport(new Viewport(0f, 0f, width, height, 0f, 1f));
        commandBuffer.SetScissor(new Rect(0, 0, (int)width, (int)height));
        commandBuffer.BindVertexBuffer(0, vertexBuffer);
        commandBuffer.BindIndexBuffer(indexBuffer, IndexType.UInt16);

        int elementOffset = 0;
        int vertexOffsetInVertices = 0;
        IntPtr lastTextureId = IntPtr.Zero;
        for (int i = 0; i < drawData.CmdListsCount; i++)
        {
            ImDrawListPtr cmdList = drawData.CmdLists[i];
            for (int j = 0; j < cmdList.CmdBuffer.Size; j++)
            {
                ImDrawCmdPtr cmd = cmdList.CmdBuffer[j];
                IntPtr textureId = cmd.GetTexID();
                if (textureId != lastTextureId && _descriptorSets.TryGetValue(textureId, out IDescriptorSet? set))
                {
                    commandBuffer.BindDescriptorSet(set, 0);
                    lastTextureId = textureId;
                }
                Rect clip = new(
                    (int)cmd.ClipRect.X,
                    (int)cmd.ClipRect.Y,
                    (int)(cmd.ClipRect.Z - cmd.ClipRect.X),
                    (int)(cmd.ClipRect.W - cmd.ClipRect.Y));
                if (clip.Width <= 0 || clip.Height <= 0)
                    continue;
                commandBuffer.SetScissor(clip);
                commandBuffer.DrawIndexed(
                    cmd.ElemCount,
                    1,
                    (uint)(elementOffset + cmd.IdxOffset),
                    vertexOffsetInVertices + (int)cmd.VtxOffset,
                    0);
            }
            elementOffset += cmdList.IdxBuffer.Size;
            vertexOffsetInVertices += cmdList.VtxBuffer.Size;
        }
        commandBuffer.SetScissor(new Rect(0, 0, (int)width, (int)height));
    }

    private IBuffer EnsureBuffer(BufferPair pair, int requiredBytes, BufferUsage usage, bool isVertex)
    {
        IBuffer? buffer = isVertex ? pair.Vertex : pair.Index;
        ref int currentSize = ref (isVertex ? ref pair.VertexSize : ref pair.IndexSize);
        if (buffer is not null && currentSize >= requiredBytes)
            return buffer;
        buffer?.Dispose();
        int newSize = Math.Max(requiredBytes, currentSize == 0 ? 256 * 1024 : currentSize * 2);
        IBuffer created = _device.CreateBuffer(new BufferDescription
        {
            Size = (ulong)newSize,
            Usage = usage | BufferUsage.TransferDestination,
            Memory = MemoryUsage.CpuToGpu,
        });
        if (isVertex)
        {
            pair.Vertex = created;
            pair.VertexSize = newSize;
        }
        else
        {
            pair.Index = created;
            pair.IndexSize = newSize;
        }
        return created;
    }

    private sealed class BufferPair
    {
        public IBuffer? Vertex;
        public int VertexSize;
        public IBuffer? Index;
        public int IndexSize;
    }

    public unsafe void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _window.TextInput -= OnTextInput;
        if (_iniPath is not null && ImGui.GetCurrentContext() != IntPtr.Zero)
            ImGui.SaveIniSettingsToDisk(_iniPath);
        foreach (BufferPair pair in _frameBuffers)
        {
            pair.Vertex?.Dispose();
            pair.Index?.Dispose();
        }
        _frameBuffers.Clear();
        foreach (IDescriptorSet set in _descriptorSets.Values)
            set.Dispose();
        _descriptorSets.Clear();
        _textureIds.Clear();
        foreach (ITexture texture in _textureKeys.Values)
            texture.Dispose();
        _textureKeys.Clear();
        _iconTextureIds.Clear();
        _fileTextureIds.Clear();
        _frameDataBuffer?.Dispose();
        _fontTexture?.Dispose();
        _sampler?.Dispose();
        _pipeline?.Dispose();
        _program?.Dispose();
        if (_iniFilename != IntPtr.Zero)
        {
            if (ImGui.GetCurrentContext() != IntPtr.Zero)
                ImGui.GetIO().NativePtr->IniFilename = null;
            Marshal.FreeHGlobal(_iniFilename);
            _iniFilename = IntPtr.Zero;
        }
        if (ImGui.GetCurrentContext() != IntPtr.Zero)
            ImGui.DestroyContext();
    }
}
