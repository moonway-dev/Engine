using System.Reflection;
using System.Text.Json;
using Engine.Mathematics;

namespace Engine.Scene;

public enum CloneIdPolicy
{
    PreserveIds,
    GenerateNewIds,
}

public sealed class SceneSnapshot
{
    private readonly SceneSnapshotDocument _document;

    public int Version => _document.Version;

    public string Name => _document.Name;

    public IReadOnlyList<SceneObjectSnapshot> Objects => _document.Objects;

    public Guid? RootId => _document.RootId;

    public PrefabSnapshotMetadata? Prefab => _document.Prefab;

    private SceneSnapshot(SceneSnapshotDocument document)
    {
        _document = document;
    }

    public static SceneSnapshot Capture(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneSnapshot snapshot = CaptureObjects(
            scene.Name,
            scene.EnumerateHierarchy(),
            scene.UnknownComponents,
            null,
            null,
            scene.ComponentRegistry);
        snapshot._document.SkyColor = scene.SkyColor;
        snapshot._document.SkyboxMode = scene.SkyboxMode.ToString();
        snapshot._document.SunDirection = [scene.SunDirection.X, scene.SunDirection.Y, scene.SunDirection.Z];
        snapshot._document.CloudsEnabled = scene.CloudsEnabled;
        snapshot._document.CloudPreset = scene.CloudPreset;
        snapshot._document.CloudCoverage = scene.CloudCoverage;
        snapshot._document.CloudDensity = scene.CloudDensity;
        snapshot._document.CloudWindSpeed = scene.CloudWindSpeed;
        return snapshot;
    }

    public static SceneSnapshot CaptureSubtree(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        HashSet<Guid> ids = [];
        List<GameObject> objects = EnumerateSubtree(root).ToList();
        foreach (GameObject gameObject in objects)
            ids.Add(gameObject.Id);
        IEnumerable<UnknownComponentPayload> unknown = root.Scene.UnknownComponents
            .Where(payload => ids.Contains(payload.OwnerId));
        return CaptureObjects(
            root.Name,
            objects,
            unknown,
            root.Id,
            null,
            root.Scene.ComponentRegistry);
    }

    internal static SceneSnapshot CapturePrefab(GameObject root, string assetKey)
    {
        SceneSnapshot snapshot = CaptureSubtree(root);
        SceneSnapshotDocument document = snapshot.ToDocument();
        document.Prefab = new PrefabSnapshotMetadata
        {
            AssetKey = assetKey,
            SourceRootId = root.Id,
        };
        return new SceneSnapshot(document);
    }

    public static SceneSnapshot Parse(string json) =>
        Parse(json, out _);

    public static SceneSnapshot Parse(string json, out IReadOnlyList<SceneDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Scene data is empty.");
        using JsonDocument source = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });
        JsonElement root = source.RootElement;
        int version = root.TryGetProperty("version", out JsonElement versionElement)
            && versionElement.TryGetInt32(out int parsedVersion)
                ? parsedVersion
                : 1;
        if (version > SceneSerializer.CurrentVersion)
            throw new InvalidOperationException($"Scene version {version} is newer than supported version {SceneSerializer.CurrentVersion}.");
        if (version < 1)
            throw new InvalidOperationException($"Scene version {version} is not supported.");

        List<SceneDiagnostic> migrationDiagnostics = [];
        JsonElement migrated = root.Clone();
        while (version < SceneSerializer.CurrentVersion)
        {
            switch (version)
            {
                case 1:
                    migrated = SceneSnapshotMigration.MigrateV1ToV2(migrated, migrationDiagnostics);
                    version = 2;
                    break;
                default:
                    throw new InvalidOperationException($"No migration is available for scene version {version}.");
            }
        }

        SceneSnapshotDocument document = JsonSerializer.Deserialize<SceneSnapshotDocument>(
            migrated.GetRawText(),
            SceneSerializer.JsonOptions)
            ?? throw new InvalidOperationException("Scene data is invalid.");
        document.Version = SceneSerializer.CurrentVersion;
        NormalizeDocument(document, migrationDiagnostics);
        ValidateHierarchy(document);
        diagnostics = migrationDiagnostics;
        return new SceneSnapshot(document);
    }

    public string ToJson() => JsonSerializer.Serialize(_document, SceneSerializer.JsonOptions);

    public SceneSnapshotRestoreResult Restore(
        CloneIdPolicy idPolicy = CloneIdPolicy.PreserveIds,
        ComponentRegistry? componentRegistry = null)
    {
        Scene scene = new(_document.Name, componentRegistry);
        if (_document.SkyColor is { } skyColor)
            scene.SkyColor = skyColor;
        if (!string.IsNullOrEmpty(_document.SkyboxMode)
            && Enum.TryParse(_document.SkyboxMode, ignoreCase: true, out SkyboxMode skyboxMode))
            scene.SkyboxMode = skyboxMode;
        if (_document.SunDirection is { Length: 3 } sunDirection)
            scene.SunDirection = new Vector3(sunDirection[0], sunDirection[1], sunDirection[2]);
        if (_document.CloudsEnabled is { } cloudsEnabled)
            scene.CloudsEnabled = cloudsEnabled;
        if (!string.IsNullOrEmpty(_document.CloudPreset))
            scene.CloudPreset = _document.CloudPreset;
        if (_document.CloudCoverage is { } cloudCoverage)
            scene.CloudCoverage = cloudCoverage;
        if (_document.CloudDensity is { } cloudDensity)
            scene.CloudDensity = cloudDensity;
        if (_document.CloudWindSpeed is { } cloudWindSpeed)
            scene.CloudWindSpeed = cloudWindSpeed;
        SceneSnapshotRestoreResult result = RestoreInto(scene, idPolicy);
        scene.Version = SceneSerializer.CurrentVersion;
        return result;
    }

    public SceneSnapshotRestoreResult RestoreInto(
        Scene scene,
        CloneIdPolicy idPolicy = CloneIdPolicy.GenerateNewIds,
        GameObject? parent = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (parent is not null && !ReferenceEquals(parent.Scene, scene))
            throw new InvalidOperationException("The restore parent belongs to another scene.");

        Dictionary<Guid, Guid> entityIds = BuildEntityIdMap(scene, idPolicy);
        Dictionary<Guid, Guid> componentIds = BuildComponentIdMap(scene, idPolicy);
        SceneReferenceMap references = new(entityIds, componentIds);
        Dictionary<Guid, GameObject> bySourceId = [];
        Dictionary<Guid, Component> componentsBySourceId = [];
        List<GameObject> createdObjects = [];
        List<GameObject> roots = [];
        List<SceneDiagnostic> diagnostics = [];

        try
        {
            foreach (SceneObjectSnapshot objectData in _document.Objects)
            {
                GameObject gameObject = scene.CreateObject(objectData.Name);
                createdObjects.Add(gameObject);
                scene.ReRegisterWithId(gameObject, entityIds[objectData.Id]);
                gameObject.Active = objectData.Active;
                gameObject.Transform.SetLocalTRS(
                    ToVector3(objectData.Transform.Position),
                    ToQuaternion(objectData.Transform.Rotation),
                    ToVector3(objectData.Transform.Scale));
                bySourceId.Add(objectData.Id, gameObject);

                foreach (SceneComponentSnapshot componentData in objectData.Components.Concat(objectData.UnknownComponents))
                {
                    Component? component = scene.ComponentRegistry.CreateForRestore(componentData.Type, gameObject);
                    if (component is null)
                    {
                        UnknownComponentPayload payload = new(
                            gameObject.Id,
                            componentIds[componentData.Id],
                            componentData.Type,
                            componentData.Enabled,
                            RemapProperties(componentData.Properties, componentData.ReferenceKinds, references),
                            CloneReferenceKinds(componentData.ReferenceKinds));
                        scene.AddUnknownComponent(payload);
                        diagnostics.Add(new SceneDiagnostic(
                            SceneDiagnosticCode.UnknownComponent,
                            $"Component type '{componentData.Type}' is not registered.",
                            gameObject.Id,
                            payload.ComponentId,
                            componentData.Type));
                        continue;
                    }
                    scene.ReRegisterWithId(component, componentIds[componentData.Id]);
                    component.Enabled = componentData.Enabled;
                    SceneSerializer.ApplyProperties(component, componentData.Properties);
                    componentsBySourceId[componentData.Id] = component;
                }
            }

            foreach (SceneObjectSnapshot objectData in _document.Objects)
            {
                GameObject gameObject = bySourceId[objectData.Id];
                if (objectData.ParentId is Guid parentId && bySourceId.TryGetValue(parentId, out GameObject? restoredParent))
                {
                    gameObject.SetParent(restoredParent);
                }
                else
                {
                    if (objectData.ParentId is Guid missingParentId)
                    {
                        diagnostics.Add(new SceneDiagnostic(
                            SceneDiagnosticCode.MissingParent,
                            $"Parent GameObject '{missingParentId}' was not found.",
                            gameObject.Id));
                    }
                    gameObject.SetParent(parent);
                    roots.Add(gameObject);
                }
            }

            foreach ((Guid sourceId, Component component) in componentsBySourceId)
            {
                SceneComponentSnapshot source = FindComponent(sourceId);
                if (source.ReferenceKinds is not null)
                {
                    foreach ((string propertyName, SceneReferenceKind kind) in source.ReferenceKinds)
                    {
                        PropertyInfo? property = component.GetType().GetProperty(
                            propertyName,
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                        if (property is null || !property.CanWrite || source.Properties is null
                            || !TryGetProperty(source.Properties, propertyName, out JsonElement serialized))
                            continue;
                        JsonElement remapped = SceneReferenceRemapper.RemapJson(serialized, kind, references);
                        SceneSerializer.ApplyProperty(component, property, remapped);
                    }
                }
                SceneReferenceRemapper.RemapCustomComponent(component, references);
            }

            foreach (Component component in componentsBySourceId.Values)
            {
                try
                {
                    component.Owner.InvokeComponentAttach(component);
                }
                catch
                {
                    component.Owner.RemoveComponentAfterFailedAttach(component);
                    throw;
                }
            }

            foreach (SceneObjectSnapshot objectData in _document.Objects)
            {
                if (objectData.PrefabInstance is null)
                    continue;
                GameObject gameObject = bySourceId[objectData.Id];
                gameObject.PrefabInstance = RestorePrefabInstance(
                    objectData.PrefabInstance,
                    entityIds,
                    componentIds,
                    bySourceId,
                    componentsBySourceId);
            }

            ApplyPrefabMetadata(bySourceId, references);
            foreach (SceneDiagnostic diagnostic in diagnostics)
                scene.AddDiagnostic(diagnostic);

            return new SceneSnapshotRestoreResult(scene, roots, references, diagnostics);
        }
        catch
        {
            foreach (GameObject gameObject in roots.Concat(createdObjects).Distinct().ToArray())
            {
                if (scene.FindById(gameObject.Id) is not null)
                    scene.Remove(gameObject);
            }
            throw;
        }
    }

    public SceneSnapshot Remap(CloneIdPolicy idPolicy)
    {
        if (idPolicy == CloneIdPolicy.PreserveIds)
            return Parse(ToJson());
        Scene temporary = Restore(CloneIdPolicy.GenerateNewIds).Scene;
        return Capture(temporary);
    }

    internal SceneSnapshotDocument ToDocument()
    {
        string json = JsonSerializer.Serialize(_document, SceneSerializer.JsonOptions);
        return JsonSerializer.Deserialize<SceneSnapshotDocument>(json, SceneSerializer.JsonOptions)!;
    }

    private static SceneSnapshot CaptureObjects(
        string name,
        IEnumerable<GameObject> source,
        IEnumerable<UnknownComponentPayload> unknownComponents,
        Guid? rootId,
        PrefabSnapshotMetadata? prefab,
        ComponentRegistry registry)
    {
        List<GameObject> objects = source.ToList();
        Dictionary<Guid, List<UnknownComponentPayload>> unknownByOwner = unknownComponents
            .GroupBy(component => component.OwnerId)
            .ToDictionary(group => group.Key, group => group.ToList());
        List<SceneObjectSnapshot> snapshots = [];
        foreach (GameObject gameObject in objects)
        {
            List<SceneComponentSnapshot> components = [];
            foreach (Component component in gameObject.Components)
            {
                Dictionary<string, SceneReferenceKind>? referenceKinds;
                Dictionary<string, JsonElement>? properties = SceneSerializer.CaptureProperties(component, out referenceKinds);
                components.Add(new SceneComponentSnapshot
                {
                    Id = component.Id,
                    Type = registry.GetKey(component.GetType()) ?? ComponentRegistry.GetComponentKey(component.GetType()),
                    Enabled = component.Enabled,
                    Properties = properties,
                    ReferenceKinds = referenceKinds,
                });
            }
            List<SceneComponentSnapshot> unknown = [];
            if (unknownByOwner.TryGetValue(gameObject.Id, out List<UnknownComponentPayload>? payloads))
            {
                unknown.AddRange(payloads.Select(payload => new SceneComponentSnapshot
                {
                    Id = payload.ComponentId,
                    Type = payload.Type,
                    Enabled = payload.Enabled,
                    Properties = CloneProperties(payload.Properties),
                    ReferenceKinds = CloneReferenceKinds(payload.ReferenceKinds),
                }));
            }
            snapshots.Add(new SceneObjectSnapshot
            {
                Id = gameObject.Id,
                Name = gameObject.Name,
                Active = gameObject.Active,
                ParentId = gameObject.Parent is not null && objects.Contains(gameObject.Parent)
                    ? gameObject.Parent.Id
                    : null,
                Transform = new TransformSnapshot
                {
                    Position = ToArray(gameObject.Transform.LocalPosition),
                    Rotation = ToArray(gameObject.Transform.LocalRotation),
                    Scale = ToArray(gameObject.Transform.LocalScale),
                },
                Components = components,
                UnknownComponents = unknown,
                PrefabInstance = CapturePrefabInstance(gameObject.PrefabInstance),
            });
        }
        return new SceneSnapshot(new SceneSnapshotDocument
        {
            Version = SceneSerializer.CurrentVersion,
            Name = name,
            RootId = rootId,
            Prefab = prefab,
            Objects = snapshots,
        });
    }

    private Dictionary<Guid, Guid> BuildEntityIdMap(Scene scene, CloneIdPolicy idPolicy)
    {
        Dictionary<Guid, Guid> result = [];
        foreach (SceneObjectSnapshot objectData in _document.Objects)
        {
            if (!result.TryAdd(objectData.Id, SelectId(
                    objectData.Id,
                    idPolicy,
                    scene.FindById,
                    result.Values)))
                throw new InvalidOperationException($"Duplicate GameObject ID '{objectData.Id}' in snapshot.");
        }
        return result;
    }

    private Dictionary<Guid, Guid> BuildComponentIdMap(Scene scene, CloneIdPolicy idPolicy)
    {
        HashSet<Guid> unknownIds = scene.UnknownComponents
            .Select(payload => payload.ComponentId)
            .ToHashSet();
        Dictionary<Guid, Guid> result = [];
        foreach (SceneComponentSnapshot componentData in _document.Objects
                     .SelectMany(gameObject => gameObject.Components.Concat(gameObject.UnknownComponents)))
        {
            if (!result.TryAdd(componentData.Id, SelectComponentId(
                    componentData.Id,
                    idPolicy,
                    scene,
                    unknownIds,
                    result.Values)))
                throw new InvalidOperationException($"Duplicate component ID '{componentData.Id}' in snapshot.");
        }
        return result;
    }

    private static Guid SelectComponentId(
        Guid sourceId,
        CloneIdPolicy idPolicy,
        Scene scene,
        ISet<Guid> unknownIds,
        IEnumerable<Guid> selectedIds)
    {
        if (sourceId == Guid.Empty)
            throw new InvalidOperationException("Snapshot IDs cannot be empty.");
        if (idPolicy == CloneIdPolicy.PreserveIds)
        {
            if (scene.FindComponentById(sourceId) is not null || unknownIds.Contains(sourceId))
                throw new InvalidOperationException($"ID '{sourceId}' already exists in the target scene.");
            return sourceId;
        }
        HashSet<Guid> used = selectedIds.ToHashSet();
        Guid generated;
        do
        {
            generated = Guid.NewGuid();
        }
        while (scene.FindComponentById(generated) is not null
               || unknownIds.Contains(generated)
               || used.Contains(generated));
        return generated;
    }

    private static Guid SelectId<T>(
        Guid sourceId,
        CloneIdPolicy idPolicy,
        Func<Guid, T?> findExisting,
        IEnumerable<Guid> selectedIds) where T : class
    {
        if (sourceId == Guid.Empty)
            throw new InvalidOperationException("Snapshot IDs cannot be empty.");
        if (idPolicy == CloneIdPolicy.PreserveIds)
        {
            if (findExisting(sourceId) is not null)
                throw new InvalidOperationException($"ID '{sourceId}' already exists in the target scene.");
            return sourceId;
        }
        HashSet<Guid> used = selectedIds.ToHashSet();
        Guid generated;
        do
        {
            generated = Guid.NewGuid();
        }
        while (findExisting(generated) is not null || used.Contains(generated));
        return generated;
    }

    private SceneComponentSnapshot FindComponent(Guid id) =>
        _document.Objects.SelectMany(gameObject => gameObject.Components.Concat(gameObject.UnknownComponents))
            .First(component => component.Id == id);

    private void ApplyPrefabMetadata(
        IReadOnlyDictionary<Guid, GameObject> bySourceId,
        SceneReferenceMap references)
    {
        if (_document.Prefab is null)
            return;
        Guid sourceRootId = _document.Prefab.SourceRootId ?? _document.RootId ?? _document.Objects[0].Id;
        if (!bySourceId.TryGetValue(sourceRootId, out GameObject? root))
            return;
        root.PrefabInstance = new PrefabInstance
        {
            AssetKey = _document.Prefab.AssetKey,
            SourceRootId = sourceRootId,
            EntityIdMap = references.EntityIds.ToDictionary(pair => pair.Key, pair => pair.Value),
            ComponentIdMap = references.ComponentIds.ToDictionary(pair => pair.Key, pair => pair.Value),
        };
    }

    private static void NormalizeDocument(
        SceneSnapshotDocument document,
        ICollection<SceneDiagnostic> diagnostics)
    {
        document.Name ??= "Scene";
        document.Objects ??= [];
        HashSet<Guid> entityIds = [];
        HashSet<Guid> componentIds = [];
        foreach (SceneObjectSnapshot gameObject in document.Objects)
        {
            Guid originalId = gameObject.Id;
            if (originalId == Guid.Empty || !entityIds.Add(originalId))
            {
                gameObject.Id = NewUniqueId(entityIds);
                diagnostics.Add(new SceneDiagnostic(
                    SceneDiagnosticCode.InvalidIdentifier,
                    $"GameObject ID '{originalId}' was invalid and has been regenerated.",
                    gameObject.Id));
            }
            gameObject.Name ??= "GameObject";
            gameObject.Transform ??= new TransformSnapshot();
            gameObject.Transform.Position = NormalizeArray(gameObject.Transform.Position, [0f, 0f, 0f]);
            gameObject.Transform.Rotation = NormalizeArray(gameObject.Transform.Rotation, [0f, 0f, 0f, 1f]);
            gameObject.Transform.Scale = NormalizeArray(gameObject.Transform.Scale, [1f, 1f, 1f]);
            gameObject.Components ??= [];
            gameObject.UnknownComponents ??= [];
            foreach (SceneComponentSnapshot component in gameObject.Components.Concat(gameObject.UnknownComponents))
            {
                if (component.Id == Guid.Empty || !componentIds.Add(component.Id))
                {
                    Guid previous = component.Id;
                    component.Id = NewUniqueId(componentIds);
                    diagnostics.Add(new SceneDiagnostic(
                        SceneDiagnosticCode.InvalidIdentifier,
                        $"Component ID '{previous}' was invalid and has been regenerated.",
                        gameObject.Id,
                        component.Id,
                        component.Type));
                }
                component.Type ??= string.Empty;
            }
        }
    }

    private static Guid NewUniqueId(ISet<Guid> used)
    {
        Guid id;
        do
        {
            id = Guid.NewGuid();
        }
        while (!used.Add(id));
        return id;
    }

    private static void ValidateHierarchy(SceneSnapshotDocument document)
    {
        Dictionary<Guid, SceneObjectSnapshot> objects = document.Objects.ToDictionary(gameObject => gameObject.Id);
        foreach (SceneObjectSnapshot gameObject in document.Objects)
        {
            if (gameObject.ParentId == gameObject.Id)
                throw new InvalidOperationException($"GameObject '{gameObject.Id}' cannot be its own parent.");
            HashSet<Guid> visited = [gameObject.Id];
            Guid? parentId = gameObject.ParentId;
            while (parentId is Guid current && objects.TryGetValue(current, out SceneObjectSnapshot? parent))
            {
                if (!visited.Add(current))
                    throw new InvalidOperationException($"Scene hierarchy contains a cycle involving GameObject '{current}'.");
                parentId = parent.ParentId;
            }
        }
    }

    private static float[] NormalizeArray(float[]? values, float[] defaults)
    {
        float[] result = defaults.ToArray();
        if (values is null)
            return result;
        Array.Copy(values, result, Math.Min(values.Length, result.Length));
        return result;
    }

    private static IEnumerable<GameObject> EnumerateSubtree(GameObject root)
    {
        yield return root;
        foreach (GameObject child in root.Children)
        foreach (GameObject descendant in EnumerateSubtree(child))
            yield return descendant;
    }

    private static Dictionary<string, JsonElement>? CloneProperties(
        Dictionary<string, JsonElement>? properties)
    {
        if (properties is null)
            return null;
        return properties.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    }

    private static PrefabInstanceSnapshot? CapturePrefabInstance(PrefabInstance? instance)
    {
        if (instance is null)
            return null;
        return new PrefabInstanceSnapshot
        {
            AssetKey = instance.AssetKey,
            SourceRootId = instance.SourceRootId,
            EntityIdMap = new Dictionary<Guid, Guid>(instance.EntityIdMap),
            ComponentIdMap = new Dictionary<Guid, Guid>(instance.ComponentIdMap),
            Overrides = instance.Overrides.Select(prefabOverride => prefabOverride.Clone()).ToList(),
        };
    }

    private static PrefabInstance RestorePrefabInstance(
        PrefabInstanceSnapshot snapshot,
        IReadOnlyDictionary<Guid, Guid> entityIds,
        IReadOnlyDictionary<Guid, Guid> componentIds,
        IReadOnlyDictionary<Guid, GameObject> bySourceId,
        IReadOnlyDictionary<Guid, Component> componentsBySourceId)
    {
        return new PrefabInstance
        {
            AssetKey = snapshot.AssetKey,
            SourceRootId = snapshot.SourceRootId,
            EntityIdMap = snapshot.EntityIdMap.ToDictionary(
                pair => pair.Key,
                pair => RemapRuntimeEntityId(pair.Value, entityIds, bySourceId)),
            ComponentIdMap = snapshot.ComponentIdMap.ToDictionary(
                pair => pair.Key,
                pair => RemapRuntimeComponentId(pair.Value, componentIds, componentsBySourceId)),
            Overrides = snapshot.Overrides.Select(prefabOverride => prefabOverride.Clone()).ToList(),
        };
    }

    private static Guid RemapRuntimeEntityId(
        Guid runtimeId,
        IReadOnlyDictionary<Guid, Guid> entityIds,
        IReadOnlyDictionary<Guid, GameObject> bySourceId)
    {
        if (entityIds.TryGetValue(runtimeId, out Guid remapped))
            return remapped;
        foreach ((Guid sourceId, GameObject gameObject) in bySourceId)
        {
            if (gameObject.Id == runtimeId && entityIds.TryGetValue(sourceId, out remapped))
                return remapped;
        }
        return runtimeId;
    }

    private static Guid RemapRuntimeComponentId(
        Guid runtimeId,
        IReadOnlyDictionary<Guid, Guid> componentIds,
        IReadOnlyDictionary<Guid, Component> componentsBySourceId)
    {
        if (componentIds.TryGetValue(runtimeId, out Guid remapped))
            return remapped;
        foreach ((Guid sourceId, Component component) in componentsBySourceId)
        {
            if (component.Id == runtimeId && componentIds.TryGetValue(sourceId, out remapped))
                return remapped;
        }
        return runtimeId;
    }

    private static Dictionary<string, SceneReferenceKind>? CloneReferenceKinds(
        Dictionary<string, SceneReferenceKind>? referenceKinds)
    {
        return referenceKinds is null
            ? null
            : new Dictionary<string, SceneReferenceKind>(referenceKinds, StringComparer.Ordinal);
    }

    private static Dictionary<string, JsonElement>? RemapProperties(
        Dictionary<string, JsonElement>? properties,
        Dictionary<string, SceneReferenceKind>? referenceKinds,
        SceneReferenceMap references)
    {
        Dictionary<string, JsonElement>? result = CloneProperties(properties);
        if (result is null || referenceKinds is null)
            return result;
        foreach ((string propertyName, SceneReferenceKind kind) in referenceKinds)
        {
            string? actualKey = result.Keys.FirstOrDefault(key => key.Equals(propertyName, StringComparison.OrdinalIgnoreCase));
            if (actualKey is not null && TryGetProperty(result, actualKey, out JsonElement value))
                result[actualKey] = SceneReferenceRemapper.RemapJson(value, kind, references);
        }
        return result;
    }

    private static bool TryGetProperty(
        IReadOnlyDictionary<string, JsonElement> properties,
        string name,
        out JsonElement value)
    {
        if (properties.TryGetValue(name, out value))
            return true;
        foreach ((string key, JsonElement candidate) in properties)
        {
            if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                value = candidate;
                return true;
            }
        }
        value = default;
        return false;
    }

    private static float[] ToArray(Vector3 value) => [value.X, value.Y, value.Z];

    private static float[] ToArray(Quaternion value) => [value.X, value.Y, value.Z, value.W];

    internal static Vector3 ToVector3(float[] values) =>
        new(Get(values, 0), Get(values, 1), Get(values, 2));

    internal static Quaternion ToQuaternion(float[] values) =>
        new(Get(values, 0), Get(values, 1), Get(values, 2), Get(values, 3, 1f));

    private static float Get(float[] values, int index, float fallback = 0f) =>
        index < values.Length ? values[index] : fallback;
}

public sealed class SceneSnapshotRestoreResult
{
    public Scene Scene { get; }

    public IReadOnlyList<GameObject> RootObjects { get; }

    public SceneReferenceMap References { get; }

    public IReadOnlyList<SceneDiagnostic> Diagnostics { get; }

    internal SceneSnapshotRestoreResult(
        Scene scene,
        IReadOnlyList<GameObject> rootObjects,
        SceneReferenceMap references,
        IReadOnlyList<SceneDiagnostic> diagnostics)
    {
        Scene = scene;
        RootObjects = rootObjects;
        References = references;
        Diagnostics = diagnostics;
    }
}

public sealed record UnknownComponentPayload(
    Guid OwnerId,
    Guid ComponentId,
    string Type,
    bool Enabled,
    Dictionary<string, JsonElement>? Properties,
    Dictionary<string, SceneReferenceKind>? ReferenceKinds = null);

public enum SceneDiagnosticCode
{
    Migrated,
    UnknownComponent,
    MissingParent,
    InvalidIdentifier,
    InvalidReference,
}

public sealed record SceneDiagnostic(
    SceneDiagnosticCode Code,
    string Message,
    Guid? GameObjectId = null,
    Guid? ComponentId = null,
    string? ComponentType = null);

internal sealed class SceneSnapshotDocument
{
    public int Version { get; set; } = SceneSerializer.CurrentVersion;
    public string Name { get; set; } = "Scene";
    public Vector4? SkyColor { get; set; }
    public string? SkyboxMode { get; set; }
    public float[]? SunDirection { get; set; }
    public bool? CloudsEnabled { get; set; }
    public string? CloudPreset { get; set; }
    public float? CloudCoverage { get; set; }
    public float? CloudDensity { get; set; }
    public float? CloudWindSpeed { get; set; }
    public Guid? RootId { get; set; }
    public PrefabSnapshotMetadata? Prefab { get; set; }
    public List<SceneObjectSnapshot> Objects { get; set; } = [];
}

public sealed class SceneObjectSnapshot
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "GameObject";
    public bool Active { get; set; } = true;
    public Guid? ParentId { get; set; }
    public TransformSnapshot Transform { get; set; } = new();
    public List<SceneComponentSnapshot> Components { get; set; } = [];
    public List<SceneComponentSnapshot> UnknownComponents { get; set; } = [];
    public PrefabInstanceSnapshot? PrefabInstance { get; set; }
}

public sealed class PrefabInstanceSnapshot
{
    public string AssetKey { get; set; } = string.Empty;
    public Guid SourceRootId { get; set; }
    public Dictionary<Guid, Guid> EntityIdMap { get; set; } = [];
    public Dictionary<Guid, Guid> ComponentIdMap { get; set; } = [];
    public List<PrefabOverride> Overrides { get; set; } = [];
}

public sealed class TransformSnapshot
{
    public float[] Position { get; set; } = [0f, 0f, 0f];
    public float[] Rotation { get; set; } = [0f, 0f, 0f, 1f];
    public float[] Scale { get; set; } = [1f, 1f, 1f];
}

public sealed class SceneComponentSnapshot
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public Dictionary<string, JsonElement>? Properties { get; set; }
    public Dictionary<string, SceneReferenceKind>? ReferenceKinds { get; set; }
}

public sealed class PrefabSnapshotMetadata
{
    public string AssetKey { get; set; } = string.Empty;
    public Guid? SourceRootId { get; set; }
}

internal static class SceneSnapshotMigration
{
    public static JsonElement MigrateV1ToV2(
        JsonElement source,
        ICollection<SceneDiagnostic> diagnostics)
    {
        string name = source.TryGetProperty("name", out JsonElement nameElement)
            ? nameElement.GetString() ?? "Scene"
            : "Scene";
        List<SceneObjectSnapshot> objects = [];
        if (source.TryGetProperty("objects", out JsonElement objectsElement)
            && objectsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement objectElement in objectsElement.EnumerateArray())
            {
                Guid objectId = ReadGuid(objectElement, "id") ?? Guid.NewGuid();
                SceneObjectSnapshot gameObject = new()
                {
                    Id = objectId,
                    Name = ReadString(objectElement, "name") ?? "GameObject",
                    Active = ReadBool(objectElement, "active", true),
                    ParentId = ReadGuid(objectElement, "parentId"),
                    Transform = ReadTransform(objectElement),
                };
                if (objectElement.TryGetProperty("components", out JsonElement components)
                    && components.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement componentElement in components.EnumerateArray())
                    {
                        string type = ReadString(componentElement, "type") ?? string.Empty;
                        Dictionary<string, JsonElement>? properties = null;
                        if (componentElement.TryGetProperty("properties", out JsonElement propertiesElement)
                            && propertiesElement.ValueKind == JsonValueKind.Object)
                        {
                            properties = propertiesElement.EnumerateObject().ToDictionary(
                                property => property.Name,
                                property => property.Value.Clone(),
                                StringComparer.Ordinal);
                        }
                        gameObject.Components.Add(new SceneComponentSnapshot
                        {
                            Id = ReadGuid(componentElement, "id")
                                ?? CreateDeterministicComponentId(objectId, type, gameObject.Components.Count),
                            Type = type,
                            Enabled = ReadBool(componentElement, "enabled", true),
                            Properties = properties,
                        });
                    }
                }
                objects.Add(gameObject);
            }
        }
        SceneSnapshotDocument migrated = new()
        {
            Version = SceneSerializer.CurrentVersion,
            Name = name,
            Objects = objects,
        };
        diagnostics.Add(new SceneDiagnostic(
            SceneDiagnosticCode.Migrated,
            "Scene data was migrated from schema v1 to v2."));
        return JsonSerializer.SerializeToElement(migrated, SceneSerializer.JsonOptions);
    }

    private static TransformSnapshot ReadTransform(JsonElement gameObject)
    {
        if (!gameObject.TryGetProperty("transform", out JsonElement transform)
            || transform.ValueKind != JsonValueKind.Object)
            return new TransformSnapshot();
        return new TransformSnapshot
        {
            Position = ReadFloatArray(transform, "position", [0f, 0f, 0f]),
            Rotation = ReadFloatArray(transform, "rotation", [0f, 0f, 0f, 1f]),
            Scale = ReadFloatArray(transform, "scale", [1f, 1f, 1f]),
        };
    }

    private static Guid CreateDeterministicComponentId(Guid ownerId, string type, int index)
    {
        Span<byte> ownerBytes = stackalloc byte[16];
        ownerId.TryWriteBytes(ownerBytes);
        byte[] typeBytes = System.Text.Encoding.UTF8.GetBytes($"{type}:{index}");
        byte[] payload = new byte[ownerBytes.Length + typeBytes.Length];
        ownerBytes.CopyTo(payload);
        typeBytes.CopyTo(payload, ownerBytes.Length);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(payload);
        return new Guid(hash.AsSpan(0, 16));
    }

    private static Guid? ReadGuid(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.String)
            return null;
        return Guid.TryParse(property.GetString(), out Guid id) ? id : null;
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool ReadBool(JsonElement element, string name, bool fallback) =>
        element.TryGetProperty(name, out JsonElement property)
            && (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
                ? property.GetBoolean()
                : fallback;

    private static float[] ReadFloatArray(JsonElement element, string name, float[] fallback)
    {
        if (!element.TryGetProperty(name, out JsonElement property)
            || property.ValueKind != JsonValueKind.Array)
            return fallback;
        return property.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Number ? value.GetSingle() : 0f)
            .ToArray();
    }
}
