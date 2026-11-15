using Engine.Core;
using Engine.Editor;
using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Editor.DragDrop;
using Engine.Editor.Models;
using Engine.Scene;
using Xunit;
using SceneModel = Engine.Scene.Scene;

namespace Engine.Tests.Rendering;

public sealed class EditorCommandHistoryTests
{
    [Fact]
    public void ExecuteUndoRedo_TracksSavePoint()
    {
        int value = 0;
        CommandHistory history = new();
        history.Reset();

        history.Execute(new PropertyCommand<int>("Set value", "value", () => value, next => value = next, 0, 1));
        Assert.Equal(1, value);
        Assert.True(history.CanUndo);
        Assert.False(history.IsAtSavePoint);

        Assert.True(history.Undo());
        Assert.Equal(0, value);
        Assert.True(history.IsAtSavePoint);

        Assert.True(history.Redo());
        Assert.Equal(1, value);
        Assert.False(history.IsAtSavePoint);

        history.MarkSavePoint();
        Assert.True(history.IsAtSavePoint);
        Assert.True(history.Undo());
        Assert.False(history.IsAtSavePoint);
    }

    [Fact]
    public void MergeableCommands_MergeUntilBoundaryAndPreserveOriginalBeforeValue()
    {
        int value = 0;
        CommandHistory history = new();
        history.Reset();

        history.Execute(new PropertyCommand<int>("Drag", "value", () => value, next => value = next, value, 1));
        history.Execute(new PropertyCommand<int>("Drag", "value", () => value, next => value = next, value, 2));
        history.Execute(new PropertyCommand<int>("Drag", "value", () => value, next => value = next, value, 3));

        Assert.Equal(1, history.UndoCount);
        Assert.Equal(3, value);
        history.Undo();
        Assert.Equal(0, value);
        history.Redo();
        Assert.Equal(3, value);

        history.EndMergeGroup();
        history.Execute(new PropertyCommand<int>("Drag", "value", () => value, next => value = next, value, 4));
        Assert.Equal(2, history.UndoCount);
    }

    [Fact]
    public void NewCommandAfterUndo_ClearsRedoBranch()
    {
        int value = 0;
        CommandHistory history = new();
        history.Reset();
        history.Execute(new PropertyCommand<int>("First", "first", () => value, next => value = next, value, 1));
        history.Execute(new PropertyCommand<int>("Second", "second", () => value, next => value = next, value, 2));
        history.Undo();
        Assert.True(history.CanRedo);

        history.Execute(new PropertyCommand<int>("Branch", "branch", () => value, next => value = next, value, 9));

        Assert.False(history.CanRedo);
        Assert.Equal(9, value);
    }
}

public sealed class SceneDocumentEditorTests
{
    [Fact]
    public void SaveAsEditUndoRedo_UpdatesPathAndDirtyState()
    {
        string directory = CreateTempDirectory();
        try
        {
            SceneModel scene = new("Main");
            GameObject gameObject = scene.CreateObject("Original");
            DelegatingSceneHost host = new(scene);
            using SceneDocument document = new(host, scene);
            string path = Path.Combine(directory, "main.scene");

            document.SaveAs(path);
            Assert.Equal(Path.GetFullPath(path), document.Path);
            Assert.False(document.IsDirty);

            document.Execute(new PropertyCommand<string>(
                "Rename",
                (gameObject.Id, "Name"),
                () => document.Scene.FindById(gameObject.Id)!.Name,
                value => document.Scene.FindById(gameObject.Id)!.Name = value,
                "Original",
                "Changed"));
            Assert.True(document.IsDirty);
            Assert.Equal("Changed", document.Scene.FindById(gameObject.Id)!.Name);

            document.Undo();
            Assert.False(document.IsDirty);
            Assert.Equal("Original", document.Scene.FindById(gameObject.Id)!.Name);

            document.Redo();
            Assert.True(document.IsDirty);
            document.Save();
            Assert.False(document.IsDirty);
            Assert.Equal("Changed", SceneModel.Load(path).FindById(gameObject.Id)!.Name);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void NewAndOpen_ReplaceHostSceneAndResetHistorySelectionAndPath()
    {
        string directory = CreateTempDirectory();
        try
        {
            SceneModel initial = new("Initial");
            GameObject selected = initial.CreateObject("Selected");
            SceneModel stored = new("Stored");
            stored.CreateObject("Loaded");
            string path = Path.Combine(directory, "stored.scene");
            stored.Save(path);
            DelegatingSceneHost host = new(initial);
            using SceneDocument document = new(host, initial);
            document.Select(selected);
            document.Execute(new PropertyCommand<string>("Rename", "name", () => selected.Name, value => selected.Name = value, selected.Name, "Dirty"));

            document.New("Fresh");
            Assert.Same(document.Scene, host.ActiveScene);
            Assert.Equal("Fresh", document.Scene.Name);
            Assert.Null(document.Path);
            Assert.Null(document.SelectedObject);
            Assert.False(document.IsDirty);
            Assert.False(document.History.CanUndo);

            document.Open(path);
            Assert.Same(document.Scene, host.ActiveScene);
            Assert.Equal("Stored", document.Scene.Name);
            Assert.NotNull(document.Scene.FindByName("Loaded"));
            Assert.Equal(Path.GetFullPath(path), document.Path);
            Assert.False(document.IsDirty);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void HierarchyCommands_UndoStructuralOperationsViaSnapshots()
    {
        SceneModel scene = new("Hierarchy");
        DelegatingSceneHost host = new(scene);
        using SceneDocument document = new(host, scene);
        HierarchyCommands hierarchy = new(document);

        GameObject parent = hierarchy.Create("Parent");
        Guid parentId = parent.Id;
        GameObject child = hierarchy.Create("Child", parent);
        Guid childId = child.Id;
        hierarchy.Rename(child, "Renamed");
        hierarchy.Reparent(child, null);

        Assert.Null(document.Scene.FindById(childId)!.Parent);
        document.Undo();
        Assert.Equal(parentId, document.Scene.FindById(childId)!.Parent!.Id);
        document.Undo();
        Assert.Equal("Child", document.Scene.FindById(childId)!.Name);

        GameObject duplicate = hierarchy.Duplicate(document.Scene.FindById(parentId))!;
        Guid duplicateId = duplicate.Id;
        Assert.NotEqual(parentId, duplicateId);
        Assert.NotNull(document.Scene.FindById(duplicateId));
        hierarchy.Delete(document.Scene.FindById(duplicateId));
        Assert.Null(document.Scene.FindById(duplicateId));
        document.Undo();
        Assert.NotNull(document.Scene.FindById(duplicateId));
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "EngineEditorTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

public sealed class EditorFilterModelTests
{
    [Fact]
    public void ProjectBrowser_RefreshClassifiesAndFiltersAssets()
    {
        string directory = Path.Combine(Path.GetTempPath(), "EngineProjectBrowserTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(directory, "Scenes"));
        Directory.CreateDirectory(Path.Combine(directory, "Models"));
        try
        {
            File.WriteAllText(Path.Combine(directory, "Scenes", "main.scene"), "{}");
            File.WriteAllText(Path.Combine(directory, "Models", "cube.obj"), string.Empty);
            File.WriteAllText(Path.Combine(directory, "readme.txt"), string.Empty);
            ProjectBrowserModel model = new(directory);

            model.Refresh();
            Assert.Contains(model.EnumerateVisible(), entry => entry.RelativePath == "Scenes/main.scene" && entry.Kind == AssetKind.Scene);
            Assert.Contains(model.EnumerateVisible(), entry => entry.RelativePath == "Models/cube.obj" && entry.Kind == AssetKind.Mesh);

            model.Filter = "cube";
            AssetEntry entry = Assert.Single(model.EnumerateVisible());
            Assert.Equal(AssetKind.Mesh, entry.Kind);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void ConsoleFilter_AppliesLevelCategoryAndText()
    {
        ConsoleFilterModel filter = new();
        LogEntry info = new(DateTimeOffset.Now, LogLevel.Information, "Renderer", "Frame ready");
        LogEntry error = new(DateTimeOffset.Now, LogLevel.Error, "Assets", "Texture missing");
        filter.SetLevelEnabled(LogLevel.Information, false);
        filter.Category = "asset";
        filter.Text = "missing";

        Assert.False(filter.Matches(info));
        Assert.True(filter.Matches(error));
        filter.SetLevelEnabled(LogLevel.Error, false);
        Assert.False(filter.Matches(error));
    }

    [Fact]
    public void TypedAssetPayload_RoundTripsKindAndRelativePath()
    {
        AssetDragPayload source = new(AssetKind.Texture, "Textures/brick.png");
        Assert.True(AssetDragPayload.TryParse(source.Serialize(), out AssetDragPayload parsed));
        Assert.Equal(source, parsed);
        Assert.False(AssetDragPayload.TryParse("invalid", out _));
    }
}
