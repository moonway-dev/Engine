using System.Security.Cryptography;
using System.Text.Json;
using Engine.Assets;
using Engine.Scene;
using Engine.Scripting;

namespace Engine.Editor.Build;

public static class AssetBundler
{
    private static readonly string[] SkippedExtensions =
    [
        ".csproj", ".sln", ".user", ".tmp", ".log", ".pdb", ".exe", ".dll", ".cache", ".pack", ".bin",
    ];

    public sealed record BundleResult(Dictionary<string, byte[]> Entries, List<string> Scenes, List<string> ReferencedAssets, int ScriptCount);

    public static BundleResult Collect(BuildRequest request, string shaderDirectory, bool includeScriptAssembly, Action<string>? log = null)
    {
        string projectRoot = Path.GetFullPath(request.ProjectRoot);
        Dictionary<string, byte[]> entries = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> pendingScenes = new(StringComparer.OrdinalIgnoreCase);

        string scenesRoot = Path.Combine(projectRoot, "Scenes");
        foreach (string scene in request.Profile.Scenes)
            pendingScenes.Add(scene);
        if (pendingScenes.Count == 0 && Directory.Exists(scenesRoot))
        {
            foreach (string file in Directory.EnumerateFiles(scenesRoot, "*.scene", SearchOption.AllDirectories))
                pendingScenes.Add(Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/'));
        }

        AssetCatalog catalog = new(projectRoot);
        Queue<string> toScan = new();
        List<string> referenced = [];
        List<string> bundledScenes = [];

        foreach (string scene in pendingScenes.Order(StringComparer.OrdinalIgnoreCase))
        {
            string source = Path.Combine(projectRoot, scene);
            if (!File.Exists(source))
            {
                log?.Invoke($"warning: scene '{scene}' does not exist, skipped");
                continue;
            }
            AddFile(entries, projectRoot, source);
            toScan.Enqueue(scene);
            bundledScenes.Add(scene);
        }

        string startup = request.Profile.StartupScene;
        string projectFile = Path.Combine(projectRoot, "Project.project");
        if (File.Exists(projectFile))
        {
            entries["Project.project"] = string.IsNullOrWhiteSpace(startup)
                ? File.ReadAllBytes(projectFile)
                : RewriteStartupScene(projectFile, startup);
        }
        entries["build.json"] = JsonSerializer.SerializeToUtf8Bytes(new BuildInfoPayload(
            string.IsNullOrWhiteSpace(request.Profile.ProductName) ? "Game" : request.Profile.ProductName,
            request.Profile.Version,
            request.Profile.Platform,
            request.Profile.Mode,
            startup), BuildInfoJsonOptions);

        while (toScan.TryDequeue(out string? relative))
        {
            string fullPath = Path.Combine(projectRoot, relative);
            if (!File.Exists(fullPath))
                continue;
            foreach (string reference in CollectReferences(entries, projectRoot, fullPath))
            {
                string? resolvedPath = ResolveReference(catalog, projectRoot, reference, log);
                if (resolvedPath is null)
                    continue;
                string resolvedRelative = Path.GetRelativePath(projectRoot, resolvedPath).Replace(Path.DirectorySeparatorChar, '/');
                if (entries.ContainsKey(resolvedRelative))
                    continue;
                AddFile(entries, projectRoot, resolvedPath);
                referenced.Add(resolvedRelative);
                if (resolvedPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)
                    || resolvedPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                    || resolvedPath.EndsWith(".scene", StringComparison.OrdinalIgnoreCase))
                    toScan.Enqueue(resolvedRelative);
            }
        }

        int scriptCount = 0;
        string scriptsRoot = Path.Combine(projectRoot, "Scripts");
        if (includeScriptAssembly && Directory.Exists(scriptsRoot))
        {
            ScriptCompiler compiler = new();
            using MemoryStream assemblyOutput = new();
            ScriptCompilationResult result = compiler.Compile(scriptsRoot, assemblyOutput);
            if (!result.Success)
            {
                foreach (string error in result.Errors.Take(16))
                    log?.Invoke($"script compile error: {error}");
                throw new InvalidOperationException("Script compilation failed; the build cannot continue.");
            }
            scriptCount = result.Scripts.Count;
            entries["Scripts/Scripts.dll"] = assemblyOutput.ToArray();
            log?.Invoke($"scripts: {scriptCount} script(s) compiled to assembly ({assemblyOutput.Length / 1024.0:F0} KB)");
        }

        AddShaderSources(entries, shaderDirectory, log);
        AddShaderCache(entries, projectRoot);

        return new BundleResult(entries, bundledScenes, referenced, scriptCount);
    }

    public static List<string> ListScriptClassNames(string scriptsRoot)
    {
        List<string> names = [];
        if (!Directory.Exists(scriptsRoot))
            return names;
        foreach (string file in Directory.EnumerateFiles(scriptsRoot, "*.cs", SearchOption.AllDirectories))
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                text,
                @"^\s*public\s+(?:sealed\s+|static\s+|abstract\s+|partial\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([^\r\n{]+)",
                System.Text.RegularExpressions.RegexOptions.Multiline))
            {
                string baseType = match.Groups[2].Value;
                if (baseType.Contains("ScriptBehaviour", StringComparison.Ordinal))
                    names.Add(match.Groups[1].Value);
            }
        }
        return names.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string GenerateScriptComponents(List<string> classNames)
    {
        System.Text.StringBuilder builder = new();
        builder.AppendLine("namespace EngineGenerated;");
        builder.AppendLine();
        foreach (string name in classNames)
        {
            builder.AppendLine($"public sealed class {name}Component : Engine.Scene.ScriptComponent");
            builder.AppendLine("{");
            builder.AppendLine($"    public {name}Component()");
            builder.AppendLine("    {");
            builder.AppendLine($"        Script = \"{name}\";");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static readonly JsonSerializerOptions BuildInfoJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private sealed record BuildInfoPayload(
        string ProductName,
        string Version,
        string Platform,
        string Mode,
        string StartupScene);

    private static void AddShaderSources(Dictionary<string, byte[]> entries, string shaderDirectory, Action<string>? log)
    {
        if (!Directory.Exists(shaderDirectory))
        {
            log?.Invoke($"warning: shader directory '{shaderDirectory}' not found; player will not render");
            return;
        }
        foreach (string file in Directory.EnumerateFiles(shaderDirectory, "*.slang", SearchOption.AllDirectories))
        {
            string key = "Shaders/" + Path.GetRelativePath(shaderDirectory, file).Replace(Path.DirectorySeparatorChar, '/');
            entries[key] = File.ReadAllBytes(file);
        }
    }

    private static void AddShaderCache(Dictionary<string, byte[]> entries, string projectRoot)
    {
        string cacheDirectory = Path.Combine(projectRoot, "Cache", "Shaders");
        if (!Directory.Exists(cacheDirectory))
            return;
        foreach (string file in Directory.EnumerateFiles(cacheDirectory, "*", SearchOption.AllDirectories))
        {
            string key = Path.GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/');
            if (!entries.ContainsKey(key))
                entries[key] = File.ReadAllBytes(file);
        }
    }

    private static byte[] RewriteStartupScene(string projectFile, string startupScene)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(projectFile));
            JsonElement cloned = document.RootElement.Clone();
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                foreach (JsonProperty property in cloned.EnumerateObject())
                {
                    if (property.Name == "LastScene")
                        writer.WriteString("LastScene", startupScene);
                    else
                        property.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return stream.ToArray();
        }
        catch (JsonException)
        {
            return File.ReadAllBytes(projectFile);
        }
    }

    private static void AddFile(Dictionary<string, byte[]> entries, string projectRoot, string fullPath)
    {
        string relative = Path.GetRelativePath(projectRoot, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        entries[relative] = File.ReadAllBytes(fullPath);
        string meta = fullPath + ".meta";
        if (File.Exists(meta))
        {
            string metaKey = relative + ".meta";
            entries[metaKey] = File.ReadAllBytes(meta);
        }
    }

    private static IEnumerable<string> CollectReferences(Dictionary<string, byte[]> entries, string projectRoot, string jsonFile)
    {
        HashSet<string> references = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(jsonFile));
            Walk(document.RootElement, references);
        }
        catch (JsonException)
        {
        }
        return references;
    }

    private static void Walk(JsonElement element, HashSet<string> references)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                string value = element.GetString() ?? string.Empty;
                if (value.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) && value.Length > 5)
                    references.Add(value[5..]);
                else if (LooksLikeAssetPath(value))
                    references.Add(value);
                break;
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                    Walk(property.Value, references);
                break;
            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                    Walk(item, references);
                break;
        }
    }

    private static bool LooksLikeAssetPath(string value)
    {
        if (value.Length == 0 || value.Length > 260 || value.Contains('\\') || value.Contains(':'))
            return false;
        string extension = Path.GetExtension(value);
        if (extension.Length < 2 || SkippedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return false;
        return extension.Equals(".obj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".mat", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".prefab", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveReference(AssetCatalog catalog, string projectRoot, string reference, Action<string>? log)
    {
        try
        {
            return catalog.ResolvePath(new AssetReference(reference.StartsWith("guid:", StringComparison.OrdinalIgnoreCase) ? reference : "guid:" + reference));
        }
        catch (Exception exception) when (exception is FileNotFoundException or ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            string direct = Path.GetFullPath(Path.Combine(projectRoot, reference));
            if (File.Exists(direct))
                return direct;
            if (IsGuidShape(reference))
                log?.Invoke($"warning: unresolved asset reference '{reference}'");
            return null;
        }
    }

    private static bool IsGuidShape(string value) =>
        value.Length == 32 && value.All(character => Uri.IsHexDigit(character));
}
