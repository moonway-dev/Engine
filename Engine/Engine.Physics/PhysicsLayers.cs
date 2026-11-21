namespace Engine.Physics;

public static class PhysicsLayers
{
    public const int MaxLayers = 32;
    private static readonly string[] DefaultNames = ["Default"];
    private static string[] _names = DefaultNames;

    public static IReadOnlyList<string> Names => _names;

    public static void SetNames(IReadOnlyList<string>? names)
    {
        if (names is null || names.Count == 0)
        {
            _names = DefaultNames;
            return;
        }
        string[] cleaned = names
            .Select(value => string.IsNullOrWhiteSpace(value) ? "Layer" : value.Trim())
            .Take(MaxLayers)
            .ToArray();
        if (cleaned.Length == 0)
        {
            _names = DefaultNames;
            return;
        }
        cleaned[0] = string.IsNullOrWhiteSpace(cleaned[0]) ? "Default" : cleaned[0];
        _names = cleaned;
    }
}
