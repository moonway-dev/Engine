using Engine.Core;
using Engine.Editor;
using Engine.Editor.Build;
using Xunit;

namespace Engine.Tests.Assets;

public sealed class BuildPipelineTests : IDisposable
{
    private readonly string _root;

    public BuildPipelineTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "engine-build-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Scenes"));
        Directory.CreateDirectory(Path.Combine(_root, "Materials"));
        Directory.CreateDirectory(Path.Combine(_root, "Textures"));
        Directory.CreateDirectory(Path.Combine(_root, "Models"));
        Directory.CreateDirectory(Path.Combine(_root, "Scripts"));

        const string textureGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa01";
        const string modelGuid = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa02";
        WriteFile("Textures/albedo.png", [1, 2, 3, 4]);
        WriteFile("Textures/albedo.png.meta", Meta(textureGuid));
        WriteFile("Models/mesh.obj", "v 0 0 0\nv 1 0 0\nv 1 1 0\nf 1 2 3\n");
        WriteFile("Models/mesh.obj.meta", Meta(modelGuid));
        WriteFile("Materials/Sample.mat",
            $$"""{ "AlbedoTexture": "guid:{{textureGuid}}", "Model": "guid:{{modelGuid}}" }""");
        WriteFile("Materials/Sample.mat.meta", Meta("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa03"));
        WriteFile("Scenes/Main.scene",
            $$"""{ "objects": [ { "name": "Cube", "components": [ { "type": "MeshRenderer", "properties": { "ResourceKey": "Models/mesh.obj", "MaterialAsset": "guid:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa03" } } ] } ] }""");
        WriteFile("Scenes/Main.scene.meta", Meta("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa04"));
        WriteFile("Scripts/TestScript.cs",
            """
            public sealed class TestScript : Engine.Scripting.ScriptBehaviour
            {
                public float Speed;
            }
            """);
        WriteFile("Scripts/TestScript.cs.meta", Meta("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa05"));
        WriteFile("Project.project", """{ "LastScene": "Scenes/Main.scene", "Exposure": 1.5 }""");
        Directory.CreateDirectory(Path.Combine(_root, "Shaders"));
        WriteFile("Shaders/PBR.slang", "void vsMain() {}\n");
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Collect_BundlesScenesAndReferencedAssets()
    {
        AssetBundler.BundleResult result = AssetBundler.Collect(NewRequest(), Path.Combine(_root, "Shaders"), includeScriptAssembly: false);

        Assert.True(result.Entries.ContainsKey("Scenes/Main.scene"));
        Assert.True(result.Entries.ContainsKey("Scenes/Main.scene.meta"));
        Assert.True(result.Entries.ContainsKey("Materials/Sample.mat"));
        Assert.True(result.Entries.ContainsKey("Textures/albedo.png"));
        Assert.True(result.Entries.ContainsKey("Textures/albedo.png.meta"));
        Assert.True(result.Entries.ContainsKey("Models/mesh.obj"));
        Assert.True(result.Entries.ContainsKey("Project.project"));
        Assert.True(result.Entries.ContainsKey("build.json"));
        Assert.True(result.Entries.ContainsKey("Shaders/PBR.slang"));
        Assert.False(result.Entries.ContainsKey("Scripts/Scripts.dll"));
        Assert.Contains("Scenes/Main.scene", result.Scenes);
        Assert.Equal(3, result.ReferencedAssets.Count);
    }

    [Fact]
    public void Collect_CompilesScriptAssemblyForManagedBuilds()
    {
        AssetBundler.BundleResult result = AssetBundler.Collect(NewRequest(), Path.Combine(_root, "Shaders"), includeScriptAssembly: true);
        byte[] assembly = result.Entries["Scripts/Scripts.dll"];
        Assert.True(assembly.Length > 0);
        Assert.Equal(1, result.ScriptCount);
    }

    [Fact]
    public void Collect_RewritesStartupSceneInProjectFile()
    {
        AssetBundler.BundleResult result = AssetBundler.Collect(NewRequest(startupScene: "Scenes/Main.scene"), Path.Combine(_root, "Shaders"), includeScriptAssembly: false);
        string project = System.Text.Encoding.UTF8.GetString(result.Entries["Project.project"]);
        Assert.Contains("\"LastScene\": \"Scenes/Main.scene\"", project);
        string buildInfo = System.Text.Encoding.UTF8.GetString(result.Entries["build.json"]);
        Assert.Contains("startupScene", buildInfo);
    }

    [Fact]
    public void PackWriter_Set_RoundTripsUnencrypted()
    {
        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal)
        {
            ["Scenes/Main.scene"] = [1, 2, 3],
            ["Materials/Sample.mat"] = [4, 5],
        };
        AssetPackWriter.PackResult packed = AssetPackWriter.Write(Path.Combine(_root, "data"), entries, encryptionKey: null);
        Assert.Equal(2, packed.EntryCount);
        Assert.Equal(2, packed.ChunkCount);

        AssetPackSet set = AssetPackSet.Load(packed.ManifestPath);
        Assert.Equal([1, 2, 3], ReadAll(set.OpenRead("Scenes/Main.scene")));
        Assert.Equal([4, 5], ReadAll(set.OpenRead("Materials/Sample.mat")));
        Assert.Contains("Scenes/Main.scene", set.ListFiles());
        set.Dispose();
    }

    [Fact]
    public void PackWriter_Set_RoundTripsEncrypted()
    {
        byte[] key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(PackCrypto.KeySizeBytes);
        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal)
        {
            ["Textures/t.png"] = new byte[] { 9, 8, 7, 6, 5 },
        };
        AssetPackWriter.PackResult packed = AssetPackWriter.Write(Path.Combine(_root, "data"), entries, key);
        AssetPackSet set = AssetPackSet.Load(packed.ManifestPath, key);
        Assert.True(set.Encrypted);
        Assert.Equal([9, 8, 7, 6, 5], ReadAll(set.OpenRead("Textures/t.png")));
        set.Dispose();

        Assert.Throws<InvalidDataException>(() => AssetPackSet.Load(packed.ManifestPath));
        byte[] wrongKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(PackCrypto.KeySizeBytes);
        AssetPackSet wrong = AssetPackSet.Load(packed.ManifestPath, wrongKey);
        Assert.Throws<System.Security.Cryptography.AuthenticationTagMismatchException>(() => ReadAll(wrong.OpenRead("Textures/t.png")));
        wrong.Dispose();
    }

    [Fact]
    public void PackWriter_ChunkNameIsStablePerAsset()
    {
        byte[] payload = [1, 2, 3];
        string first = AssetPackWriter.ChunkName("Scenes/Main.scene", payload);
        Assert.Equal(AssetPackWriter.ChunkName(@"Scenes\Main.scene", payload), first);
        Assert.Equal(AssetPackWriter.ChunkName("Scenes/Main.scene", [1, 2, 3]), first);
        Assert.DoesNotContain("/", first);
        Assert.DoesNotContain("\\", first);
        Assert.EndsWith(".pack", first);
        Assert.NotEqual(AssetPackWriter.ChunkName("Scenes/Other.scene", payload), first);
        Assert.NotEqual(AssetPackWriter.ChunkName("Scenes/Main.scene", [9, 9, 9]), first);
    }

    [Fact]
    public void VirtualFileSystem_FallsBackToDiskWhenUnmounted()
    {
        string file = Path.Combine(_root, "Project.project");
        Assert.True(VirtualFileSystem.Exists(file));
        Assert.Equal(File.ReadAllText(file), VirtualFileSystem.ReadAllText(file));
    }

    [Fact]
    public void VirtualFileSystem_PackMounted_HidesUnlistedFiles()
    {
        Dictionary<string, byte[]> entries = new(StringComparer.Ordinal) { ["Only/This.asset"] = [1] };
        AssetPackWriter.PackResult packed = AssetPackWriter.Write(Path.Combine(_root, "data"), entries, null);
        try
        {
            VirtualFileSystem.Mount(_root, AssetPackSet.Load(packed.ManifestPath));
            Assert.True(VirtualFileSystem.Exists(Path.Combine(_root, "Only", "This.asset")));
            Assert.False(VirtualFileSystem.Exists(Path.Combine(_root, "Project.project")));
            Assert.Null(VirtualFileSystem.TryOpenRead(Path.Combine(_root, "Project.project")));
        }
        finally
        {
            VirtualFileSystem.UnmountAll();
        }
    }

    [Fact]
    public void ListScriptClassNames_FindsBehaviourClasses()
    {
        List<string> names = AssetBundler.ListScriptClassNames(Path.Combine(_root, "Scripts"));
        Assert.Equal(["TestScript"], names);
    }

    [Fact]
    public void GenerateScriptComponents_EmitsComponentPerScript()
    {
        string source = AssetBundler.GenerateScriptComponents(["TestScript", "Other"]);
        Assert.Contains("public sealed class TestScriptComponent : Engine.Scene.Script", source);
        Assert.Contains("Script = \"TestScript\";", source);
        Assert.Contains("public sealed class OtherComponent : Engine.Scene.Script", source);
    }

    [Fact]
    public void SanitizeAssemblyName_StripsInvalidCharacters()
    {
        Assert.Equal("My_Game", BuildRunner.SanitizeAssemblyName("My Game!"));
        Assert.Equal("Game123", BuildRunner.SanitizeAssemblyName("123"));
        Assert.Equal("Game", BuildRunner.SanitizeAssemblyName("   "));
    }

    [Fact]
    public void ProjectSettings_BuildSectionRoundTrips()
    {
        string path = Path.Combine(_root, "settings.json");
        ProjectSettings settings = new()
        {
            Build = new EngineBuildProfile
            {
                ProductName = "SpaceGame",
                Version = "2.3.4",
                Platform = "win-x64",
                Mode = "Managed",
                Scenes = ["Scenes/Main.scene", "Scenes/Arena.scene"],
                StartupScene = "Scenes/Arena.scene",
                OutputDirectory = "Artifacts",
            },
        };
        settings.Save(path);
        ProjectSettings loaded = ProjectSettings.Load(path);
        Assert.Equal("SpaceGame", loaded.Build.ProductName);
        Assert.Equal("2.3.4", loaded.Build.Version);
        Assert.Equal("Managed", loaded.Build.Mode);
        Assert.Equal(2, loaded.Build.Scenes.Count);
        Assert.Equal("Scenes/Arena.scene", loaded.Build.StartupScene);
        Assert.Equal("Artifacts", loaded.Build.OutputDirectory);
    }

    private BuildRequest NewRequest(string? startupScene = null, string[]? scenes = null) => new()
    {
        ProjectRoot = _root,
        EngineRoot = _root,
        Profile = new EngineBuildProfile
        {
            ProductName = "TestGame",
            Version = "1.0",
            Scenes = scenes is null ? ["Scenes/Main.scene"] : [.. scenes],
            StartupScene = startupScene ?? string.Empty,
            Mode = "Aot",
        },
    };

    private static byte[] ReadAll(Stream stream)
    {
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    private static string Meta(string guid) => $$"""{ "Version": 1, "Guid": "{{guid}}" }""";

    private void WriteFile(string relativePath, string content)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    private void WriteFile(string relativePath, byte[] content)
    {
        string fullPath = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllBytes(fullPath, content);
    }
}
