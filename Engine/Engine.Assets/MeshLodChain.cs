using System.Globalization;
using Engine.Rendering;

namespace Engine.Assets;

public sealed record MeshLodChainOptions(
    int MaxLevels = 4,
    float Ratio = 0.5f,
    int MinTriangles = 384,
    long MemoryBudget = 1536L * 1024 * 1024,
    TimeSpan? TimeBudget = null)
{
    public string SettingsKey => string.Create(CultureInfo.InvariantCulture, $"{MaxLevels}|{Ratio:R}|{MinTriangles}|{MemoryBudget}|{TimeBudget?.Ticks ?? -1}");
}

public static class MeshLodChainGenerator
{
    public static IReadOnlyList<MeshLodData> Generate(
        Vertex[] vertices,
        uint[] indices,
        MeshLodChainOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxLevels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MemoryBudget);
        if (!(options.Ratio > 0f && options.Ratio < 1f))
            throw new ArgumentOutOfRangeException(nameof(options.Ratio));
        if (options.TimeBudget is { } timeBudget && timeBudget < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options.TimeBudget));
        int floor = Math.Max(options.MinTriangles, 4);
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        List<MeshLodData> levels = [];
        Vertex[] levelVertices = vertices;
        uint[] levelIndices = indices;
        float error = 0f;
        for (int level = 0; level < options.MaxLevels; level++)
        {
            int triangles = levelIndices.Length / 3;
            if (triangles <= floor)
                break;
            float ratio = MathF.Max(options.Ratio, (float)floor / triangles);
            TimeSpan? remaining = null;
            if (options.TimeBudget is { } budget)
            {
                TimeSpan available = budget - System.Diagnostics.Stopwatch.GetElapsedTime(started);
                if (available <= TimeSpan.Zero)
                    throw new MeshLodGenerationTimeoutException();
                remaining = available;
            }
            MeshLodData data = MeshSimplifier.Simplify(
                levelVertices,
                levelIndices,
                ratio,
                options.MemoryBudget,
                cancellationToken,
                remaining);
            if (data.Indices.Length / 3 >= triangles)
                break;
            error += data.Error;
            levels.Add(data with { Error = error });
            levelVertices = data.Vertices;
            levelIndices = data.Indices;
        }
        return levels;
    }
}
