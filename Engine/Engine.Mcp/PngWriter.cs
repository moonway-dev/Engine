using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Engine.Mcp;

public static class PngWriter
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static void Save(string path, int width, int height, ReadOnlySpan<byte> bgraPixels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "PNG dimensions must be positive.");
        if (bgraPixels.Length < width * height * 4)
            throw new ArgumentException("Pixel buffer is smaller than the requested image.", nameof(bgraPixels));
        byte[] rgba = new byte[height * (1 + width * 4)];
        for (int row = 0; row < height; row++)
        {
            int scanline = row * (1 + width * 4);
            rgba[scanline] = 0;
            ReadOnlySpan<byte> source = bgraPixels.Slice(row * width * 4, width * 4);
            for (int index = 0; index < width; index++)
            {
                int target = scanline + 1 + index * 4;
                rgba[target] = source[index * 4 + 2];
                rgba[target + 1] = source[index * 4 + 1];
                rgba[target + 2] = source[index * 4];
                rgba[target + 3] = source[index * 4 + 3];
            }
        }
        using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Span<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];
        stream.Write(signature);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(stream, 0x49484452, header);
        using MemoryStream compressed = new();
        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(rgba);
        compressed.Position = 0;
        byte[] idat = new byte[compressed.Length];
        compressed.ReadExactly(idat);
        WriteChunk(stream, 0x49444154, idat);
        WriteChunk(stream, 0x49454E44, []);
    }

    private static void WriteChunk(FileStream stream, uint type, ReadOnlySpan<byte> data)
    {
        Span<byte> prefix = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(prefix, (uint)data.Length);
        BinaryPrimitives.WriteUInt32BigEndian(prefix.Slice(4), type);
        stream.Write(prefix);
        stream.Write(data);
        Span<byte> footer = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(footer, Crc(type, data));
        stream.Write(footer);
    }

    private static uint Crc(uint type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        crc = CrcStep(crc, (byte)(type >> 24));
        crc = CrcStep(crc, (byte)(type >> 16));
        crc = CrcStep(crc, (byte)(type >> 8));
        crc = CrcStep(crc, (byte)type);
        foreach (byte value in data)
            crc = CrcStep(crc, value);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint CrcStep(uint crc, byte value) => CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
                value = (value & 1) != 0 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            table[index] = value;
        }
        return table;
    }
}
