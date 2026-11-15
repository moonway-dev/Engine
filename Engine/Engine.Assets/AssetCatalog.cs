using System.Text.Json;
using Engine.Scene;

namespace Engine.Assets;

public sealed class AssetCatalog
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _diagnostics = [];

    public AssetCatalog(string rootDirectory)
    {
        RootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(RootDirectory);
        Refresh();
    }

    public string RootDirectory { get; }
    public IReadOnlyList<string> Diagnostics => _diagnostics;
    public event Action<string>? Diagnostic;

    public AssetReference GetReference(string path)
    {
        string fullPath = Contain(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Asset does not exist.", fullPath);
        if (!_ids.TryGetValue(fullPath, out string? id))
        {
            id = ReadOrCreateId(fullPath);
            if (_paths.TryGetValue(id, out string? existing) && !existing.Equals(fullPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Duplicate asset GUID '{id}' at '{fullPath}' and '{existing}'.");
            _ids[fullPath] = id;
            _paths[id] = fullPath;
        }
        return new AssetReference("guid:" + id);
    }

    public string ResolvePath(AssetReference reference)
    {
        if (reference.IsEmpty)
            throw new ArgumentException("Asset reference is empty.", nameof(reference));
        if (!reference.Key.StartsWith("guid:", StringComparison.OrdinalIgnoreCase))
            return Contain(reference.Key);
        string id = ParseId(reference.Key[5..]);
        if (!_paths.TryGetValue(id, out string? path))
        {
            Refresh();
            if (!_paths.TryGetValue(id, out path))
                throw new FileNotFoundException($"Unknown asset GUID '{id}'.");
        }
        return Contain(path);
    }

    public void Rename(AssetReference reference, string newPath)
    {
        string source = ResolvePath(reference);
        GetReference(source);
        string destination = Contain(newPath);
        if (File.Exists(destination) || File.Exists(destination + ".meta"))
            throw new IOException("An asset or metadata already exists at the destination.");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Move(source, destination);
        try
        {
            File.Move(source + ".meta", destination + ".meta");
        }
        catch
        {
            File.Move(destination, source);
            throw;
        }
        Refresh();
    }

    public void Refresh()
    {
        _diagnostics.Clear();
        Dictionary<string, string> paths = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> ids = new(StringComparer.OrdinalIgnoreCase);
        EnumerationOptions options = new() { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true };
        foreach (string path in Directory.EnumerateFiles(RootDirectory, "*", options).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                string id = ReadOrCreateId(path);
                if (paths.TryGetValue(id, out string? duplicate))
                {
                    Report($"Duplicate asset GUID '{id}' at '{duplicate}' and '{path}'.");
                    continue;
                }
                paths[id] = path;
                ids[path] = id;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                Report($"Asset metadata '{path}': {exception.Message}");
                if (_ids.TryGetValue(path, out string? oldId) && !paths.ContainsKey(oldId))
                {
                    paths[oldId] = path;
                    ids[path] = oldId;
                }
            }
        }
        _paths.Clear();
        _ids.Clear();
        foreach (var pair in paths)
            _paths.Add(pair.Key, pair.Value);
        foreach (var pair in ids)
            _ids.Add(pair.Key, pair.Value);
    }

    internal string Contain(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string fullPath = Path.GetFullPath(Path.Combine(RootDirectory, path));
        string prefix = Path.TrimEndingDirectorySeparator(RootDirectory) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Asset path must be inside the asset root.", nameof(path));
        for (string? current = fullPath; current is not null && !current.Equals(RootDirectory, StringComparison.OrdinalIgnoreCase); current = Path.GetDirectoryName(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new ArgumentException("Asset paths cannot traverse symbolic links or junctions.", nameof(path));
        }
        return fullPath;
    }

    private static string ReadOrCreateId(string path)
    {
        string metadataPath = path + ".meta";
        if (File.Exists(metadataPath))
        {
            if ((File.GetAttributes(metadataPath) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Asset metadata cannot be a symbolic link.");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(metadataPath));
            if (!document.RootElement.TryGetProperty("Guid", out JsonElement value) && !document.RootElement.TryGetProperty("guid", out value))
                throw new InvalidDataException("Metadata has no GUID.");
            return ParseId(value.GetString() ?? string.Empty);
        }
        string id = Guid.NewGuid().ToString("N");
        using FileStream stream = new(metadataPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, new { Version = 1, Guid = id });
        return id;
    }

    private static string ParseId(string id) => Guid.TryParse(id, out Guid guid) && guid != Guid.Empty
        ? guid.ToString("N")
        : throw new InvalidDataException($"Invalid asset GUID '{id}'.");

    private void Report(string message)
    {
        _diagnostics.Add(message);
        Diagnostic?.Invoke(message);
    }
}
