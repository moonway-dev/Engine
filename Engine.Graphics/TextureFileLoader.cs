using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Engine.Math;

namespace Engine.Graphics;

public sealed class TextureFileData
{
    public TextureFileData(int width, int height, Vector4[] pixels)
    {
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }
    public Vector4[] Pixels { get; }
}

public static class TextureFileLoader
{
    private const float ByteToFloat = 1f / 255f;

    public static TextureFileData Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Texture path is empty.", nameof(path));

        string resolvedPath = Path.GetFullPath(path);
        if (!File.Exists(resolvedPath))
            throw new FileNotFoundException($"Texture file not found: {resolvedPath}", resolvedPath);

        using Bitmap bitmap = new Bitmap(resolvedPath);
        Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        BitmapData data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int stride = System.Math.Abs(data.Stride);
            byte[] buffer = new byte[stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            
            Vector4[] pixels = new Vector4[data.Width * data.Height];
            for (int y = 0; y < data.Height; y++)
            {
                int srcRow = y * stride;
                int destRow = y * data.Width;
                for (int x = 0; x < data.Width; x++)
                {
                    int srcIndex = srcRow + x * 4;
                    byte b = buffer[srcIndex + 0];
                    byte g = buffer[srcIndex + 1];
                    byte r = buffer[srcIndex + 2];
                    byte a = buffer[srcIndex + 3];
                    pixels[destRow + x] = new Vector4(
                        r * ByteToFloat,
                        g * ByteToFloat,
                        b * ByteToFloat,
                        a * ByteToFloat
                    );
                }
            }
            return new TextureFileData(data.Width, data.Height, pixels);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }
}

