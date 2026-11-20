using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Scripting;

public static class ScriptScene
{
    public static Func<GameObject, AssetReference, Vector3?, Quaternion?, GameObject?>? PrefabSpawner { get; set; }

    public static Func<string, GameObject?>? Finder { get; set; }

    public static Action<GameObject>? Destroyer { get; set; }

    public static GameObject? Spawn(GameObject context, AssetReference prefab, Vector3? position = null, Quaternion? rotation = null) =>
        PrefabSpawner?.Invoke(context, prefab, position, rotation);

    public static GameObject? Spawn(GameObject context, string prefabPath, Vector3? position = null, Quaternion? rotation = null) =>
        PrefabSpawner?.Invoke(context, new AssetReference(prefabPath), position, rotation);

    public static GameObject? Find(string name) => Finder?.Invoke(name);

    public static void Destroy(GameObject gameObject) => Destroyer?.Invoke(gameObject);
}
