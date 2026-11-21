namespace Engine.Core;

public interface IVirtualFileSource
{
    bool Exists(string key);

    Stream OpenRead(string key);

    IReadOnlyList<string> ListFiles();
}

public static class VirtualFileSystem
{
    private static readonly List<(string Root, IVirtualFileSource Source)> Sources = [];
    private static readonly Lock Gate = new();

    public static bool IsMounted
    {
        get
        {
            lock (Gate)
                return Sources.Count > 0;
        }
    }

    public static void Mount(string root, IVirtualFileSource source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(source);
        string normalized = NormalizeRoot(root);
        lock (Gate)
        {
            if (Sources.Any(entry => entry.Root.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A virtual file source is already mounted at '{normalized}'.");
            Sources.Add((normalized, source));
        }
    }

    public static void UnmountAll()
    {
        lock (Gate)
            Sources.Clear();
    }

    public static bool Exists(string path)
    {
        if (TryMap(path, out IVirtualFileSource? source, out string key))
            return source.Exists(key);
        if (IsMounted)
            return false;
        return File.Exists(path);
    }

    public static (long Length, long LastWriteTicks)? TryGetStamp(string path)
    {
        if (TryMap(path, out IVirtualFileSource? source, out string key))
        {
            if (!source.Exists(key))
                return null;
            using Stream stream = source.OpenRead(key);
            return (stream.Length, 0);
        }
        if (IsMounted)
            return null;
        FileInfo info = new(path);
        return info.Exists ? (info.Length, info.LastWriteTimeUtc.Ticks) : null;
    }

    public static Stream? TryOpenRead(string path)
    {
        if (TryMap(path, out IVirtualFileSource? source, out string key))
            return source.Exists(key) ? source.OpenRead(key) : null;
        if (IsMounted)
            return null;
        return File.Exists(path) ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read) : null;
    }

    public static Stream OpenRead(string path) =>
        TryOpenRead(path) ?? throw new FileNotFoundException("Virtual file not found.", path);

    public static byte[] ReadAllBytes(string path) =>
        TryReadAllBytes(path) ?? throw new FileNotFoundException("Virtual file not found.", path);

    public static byte[]? TryReadAllBytes(string path)
    {
        using Stream? stream = TryOpenRead(path);
        if (stream is null)
            return null;
        using MemoryStream memory = new();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static string ReadAllText(string path) =>
        TryReadAllText(path) ?? throw new FileNotFoundException("Virtual file not found.", path);

    public static string? TryReadAllText(string path)
    {
        byte[]? bytes = TryReadAllBytes(path);
        return bytes is null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    public static IReadOnlyList<string> EnumerateFiles()
    {
        lock (Gate)
        {
            List<string> keys = [];
            foreach ((string root, IVirtualFileSource source) in Sources)
            {
                foreach (string key in source.ListFiles())
                    keys.Add(Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar)));
            }
            return keys;
        }
    }

    private static bool TryMap(string path, out IVirtualFileSource source, out string key)
    {
        source = null!;
        key = string.Empty;
        lock (Gate)
        {
            if (Sources.Count == 0)
                return false;
        }
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
        lock (Gate)
        {
            foreach ((string root, IVirtualFileSource candidate) in Sources)
            {
                if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    continue;
                string remainder = full[root.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (remainder.Length == 0)
                    continue;
                source = candidate;
                key = remainder.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                return true;
            }
        }
        return false;
    }

    private static string NormalizeRoot(string root)
    {
        string full = Path.GetFullPath(root);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }
}

public static class PackCrypto
{
    public const int KeySizeBytes = 32;
    public const int NonceSizeBytes = 12;
    public const int TagSizeBytes = 16;

    public static byte[] Encrypt(byte[] plaintext, byte[] key, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Pack key must be {KeySizeBytes} bytes.", nameof(key));
        if (nonce.Length != NonceSizeBytes)
            throw new ArgumentException($"Pack nonce must be {NonceSizeBytes} bytes.", nameof(nonce));
        byte[] ciphertext = new byte[plaintext.Length + TagSizeBytes];
        using System.Security.Cryptography.AesGcm aes = new(key, TagSizeBytes);
        aes.Encrypt(nonce, plaintext, ciphertext.AsSpan(0, plaintext.Length), ciphertext.AsSpan(plaintext.Length));
        return ciphertext;
    }

    public static byte[] Decrypt(byte[] ciphertextWithTag, byte[] key, byte[] nonce)
    {
        if (key.Length != KeySizeBytes)
            throw new ArgumentException($"Pack key must be {KeySizeBytes} bytes.", nameof(key));
        if (nonce.Length != NonceSizeBytes)
            throw new ArgumentException($"Pack nonce must be {NonceSizeBytes} bytes.", nameof(nonce));
        if (ciphertextWithTag.Length < TagSizeBytes)
            throw new InvalidDataException("Pack entry payload is truncated.");
        byte[] plaintext = new byte[ciphertextWithTag.Length - TagSizeBytes];
        using System.Security.Cryptography.AesGcm aes = new(key, TagSizeBytes);
        aes.Decrypt(nonce, ciphertextWithTag.AsSpan(0, plaintext.Length), ciphertextWithTag.AsSpan(plaintext.Length), plaintext);
        return plaintext;
    }

    public static byte[] ParseKeyHex(string hex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hex);
        byte[] key = Convert.FromHexString(hex.Trim());
        if (key.Length != KeySizeBytes)
            throw new InvalidDataException($"Pack key must be {KeySizeBytes} bytes.");
        return key;
    }
}

public sealed class AssetPackSet : IVirtualFileSource, IDisposable
{
    private const uint ManifestMagic = 0x4D474E45;
    private const uint ChunkMagic = 0x50474E45;
    private const int ManifestVersion = 1;

    private readonly Dictionary<string, (int ChunkIndex, long Offset, int Length, byte[] Nonce)> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ChunkFile> _chunks = [];

    private AssetPackSet()
    {
    }

    public static AssetPackSet Load(string manifestPath, byte[]? decryptionKey = null)
    {
        string rootDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        AssetPackSet set = new();
        using FileStream stream = new(manifestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: false);
        if (reader.ReadUInt32() != ManifestMagic)
            throw new InvalidDataException($"'{manifestPath}' is not an engine pack manifest.");
        int version = reader.ReadInt32();
        if (version != ManifestVersion)
            throw new InvalidDataException($"Unsupported pack manifest version {version} in '{manifestPath}'.");
        int flags = reader.ReadInt32();
        bool encrypted = (flags & 1) != 0;
        if (encrypted && decryptionKey is null)
            throw new InvalidDataException("The build is encrypted but no pack key is available.");
        int chunkCount = reader.ReadInt32();
        for (int i = 0; i < chunkCount; i++)
        {
            string name = reader.ReadString();
            long size = reader.ReadInt64();
            byte[] hash = reader.ReadBytes(32);
            set._chunks.Add(new ChunkFile(Path.Combine(rootDirectory, name.Replace('/', Path.DirectorySeparatorChar)), size, hash));
        }
        int entryCount = reader.ReadInt32();
        for (int i = 0; i < entryCount; i++)
        {
            string path = reader.ReadString();
            int chunkIndex = reader.ReadInt32();
            long offset = reader.ReadInt64();
            int length = reader.ReadInt32();
            byte[] nonce = reader.ReadBytes(PackCrypto.NonceSizeBytes);
            set._entries[path] = (chunkIndex, offset, length, nonce);
        }
        set.Encrypted = encrypted;
        set.DecryptionKey = decryptionKey;
        set.ManifestPath = manifestPath;
        return set;
    }

    public bool Encrypted { get; private set; }

    public byte[]? DecryptionKey { get; private set; }

    public string ManifestPath { get; private set; } = string.Empty;

    public bool Exists(string key) => _entries.ContainsKey(key);

    public Stream OpenRead(string key)
    {
        if (!_entries.TryGetValue(key, out (int ChunkIndex, long Offset, int Length, byte[] Nonce) entry))
            throw new FileNotFoundException("Pack entry not found.", key);
        ChunkFile chunk = _chunks[entry.ChunkIndex];
        byte[] payload = ReadChunkBytes(chunk, entry.Offset, entry.Length);
        if (Encrypted)
            payload = PackCrypto.Decrypt(payload, DecryptionKey!, entry.Nonce);
        return new MemoryStream(payload, writable: false);
    }

    private static byte[] ReadChunkBytes(ChunkFile chunk, long offset, int length)
    {
        using FileStream stream = new(chunk.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < chunk.Size)
            throw new InvalidDataException($"Pack chunk '{chunk.Path}' is truncated.");
        stream.Seek(offset, SeekOrigin.Begin);
        byte[] buffer = new byte[length];
        int read = 0;
        while (read < length)
        {
            int count = stream.Read(buffer, read, length - read);
            if (count == 0)
                throw new InvalidDataException($"Pack chunk '{chunk.Path}' ended early.");
            read += count;
        }
        return buffer;
    }

    public IReadOnlyList<string> ListFiles() => [.. _entries.Keys.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)];

    public IReadOnlyList<ChunkFile> Chunks => _chunks;

    public void Dispose()
    {
    }

    public sealed record ChunkFile(string Path, long Size, byte[] Sha256);

    public static bool IsManifest(string path)
    {
        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using BinaryReader reader = new(stream, System.Text.Encoding.UTF8, leaveOpen: false);
            return reader.ReadUInt32() == ManifestMagic;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
