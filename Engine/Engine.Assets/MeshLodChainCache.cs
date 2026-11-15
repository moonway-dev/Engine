using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Engine.Rendering;

namespace Engine.Assets;

public static class MeshLodChainCache
{
    private const int Version = 2;
    private const int Magic = 0x444F444C;

    public static IReadOnlyList<MeshLodData>? TryLoad(
        string sourcePath,
        string partId,
        MeshLodChainOptions options,
        string directory,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo info = new(sourcePath);
        if (!info.Exists)
            return null;
        string path = CachePath(sourcePath, partId, options, directory);
        try
        {
            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader = new(stream, Encoding.UTF8, true);
            if (reader.ReadInt32() != Magic || reader.ReadInt32() != Version
                || reader.ReadInt32() != Marshal.SizeOf<Vertex>())
                return null;
            long sourceLength = reader.ReadInt64();
            long sourceStamp = reader.ReadInt64();
            byte[] sourceHash = reader.ReadBytes(32);
            int maxLevels = reader.ReadInt32();
            float ratio = reader.ReadSingle();
            int minTriangles = reader.ReadInt32();
            long memoryBudget = reader.ReadInt64();
            long timeBudgetTicks = reader.ReadInt64();
            if (sourceLength != info.Length || sourceStamp != info.LastWriteTimeUtc.Ticks
                || sourceHash.Length != 32 || !CryptographicOperations.FixedTimeEquals(sourceHash, HashSource(sourcePath, cancellationToken))
                || maxLevels != options.MaxLevels || ratio != options.Ratio || minTriangles != options.MinTriangles
                || memoryBudget != options.MemoryBudget || timeBudgetTicks != (options.TimeBudget?.Ticks ?? -1))
                return null;
            int levelCount = reader.ReadInt32();
            if (levelCount < 0 || levelCount > options.MaxLevels)
                return null;
            MeshLodData[] levels = new MeshLodData[levelCount];
            float previousError = 0f;
            int previousTriangles = int.MaxValue;
            for (int i = 0; i < levels.Length; i++)
            {
                int vertexCount = reader.ReadInt32();
                int indexCount = reader.ReadInt32();
                float error = reader.ReadSingle();
                long bytes = checked((long)vertexCount * Marshal.SizeOf<Vertex>() + (long)indexCount * sizeof(uint));
                if (vertexCount <= 0 || indexCount < 0 || indexCount % 3 != 0 || indexCount / 3 >= previousTriangles
                    || !float.IsFinite(error) || error < previousError || bytes > stream.Length - stream.Position)
                    return null;
                Vertex[] vertices = new Vertex[vertexCount];
                uint[] indices = new uint[indexCount];
                ReadBlocks(stream, MemoryMarshal.AsBytes(vertices.AsSpan()), cancellationToken);
                ReadBlocks(stream, MemoryMarshal.AsBytes(indices.AsSpan()), cancellationToken);
                for (int j = 0; j < indices.Length; j++)
                {
                    if ((j & 65535) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (indices[j] >= vertexCount)
                        return null;
                }
                levels[i] = new MeshLodData(vertices, indices, error);
                previousError = error;
                previousTriangles = indexCount / 3;
            }
            if (stream.Position != stream.Length)
                return null;
            cancellationToken.ThrowIfCancellationRequested();
            return levels;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    public static void Save(
        string sourcePath,
        string partId,
        MeshLodChainOptions options,
        string directory,
        IReadOnlyList<MeshLodData> levels,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo info = new(sourcePath);
        if (!info.Exists || levels.Count == 0)
            return;
        string path = CachePath(sourcePath, partId, options, directory);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            byte[] sourceHash = HashSource(sourcePath, cancellationToken);
            Directory.CreateDirectory(directory);
            using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (BinaryWriter writer = new(stream, Encoding.UTF8, true))
            {
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(Marshal.SizeOf<Vertex>());
                writer.Write(info.Length);
                writer.Write(info.LastWriteTimeUtc.Ticks);
                writer.Write(sourceHash);
                writer.Write(options.MaxLevels);
                writer.Write(options.Ratio);
                writer.Write(options.MinTriangles);
                writer.Write(options.MemoryBudget);
                writer.Write(options.TimeBudget?.Ticks ?? -1);
                writer.Write(levels.Count);
                foreach (MeshLodData level in levels)
                {
                    writer.Write(level.Vertices.Length);
                    writer.Write(level.Indices.Length);
                    writer.Write(level.Error);
                    WriteBlocks(stream, MemoryMarshal.AsBytes(level.Vertices.AsSpan()), cancellationToken);
                    WriteBlocks(stream, MemoryMarshal.AsBytes(level.Indices.AsSpan()), cancellationToken);
                }
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            FileInfo current = new(sourcePath);
            if (!current.Exists || current.Length != info.Length || current.LastWriteTimeUtc.Ticks != info.LastWriteTimeUtc.Ticks
                || !CryptographicOperations.FixedTimeEquals(sourceHash, HashSource(sourcePath, cancellationToken)))
                return;
            File.Move(temporary, path, true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static byte[] HashSource(string sourcePath, CancellationToken cancellationToken)
    {
        using FileStream stream = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, read);
        }
        return hash.GetHashAndReset();
    }

    private static string CachePath(string sourcePath, string partId, MeshLodChainOptions options, string directory)
    {
        string key = string.Join('|', Path.GetFullPath(sourcePath), partId, options.SettingsKey);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(directory, hash + ".lods");
    }

    private static void ReadBlocks(Stream stream, Span<byte> bytes, CancellationToken token)
    {
        while (!bytes.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(bytes.Length, 1024 * 1024);
            stream.ReadExactly(bytes[..count]);
            bytes = bytes[count..];
        }
    }

    private static void WriteBlocks(Stream stream, ReadOnlySpan<byte> bytes, CancellationToken token)
    {
        while (!bytes.IsEmpty)
        {
            token.ThrowIfCancellationRequested();
            int count = Math.Min(bytes.Length, 1024 * 1024);
            stream.Write(bytes[..count]);
            bytes = bytes[count..];
        }
    }
}
