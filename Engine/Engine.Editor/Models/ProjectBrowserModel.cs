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
    Script,
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

public enum FavoritesFilter
{
    None,
    AllMaterials,
    AllModels,
    AllPrefabs,
}

public sealed class ProjectBrowserModel
{
    private static readonly IReadOnlyList<AssetEntry> NoChildren = Array.Empty<AssetEntry>();
    private readonly List<AssetEntry> _roots = [];

    public string AssetsRoot { get; }

    public string Filter { get; set; } = string.Empty;

    public string CurrentDirectory { get; private set; } = string.Empty;

    public FavoritesFilter Favorites { get; set; } = FavoritesFilter.None;

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

    public AssetEntry? FindCurrentFolder()
    {
        AssetEntry? current = _roots.FirstOrDefault();
        foreach (string segment in SplitRelative(CurrentDirectory))
        {
            current = current?.Children.FirstOrDefault(child => child.IsDirectory && string.Equals(child.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (current is null)
                return null;
        }
        return current;
    }

    public bool Navigate(string relativeDirectory)
    {
        try
        {
            string full = Path.GetFullPath(Path.Combine(AssetsRoot, relativeDirectory ?? string.Empty));
            string relative = Path.GetRelativePath(AssetsRoot, full);
            if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                || Path.IsPathRooted(relative) || !Directory.Exists(full))
                return false;
            CurrentDirectory = relative == "." ? string.Empty : NormalizeRelative(relative);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }
    }

    public IReadOnlyList<AssetEntry> CurrentEntries()
    {
        string query = Filter.Trim();
        AssetKind? kind = Favorites switch
        {
            FavoritesFilter.AllMaterials => AssetKind.Material,
            FavoritesFilter.AllModels => AssetKind.Mesh,
            FavoritesFilter.AllPrefabs => AssetKind.Prefab,
            _ => null,
        };
        IEnumerable<AssetEntry> entries;
        if (kind is not null || query.Length > 0)
        {
            entries = Enumerate(_roots).Where(entry => !entry.IsDirectory);
            if (kind is not null)
                entries = entries.Where(entry => entry.Kind == kind);
        }
        else
        {
            AssetEntry? folder = FindCurrentFolder();
            if (folder is null)
            {
                CurrentDirectory = string.Empty;
                folder = _roots.FirstOrDefault();
            }
            entries = folder?.Children ?? NoChildren;
        }
        return [.. entries.Where(entry => Matches(entry, query))
            .OrderByDescending(entry => entry.IsDirectory)
            .ThenBy(entry => entry.Name, NameComparer)
            .ThenBy(entry => entry.RelativePath, NameComparer)];
    }

    private static bool Matches(AssetEntry entry, string query) => query.Length == 0
        || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)
        || entry.Kind.ToString().Contains(query, StringComparison.OrdinalIgnoreCase);

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
            ".cs" => AssetKind.Script,
            _ => AssetKind.Unknown,
        };
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

    private static string[] SplitRelative(string relative) =>
        relative.Length == 0 ? [] : relative.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private AssetEntry BuildDirectory(string path, string relativePath, string name)
    {
        List<AssetEntry> children = [];
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(path).OrderBy(value => value, PathComparer))
            {
                if (Path.GetFileName(directory).Equals("Cache", StringComparison.OrdinalIgnoreCase))
                    continue;
                string childRelative = CombineRelative(relativePath, Path.GetFileName(directory));
                children.Add(BuildDirectory(directory, childRelative, Path.GetFileName(directory)));
            }
            foreach (string file in Directory.EnumerateFiles(path).OrderBy(value => value, PathComparer))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".project", StringComparison.OrdinalIgnoreCase))
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

    private static StringComparer NameComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static string CombineRelative(string parent, string name) =>
        parent.Length == 0 ? name : Path.Combine(parent, name);

    private static string NormalizeRelative(string path) => path.Replace('\\', '/');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
}
