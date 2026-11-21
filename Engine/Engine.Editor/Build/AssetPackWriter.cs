using System.Security.Cryptography;
using System.Text;
using Engine.Core;

namespace Engine.Editor.Build;

public static class AssetPackWriter
{
    private const uint ManifestMagic = 0x4D474E45;
    private const uint ChunkMagic = 0x50474E45;
    private const int ManifestVersion = 1;
    private const int ChunkHeaderBytes = 8;
    private const int ChunkNameHexChars = 20;

    public sealed record PackResult(int EntryCount, int ChunkCount, long TotalBytes, string ManifestPath);

    public static PackResult Write(
        string dataDirectory,
        IReadOnlyDictionary<string, byte[]> entries,
        byte[]? encryptionKey,
        Action<string>? log = null)
    {
        Directory.CreateDirectory(dataDirectory);
        byte[]? key = encryptionKey;
        if (key is not null && key.Length != PackCrypto.KeySizeBytes)
            throw new ArgumentException($"Encryption key must be {PackCrypto.KeySizeBytes} bytes.", nameof(encryptionKey));
        bool encrypted = key is not null;

        List<string> paths = [.. entries.Keys.OrderBy(value => value, StringComparer.Ordinal)];
        long totalBytes = 0;
        List<(string Name, long Size, byte[] Sha256)> chunks = [];
        Dictionary<string, (int ChunkIndex, long Offset, int Length, byte[] Nonce)> written = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            byte[] payload = entries[path];
            byte[] nonce = DeterministicNonce(path, payload);
            byte[] blob = key is not null ? PackCrypto.Encrypt(payload, key, nonce) : payload;
            byte[] fileBytes = BuildChunkFile(blob);
            string name = ChunkName(path, payload);
            string chunkPath = Path.Combine(dataDirectory, name);
            File.WriteAllBytes(chunkPath, fileBytes);
            int chunkIndex = chunks.FindIndex(chunk => chunk.Name == name);
            if (chunkIndex < 0)
            {
                chunks.Add((name, fileBytes.LongLength, SHA256.HashData(fileBytes)));
                chunkIndex = chunks.Count - 1;
                totalBytes += fileBytes.LongLength;
            }
            written[path] = (chunkIndex, ChunkHeaderBytes, blob.Length, nonce);
        }

        string manifestPath = Path.Combine(dataDirectory, "manifest.bin");
        using (FileStream manifestStream = File.Create(manifestPath))
        {
            using BinaryWriter writer = new(manifestStream, Encoding.UTF8, leaveOpen: false);
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

    private static byte[] BuildChunkFile(byte[] blob)
    {
        using MemoryStream buffer = new(ChunkHeaderBytes + blob.Length);
        using (BinaryWriter writer = new(buffer, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(ChunkMagic);
            writer.Write(ManifestVersion);
        }
        buffer.Write(blob, 0, blob.Length);
        return buffer.ToArray();
    }

    public static string ChunkName(string entryPath, ReadOnlySpan<byte> payload)
    {
        string normalized = entryPath.Replace('\\', '/').TrimStart('/');
        byte[] pathBytes = Encoding.UTF8.GetBytes(normalized);
        byte[] combined = new byte[pathBytes.Length + payload.Length];
        pathBytes.AsSpan().CopyTo(combined);
        payload.CopyTo(combined.AsSpan(pathBytes.Length));
        return Convert.ToHexString(SHA256.HashData(combined), 0, ChunkNameHexChars).ToLowerInvariant() + ".pack";
    }

    private static byte[] DeterministicNonce(string entryPath, ReadOnlySpan<byte> payload)
    {
        string normalized = entryPath.Replace('\\', '/').TrimStart('/');
        byte[] pathBytes = Encoding.UTF8.GetBytes(normalized);
        byte[] combined = new byte[pathBytes.Length + payload.Length];
        pathBytes.AsSpan().CopyTo(combined);
        payload.CopyTo(combined.AsSpan(pathBytes.Length));
        byte[] hash = SHA256.HashData(combined);
        byte[] nonce = new byte[PackCrypto.NonceSizeBytes];
        hash.AsSpan(0, PackCrypto.NonceSizeBytes).CopyTo(nonce);
        return nonce;
    }
}
