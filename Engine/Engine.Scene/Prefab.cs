using System.Reflection;
using System.Text.Json;

namespace Engine.Scene;

public sealed class PrefabAsset
{
    private SceneSnapshot _snapshot;

    public string AssetKey { get; }

    public Guid SourceRootId => _snapshot.Prefab?.SourceRootId
        ?? _snapshot.RootId
        ?? throw new InvalidOperationException("The prefab has no source root ID.");

    public SceneSnapshot Snapshot => _snapshot;

    private PrefabAsset(string assetKey, SceneSnapshot snapshot)
    {
        AssetKey = assetKey;
        _snapshot = snapshot;
    }

    public static PrefabAsset Capture(GameObject root, string assetKey)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetKey);
        return new PrefabAsset(assetKey, SceneSnapshot.CapturePrefab(root, assetKey));
    }

    public static PrefabAsset FromJson(string json, string? assetKey = null)
    {
        SceneSnapshot snapshot = SceneSnapshot.Parse(json);
        if (snapshot.Objects.Count == 0)
            throw new InvalidOperationException("Prefab data contains no objects.");
        string resolvedKey = assetKey ?? snapshot.Prefab?.AssetKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(resolvedKey))
            resolvedKey = snapshot.Name;
        SceneSnapshotDocument document = snapshot.ToDocument();
        Guid sourceRootId = document.Prefab?.SourceRootId
            ?? document.RootId
            ?? document.Objects.First(gameObject => gameObject.ParentId is null).Id;
        document.RootId = sourceRootId;
        document.Prefab = new PrefabSnapshotMetadata
        {
            AssetKey = resolvedKey,
            SourceRootId = sourceRootId,
        };
        return new PrefabAsset(resolvedKey, SceneSnapshot.Parse(JsonSerializer.Serialize(document, SceneSerializer.JsonOptions)));
    }

    public static PrefabAsset Load(string path, string? assetKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return FromJson(Engine.Core.VirtualFileSystem.ReadAllText(path), assetKey ?? path);
    }

    public string ToJson() => _snapshot.ToJson();

    public void Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, ToJson());
    }

    public GameObject Instantiate(
        Scene scene,
        GameObject? parent = null,
        IEnumerable<PrefabOverride>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        SceneSnapshotRestoreResult result = _snapshot.RestoreInto(
            scene,
            CloneIdPolicy.GenerateNewIds,
            parent);
        Guid runtimeRootId = result.References.RemapEntity(SourceRootId);
        GameObject root = scene.FindById(runtimeRootId)
            ?? throw new InvalidOperationException("The instantiated prefab root could not be resolved.");
        PrefabInstance instance = root.PrefabInstance ?? new PrefabInstance
        {
            AssetKey = AssetKey,
            SourceRootId = SourceRootId,
            EntityIdMap = result.References.EntityIds.ToDictionary(pair => pair.Key, pair => pair.Value),
            ComponentIdMap = result.References.ComponentIds.ToDictionary(pair => pair.Key, pair => pair.Value),
        };
        root.PrefabInstance = instance;
        if (overrides is not null)
        {
            foreach (PrefabOverride prefabOverride in overrides)
                SetOverride(root, prefabOverride);
            ApplyOverrides(root);
        }
        return root;
    }

    public void SetOverride(
        GameObject instanceRoot,
        Guid sourceObjectId,
        string property,
        object? value)
    {
        SetOverride(instanceRoot, new PrefabOverride
        {
            TargetKind = PrefabOverrideTarget.GameObject,
            SourceId = sourceObjectId,
            Property = property,
            Value = JsonSerializer.SerializeToElement(value, SceneSerializer.JsonOptions),
        });
    }

    public void SetComponentOverride(
        GameObject instanceRoot,
        Guid sourceComponentId,
        string property,
        object? value)
    {
        PrefabInstance instance = RequireInstance(instanceRoot);
        SetOverride(instanceRoot, new PrefabOverride
        {
            TargetKind = PrefabOverrideTarget.Component,
            SourceId = sourceComponentId,
            Property = property,
            Value = JsonSerializer.SerializeToElement(value, SceneSerializer.JsonOptions),
            ReferenceKind = GetOverrideReferenceKind(instanceRoot.Scene, instance, sourceComponentId, property),
        });
    }

    public void SetOverride(GameObject instanceRoot, PrefabOverride prefabOverride)
    {
        PrefabInstance instance = RequireInstance(instanceRoot);
        if (prefabOverride.SourceId == Guid.Empty)
            throw new ArgumentException("Override source IDs cannot be empty.", nameof(prefabOverride));
        if (string.IsNullOrWhiteSpace(prefabOverride.Property))
            throw new ArgumentException("Override properties cannot be empty.", nameof(prefabOverride));
        int existing = instance.Overrides.FindIndex(item =>
            item.TargetKind == prefabOverride.TargetKind
            && item.SourceId == prefabOverride.SourceId
            && item.Property.Equals(prefabOverride.Property, StringComparison.OrdinalIgnoreCase));
        PrefabOverride clone = prefabOverride.Clone();
        if (existing >= 0)
            instance.Overrides[existing] = clone;
        else
            instance.Overrides.Add(clone);
    }

    public void ApplyOverrides(GameObject instanceRoot)
    {
        PrefabInstance instance = RequireInstance(instanceRoot);
        foreach (PrefabOverride prefabOverride in instance.Overrides)
            ApplyOverride(instanceRoot.Scene, instance, prefabOverride);
    }

    public void RevertOverrides(GameObject instanceRoot)
    {
        PrefabInstance instance = RequireInstance(instanceRoot);
        foreach (PrefabOverride prefabOverride in instance.Overrides)
            RevertOverride(instanceRoot.Scene, instance, prefabOverride);
        instance.Overrides.Clear();
    }

    public void ApplyInstanceToPrefab(GameObject instanceRoot)
    {
        PrefabInstance instance = RequireInstance(instanceRoot);
        SceneSnapshotDocument document = _snapshot.ToDocument();
        foreach (PrefabOverride prefabOverride in instance.Overrides)
        {
            JsonElement currentValue = ReadRuntimeValue(instanceRoot.Scene, instance, prefabOverride);
            currentValue = RemapOverrideValueToSource(currentValue, prefabOverride, instance, instanceRoot.Scene);
            WriteSnapshotValue(document, prefabOverride, currentValue);
        }
        instance.Overrides.Clear();
        _snapshot = SceneSnapshot.Parse(JsonSerializer.Serialize(document, SceneSerializer.JsonOptions));
    }

    private PrefabInstance RequireInstance(GameObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        PrefabInstance instance = root.PrefabInstance
            ?? throw new InvalidOperationException("The GameObject is not a prefab instance root.");
        if (!instance.AssetKey.Equals(AssetKey, StringComparison.Ordinal))
            throw new InvalidOperationException($"The instance belongs to prefab '{instance.AssetKey}', not '{AssetKey}'.");
        return instance;
    }

    private void ApplyOverride(Scene scene, PrefabInstance instance, PrefabOverride prefabOverride)
    {
        object target = ResolveRuntimeTarget(scene, instance, prefabOverride);
        ApplyTargetProperty(target, prefabOverride.Property, prefabOverride.Value);
    }

    private void RevertOverride(Scene scene, PrefabInstance instance, PrefabOverride prefabOverride)
    {
        JsonElement sourceValue = ReadSnapshotValue(_snapshot.ToDocument(), prefabOverride);
        sourceValue = RemapOverrideValueToInstance(sourceValue, prefabOverride, instance, scene);
        object target = ResolveRuntimeTarget(scene, instance, prefabOverride);
        ApplyTargetProperty(target, prefabOverride.Property, sourceValue);
    }

    private static SceneReferenceKind? GetOverrideReferenceKind(
        Scene scene,
        PrefabInstance instance,
        Guid sourceComponentId,
        string propertyName)
    {
        if (!instance.ComponentIdMap.TryGetValue(sourceComponentId, out Guid componentId))
            return null;
        Component? component = scene.FindComponentById(componentId);
        PropertyInfo? property = component?.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property is null ? null : SceneReferenceRemapper.GetReferenceKind(property);
    }

    private static JsonElement RemapOverrideValueToInstance(
        JsonElement value,
        PrefabOverride prefabOverride,
        PrefabInstance instance,
        Scene scene)
    {
        if (prefabOverride.TargetKind != PrefabOverrideTarget.Component)
            return value;
        if (!instance.ComponentIdMap.TryGetValue(prefabOverride.SourceId, out Guid componentId))
            return value;
        Component? component = scene.FindComponentById(componentId);
        if (component is null)
            return value;
        SceneReferenceKind? kind = prefabOverride.ReferenceKind;
        if (kind is null)
        {
            PropertyInfo? property = component.GetType().GetProperty(
                prefabOverride.Property,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            kind = property is null ? null : SceneReferenceRemapper.GetReferenceKind(property);
        }
        if (kind is null)
            return value;
        SceneReferenceMap map = new(instance.EntityIdMap, instance.ComponentIdMap);
        return SceneReferenceRemapper.RemapJson(value, kind.Value, map);
    }

    private static JsonElement RemapOverrideValueToSource(
        JsonElement value,
        PrefabOverride prefabOverride,
        PrefabInstance instance,
        Scene scene)
    {
        if (prefabOverride.TargetKind != PrefabOverrideTarget.Component)
            return value;
        if (!instance.ComponentIdMap.TryGetValue(prefabOverride.SourceId, out Guid componentId))
            return value;
        Component? component = scene.FindComponentById(componentId);
        if (component is null)
            return value;
        SceneReferenceKind? kind = prefabOverride.ReferenceKind;
        if (kind is null)
        {
            PropertyInfo? property = component.GetType().GetProperty(
                prefabOverride.Property,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            kind = property is null ? null : SceneReferenceRemapper.GetReferenceKind(property);
        }
        if (kind is null)
            return value;
        Dictionary<Guid, Guid> entityIds = instance.EntityIdMap.ToDictionary(pair => pair.Value, pair => pair.Key);
        Dictionary<Guid, Guid> componentIds = instance.ComponentIdMap.ToDictionary(pair => pair.Value, pair => pair.Key);
        SceneReferenceMap map = new(entityIds, componentIds);
        return SceneReferenceRemapper.RemapJson(value, kind.Value, map);
    }

    private static object ResolveRuntimeTarget(
        Scene scene,
        PrefabInstance instance,
        PrefabOverride prefabOverride)
    {
        if (prefabOverride.TargetKind == PrefabOverrideTarget.GameObject)
        {
            if (!instance.EntityIdMap.TryGetValue(prefabOverride.SourceId, out Guid runtimeId))
                throw new InvalidOperationException($"Prefab GameObject '{prefabOverride.SourceId}' is not part of this instance.");
            return scene.FindById(runtimeId)
                ?? throw new InvalidOperationException($"Prefab instance GameObject '{runtimeId}' no longer exists.");
        }
        if (!instance.ComponentIdMap.TryGetValue(prefabOverride.SourceId, out Guid componentId))
            throw new InvalidOperationException($"Prefab component '{prefabOverride.SourceId}' is not part of this instance.");
        return scene.FindComponentById(componentId)
            ?? throw new InvalidOperationException($"Prefab instance component '{componentId}' no longer exists.");
    }

    private static void ApplyTargetProperty(object target, string propertyName, JsonElement value)
    {
        if (target is GameObject gameObject)
        {
            if (propertyName.Equals(nameof(GameObject.Name), StringComparison.OrdinalIgnoreCase))
            {
                gameObject.Name = value.GetString() ?? string.Empty;
                return;
            }
            if (propertyName.Equals(nameof(GameObject.Active), StringComparison.OrdinalIgnoreCase))
            {
                gameObject.Active = value.GetBoolean();
                return;
            }
            if (TryApplyTransformProperty(gameObject.Transform, propertyName, value))
                return;
            throw new InvalidOperationException($"GameObject property '{propertyName}' is not prefab-overridable.");
        }
        Component component = (Component)target;
        if (propertyName.Equals(nameof(Component.Enabled), StringComparison.OrdinalIgnoreCase))
        {
            component.Enabled = value.GetBoolean();
            return;
        }
        PropertyInfo property = component.GetType().GetProperty(
            propertyName,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
            ?? throw new InvalidOperationException(
                $"Component property '{component.GetType().Name}.{propertyName}' does not exist.");
        SceneSerializer.ApplyProperty(component, property, value);
    }

    private static bool TryApplyTransformProperty(
        Transform transform,
        string propertyName,
        JsonElement value)
    {
        if (propertyName.Equals("Transform.LocalPosition", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals(nameof(Transform.LocalPosition), StringComparison.OrdinalIgnoreCase))
        {
            transform.LocalPosition = JsonSerializer.Deserialize<Engine.Mathematics.Vector3>(value, SceneSerializer.JsonOptions);
            return true;
        }
        if (propertyName.Equals("Transform.LocalRotation", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals(nameof(Transform.LocalRotation), StringComparison.OrdinalIgnoreCase))
        {
            transform.LocalRotation = JsonSerializer.Deserialize<Engine.Mathematics.Quaternion>(value, SceneSerializer.JsonOptions);
            return true;
        }
        if (propertyName.Equals("Transform.LocalScale", StringComparison.OrdinalIgnoreCase)
            || propertyName.Equals(nameof(Transform.LocalScale), StringComparison.OrdinalIgnoreCase))
        {
            transform.LocalScale = JsonSerializer.Deserialize<Engine.Mathematics.Vector3>(value, SceneSerializer.JsonOptions);
            return true;
        }
        return false;
    }

    private static JsonElement ReadRuntimeValue(
        Scene scene,
        PrefabInstance instance,
        PrefabOverride prefabOverride)
    {
        object target = ResolveRuntimeTarget(scene, instance, prefabOverride);
        object? value;
        Type valueType;
        if (target is GameObject gameObject)
        {
            if (prefabOverride.Property.Equals(nameof(GameObject.Name), StringComparison.OrdinalIgnoreCase))
            {
                value = gameObject.Name;
                valueType = typeof(string);
            }
            else if (prefabOverride.Property.Equals(nameof(GameObject.Active), StringComparison.OrdinalIgnoreCase))
            {
                value = gameObject.Active;
                valueType = typeof(bool);
            }
            else if (prefabOverride.Property.EndsWith(nameof(Transform.LocalPosition), StringComparison.OrdinalIgnoreCase))
            {
                value = gameObject.Transform.LocalPosition;
                valueType = typeof(Engine.Mathematics.Vector3);
            }
            else if (prefabOverride.Property.EndsWith(nameof(Transform.LocalRotation), StringComparison.OrdinalIgnoreCase))
            {
                value = gameObject.Transform.LocalRotation;
                valueType = typeof(Engine.Mathematics.Quaternion);
            }
            else if (prefabOverride.Property.EndsWith(nameof(Transform.LocalScale), StringComparison.OrdinalIgnoreCase))
            {
                value = gameObject.Transform.LocalScale;
                valueType = typeof(Engine.Mathematics.Vector3);
            }
            else
            {
                throw new InvalidOperationException($"GameObject property '{prefabOverride.Property}' is not prefab-overridable.");
            }
        }
        else
        {
            Component component = (Component)target;
            if (prefabOverride.Property.Equals(nameof(Component.Enabled), StringComparison.OrdinalIgnoreCase))
            {
                value = component.Enabled;
                valueType = typeof(bool);
            }
            else
            {
                PropertyInfo property = component.GetType().GetProperty(
                    prefabOverride.Property,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                    ?? throw new InvalidOperationException(
                        $"Component property '{component.GetType().Name}.{prefabOverride.Property}' does not exist.");
                value = property.GetValue(component);
                valueType = property.PropertyType;
            }
        }
        return JsonSerializer.SerializeToElement(value, valueType, SceneSerializer.JsonOptions);
    }

    private static JsonElement ReadSnapshotValue(
        SceneSnapshotDocument document,
        PrefabOverride prefabOverride)
    {
        if (prefabOverride.TargetKind == PrefabOverrideTarget.GameObject)
        {
            SceneObjectSnapshot gameObject = document.Objects.FirstOrDefault(item => item.Id == prefabOverride.SourceId)
                ?? throw new InvalidOperationException($"Prefab GameObject '{prefabOverride.SourceId}' does not exist.");
            if (prefabOverride.Property.Equals(nameof(GameObject.Name), StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.SerializeToElement(gameObject.Name, SceneSerializer.JsonOptions);
            if (prefabOverride.Property.Equals(nameof(GameObject.Active), StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.SerializeToElement(gameObject.Active, SceneSerializer.JsonOptions);
            if (prefabOverride.Property.EndsWith(nameof(Transform.LocalPosition), StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.SerializeToElement(SceneSnapshot.ToVector3(gameObject.Transform.Position), SceneSerializer.JsonOptions);
            if (prefabOverride.Property.EndsWith(nameof(Transform.LocalRotation), StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.SerializeToElement(SceneSnapshot.ToQuaternion(gameObject.Transform.Rotation), SceneSerializer.JsonOptions);
            if (prefabOverride.Property.EndsWith(nameof(Transform.LocalScale), StringComparison.OrdinalIgnoreCase))
                return JsonSerializer.SerializeToElement(SceneSnapshot.ToVector3(gameObject.Transform.Scale), SceneSerializer.JsonOptions);
            throw new InvalidOperationException($"GameObject property '{prefabOverride.Property}' is not prefab-overridable.");
        }
        SceneComponentSnapshot component = document.Objects
            .SelectMany(gameObject => gameObject.Components.Concat(gameObject.UnknownComponents))
            .FirstOrDefault(item => item.Id == prefabOverride.SourceId)
            ?? throw new InvalidOperationException($"Prefab component '{prefabOverride.SourceId}' does not exist.");
        if (prefabOverride.Property.Equals(nameof(Component.Enabled), StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.SerializeToElement(component.Enabled, SceneSerializer.JsonOptions);
        if (component.Properties is null || !TryGetValue(component.Properties, prefabOverride.Property, out JsonElement value))
            throw new InvalidOperationException(
                $"Prefab component property '{component.Type}.{prefabOverride.Property}' does not exist.");
        return value.Clone();
    }

    private static void WriteSnapshotValue(
        SceneSnapshotDocument document,
        PrefabOverride prefabOverride,
        JsonElement value)
    {
        if (prefabOverride.TargetKind == PrefabOverrideTarget.GameObject)
        {
            SceneObjectSnapshot gameObject = document.Objects.FirstOrDefault(item => item.Id == prefabOverride.SourceId)
                ?? throw new InvalidOperationException($"Prefab GameObject '{prefabOverride.SourceId}' does not exist.");
            if (prefabOverride.Property.Equals(nameof(GameObject.Name), StringComparison.OrdinalIgnoreCase))
                gameObject.Name = value.GetString() ?? string.Empty;
            else if (prefabOverride.Property.Equals(nameof(GameObject.Active), StringComparison.OrdinalIgnoreCase))
                gameObject.Active = value.GetBoolean();
            else if (prefabOverride.Property.EndsWith(nameof(Transform.LocalPosition), StringComparison.OrdinalIgnoreCase))
                gameObject.Transform.Position = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
            else if (prefabOverride.Property.EndsWith(nameof(Transform.LocalRotation), StringComparison.OrdinalIgnoreCase))
                gameObject.Transform.Rotation = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
            else if (prefabOverride.Property.EndsWith(nameof(Transform.LocalScale), StringComparison.OrdinalIgnoreCase))
                gameObject.Transform.Scale = value.EnumerateArray().Select(item => item.GetSingle()).ToArray();
            else
                throw new InvalidOperationException($"GameObject property '{prefabOverride.Property}' is not prefab-overridable.");
            return;
        }
        SceneComponentSnapshot component = document.Objects
            .SelectMany(gameObject => gameObject.Components.Concat(gameObject.UnknownComponents))
            .FirstOrDefault(item => item.Id == prefabOverride.SourceId)
            ?? throw new InvalidOperationException($"Prefab component '{prefabOverride.SourceId}' does not exist.");
        if (prefabOverride.Property.Equals(nameof(Component.Enabled), StringComparison.OrdinalIgnoreCase))
        {
            component.Enabled = value.GetBoolean();
            return;
        }
        component.Properties ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        component.Properties[prefabOverride.Property] = value.Clone();
    }

    private static bool TryGetValue(
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
}

public sealed class PrefabInstance
{
    public string AssetKey { get; internal set; } = string.Empty;

    public Guid SourceRootId { get; internal set; }

    public Dictionary<Guid, Guid> EntityIdMap { get; internal set; } = [];

    public Dictionary<Guid, Guid> ComponentIdMap { get; internal set; } = [];

    public List<PrefabOverride> Overrides { get; internal set; } = [];
}

public enum PrefabOverrideTarget
{
    GameObject,
    Component,
}

public sealed class PrefabOverride
{
    public PrefabOverrideTarget TargetKind { get; set; }

    public Guid SourceId { get; set; }

    public string Property { get; set; } = string.Empty;

    public JsonElement Value { get; set; }

    public SceneReferenceKind? ReferenceKind { get; set; }

    internal PrefabOverride Clone() => new()
    {
        TargetKind = TargetKind,
        SourceId = SourceId,
        Property = Property,
        Value = Value.Clone(),
        ReferenceKind = ReferenceKind,
    };
}

public static class Prefab
{
    public static void Save(GameObject gameObject, string path)
    {
        ArgumentNullException.ThrowIfNull(gameObject);
        PrefabAsset.Capture(gameObject, path).Save(path);
    }

    public static GameObject Instantiate(Scene scene, string path, GameObject? parent = null) =>
        PrefabAsset.Load(path, path).Instantiate(scene, parent);

    public static PrefabAsset Load(string path, string? assetKey = null) =>
        PrefabAsset.Load(path, assetKey);
}
