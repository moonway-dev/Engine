using System.Security.Cryptography;
using Engine.Core;

namespace Engine.Editor.Build;

public static class AssetPackWriter
{
    private const uint ManifestMagic = 0x4D474E45;
    private const uint ChunkMagic = 0x50474E45;
    private const int ManifestVersion = 1;
    private const int ChunkHeaderBytes = 8;

    public sealed record PackResult(int EntryCount, int ChunkCount, long TotalBytes, string ManifestPath);

    public static PackResult Write(
        string dataDirectory,
        IReadOnlyDictionary<string, byte[]> entries,
        byte[]? encryptionKey,
        Action<string>? log = null)
    {
        Directory.CreateDirectory(dataDirectory);
        string packsDirectory = Path.Combine(dataDirectory, "packs");
        Directory.CreateDirectory(packsDirectory);
        byte[]? key = encryptionKey;
        if (key is not null && key.Length != PackCrypto.KeySizeBytes)
            throw new ArgumentException($"Encryption key must be {PackCrypto.KeySizeBytes} bytes.", nameof(encryptionKey));
        bool encrypted = key is not null;

        List<string> paths = [.. entries.Keys.OrderBy(value => value, StringComparer.Ordinal)];
        Dictionary<string, List<string>> chunkAssignment = [];
        foreach (string path in paths)
        {
            string chunk = ChunkName(path);
            if (!chunkAssignment.TryGetValue(chunk, out List<string> list))
                chunkAssignment[chunk] = list = [];
            list.Add(path);
        }

        long totalBytes = 0;
        List<(string Name, long Size, byte[] Sha256)> chunks = [];
        Dictionary<string, (int ChunkIndex, long Offset, int Length, byte[] Nonce)> written = new(StringComparer.OrdinalIgnoreCase);
        int chunkIndex = 0;
        foreach ((string chunkName, List<string> chunkPaths) in chunkAssignment.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            string chunkPath = Path.Combine(dataDirectory, chunkName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(chunkPath)!);
            long chunkSize;
            using (FileStream stream = File.Create(chunkPath))
            {
                using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: false);
                writer.Write(ChunkMagic);
                writer.Write(ManifestVersion);
                foreach (string path in chunkPaths)
                {
                    byte[] payload = entries[path];
                    byte[] nonce = RandomNumberGenerator.GetBytes(PackCrypto.NonceSizeBytes);
                    byte[] blob = key is not null ? PackCrypto.Encrypt(payload, key, nonce) : payload;
                    long offset = stream.Position;
                    writer.Write(blob);
                    written[path] = (chunkIndex, offset, blob.Length, nonce);
                    totalBytes += blob.Length;
                }
                chunkSize = stream.Length;
                stream.Flush(true);
            }
            byte[] hash = SHA256.HashData(File.ReadAllBytes(chunkPath));
            chunks.Add((chunkName, chunkSize, hash));
            chunkIndex++;
        }

        string manifestPath = Path.Combine(dataDirectory, "manifest.bin");
        using (FileStream manifestStream = File.Create(manifestPath))
        {
            using BinaryWriter writer = new(manifestStream, System.Text.Encoding.UTF8, leaveOpen: false);
            writer.Write(ManifestMagic);
            writer.Write(ManifestVersion);
            writer.Write(encrypted ? 1 : 0);
            writer.Write(chunks.Count);
            foreach ((string name, long size, byte[] hash) in chunks)
            {
                writer.Write(name);
                writer.Write(size);
                writer.Write(hash);
            }
            writer.Write(written.Count);
            foreach (string path in paths)
            {
                (int index, long offset, int length, byte[] nonce) = written[path];
                writer.Write(path);
                writer.Write(index);
                writer.Write(offset);
                writer.Write(length);
                writer.Write(nonce);
            }
        }
        log?.Invoke($"pack: {written.Count} entries → {chunks.Count} chunk file(s), {totalBytes / 1024.0:F0} KB, encryption: {(encrypted ? "AES-256-GCM" : "off")}");
        return new PackResult(written.Count, chunks.Count, totalBytes, manifestPath);
    }

    public static string ChunkName(string entryPath)
    {
        string normalized = entryPath.Replace('\\', '/').TrimStart('/');
        string directory = Path.GetDirectoryName(normalized)?.Replace('\\', '/') ?? string.Empty;
        string fileName = Path.GetFileName(normalized);
        return string.IsNullOrEmpty(directory) || directory == "."
            ? $"packs/{fileName}.pack"
            : $"packs/{directory}/{fileName}.pack";
    }
}
