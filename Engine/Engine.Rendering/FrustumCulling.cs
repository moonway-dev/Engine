using Engine.Mathematics;

namespace Engine.Rendering;

public static class FrustumCulling
{
    public static bool IsVisible(Vector3 minimum, Vector3 maximum, Matrix4x4 modelViewProjection)
    {
        int outside = 63;
        for (int i = 0; i < 8; i++)
        {
            Vector4 point = Vector4.Transform(new Vector4(
                (i & 1) == 0 ? minimum.X : maximum.X,
                (i & 2) == 0 ? minimum.Y : maximum.Y,
                (i & 4) == 0 ? minimum.Z : maximum.Z, 1f), modelViewProjection);
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z) || !float.IsFinite(point.W))
                return true;
            int mask = 0;
            if (point.X < -point.W) mask |= 1;
            if (point.X > point.W) mask |= 2;
            if (point.Y < -point.W) mask |= 4;
            if (point.Y > point.W) mask |= 8;
            if (point.Z < 0f) mask |= 16;
            if (point.Z > point.W) mask |= 32;
            outside &= mask;
            if (outside == 0)
                return true;
        }
        return outside == 0;
    }
}
