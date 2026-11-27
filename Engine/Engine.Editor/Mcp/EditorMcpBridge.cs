using System.Collections.Concurrent;
using Engine.Core;
using Engine.Editor.Documents;
using Engine.Mathematics;
using Engine.RHI;
using Engine.Rendering;
using Engine.Scene;
using JsonElement = System.Text.Json.JsonElement;
using McpVec3 = Engine.Mcp.McpVec3;
using McpQuat = Engine.Mcp.McpQuat;

namespace Engine.Editor.Mcp;

public sealed class EditorMcpBridge : Engine.Mcp.IMcpEngineBridge, IDisposable
{
    private const int MainThreadTimeoutMilliseconds = 10000;

    private readonly Func<EditorContext?> _context;
    private readonly Func<PlayModeController?> _playMode;
    private readonly Func<MemoryLogSink?> _logs;
    private readonly Func<IRHI?> _rhi;
    private readonly Func<ITexture?> _sceneTexture;
    private readonly Func<ITexture?> _gameTexture;
    private readonly string _projectRoot;
    private readonly string _scriptsRoot;
    private readonly int _mainThreadId;
    private readonly ConcurrentQueue<PendingWork> _queue = [];
    private bool _disposed;

    public EditorMcpBridge(
        Func<EditorContext?> context,
        Func<PlayModeController?> playMode,
        Func<MemoryLogSink?> logs,
        Func<IRHI?> rhi,
        Func<ITexture?> sceneTexture,
        Func<ITexture?> gameTexture,
        string projectRoot)
    {
        _context = context;
        _playMode = playMode;
        _logs = logs;
        _rhi = rhi;
        _sceneTexture = sceneTexture;
        _gameTexture = gameTexture;
        _projectRoot = Path.GetFullPath(projectRoot);
        _scriptsRoot = Path.Combine(_projectRoot, "Scripts");
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    public void Pump()
    {
        while (_queue.TryDequeue(out PendingWork? work))
            work.Execute();
    }

    private T Run<T>(Func<T> action) => (T)Run((Delegate)action);

    private object Run(Delegate action)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(EditorMcpBridge));
        if (Environment.CurrentManagedThreadId == _mainThreadId)
            return action.DynamicInvoke()!;
        PendingWork work = new(action);
        _queue.Enqueue(work);
        if (!work.Completed.Wait(MainThreadTimeoutMilliseconds))
            throw new TimeoutException("The editor main thread did not respond in time.");
        if (work.Error is not null)
            throw new InvalidOperationException(work.Error.Message, work.Error);
        return work.Result!;
    }

    private EditorContext RequireContext() =>
        _context() ?? throw new InvalidOperationException("The editor context is not available yet.");

    private Scene.Scene RequireScene() => RequireContext().Document.Scene;

    public Engine.Mcp.McpSceneInfo GetEngineInfo() => Run(() => DescribeScene());

    public IReadOnlyList<Engine.Mcp.McpObjectSummary> ListObjects(Guid? parentId) => Run(() =>
    {
        Scene.Scene scene = RequireScene();
        if (parentId is Guid id)
        {
            GameObject? parent = scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
            return parent.Children.Select(McpMappers.Summarize).ToList();
        }
        return scene.EnumerateHierarchy().Select(McpMappers.Summarize).ToList();
    });

    public Engine.Mcp.McpObjectDetail GetObject(Guid? id, string? name) => Run(() =>
    {
        Scene.Scene scene = RequireScene();
        GameObject? target = id is Guid guid ? scene.FindById(guid)
            : name is not null ? scene.FindByName(name) ?? throw new KeyNotFoundException($"No object named '{name}'.")
            : throw new ArgumentException("Provide 'id' or 'name'.");
        return target is null && id is not null ? throw new KeyNotFoundException($"No object with id '{id}'.") : McpMappers.Detail(target!);
    });

    public Engine.Mcp.McpObjectDetail CreateObject(string name, Guid? parentId, IReadOnlyList<string>? components, McpVec3? position, McpQuat? rotation, McpVec3? scale) => Run(() =>
    {
        EditorContext context = RequireContext();
        GameObject? parent = parentId is Guid id ? context.Document.Scene.FindById(id) ?? throw new KeyNotFoundException($"No parent with id '{id}'.") : null;
        GameObject created = context.Hierarchy.Create(string.IsNullOrWhiteSpace(name) ? "GameObject" : name, parent);
        if (components is { Count: > 0 })
        {
            context.Document.Execute(new Commands.SnapshotCommand("MCP Attach Components", context.Document, () =>
            {
                GameObject? current = context.Document.Scene.FindById(created.Id);
                if (current is null)
                    return;
                foreach (string key in components)
                    current.AddComponent(key);
            }), allowMerge: false);
        }
        McpMappers.ApplyTransform(created, position, rotation, null, scale, world: false);
        return McpMappers.Detail(created);
    });

    public bool DeleteObject(Guid id) => Run(() =>
    {
        EditorContext context = RequireContext();
        GameObject? target = context.Document.Scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        return context.Hierarchy.Delete(target);
    });

    public bool RenameObject(Guid id, string name) => Run(() =>
    {
        EditorContext context = RequireContext();
        GameObject? target = context.Document.Scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        return context.Hierarchy.Rename(target, name);
    });

    public bool ReparentObject(Guid id, Guid? parentId) => Run(() =>
    {
        EditorContext context = RequireContext();
        GameObject? target = context.Document.Scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        GameObject? newParent = parentId is Guid parentGuid ? context.Document.Scene.FindById(parentGuid) ?? throw new KeyNotFoundException($"No parent with id '{parentId}'.") : null;
        if (newParent is not null && (ReferenceEquals(newParent, target) || newParent.IsAncestorOf(target)))
            throw new InvalidOperationException("Cannot parent an object under itself or one of its descendants.");
        context.Document.Execute(new Commands.SnapshotCommand("MCP Set Parent", context.Document, () =>
        {
            GameObject? current = context.Document.Scene.FindById(id);
            GameObject? parent = parentId is Guid guid ? context.Document.Scene.FindById(guid) : null;
            current?.SetParent(parent);
        }), allowMerge: false);
        return true;
    });

    public Engine.Mcp.McpObjectDetail DuplicateObject(Guid id) => Run(() =>
    {
        EditorContext context = RequireContext();
        GameObject? target = context.Document.Scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        GameObject duplicate = context.Hierarchy.Duplicate(target) ?? throw new InvalidOperationException("The object could not be duplicated.");
        return McpMappers.Detail(duplicate);
    });

    public Engine.Mcp.McpObjectDetail SetTransform(Guid id, McpVec3? position, McpQuat? rotation, McpVec3? eulerDegrees, McpVec3? scale, bool world) => Run(() =>
    {
        EditorContext context = RequireContext();
        GameObject? target = context.Document.Scene.FindById(id) ?? throw new KeyNotFoundException($"No object with id '{id}'.");
        context.Document.Execute(new Commands.SnapshotCommand("MCP Set Transform", context.Document, () =>
        {
            GameObject? current = context.Document.Scene.FindById(id);
            if (current is not null)
                McpMappers.ApplyTransform(current, position, rotation, eulerDegrees, scale, world);
        }), allowMerge: true);
        return McpMappers.Detail(target);
    });

    public Engine.Mcp.McpComponentInfo AddComponent(Guid objectId, string componentKey) => Run(() =>
    {
        EditorContext context = RequireContext();
        GameObject? target = context.Document.Scene.FindById(objectId) ?? throw new KeyNotFoundException($"No object with id '{objectId}'.");
        Component component = null!;
        context.Document.Execute(new Commands.SnapshotCommand("MCP Add Component", context.Document, () =>
        {
            GameObject? current = context.Document.Scene.FindById(objectId);
            if (current is null)
                return;
            try
            {
                component = current.AddComponent(componentKey);
            }
            catch (InvalidOperationException) when (current.Components.Any(candidate => candidate.ComponentTypeName.Equals(componentKey, StringComparison.OrdinalIgnoreCase)))
            {
                component = current.Components.First(candidate => candidate.ComponentTypeName.Equals(componentKey, StringComparison.OrdinalIgnoreCase));
            }
        }), allowMerge: false);
        return ComponentValueConverter.Describe(component);
    });

    public bool RemoveComponent(Guid componentId) => Run(() =>
    {
        EditorContext context = RequireContext();
        Component? component = context.Document.Scene.FindComponentById(componentId) ?? throw new KeyNotFoundException($"No component with id '{componentId}'.");
        GameObject owner = component.Owner;
        context.Document.Execute(new Commands.SnapshotCommand("MCP Remove Component", context.Document, () =>
        {
            GameObject? current = context.Document.Scene.FindById(owner.Id);
            current?.RemoveComponent(component);
        }), allowMerge: false);
        return true;
    });

    public object? GetComponentProperty(Guid componentId, string property) => Run(() =>
    {
        Component component = RequireComponent(componentId);
        return ComponentValueConverter.Read(component, property);
    });

    public void SetComponentProperty(Guid componentId, string property, JsonElement value) => Run(() =>
    {
        EditorContext context = RequireContext();
        Component component = RequireComponent(componentId);
        context.Document.Execute(new Commands.SnapshotCommand("MCP Set Property", context.Document, () =>
        {
            Component? current = context.Document.Scene.FindComponentById(componentId);
            if (current is not null)
                ComponentValueConverter.Write(current, property, value);
        }), allowMerge: true);
    });

    public IReadOnlyList<string> ListComponentTypes() => Run(() =>
    {
        Scene.Scene scene = RequireScene();
        return scene.ComponentRegistry.RegisteredKeys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToList();
    });

    public IReadOnlyDictionary<string, object?> GetSceneSettings() => Run(() => McpMappers.SceneSettings(RequireScene()));

    public void SetSceneSettings(IReadOnlyDictionary<string, JsonElement> settings) => Run(() =>
    {
        Scene.Scene scene = RequireScene();
        McpMappers.ApplySceneSettings(scene, settings);
    });

    public string SaveScene(string? path) => Run(() =>
    {
        SceneDocument document = RequireContext().Document;
        if (string.IsNullOrWhiteSpace(path))
        {
            document.Save();
            return Path.GetFullPath(document.Path!);
        }
        document.SaveAs(path);
        return Path.GetFullPath(document.Path!);
    });

    public Engine.Mcp.McpSceneInfo OpenScene(string path) => Run(() =>
    {
        EditorContext context = RequireContext();
        context.Document.Open(path);
        return DescribeScene();
    });

    public Engine.Mcp.McpSceneInfo NewScene(string name) => Run(() =>
    {
        EditorContext context = RequireContext();
        context.Document.New(name);
        return DescribeScene();
    });

    public string SetPlayState(string action) => Run(() =>
    {
        PlayModeController? playMode = _playMode() ?? throw new InvalidOperationException("Play mode is not initialized.");
        switch (action.Trim().ToLowerInvariant())
        {
            case "play":
            case "toggle":
                if (action.Trim().Equals("toggle", StringComparison.OrdinalIgnoreCase) && playMode.State != EditorPlayState.Stopped)
                    playMode.ExitPlay();
                else
                    playMode.EnterPlay();
                break;
            case "stop":
                playMode.ExitPlay();
                break;
            case "pause":
                playMode.Pause();
                break;
            case "resume":
                playMode.Resume();
                break;
            case "step":
                playMode.Step();
                break;
            default:
                throw new ArgumentException($"Unknown play action '{action}'.");
        }
        return playMode.State.ToString();
    });

    public string Screenshot(string? path, string view) => Run(() =>
    {
        IRHI? rhi = _rhi() ?? throw new InvalidOperationException("The RHI is not available.");
        ITexture? texture = string.Equals(view, "scene", StringComparison.OrdinalIgnoreCase) ? _sceneTexture() : _gameTexture();
        if (texture is null)
            throw new InvalidOperationException($"The {view} view has no texture to capture.");
        byte[]? pixels = rhi.ReadTexturePixels(texture, ImageLayout.ShaderReadOnlyOptimal);
        if (pixels is null)
            throw new InvalidOperationException("The capture readback returned no pixels.");
        string resolved = ResolveWritablePath(path, Path.Combine(_projectRoot, "Cache", "Screenshots"), ".png");
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        Engine.Mcp.PngWriter.Save(resolved, (int)texture.Width, (int)texture.Height, pixels);
        return resolved;
    });

    public IReadOnlyList<Engine.Mcp.McpLogEntry> GetLogs(int count, string? minLevel) => Run(() =>
    {
        MemoryLogSink? sink = _logs();
        if (sink is null || count <= 0)
            return [];
        LogLevel minimum = Enum.TryParse<LogLevel>(minLevel, ignoreCase: true, out LogLevel parsed) ? parsed : LogLevel.Trace;
        IReadOnlyList<LogEntry> entries = sink.GetEntries();
        return entries.Where(entry => entry.Level >= minimum)
            .TakeLast(count)
            .Select(entry => new Engine.Mcp.McpLogEntry(entry.Timestamp, entry.Level.ToString(), entry.Category, entry.Exception is null ? entry.Message : $"{entry.Message} ({entry.Exception.Message})"))
            .ToList();
    });

    public IReadOnlyList<string> ListScripts() => Run(() =>
    {
        if (!Directory.Exists(_scriptsRoot))
            return [];
        return Directory.EnumerateFiles(_scriptsRoot, "*.cs", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(_scriptsRoot, file).Replace('\\', '/'))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    });

    public string ReadScript(string name) => Run(() => File.ReadAllText(ResolveScriptPath(name)));

    public void WriteScript(string name, string content) => Run(() =>
    {
        string resolved = ResolveScriptPath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(resolved)!);
        File.WriteAllText(resolved, content);
    });

    public void Dispose() => _disposed = true;

    private Engine.Mcp.McpSceneInfo DescribeScene()
    {
        SceneDocument document = RequireContext().Document;
        Dictionary<string, object?> settings = McpMappers.SceneSettings(document.Scene);
        return new Engine.Mcp.McpSceneInfo(
            document.Scene.Name,
            document.Path,
            document.IsDirty,
            _playMode()?.State.ToString() ?? EditorPlayState.Stopped.ToString(),
            document.Scene.AllObjects.Count,
            document.Scene.AllComponents.Count,
            settings,
            _projectRoot);
    }

    private Component RequireComponent(Guid componentId)
    {
        Component? component = RequireScene().FindComponentById(componentId);
        return component ?? throw new KeyNotFoundException($"No component with id '{componentId}'.");
    }

    private string ResolveWritablePath(string? requested, string directory, string extension)
    {
        if (string.IsNullOrWhiteSpace(requested))
            return Path.Combine(directory, $"screenshot-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}{extension}");
        string full = Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(_projectRoot, requested));
        if (!full.StartsWith(_projectRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Screenshots must be written inside the project directory.");
        if (!Path.GetExtension(full).Equals(extension, StringComparison.OrdinalIgnoreCase))
            full += extension;
        return full;
    }

    private string ResolveScriptPath(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string relative = name.Replace('\\', '/');
        string full = Path.GetFullPath(Path.Combine(_scriptsRoot, relative));
        if (!full.StartsWith(_scriptsRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Scripts must live inside the project Scripts folder.");
        return full;
    }

    private sealed class PendingWork
    {
        private readonly Delegate _action;

        public PendingWork(Delegate action) => _action = action;

        public ManualResetEventSlim Completed { get; } = new(false);

        public object? Result { get; private set; }

        public Exception? Error { get; private set; }

        public void Execute()
        {
            try
            {
                Result = _action.DynamicInvoke();
            }
            catch (Exception exception)
            {
                Error = exception.InnerException ?? exception;
            }
            finally
            {
                Completed.Set();
            }
        }
    }
}
