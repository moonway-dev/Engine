using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Engine.Rendering;

namespace Engine.Assets;

public static class ModelCache
{
    private const int Version = 4;
    private const int Magic = 0x454D444C;

    public static ModelData Load(string source, string directory, out bool cacheHit, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileInfo info = new(source);
        long length = info.Length;
        long stamp = info.LastWriteTimeUtc.Ticks;
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(source))));
        string path = Path.Combine(directory, key + ".model");
        byte[]? sourceHash = null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            using BinaryReader reader = new(stream, Encoding.UTF8, true);
            if (reader.ReadInt32() == Magic && reader.ReadInt32() == Version
                && reader.ReadInt32() == Marshal.SizeOf<Vertex>())
            {
                long cachedLength = reader.ReadInt64();
                long cachedStamp = reader.ReadInt64();
                byte[] cachedHash = reader.ReadBytes(32);
                if (cachedHash.Length != 32)
                    throw new EndOfStreamException();
                if (cachedLength != length || cachedStamp != stamp)
                {
                    sourceHash = HashSource(source, cancellationToken);
                    if (!sourceHash.AsSpan().SequenceEqual(cachedHash))
                        throw new InvalidDataException("Model source changed.");
                }
                int vertexCount = reader.ReadInt32();
                int indexCount = reader.ReadInt32();
                int partCount = reader.ReadInt32();
                long bytes = checked((long)vertexCount * Marshal.SizeOf<Vertex>() + (long)indexCount * sizeof(uint));
                if (vertexCount < 0 || indexCount < 0 || indexCount % 3 != 0 || partCount < 0
                    || partCount > 65536 || bytes > stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid model cache dimensions.");
                ModelPart[] parts = new ModelPart[partCount];
                for (int i = 0; i < parts.Length; i++)
                {
                    string id = ReadName(reader);
                    string name = ReadName(reader);
                    int first = reader.ReadInt32();
                    int count = reader.ReadInt32();
                    if (first < 0 || count < 0 || first % 3 != 0 || count % 3 != 0 || (long)first + count > indexCount)
                        throw new InvalidDataException("Invalid model cache part.");
                    parts[i] = new ModelPart(id, name, first, count);
                }
                if (bytes != stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid model cache length.");
                cancellationToken.ThrowIfCancellationRequested();
                Vertex[] vertices = new Vertex[vertexCount];
                uint[] indices = new uint[indexCount];
                ReadBlocks(stream, MemoryMarshal.AsBytes(vertices.AsSpan()), cancellationToken);
                ReadBlocks(stream, MemoryMarshal.AsBytes(indices.AsSpan()), cancellationToken);
                for (int i = 0; i < indices.Length; i++)
                {
                    if ((i & 65535) == 0)
                        cancellationToken.ThrowIfCancellationRequested();
                    if (indices[i] >= vertexCount)
                        throw new InvalidDataException("Invalid model cache index.");
                }
                cacheHit = true;
                return new ModelData(vertices, indices, parts);
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
        }
        sourceHash ??= HashSource(source, cancellationToken);
        ModelData model = ModelImporter.LoadModel(source, cancellationToken);
        info.Refresh();
        if (info.Length == length && info.LastWriteTimeUtc.Ticks == stamp)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(directory);
                using (FileStream stream = new(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (BinaryWriter writer = new(stream, Encoding.UTF8, true))
                {
                    writer.Write(Magic);
                    writer.Write(Version);
                    writer.Write(Marshal.SizeOf<Vertex>());
                    writer.Write(length);
                    writer.Write(stamp);
                    writer.Write(sourceHash);
                    writer.Write(model.Vertices.Length);
                    writer.Write(model.Indices.Length);
                    writer.Write(model.Parts.Length);
                    foreach (ModelPart part in model.Parts)
                    {
                        WriteName(writer, part.Id);
                        WriteName(writer, part.Name);
                        writer.Write(part.FirstIndex);
                        writer.Write(part.IndexCount);
                    }
                    WriteBlocks(stream, MemoryMarshal.AsBytes(model.Vertices.AsSpan()), cancellationToken);
                    WriteBlocks(stream, MemoryMarshal.AsBytes(model.Indices.AsSpan()), cancellationToken);
                    stream.Flush(true);
                }
                cancellationToken.ThrowIfCancellationRequested();
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
        cacheHit = false;
        return model;
    }

    private static byte[] HashSource(string path, CancellationToken token)
    {
        using FileStream stream = File.OpenRead(path);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
        }
        return hash.GetHashAndReset();
    }

    private static string ReadName(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        if (length < 0 || length > 65536 || length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new InvalidDataException("Invalid model cache name.");
        byte[] bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteName(BinaryWriter writer, string name)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        writer.Write(bytes.Length);
        writer.Write(bytes);
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
