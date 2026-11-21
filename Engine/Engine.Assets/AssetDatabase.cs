using System.Security.Cryptography;
using Engine.Scene;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Rendering;

namespace Engine.Assets;

public enum TextureSemantic
{
    Albedo,
    Metallic,
    Roughness,
    Normal,
    AmbientOcclusion,
    Emission,
}

public sealed class AssetDatabase : IDisposable
{
    private readonly IDevice _device;
    private readonly string _rootDirectory;
    private readonly Dictionary<string, Mesh> _meshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<MeshImportResult>> _meshImports = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _failedMeshes = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _importCancellation = new();
    private readonly SemaphoreSlim _importGate = new(1, 1);
    private readonly Dictionary<(string Path, string Part), LodChainState> _lodChains = [];
    private readonly SemaphoreSlim _lodGate = new(1, 1);

    public int PendingMeshImports => _meshImports.Count;
    public double LastMeshUploadMilliseconds { get; private set; }
    public double LastMeshImportMilliseconds { get; private set; }
    public bool LastMeshCacheHit { get; private set; }
    private sealed record MeshImportResult(ModelData Model, double Milliseconds, bool CacheHit);
    private string ModelCacheDirectory => Path.Combine(_rootDirectory, "Cache", "Models");
    private readonly Dictionary<string, ModelData> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string Path, string Part), Mesh> _partMeshes = [];

    public MeshLodChainOptions LodOptions { get; set; } = new();
    public int LodUploadBudget { get; set; } = 2;
    public int PendingLodUploads => _lodChains.Values.Sum(state => state.Pending.Count);
    public int GeneratingLodChains => _lodChains.Values.Count(state => state.Generation is not null);
    public int UploadedLodLevels { get; private set; }
    public double LastLodGenerationMilliseconds { get; private set; }
    public bool LastLodCacheHit { get; private set; }
    public int LodChainCount => _lodChains.Count;

    private sealed class LodChainState
    {
        public required string Path { get; init; }
        public required string PartId { get; init; }
        public required MeshLodChainOptions Options { get; init; }
        public Mesh? Mesh { get; set; }
        public Task<IReadOnlyList<MeshLodData>>? Generation { get; set; }
        public bool Failed { get; set; }
        public Queue<MeshLodData> Pending { get; } = new();
        public bool HasUploadedLevels { get; set; }
    }

    private void BeginLodChain(string path, string partId, ModelData model, Mesh mesh)
    {
        if (mesh.IndexCount / 3 <= Math.Max(LodOptions.MinTriangles, 4))
            return;
        (string Path, string Part) key = (path, partId);
        if (_lodChains.TryGetValue(key, out LodChainState? existing))
        {
            existing.Mesh = mesh;
            return;
        }
        LodChainState state = new()
        {
            Path = path,
            PartId = partId,
            Options = LodOptions,
            Mesh = mesh,
        };
        _lodChains[key] = state;
        CancellationToken token = _importCancellation.Token;
        string sourcePath = path;
        state.Generation = Task.Run(async () =>
        {
            await _lodGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                IReadOnlyList<MeshLodData>? chain = MeshLodChainCache.TryLoad(sourcePath, partId, state.Options, ModelCacheDirectory, token);
                bool cacheHit = chain is not null;
                if (chain is null)
                {
                    (Vertex[] vertices, uint[] indices) = model.ExtractPart(partId);
                    chain = MeshLodChainGenerator.Generate(vertices, indices, state.Options, token);
                    if (chain.Count > 0)
                        MeshLodChainCache.Save(sourcePath, partId, state.Options, ModelCacheDirectory, chain, token);
                }
                double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                LastLodGenerationMilliseconds = milliseconds;
                LastLodCacheHit = cacheHit;
                System.Diagnostics.Trace.WriteLine($"LOD chain {sourcePath}#{partId}: {milliseconds:F1} ms; levels {chain.Count}; cache {cacheHit}");
                return chain;
            }
            finally
            {
                _lodGate.Release();
            }
        }, token);
    }

    public void PumpLodUploads()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_lodChains.Count == 0)
            return;
        int budget = Math.Max(1, LodUploadBudget);
        int uploaded = 0;
        foreach (LodChainState state in _lodChains.Values)
        {
            if (state.Generation is { } generation)
            {
                if (!generation.IsCompleted)
                    continue;
                state.Generation = null;
                try
                {
                    IReadOnlyList<MeshLodData> chain = generation.GetAwaiter().GetResult();
                    if (state.HasUploadedLevels && chain.Count > 0)
                    {
                        state.Failed = true;
                        Diagnostic?.Invoke($"LOD chain '{state.Path}#{state.PartId}': received a replacement chain after upload started.");
                        continue;
                    }
                    foreach (MeshLodData entry in chain)
                        state.Pending.Enqueue(entry);
                }
                catch (MeshLodMemoryBudgetException exception)
                {
                    state.Failed = true;
                    Diagnostic?.Invoke($"LOD chain '{state.Path}#{state.PartId}': {exception.Message} Source: {exception.EstimatedBytes / (1024.0 * 1024.0):F0} MiB estimated, {exception.MemoryBudget / (1024.0 * 1024.0):F0} MiB budget.");
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    state.Failed = true;
                    Diagnostic?.Invoke($"LOD chain '{state.Path}#{state.PartId}': {exception.Message}");
                }
            }
            if (state.Mesh is null || state.Failed)
                continue;
            while (uploaded < budget && state.Pending.Count > 0)
            {
                MeshLodData entry = state.Pending.Dequeue();
                try
                {
                    state.Mesh.AddLod(entry.Vertices, entry.Indices, entry.Error);
                    state.HasUploadedLevels = true;
                    uploaded++;
                    UploadedLodLevels++;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
                {
                    state.Failed = true;
                    Diagnostic?.Invoke($"LOD upload '{state.Path}#{state.PartId}': {exception.Message}");
                    break;
                }
            }
            if (uploaded >= budget)
                break;
        }
    }
    private readonly Dictionary<(string Path, TextureSemantic Semantic), ITexture> _textures = [];
    private readonly Dictionary<string, TextureFileState> _textureFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _textureRefreshQueue = [];
    private int _textureRefreshBudget = 16;

    private readonly record struct FileStamp(bool Exists, long Length, DateTime LastWriteTimeUtc);

    private sealed class TextureFileState(FileStamp stamp, string hash)
    {
        public FileStamp Stamp { get; set; } = stamp;
        public string Hash { get; set; } = hash;
    }

    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMilliseconds(250);

    public int TextureRefreshBudget
    {
        get => _textureRefreshBudget;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
            _textureRefreshBudget = value;
        }
    }
    private readonly Dictionary<string, long> _textureRevisions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ITexture> _retiredTextures = [];
    private DateTime _nextRefresh;
    private long _textureRevision;
    private bool _disposed;

    public AssetCatalog Catalog { get; }
    public MaterialAssetStore Materials { get; }
    public event Action<string>? Diagnostic;

    public AssetDatabase(IDevice device, string rootDirectory)
    {
        _device = device;
        _rootDirectory = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(_rootDirectory))
            Directory.CreateDirectory(_rootDirectory);
        Catalog = new AssetCatalog(_rootDirectory);
        Materials = new MaterialAssetStore(Catalog);
        Materials.Diagnostic += message => Diagnostic?.Invoke(message);
    }

    public string ResolvePath(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        return Catalog.ResolvePath(new AssetReference(relativePath));
    }

    public Mesh LoadMesh(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string path = NormalizeAssetPath(relativePath);
        if (_meshes.TryGetValue(path, out Mesh? mesh))
            return mesh;
        ModelData data = ModelCache.Load(path, ModelCacheDirectory, out bool cacheHit);
        LastMeshCacheHit = cacheHit;
        mesh = new Mesh(_device, data.Vertices, data.Indices, Path.GetFileNameWithoutExtension(path));
        _meshes[path] = mesh;
        BeginLodChain(path, string.Empty, data, mesh);
        return mesh;
    }

    public Mesh? RequestMesh(string relativePath) => RequestMesh(relativePath, string.Empty);

    public Mesh? RequestMesh(string relativePath, string partId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string path = NormalizeAssetPath(relativePath);
        if (string.IsNullOrEmpty(partId) && _meshes.TryGetValue(path, out Mesh? cached))
            return cached;
        if (!string.IsNullOrEmpty(partId) && _partMeshes.TryGetValue((path, partId), out cached))
            return cached;
        ModelData? model = RequestModel(relativePath);
        if (model is null)
            return null;
        try
        {
            var data = model.ExtractPart(partId);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            Mesh mesh = new(_device, data.Vertices, data.Indices, Path.GetFileNameWithoutExtension(path));
            LastMeshUploadMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (string.IsNullOrEmpty(partId))
                _meshes.Add(path, mesh);
            else
                _partMeshes.Add((path, partId), mesh);
            BeginLodChain(path, partId, model, mesh);
            return mesh;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _failedMeshes.Add(path);
            Diagnostic?.Invoke($"Mesh '{relativePath}': {exception.Message}");
            return null;
        }
    }

    public ModelData? RequestModel(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        string path = NormalizeAssetPath(relativePath);
        if (_models.TryGetValue(path, out ModelData? model))
            return model;
        if (_failedMeshes.Contains(path))
            return null;
        if (!_meshImports.TryGetValue(path, out Task<MeshImportResult>? import))
        {
            CancellationToken token = _importCancellation.Token;
            import = Task.Run(async () =>
            {
                await _importGate.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    long start = System.Diagnostics.Stopwatch.GetTimestamp();
                    var data = ModelCache.Load(path, ModelCacheDirectory, out bool cacheHit, token);
                    double milliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    System.Diagnostics.Trace.WriteLine($"Model import {path}: {milliseconds:F1} ms; cache {cacheHit}");
                    return new MeshImportResult(data, milliseconds, cacheHit);
                }
                finally
                {
                    _importGate.Release();
                }
            }, token);
            _meshImports.Add(path, import);
            return null;
        }
        if (!import.IsCompleted)
            return null;
        _meshImports.Remove(path);
        try
        {
            MeshImportResult result = import.GetAwaiter().GetResult();
            ModelData data = result.Model;
            LastMeshImportMilliseconds = result.Milliseconds;
            LastMeshCacheHit = result.CacheHit;
            _models.Add(path, data);
            return data;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _failedMeshes.Add(path);
            Diagnostic?.Invoke($"Mesh '{relativePath}': {exception.Message}");
            return null;
        }
    }

    public bool IsMeshImportFailed(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _failedMeshes.Contains(NormalizeAssetPath(relativePath));
    }

    public bool RetryMeshImport(string relativePath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _failedMeshes.Remove(NormalizeAssetPath(relativePath));
    }

    public string ResolvePath(AssetReference reference) => ResolvePath(reference.Key);
    public Mesh LoadMesh(AssetReference reference) => LoadMesh(reference.Key);
    public ITexture LoadTexture(AssetReference reference) => LoadTexture(reference.Key);
    public ITexture LoadTexture(AssetReference reference, TextureSemantic semantic) => LoadTexture(reference.Key, semantic);
    public ITexture LoadTexture(string relativePath) => LoadTexture(relativePath, TextureSemantic.Albedo);

    public long GetTextureRevision(AssetReference reference) => GetTextureRevision(reference.Key);

    public long GetTextureRevision(string reference)
    {
        Refresh();
        string path = NormalizeAssetPath(reference);
        return _textureRevisions.GetValueOrDefault(path);
    }

    public void Refresh() => Refresh(false);

    public void Refresh(bool force)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Materials.Refresh();
        DateTime now = DateTime.UtcNow;
        if (!force && now < _nextRefresh)
            return;
        _nextRefresh = now + RefreshInterval;
        int count = force ? _textureRefreshQueue.Count : Math.Min(TextureRefreshBudget, _textureRefreshQueue.Count);
        for (int i = 0; i < count; i++)
        {
            string path = _textureRefreshQueue.Dequeue();
            _textureRefreshQueue.Enqueue(path);
            TextureFileState state = _textureFiles[path];
            try
            {
                FileStamp stamp = GetFileStamp(path);
                if (!force && stamp == state.Stamp)
                    continue;
                state.Stamp = stamp;
                string hash = HashTextureFile(path);
                if (hash == state.Hash)
                    continue;
                var keys = _textures.Keys.Where(key => key.Path.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
                List<((string Path, TextureSemantic Semantic) Key, ITexture Texture)> replacements = [];
                try
                {
                    foreach (var key in keys)
                        replacements.Add((key, CreateTexture(path, key.Semantic)));
                    if (GetFileStamp(path) != stamp)
                    {
                        state.Stamp = default;
                        throw new IOException("Texture changed while refreshing.");
                    }
                }
                catch
                {
                    foreach (var replacement in replacements)
                        replacement.Texture.Dispose();
                    throw;
                }
                foreach (var replacement in replacements)
                {
                    _retiredTextures.Add(_textures[replacement.Key]);
                    _textures[replacement.Key] = replacement.Texture;
                }
                state.Hash = hash;
                _textureRevisions[path] = ++_textureRevision;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
            {
                Diagnostic?.Invoke($"Texture '{path}': {exception.Message}");
            }
        }
    }

    public ITexture LoadTexture(string relativePath, TextureSemantic semantic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Refresh();
        string path = NormalizeAssetPath(relativePath);
        (string Path, TextureSemantic Semantic) key = (path, semantic);
        if (_textures.TryGetValue(key, out ITexture? texture))
            return texture;
        FileStamp stamp = GetFileStamp(path);
        string hash = HashTextureFile(path);
        texture = CreateTexture(path, semantic);
        _textures[key] = texture;
        if (!_textureFiles.ContainsKey(path))
        {
            _textureFiles[path] = new TextureFileState(stamp, hash);
            _textureRefreshQueue.Enqueue(path);
        }
        _textureRevisions[path] = ++_textureRevision;
        return texture;
    }

    private ITexture CreateTexture(string path, TextureSemantic semantic)
    {
        ImageData image;
        try
        {
            image = ImageLoader.Load(path);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException($"Cannot decode texture '{path}'.", exception);
        }
        ITexture texture = _device.CreateTexture(new TextureDescription
        {
            Extent = new Size2D(image.Width, image.Height),
            Format = GetTextureFormat(semantic),
        });
        try
        {
            _device.UploadTextureData(texture, image.Pixels);
            return texture;
        }
        catch
        {
            texture.Dispose();
            throw;
        }
    }

    public static TextureFormat GetTextureFormat(TextureSemantic semantic) => semantic switch
    {
        TextureSemantic.Albedo or TextureSemantic.Emission => TextureFormat.R8G8B8A8Srgb,
        TextureSemantic.Metallic or TextureSemantic.Roughness or TextureSemantic.Normal or TextureSemantic.AmbientOcclusion => TextureFormat.R8G8B8A8UNorm,
        _ => throw new ArgumentOutOfRangeException(nameof(semantic), semantic, null),
    };

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _importCancellation.Cancel();
        try
        {
            Task.WhenAll(_meshImports.Values).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        _meshImports.Clear();
        try
        {
            Task[] lodGenerations = _lodChains.Values
                .Select(state => state.Generation)
                .Where(task => task is not null)
                .Select(task => (Task)task!)
                .ToArray();
            Task.WhenAll(lodGenerations).GetAwaiter().GetResult();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
        }
        _lodChains.Clear();
        _importCancellation.Dispose();
        _importGate.Dispose();
        _lodGate.Dispose();
        foreach (Mesh mesh in _meshes.Values)
            mesh.Dispose();
        foreach (Mesh mesh in _partMeshes.Values)
            mesh.Dispose();
        _partMeshes.Clear();
        _models.Clear();
        foreach (ITexture texture in _textures.Values)
            texture.Dispose();
        foreach (ITexture texture in _retiredTextures)
            texture.Dispose();
        _retiredTextures.Clear();
        _meshes.Clear();
        _textures.Clear();
        _textureFiles.Clear();
        _textureRefreshQueue.Clear();
        _textureRevisions.Clear();
    }

    private static FileStamp GetFileStamp(string path)
    {
        (long Length, long LastWriteTicks)? stamp = Core.VirtualFileSystem.TryGetStamp(path);
        return stamp is { } value ? new FileStamp(true, value.Item1, new DateTime(value.Item2, DateTimeKind.Utc)) : default;
    }

    private static string HashTextureFile(string path)
    {
        using Stream stream = Core.VirtualFileSystem.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private string NormalizeAssetPath(string relativePath)
    {
        return Catalog.ResolvePath(new AssetReference(relativePath));
    }
}
