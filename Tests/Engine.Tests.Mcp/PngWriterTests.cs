using System.IO;
using Engine.Mcp;
using StbImageSharp;
using Xunit;

namespace Engine.Tests.Mcp;

public sealed class PngWriterTests
{
    [Fact]
    public void SavesDecodablePngWithBgraToRgbaConversion()
    {
        int width = 5;
        int height = 3;
        byte[] bgra = new byte[width * height * 4];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            bgra[pixel * 4] = (byte)pixel;
            bgra[pixel * 4 + 1] = 64;
            bgra[pixel * 4 + 2] = 128;
            bgra[pixel * 4 + 3] = 255;
        }
        string path = Path.Combine(Path.GetTempPath(), $"mcp-png-{Guid.NewGuid():N}.png");
        try
        {
            PngWriter.Save(path, width, height, bgra);
            byte[] file = File.ReadAllBytes(path);
            Assert.Equal(0x89, file[0]);
            Assert.Equal((byte)'P', file[1]);
            ImageResult image = ImageResult.FromMemory(file, ColorComponents.RedGreenBlueAlpha);
            Assert.Equal(width, image.Width);
            Assert.Equal(height, image.Height);
            Assert.Equal(4, (int)image.Comp);
            Assert.Equal(128, image.Data[0]);
            Assert.Equal(64, image.Data[1]);
            Assert.Equal((byte)0, image.Data[2]);
            Assert.Equal(255, image.Data[3]);
            Assert.Equal(128, image.Data[16]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsUndersizedBuffers()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mcp-png-{Guid.NewGuid():N}.png");
        Assert.Throws<ArgumentException>(() => PngWriter.Save(path, 8, 8, new byte[16]));
        Assert.Throws<ArgumentOutOfRangeException>(() => PngWriter.Save(path, 0, 8, new byte[8 * 8 * 4]));
    }
}
