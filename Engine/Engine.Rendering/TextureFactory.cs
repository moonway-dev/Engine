using Engine.Mathematics;
using Engine.RHI;

namespace Engine.Rendering;

public static class TextureFactory
{
    public static byte[] Checkerboard(int size, Vector3 colorA, Vector3 colorB, int cells = 8)
    {
        byte[] pixels = new byte[size * size * 4];
        int cellSize = Math.Max(1, size / cells);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool even = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                Vector3 color = even ? colorA : colorB;
                int index = (y * size + x) * 4;
                pixels[index] = ToByte(color.X);
                pixels[index + 1] = ToByte(color.Y);
                pixels[index + 2] = ToByte(color.Z);
                pixels[index + 3] = 255;
            }
        }
        return pixels;
    }

    private static byte ToByte(float value) => (byte)(MathUtil.Clamp(value, 0f, 1f) * 255f);
}
