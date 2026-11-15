namespace Engine.Assets;

public static class ModelImporter
{
    public static ModelData LoadModel(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".obj" => ObjModelLoader.LoadModel(path, cancellationToken),
            ".fbx" => FbxModelLoader.LoadModel(path, cancellationToken),
            string extension => throw new NotSupportedException($"Model format '{extension}' is not supported."),
        };
    }
}
