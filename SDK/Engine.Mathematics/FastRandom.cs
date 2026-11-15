namespace Engine.Mathematics;

public struct FastRandom
{
    private const ulong GoldenGamma = 0x9E3779B97F4A7C15UL;

    private ulong _state;

    public FastRandom(ulong seed)
    {
        _state = seed;
    }

    public static FastRandom Create() => new((ulong)Environment.TickCount64 * GoldenGamma ^ 0xD1B54A32D192ED03UL);

    public ulong NextUInt64()
    {
        _state += GoldenGamma;
        ulong z = _state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public uint NextUInt() => (uint)(NextUInt64() >> 32);

    public float NextFloat() => (NextUInt64() >> 40) / 16777216f;

    public float NextFloat(float min, float max) => min + (max - min) * NextFloat();

    public int NextInt(int min, int maxExclusive) =>
        min + (int)(NextUInt() % (uint)(maxExclusive - min));

    public bool NextBool(float probability = 0.5f) => NextFloat() < probability;

    public int NextSign() => NextBool() ? 1 : -1;

    public Vector2 NextVector2InUnitCircle()
    {
        float angle = NextFloat() * MathUtil.TwoPi;
        float radius = MathF.Sqrt(NextFloat());
        return new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
    }

    public Vector2 NextVector2OnUnitCircle()
    {
        float angle = NextFloat() * MathUtil.TwoPi;
        return new Vector2(MathF.Cos(angle), MathF.Sin(angle));
    }

    public Vector3 NextVector3OnUnitSphere()
    {
        float z = NextFloat() * 2f - 1f;
        float angle = NextFloat() * MathUtil.TwoPi;
        float radius = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        return new Vector3(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, z);
    }

    public Vector3 NextVector3InUnitSphere() => NextVector3OnUnitSphere() * MathF.Cbrt(NextFloat());

    public Vector3 NextVector3InBox(Vector3 halfExtents) => new(
        NextFloat(-halfExtents.X, halfExtents.X),
        NextFloat(-halfExtents.Y, halfExtents.Y),
        NextFloat(-halfExtents.Z, halfExtents.Z));
}
