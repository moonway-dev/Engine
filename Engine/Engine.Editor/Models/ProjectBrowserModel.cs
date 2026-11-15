namespace Engine.Editor.Models;

public enum AssetKind
{
    Unknown,
    Folder,
    Scene,
    Prefab,
    Mesh,
    Texture,
    Shader,
    Material,
}

public sealed record AssetEntry(
    string Name,
    string FullPath,
    string RelativePath,
    AssetKind Kind,
    IReadOnlyList<AssetEntry> Children)
{
    public bool IsDirectory => Kind == AssetKind.Folder;
}

public sealed class ProjectBrowserModel
{
    private static readonly IReadOnlyList<AssetEntry> NoChildren = Array.Empty<AssetEntry>();
    private readonly List<AssetEntry> _roots = [];

    public string AssetsRoot { get; }

    public string Filter { get; set; } = string.Empty;

    public IReadOnlyList<AssetEntry> Roots => _roots;

    public ProjectBrowserModel(string assetsRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetsRoot);
        AssetsRoot = Path.GetFullPath(assetsRoot);
    }

    public void Refresh()
    {
        Directory.CreateDirectory(AssetsRoot);
        _roots.Clear();
        _roots.Add(BuildDirectory(AssetsRoot, string.Empty, Path.GetFileName(AssetsRoot)));
    }

    public IEnumerable<AssetEntry> EnumerateVisible()
    {
        string query = Filter?.Trim() ?? string.Empty;
        IEnumerable<AssetEntry> entries = Enumerate(_roots);
        if (query.Length == 0)
            return entries;
        return entries.Where(entry => entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Kind.ToString().Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public static AssetKind Classify(string path, bool isDirectory = false)
    {
        if (isDirectory)
            return AssetKind.Folder;
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".scene" => AssetKind.Scene,
            ".prefab" => AssetKind.Prefab,
            ".obj" or ".fbx" or ".gltf" or ".glb" => AssetKind.Mesh,
            ".png" or ".jpg" or ".jpeg" or ".tga" or ".bmp" or ".hdr" => AssetKind.Texture,
            ".slang" or ".hlsl" or ".glsl" => AssetKind.Shader,
            ".mat" or ".material" => AssetKind.Material,
            _ => AssetKind.Unknown,
        };
    }

    private AssetEntry BuildDirectory(string path, string relativePath, string name)
    {
        List<AssetEntry> children = [];
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(path).OrderBy(value => value, PathComparer))
            {
                string childRelative = CombineRelative(relativePath, Path.GetFileName(directory));
                children.Add(BuildDirectory(directory, childRelative, Path.GetFileName(directory)));
            }
            foreach (string file in Directory.EnumerateFiles(path).OrderBy(value => value, PathComparer))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    continue;
                string childRelative = CombineRelative(relativePath, Path.GetFileName(file));
                children.Add(new AssetEntry(
                    Path.GetFileName(file),
                    Path.GetFullPath(file),
                    NormalizeRelative(childRelative),
                    Classify(file),
                    NoChildren));
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
        return new AssetEntry(
            name,
            Path.GetFullPath(path),
            NormalizeRelative(relativePath),
            AssetKind.Folder,
            children);
    }

    private static IEnumerable<AssetEntry> Enumerate(IEnumerable<AssetEntry> roots)
    {
        foreach (AssetEntry entry in roots)
        {
            yield return entry;
            foreach (AssetEntry child in Enumerate(entry.Children))
                yield return child;
        }
    }

    private static string CombineRelative(string parent, string name) =>
        parent.Length == 0 ? name : Path.Combine(parent, name);

    private static string NormalizeRelative(string path) => path.Replace('\\', '/');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
