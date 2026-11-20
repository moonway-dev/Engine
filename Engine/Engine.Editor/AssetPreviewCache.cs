using Engine.Assets;
using Engine.Editor.Models;
using Engine.Rendering;
using Engine.RHI;
using Engine.Scene;

namespace Engine.Editor;

public readonly record struct AssetPreview(IntPtr Texture, int Width, int Height, bool HasTransparency = false, string? Error = null);

public sealed class AssetPreviewCache : IDisposable
{
    private sealed class Entry(AssetEntry asset)
    {
        public AssetEntry Asset { get; } = asset;
        public string Signature { get; set; } = string.Empty;
        public DateTime NextCheck { get; set; }
        public long LastRequested { get; set; }
        public AssetPreview Preview { get; set; }
        public ITexture? Texture { get; set; }
        public Task<PreparedData>? Task { get; set; }
        public CancellationTokenSource? Cancellation { get; set; }
        public MaterialProperties? Properties { get; set; }
        public string?[] TexturePaths { get; set; } = [];
        public bool NeedsUpdate { get; set; }
        public bool Queued { get; set; }
    }

    private sealed record PreparedData(ImageData? Image = null, ModelData? Model = null, bool HasTransparency = false, ImageData?[]? MaterialTextures = null);
    private readonly IDevice _device;
    private readonly MaterialAssetStore _materials;
    private readonly string _root;
    private readonly Func<AssetPreviewRenderer> _rendererFactory;
    private readonly Func<ITexture, IntPtr> _register;
    private readonly Action<ITexture> _unregister;
    private readonly GpuResourceRetirement _retirement;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Entry> _queue = [];
    private readonly List<(Task Task, CancellationTokenSource Cancellation)> _cancelled = [];
    private readonly List<AssetPreviewRenderer.PreviewJob> _jobs = [];
    private AssetPreviewRenderer? _renderer;
    private long _requestFrame;
    private bool _disposed;

    public int MaximumEntries { get; set; } = 256;
    public int MaximumConcurrentImports { get; set; } = 2;
    public int Count => _entries.Count;
    public int PendingCount => _queue.Count;

    public AssetPreviewCache(IDevice device, MaterialAssetStore materials, string root,
        Func<AssetPreviewRenderer> rendererFactory, Func<ITexture, IntPtr> register, Action<ITexture> unregister, GpuResourceRetirement retirement)
    {
        _device = device;
        _materials = materials;
        _root = root;
        _rendererFactory = rendererFactory;
        _register = register;
        _unregister = unregister;
        _retirement = retirement;
    }

    public void BeginRequests() => _requestFrame++;

    public AssetPreview Request(AssetEntry asset)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (asset.Kind is not (AssetKind.Texture or AssetKind.Mesh or AssetKind.Material))
            return default;
        if (!_entries.TryGetValue(asset.FullPath, out Entry? entry))
        {
            if (_entries.Count >= Math.Max(1, MaximumEntries))
            {
                Entry oldest = _entries.Values.MinBy(value => value.LastRequested)!;
                if (oldest.LastRequested >= _requestFrame)
                    return default;
                Remove(oldest);
            }
            entry = new Entry(asset);
            _entries.Add(asset.FullPath, entry);
        }
        entry.LastRequested = _requestFrame;
        if (DateTime.UtcNow >= entry.NextCheck)
        {
            entry.NextCheck = DateTime.UtcNow.AddMilliseconds(250);
            try
            {
                string signature = Stamp(asset.FullPath);
                if (asset.Kind == AssetKind.Material)
                {
                    ResolvedMaterial material = _materials.Resolve(new AssetReference(asset.RelativePath));
                    entry.Properties = material.Properties;
                    entry.TexturePaths = References(material.Properties).Select(reference => reference.IsEmpty ? null : _materials.ResolvePath(reference)).ToArray();
                    signature += ":" + material.Revision + ":" + string.Join("|", entry.TexturePaths.Select(path => path is null ? string.Empty : path + ":" + Stamp(path)));
                }
                if (entry.Signature != signature)
                {
                    entry.Signature = signature;
                    Cancel(entry);
                    entry.Preview = entry.Preview with { Error = null };
                    entry.NeedsUpdate = true;
                    Enqueue(entry);
                }
            }
            catch (Exception exception) when (IsAssetError(exception))
            {
                entry.Preview = entry.Preview with { Error = exception.Message };
            }
        }
        if (entry.NeedsUpdate && !entry.Queued)
            Enqueue(entry);
        return entry.Preview;
    }

    private static bool IsAssetError(Exception exception) => exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or System.Text.Json.JsonException;
    private static string Stamp(string path)
    {
        FileInfo info = new(path);
        return info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : "missing";
    }

    private static AssetReference[] References(MaterialProperties properties) =>
    [properties.AlbedoTexture, properties.MetallicTexture, properties.RoughnessTexture, properties.NormalTexture, properties.AmbientOcclusionTexture, properties.EmissionTexture];

    private void Enqueue(Entry entry)
    {
        if (entry.Queued)
            return;
        entry.Queued = true;
        _queue.Add(entry);
    }

    public void PrepareFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        for (int i = _cancelled.Count - 1; i >= 0; i--)
        {
            if (!_cancelled[i].Task.IsCompleted)
                continue;
            _ = _cancelled[i].Task.Exception;
            _cancelled[i].Cancellation.Dispose();
            _cancelled.RemoveAt(i);
        }
        foreach (Entry entry in _entries.Values)
            if (entry.LastRequested < _requestFrame - 1 && entry.Queued)
                Cancel(entry);
        _queue.RemoveAll(entry => !entry.Queued);
        int running = _queue.Count(entry => entry.Task is not null) + _cancelled.Count;
        foreach (Entry entry in _queue.OrderByDescending(entry => entry.LastRequested).ToArray())
        {
            if (entry.Task is null && running < MaximumConcurrentImports)
            {
                entry.Cancellation = new CancellationTokenSource();
                CancellationToken token = entry.Cancellation.Token;
                string path = entry.Asset.FullPath;
                AssetKind kind = entry.Asset.Kind;
                string?[] textures = entry.TexturePaths;
                entry.Task = Task.Run(() => PrepareData(path, kind, textures, token), token);
                running++;
            }
        }
        Entry? ready = _queue.FirstOrDefault(entry => entry.Task is { IsCompleted: true });
        if (ready is not null)
        {
            ready.Queued = false;
            ready.NeedsUpdate = false;
            _queue.Remove(ready);
            try
            {
                Upload(ready, ready.Task?.GetAwaiter().GetResult());
            }
            catch (Exception exception) when (IsAssetError(exception) || exception is OperationCanceledException)
            {
                ready.Preview = ready.Preview with { Error = exception.Message };
            }
            finally
            {
                ready.Task = null;
                ready.Cancellation?.Dispose();
                ready.Cancellation = null;
            }
        }
        foreach (Entry entry in _entries.Values.OrderBy(entry => entry.LastRequested).ToArray())
        {
            if (_entries.Count <= Math.Max(1, MaximumEntries))
                break;
            if (entry.LastRequested >= _requestFrame - 1)
                continue;
            Remove(entry);
        }
    }

    private PreparedData PrepareData(string path, AssetKind kind, string?[] texturePaths, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (kind == AssetKind.Material)
        {
            ImageData?[] images = new ImageData?[texturePaths.Length];
            for (int i = 0; i < images.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                if (texturePaths[i] is { } texturePath)
                    images[i] = Resize(ImageLoader.Load(texturePath), 512);
            }
            return new PreparedData(MaterialTextures: images);
        }
        if (kind == AssetKind.Mesh)
        {
            ModelData model = ModelPreviewGeometry.Reduce(ModelCache.Load(path, Path.Combine(_root, "Cache", "Models"), out _, token), token);
            if (model.Vertices.Length == 0 || model.Indices.Length == 0)
                throw new InvalidDataException("Model has no geometry.");
            long bytes = (long)model.Vertices.Length * System.Runtime.InteropServices.Marshal.SizeOf<Vertex>() + (long)model.Indices.Length * sizeof(uint);
            if (bytes > 64L * 1024 * 1024)
                throw new InvalidDataException("Model exceeds the 64 MiB preview geometry budget.");
            return new PreparedData(Model: model);
        }
        ImageData image = ImageLoader.Load(path);
        token.ThrowIfCancellationRequested();
        bool transparent = false;
        for (int i = 3; i < image.Pixels.Length; i += 4)
            if (image.Pixels[i] < 255) { transparent = true; break; }
        return new PreparedData(Image: Resize(image, AssetPreviewRenderer.PreviewSize), HasTransparency: transparent);
    }

    public static ImageData Resize(ImageData image, int maximum)
    {
        if (Math.Max(image.Width, image.Height) <= maximum)
            return image;
        float scale = (float)maximum / Math.Max(image.Width, image.Height);
        int width = Math.Max(1, (int)MathF.Round(image.Width * scale));
        int height = Math.Max(1, (int)MathF.Round(image.Height * scale));
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sx = MathF.Max(0f, (x + 0.5f) / scale - 0.5f);
                float sy = MathF.Max(0f, (y + 0.5f) / scale - 0.5f);
                int x0 = Math.Min(image.Width - 1, (int)sx);
                int y0 = Math.Min(image.Height - 1, (int)sy);
                int x1 = Math.Min(image.Width - 1, x0 + 1);
                int y1 = Math.Min(image.Height - 1, y0 + 1);
                for (int c = 0; c < 4; c++)
                {
                    float top = image.Pixels[(y0 * image.Width + x0) * 4 + c] * (1f - (sx - x0)) + image.Pixels[(y0 * image.Width + x1) * 4 + c] * (sx - x0);
                    float bottom = image.Pixels[(y1 * image.Width + x0) * 4 + c] * (1f - (sx - x0)) + image.Pixels[(y1 * image.Width + x1) * 4 + c] * (sx - x0);
                    pixels[(y * width + x) * 4 + c] = (byte)Math.Clamp(top * (1f - (sy - y0)) + bottom * (sy - y0), 0, 255);
                }
            }
        return new ImageData(pixels, width, height, 4);
    }

    private void Upload(Entry entry, PreparedData? data)
    {
        ITexture texture;
        bool transparent = false;
        if (data?.Image is { } image)
        {
            texture = _device.CreateTexture(new TextureDescription { Extent = new Engine.Mathematics.Size2D(image.Width, image.Height), Format = TextureFormat.R8G8B8A8Srgb });
            try { _device.UploadTextureData(texture, image.Pixels); }
            catch { texture.Dispose(); throw; }
            transparent = data.HasTransparency;
        }
        else
        {
            _renderer ??= _rendererFactory();
            Mesh? mesh = data?.Model is { } model ? new Mesh(_device, model.Vertices, model.Indices, entry.Asset.Name) : null;
            Material material = CreateMaterial(entry.Properties);
            List<ITexture> maps = [];
            AssetPreviewRenderer.PreviewJob job;
            try
            {
                if (data?.MaterialTextures is { } images)
                {
                    TextureSemantic[] semantics = [TextureSemantic.Albedo, TextureSemantic.Metallic, TextureSemantic.Roughness, TextureSemantic.Normal, TextureSemantic.AmbientOcclusion, TextureSemantic.Emission];
                    for (int i = 0; i < images.Length; i++)
                    {
                        if (images[i] is not { } map)
                            continue;
                        ITexture mapTexture = _device.CreateTexture(new TextureDescription { Extent = new Engine.Mathematics.Size2D(map.Width, map.Height), Format = AssetDatabase.GetTextureFormat(semantics[i]) });
                        maps.Add(mapTexture);
                        _device.UploadTextureData(mapTexture, map.Pixels);
                        material.SetTexture((MaterialTextureSlot)i, mapTexture);
                    }
                }
                job = _renderer.Prepare(mesh, material, mesh is not null);
            }
            catch
            {
                mesh?.Dispose();
                foreach (ITexture map in maps)
                    map.Dispose();
                throw;
            }
            foreach (ITexture map in maps)
                job.Own(map);
            _jobs.Add(job);
            texture = job.Output;
        }
        IntPtr id = _register(texture);
        ReleaseTexture(entry);
        entry.Texture = texture;
        entry.Preview = new AssetPreview(id, (int)texture.Width, (int)texture.Height, transparent);
    }

    private static Material CreateMaterial(MaterialProperties? properties)
    {
        if (properties is null)
            return new Material();
        Material material = new()
        {
            AlbedoColor = properties.AlbedoColor,
            Metallic = properties.Metallic,
            Roughness = properties.Roughness,
            EmissiveIntensity = properties.EmissiveIntensity,
            EmissionColor = properties.EmissionColor,
            LegacyEmission = properties.LegacyEmission,
            UvScale = properties.UvScale,
            UvOffset = properties.UvOffset,
            NormalScale = properties.NormalScale,
            AoStrength = properties.AoStrength,
            MetallicChannel = properties.MetallicChannel,
            RoughnessChannel = properties.RoughnessChannel,
            AmbientOcclusionChannel = properties.AmbientOcclusionChannel,
        };
        return material;
    }

    public void Record(ICommandBuffer commands)
    {
        foreach (AssetPreviewRenderer.PreviewJob job in _jobs)
        {
            job.Record(commands);
            _retirement.Retire(job);
        }
        _jobs.Clear();
    }

    private void Cancel(Entry entry)
    {
        entry.Queued = false;
        if (entry.Task is not null && entry.Cancellation is not null)
        {
            entry.Cancellation.Cancel();
            _cancelled.Add((entry.Task, entry.Cancellation));
        }
        entry.Task = null;
        entry.Cancellation = null;
        _queue.Remove(entry);
    }

    private void ReleaseTexture(Entry entry)
    {
        if (entry.Texture is null)
            return;
        _unregister(entry.Texture);
        _retirement.Retire(entry.Texture);
        entry.Texture = null;
    }

    private void Remove(Entry entry)
    {
        Cancel(entry);
        ReleaseTexture(entry);
        _entries.Remove(entry.Asset.FullPath);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (Entry entry in _entries.Values.ToArray())
            Remove(entry);
        foreach (var cancelled in _cancelled)
        {
            try { cancelled.Task.GetAwaiter().GetResult(); }
            catch (Exception exception) when (IsAssetError(exception) || exception is OperationCanceledException) { }
            cancelled.Cancellation.Dispose();
        }
        _cancelled.Clear();
        foreach (AssetPreviewRenderer.PreviewJob job in _jobs)
            job.Dispose();
        _jobs.Clear();
        _retirement.Dispose();
        _renderer?.Dispose();
    }
}
