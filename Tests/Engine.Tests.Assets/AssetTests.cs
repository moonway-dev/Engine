using Engine.Assets;
using Engine.Rendering;
using Xunit;

namespace Engine.Tests.Assets;

public class ObjModelLoaderTests
{
    [Fact]
    public void Load_Cube_ProducesVerticesAndTriangles()
    {
        string path = WriteTemp("""
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            vt 0 0
            vt 1 0
            vt 1 1
            vt 0 1
            vn 0 0 -1
            f 1/1/1 2/2/1 3/3/1
            f 1/1/1 3/3/1 4/4/1
            """);
        (Vertex[] vertices, uint[] indices) = ObjModelLoader.Load(path);
        Assert.Equal(4, vertices.Length);
        Assert.Equal(6, indices.Length);
        Assert.Equal(0u, indices[0]);
        Assert.Equal(1u, indices[1]);
        Assert.Equal(2u, indices[2]);
        Assert.Equal(1f, vertices[1].Position.X);
        Assert.Equal(1f, vertices[2].UV.X);
        Assert.Equal(-1f, vertices[0].Normal.Z);
    }

    [Fact]
    public void Load_QuadInTwoTriangles_SplitsFan()
    {
        string path = WriteTemp("""
            v 0 0 0
            v 1 0 0
            v 1 1 0
            v 0 1 0
            f 1 2 3 4
            """);
        (Vertex[] vertices, uint[] indices) = ObjModelLoader.Load(path);
        Assert.Equal(4, vertices.Length);
        Assert.Equal(6, indices.Length);
        Assert.Equal([0u, 1u, 2u, 0u, 2u, 3u], indices);
    }

    [Fact]
    public void Load_NoNormals_GeneratesSmoothNormals()
    {
        string path = WriteTemp("""
            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);
        (Vertex[] vertices, _) = ObjModelLoader.Load(path);
        float length = vertices[0].Normal.Length;
        Assert.Equal(1f, length, 4);
    }

    [Fact]
    public void Load_CommentsAndEmptyLines_AreIgnored()
    {
        string path = WriteTemp("""
            # header comment

            v 0 0 0
            v 1 0 0
            v 0 1 0
            f 1 2 3
            """);
        (Vertex[] vertices, uint[] indices) = ObjModelLoader.Load(path);
        Assert.Equal(3, vertices.Length);
        Assert.Equal(3, indices.Length);
    }

    [Fact]
    public void Load_TabsNegativeIndicesAndInlineComment_PreserveFan()
    {
        string path = WriteTemp("v\t0 0 0\nv 1 0 0\nv 1 1 0\nv 0 1 0\nf\t-4 -3 -2 -1 # quad");
        try
        {
            var data = ObjModelLoader.Load(path);
            Assert.Equal(4, data.Vertices.Length);
            Assert.Equal(new uint[] { 0, 1, 2, 0, 2, 3 }, data.Indices);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("0")]
    [InlineData("4")]
    [InlineData("-4")]
    public void Load_InvalidIndex_IsRejected(string index)
    {
        string path = WriteTemp($"v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 {index}");
        try
        {
            Assert.Throws<InvalidDataException>(() => ObjModelLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CancelledImport_Stops()
    {
        string path = WriteTemp("v 0 0 0");
        try
        {
            Assert.Throws<OperationCanceledException>(() => ObjModelLoader.Load(path, new CancellationToken(true)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LoadModel_RepeatedGroups_MergeRangesWithoutLosingTriangles()
    {
        string path = WriteTemp("v 0 0 0\nv 1 0 0\nv 0 1 0\nv 0 0 1\ng First\nf 1 2 3\ng Second\nf 1 3 4\ng First\nf 1 4 2");
        try
        {
            ModelData model = ObjModelLoader.LoadModel(path);
            Assert.Equal(2, model.Parts.Length);
            Assert.Equal("First", model.Parts[0].Name);
            Assert.Equal(6, model.Parts[0].IndexCount);
            Assert.Equal(6, model.Parts[1].FirstIndex);
            Assert.Equal(9, model.Indices.Length);
            var part = model.ExtractPart("Second");
            Assert.Equal(3, part.Vertices.Length);
            Assert.Equal(new uint[] { 0, 1, 2 }, part.Indices);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ModelCache_WarmReadAndCorruptionAndSourceChange_AreHandled()
    {
        string path = WriteTemp("v 0 0 0\nv 1 0 0\nv 0 1 0\ng Triangle\nf 1 2 3");
        string directory = Path.Combine(Path.GetTempPath(), "EngineModelCacheTests", Guid.NewGuid().ToString("N"));
        try
        {
            ModelData cold = ModelCache.Load(path, directory, out bool first);
            ModelData warm = ModelCache.Load(path, directory, out bool second);
            Assert.False(first);
            Assert.True(second);
            Assert.Equal(cold.Indices, warm.Indices);
            Assert.Equal(cold.Parts, warm.Parts);
            Assert.Equal(cold.Vertices[1].Position, warm.Vertices[1].Position);
            string cache = Directory.GetFiles(directory, "*.model").Single();
            File.WriteAllBytes(cache, [1, 2, 3]);
            ModelCache.Load(path, directory, out bool damaged);
            Assert.False(damaged);
            ModelCache.Load(path, directory, out bool repaired);
            Assert.True(repaired);
            File.SetLastWriteTimeUtc(path, File.GetLastWriteTimeUtc(path).AddSeconds(5));
            ModelCache.Load(path, directory, out bool touched);
            Assert.True(touched);
            File.AppendAllText(path, "\n# changed");
            ModelCache.Load(path, directory, out bool changed);
            Assert.False(changed);
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(directory))
                Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Load_MixedNormals_GeneratesOnlyMissingNormals()
    {
        string path = WriteTemp("v 0 0 0\nv 1 0 0\nv 0 1 0\nvn 0 0 -1\nf 1//1 2 3");
        try
        {
            var model = ObjModelLoader.Load(path);
            Assert.Equal(-1f, model.Vertices[0].Normal.Z);
            Assert.Equal(1f, model.Vertices[1].Normal.Z);
            Assert.Equal(1f, model.Vertices[2].Normal.Z);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    public void Load_NonFinitePosition_IsRejected(string value)
    {
        string path = WriteTemp($"v {value} 0 0");
        try
        {
            Assert.Throws<InvalidDataException>(() => ObjModelLoader.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WriteTemp(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), "EngineAssetTests", $"{Guid.NewGuid():N}.obj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content.Replace("\r\n", "\n"));
        return path;
    }
}

public class ImageLoaderTests
{
    [Fact]
    public void Load_PngBytes_ReturnsRgbaPixels()
    {
        ImageData image = ImageLoader.Load(CreatePng(2, 2));
        Assert.Equal(2, image.Width);
        Assert.Equal(2, image.Height);
        Assert.Equal(4, image.Channels);
        Assert.Equal(image.Width * image.Height * 4, image.Pixels.Length);
    }

    internal static byte[] CreatePng(int width, int height)
    {
        byte[] raw = new byte[height * (1 + width * 4)];
        for (int y = 0; y < height; y++)
        {
            int rowStart = y * (1 + width * 4);
            raw[rowStart] = 0;
            for (int x = 0; x < width; x++)
            {
                int pixelStart = rowStart + 1 + x * 4;
                raw[pixelStart] = (byte)(x * 100);
                raw[pixelStart + 1] = (byte)(y * 100);
                raw[pixelStart + 2] = 64;
                raw[pixelStart + 3] = 255;
            }
        }
        byte[] compressed = Compress(raw);
        using MemoryStream stream = new();
        stream.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        WriteChunk(stream, "IHDR", [.. BitConverter.GetBytes(width).Reverse(), .. BitConverter.GetBytes(height).Reverse(), 8, 6, 0, 0, 0]);
        WriteChunk(stream, "IDAT", compressed);
        WriteChunk(stream, "IEND", []);
        return stream.ToArray();
    }

    private static byte[] Compress(byte[] data)
    {
        using MemoryStream output = new();
        using (System.IO.Compression.ZLibStream zlib = new(output, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string tag, byte[] data)
    {
        byte[] tagBytes = System.Text.Encoding.ASCII.GetBytes(tag);
        stream.Write(BitConverter.GetBytes(data.Length).Reverse().ToArray());
        stream.Write(tagBytes);
        stream.Write(data);
        uint crc = Crc32([.. tagBytes, .. data]);
        stream.Write(BitConverter.GetBytes(crc).Reverse().ToArray());
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320 & (~(crc & 1) + 1));
        }
        return crc ^ 0xFFFFFFFF;
    }
}
