using StbImageSharp;

namespace Engine.Assets;

public sealed class ImageData
{
    public byte[] Pixels { get; }
    public int Width { get; }
    public int Height { get; }
    public int Channels { get; }

    public ImageData(byte[] pixels, int width, int height, int channels)
    {
        Pixels = pixels;
        Width = width;
        Height = height;
        Channels = channels;
    }
}

public static class ImageLoader
{
    public static ImageData Load(string path) => Load(Core.VirtualFileSystem.ReadAllBytes(path));

    public static ImageData Load(byte[] bytes)
    {
        ImageResult result = ImageResult.FromMemory(bytes, ColorComponents.RedGreenBlueAlpha);
        return new ImageData(result.Data, result.Width, result.Height, 4);
    }
}
