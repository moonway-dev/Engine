using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

namespace Engine.Rendering;

public static class VolumetricCloudNoise
{
    public sealed record NoiseData(byte[] Shape, byte[] Detail, byte[] Weather, int ShapeSize, int DetailSize, int WeatherSize);

    private const int MaxFrequency = 64;

    private static float[][]? _jitterTables;
    private static float[][]? _gradientTables;
    private static float[][]? _gradient2Tables;

    public static NoiseData Generate(int shapeSize, int detailSize, int weatherSize)
    {
        BuildTables();
        byte[] shape = GenerateShape(shapeSize);
        byte[] detail = GenerateDetail(detailSize);
        byte[] weather = GenerateWeather(weatherSize);
        return new NoiseData(shape, detail, weather, shapeSize, detailSize, weatherSize);
    }

    private static void BuildTables()
    {
        if (_jitterTables is not null)
            return;
        _jitterTables = new float[MaxFrequency + 1][];
        _gradientTables = new float[MaxFrequency + 1][];
        _gradient2Tables = new float[MaxFrequency + 1][];
        for (int frequency = 1; frequency <= MaxFrequency; frequency++)
        {
            int count = frequency * frequency * frequency;
            float[] jitter = new float[count * 3];
            float[] gradient = new float[count * 3];
            float[] gradient2 = new float[count * 2];
            for (int z = 0; z < frequency; z++)
            {
                for (int y = 0; y < frequency; y++)
                {
                    for (int x = 0; x < frequency; x++)
                    {
                        int index = (z * frequency + y) * frequency + x;
                        jitter[index * 3] = Hash3(x, y, z, 913, 271, 507);
                        jitter[index * 3 + 1] = Hash3(x, y, z, 173, 819, 631);
                        jitter[index * 3 + 2] = Hash3(x, y, z, 401, 577, 233);

                        float angle = Hash3(x, y, z, 31, 97, 173) * MathF.Tau;
                        float elevation = Hash3(x, y, z, 71, 131, 251) * 2f - 1f;
                        float horizontal = MathF.Sqrt(MathF.Max(0f, 1f - elevation * elevation));
                        gradient[index * 3] = MathF.Cos(angle) * horizontal;
                        gradient[index * 3 + 1] = MathF.Sin(angle) * horizontal;
                        gradient[index * 3 + 2] = elevation;

                        float angle2 = Hash2(x, y, 0) * MathF.Tau;
                        gradient2[index * 2] = MathF.Cos(angle2);
                        gradient2[index * 2 + 1] = MathF.Sin(angle2);
                    }
                }
            }
            _jitterTables[frequency] = jitter;
            _gradientTables[frequency] = gradient;
            _gradient2Tables[frequency] = gradient2;
        }
    }

    private static byte[] GenerateShape(int size)
    {
        byte[] data = new byte[size * size * size * 4];
        const int baseFrequency = 4;
        Parallel.For(0, size, z =>
        {
            for (int y = 0; y < size; y++)
            {
                float vy = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    var uvw = new NVector3((float)x / size, vy, (float)z / size);
                    float perlin = TileablePerlinFbm(uvw, baseFrequency);
                    float worley = TileableWorleyFbm(uvw, baseFrequency);
                    float perlinWorley = Remap(perlin, 0f, 1f, worley, 1f);
                    float w0 = TileableWorley(uvw, baseFrequency * 2f);
                    float w1 = TileableWorley(uvw, baseFrequency * 4f);
                    float w2 = TileableWorley(uvw, baseFrequency * 8f);
                    int index = (x + y * size + z * size * size) * 4;
                    data[index] = ToByte(perlinWorley);
                    data[index + 1] = ToByte(w0);
                    data[index + 2] = ToByte(w1);
                    data[index + 3] = ToByte(w2);
                }
            }
        });
        return data;
    }

    private static byte[] GenerateDetail(int size)
    {
        byte[] data = new byte[size * size * size * 4];
        Parallel.For(0, size, z =>
        {
            for (int y = 0; y < size; y++)
            {
                float vy = (float)y / size;
                for (int x = 0; x < size; x++)
                {
                    var uvw = new NVector3((float)x / size, vy, (float)z / size);
                    int index = (x + y * size + z * size * size) * 4;
                    data[index] = ToByte(TileableWorley(uvw, 6f));
                    data[index + 1] = ToByte(TileableWorley(uvw, 12f));
                    data[index + 2] = ToByte(TileableWorley(uvw, 24f));
                    data[index + 3] = 255;
                }
            }
        });
        return data;
    }

    private static byte[] GenerateWeather(int size)
    {
        byte[] data = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var uv = new NVector2((float)x / size, (float)y / size);
                float coverage = TileablePerlinFbm2(uv, 3, 2) * 0.55f + TileablePerlinFbm2(uv, 9, 2) * 0.3f + TileablePerlinFbm2(uv, 21, 2) * 0.15f;
                coverage = Math.Clamp((coverage - 0.29f) / 0.42f, 0f, 1f);
                float type = TileablePerlinFbm2(uv, 4, 2);
                int index = (x + y * size) * 4;
                data[index] = ToByte(coverage);
                data[index + 1] = ToByte(type);
                data[index + 2] = ToByte(type * coverage);
                data[index + 3] = 255;
            }
        }
        return data;
    }

    private static float TileablePerlinFbm(NVector3 p, int baseFrequency)
    {
        float total = 0f;
        float amplitude = 0.5f;
        float sum = 0f;
        for (int octave = 0; octave < 3; octave++)
        {
            total += TileablePerlin(p, baseFrequency << octave) * amplitude;
            sum += amplitude;
            amplitude *= 0.5f;
        }
        return Math.Clamp(total / sum * 0.5f + 0.5f, 0f, 1f);
    }

    private static float TileablePerlinFbm2(NVector2 p, int baseFrequency, int octaves)
    {
        float total = 0f;
        float amplitude = 0.5f;
        float sum = 0f;
        for (int octave = 0; octave < octaves; octave++)
        {
            total += TileablePerlin2(p, baseFrequency << octave) * amplitude;
            sum += amplitude;
            amplitude *= 0.5f;
        }
        return Math.Clamp(total / sum * 0.5f + 0.5f, 0f, 1f);
    }

    private static float TileableWorleyFbm(NVector3 p, int baseFrequency)
    {
        float total = 0f;
        float amplitude = 0.5f;
        float sum = 0f;
        for (int octave = 0; octave < 3; octave++)
        {
            total += TileableWorley(p, baseFrequency << octave) * amplitude;
            sum += amplitude;
            amplitude *= 0.5f;
        }
        return Math.Clamp(total / sum, 0f, 1f);
    }

    private static float TileableWorley(NVector3 p, float frequency)
    {
        int period = (int)MathF.Round(frequency);
        NVector3 position = p * frequency;
        int cx = (int)MathF.Floor(position.X);
        int cy = (int)MathF.Floor(position.Y);
        int cz = (int)MathF.Floor(position.Z);
        float fx = position.X - cx;
        float fy = position.Y - cy;
        float fz = position.Z - cz;
        float[] jitter = _jitterTables![period];
        float minDistanceSquared = 1f;
        for (int dz = -1; dz <= 1; dz++)
        {
            int wz = Mod(cz + dz, period);
            float oz = dz - fz;
            for (int dy = -1; dy <= 1; dy++)
            {
                int wy = Mod(cy + dy, period);
                float oy = dy - fy;
                int rowBase = (wz * period + wy) * period;
                for (int dx = -1; dx <= 1; dx++)
                {
                    int wx = Mod(cx + dx, period);
                    int index = (rowBase + wx) * 3;
                    float ddx = dx + jitter[index] - fx;
                    float ddy = dy + jitter[index + 1] - fy;
                    float ddz = dz + jitter[index + 2] - fz;
                    float distanceSquared = ddx * ddx + ddy * ddy + ddz * ddz;
                    if (distanceSquared < minDistanceSquared)
                        minDistanceSquared = distanceSquared;
                }
            }
        }
        return Math.Clamp(1f - MathF.Sqrt(minDistanceSquared), 0f, 1f);
    }

    private static float TileablePerlin(NVector3 p, int period)
    {
        NVector3 position = p * period;
        int cx = (int)MathF.Floor(position.X);
        int cy = (int)MathF.Floor(position.Y);
        int cz = (int)MathF.Floor(position.Z);
        float fx = position.X - cx;
        float fy = position.Y - cy;
        float fz = position.Z - cz;
        float[] gradient = _gradientTables![period];
        float total = 0f;
        for (int dz = 0; dz < 2; dz++)
        {
            float oz = fz - dz;
            float wz = Weight(oz);
            int wzIndex = Mod(cz + dz, period);
            for (int dy = 0; dy < 2; dy++)
            {
                float oy = fy - dy;
                float wyz = wz * Weight(oy);
                int rowIndex = (wzIndex * period + Mod(cy + dy, period)) * period;
                for (int dx = 0; dx < 2; dx++)
                {
                    float ox = fx - dx;
                    int index = (rowIndex + Mod(cx + dx, period)) * 3;
                    float dot = gradient[index] * ox + gradient[index + 1] * oy + gradient[index + 2] * oz;
                    total += dot * wyz * Weight(ox);
                }
            }
        }
        return Math.Clamp(total * 0.7071f + 0.5f, 0f, 1f);
    }

    private static float TileablePerlin2(NVector2 p, int period)
    {
        NVector2 position = p * period;
        int cx = (int)MathF.Floor(position.X);
        int cy = (int)MathF.Floor(position.Y);
        float fx = position.X - cx;
        float fy = position.Y - cy;
        float[] gradient = _gradient2Tables![period];
        float total = 0f;
        for (int dy = 0; dy < 2; dy++)
        {
            float oy = fy - dy;
            float wy = Weight(oy);
            int rowIndex = Mod(cy + dy, period) * period;
            for (int dx = 0; dx < 2; dx++)
            {
                float ox = fx - dx;
                int index = (rowIndex + Mod(cx + dx, period)) * 2;
                total += (gradient[index] * ox + gradient[index + 1] * oy) * wy * Weight(ox);
            }
        }
        return Math.Clamp(total * 0.7071f + 0.5f, 0f, 1f);
    }

    private static int Mod(int value, int period)
    {
        int result = value % period;
        return result < 0 ? result + period : result;
    }

    private static float Hash2(int x, int y, int z)
    {
        uint hash = (uint)(x * 374761393 + y * 668265263 + z * 915488749);
        hash = (hash ^ (hash >> 13)) * 1274126177;
        hash ^= hash >> 16;
        return (hash & 0xFFFFFF) / (float)0xFFFFFF;
    }

    private static float Hash3(int x, int y, int z, int saltA, int saltB, int saltC)
    {
        uint hash = (uint)(x * 374761393 + y * 668265263 + z * 915488749 + saltA * 7 + saltB * 31 + saltC * 129);
        hash = (hash ^ (hash >> 13)) * 1274126177;
        hash ^= hash >> 16;
        return (hash & 0xFFFFFF) / (float)0xFFFFFF;
    }

    private static float Weight(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Remap(float value, float sourceMin, float sourceMax, float targetMin, float targetMax) =>
        targetMin + (value - sourceMin) * (targetMax - targetMin) / MathF.Max(sourceMax - sourceMin, 1e-5f);

    private static byte ToByte(float value) => (byte)Math.Clamp(value * 255f, 0f, 255f);
}
