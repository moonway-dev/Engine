using Engine.Editor.Commands;
using Engine.Editor.Documents;
using Engine.Scene;

namespace Engine.Editor;

public sealed class HierarchyCommands
{
    private readonly SceneDocument _document;

    public HierarchyCommands(SceneDocument document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public GameObject Create(string name = "GameObject", GameObject? parent = null)
    {
        ValidateParent(parent);
        Guid? parentId = parent?.Id;
        Guid createdId = Guid.Empty;
        _document.Execute(new SnapshotCommand("Create GameObject", _document, () =>
        {
            GameObject? currentParent = parentId is Guid id ? _document.Scene.FindById(id) : null;
            GameObject created = _document.Scene.CreateObject(UniqueName(name), currentParent);
            createdId = created.Id;
            _document.Select(created);
        }), allowMerge: false);
        return _document.Scene.FindById(createdId)
            ?? _document.SelectedObject
            ?? throw new InvalidOperationException("The created GameObject could not be resolved.");
    }

    public bool Delete(GameObject? gameObject)
    {
        if (!BelongsToDocument(gameObject))
            return false;
        Guid id = gameObject!.Id;
        _document.Execute(new SnapshotCommand("Delete GameObject", _document, () =>
        {
            GameObject? current = _document.Scene.FindById(id);
            if (current is null)
                return;
            _document.Scene.Remove(current);
            if (_document.SelectedObjectId == id || _document.SelectedObject is null)
                _document.Select((GameObject?)null);
        }), allowMerge: false);
        return true;
    }

    public GameObject? Duplicate(GameObject? gameObject)
    {
        if (!BelongsToDocument(gameObject))
            return null;
        Guid sourceId = gameObject!.Id;
        Guid? sourceParentId = gameObject.Parent?.Id;
        Guid duplicateId = Guid.Empty;
        _document.Execute(new SnapshotCommand("Duplicate GameObject", _document, () =>
        {
            GameObject source = _document.Scene.FindById(sourceId)
                ?? throw new InvalidOperationException("The source GameObject no longer exists.");
            GameObject? parent = sourceParentId is Guid parentId ? _document.Scene.FindById(parentId) : null;
            GameObject duplicate = source.Clone(CloneIdPolicy.GenerateNewIds, parent);
            duplicate.Name = UniqueName(source.Name);
            duplicateId = duplicate.Id;
            _document.Select(duplicate);
        }), allowMerge: false);
        return _document.Scene.FindById(duplicateId) ?? _document.SelectedObject;
    }

    public bool Rename(GameObject? gameObject, string name)
    {
        if (!BelongsToDocument(gameObject))
            return false;
        string normalized = string.IsNullOrWhiteSpace(name) ? "GameObject" : name.Trim();
        if (gameObject!.Name == normalized)
            return false;
        Guid id = gameObject.Id;
        string before = gameObject.Name;
        _document.Execute(new PropertyCommand<string>(
            "Rename GameObject",
            (id, nameof(GameObject.Name)),
            () => _document.Scene.FindById(id)?.Name ?? normalized,
            value =>
            {
                GameObject? current = _document.Scene.FindById(id);
                if (current is not null)
                    current.Name = value;
            },
            before,
            normalized),
            allowMerge: false);
        return true;
    }

    public bool Reparent(GameObject? gameObject, GameObject? newParent)
    {
        if (!BelongsToDocument(gameObject))
            return false;
        ValidateParent(newParent);
        if (gameObject!.Parent == newParent)
            return false;
        if (newParent == gameObject || (newParent is not null && gameObject.IsAncestorOf(newParent)))
            return false;
        Guid id = gameObject.Id;
        Guid? parentId = newParent?.Id;
        _document.Execute(new SnapshotCommand("Reparent GameObject", _document, () =>
        {
            GameObject current = _document.Scene.FindById(id)
                ?? throw new InvalidOperationException("The GameObject no longer exists.");
            GameObject? parent = parentId is Guid value ? _document.Scene.FindById(value) : null;
            current.SetParent(parent);
            _document.Select(current);
        }), allowMerge: false);
        return true;
    }

    private bool BelongsToDocument(GameObject? gameObject) =>
        gameObject is not null && ReferenceEquals(gameObject.Scene, _document.Scene)
        && ReferenceEquals(_document.Scene.FindById(gameObject.Id), gameObject);

    private void ValidateParent(GameObject? parent)
    {
        if (parent is not null && !BelongsToDocument(parent))
            throw new InvalidOperationException("The parent does not belong to the active scene.");
    }

    private string UniqueName(string requested)
    {
        string baseName = string.IsNullOrWhiteSpace(requested) ? "GameObject" : requested.Trim();
        HashSet<string> names = _document.Scene.AllObjects
            .Select(gameObject => gameObject.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName))
            return baseName;
        for (int index = 1; ; index++)
        {
            string candidate = $"{baseName} ({index})";
            if (!names.Contains(candidate))
                return candidate;
        }
    }
}
