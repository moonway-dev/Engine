using Engine.Mathematics;
using Engine.Scene;

namespace Engine.Rendering;

public readonly record struct PickingRay(Vector3 Origin, Vector3 Direction);

public readonly record struct PickingHit(GameObject GameObject, float Distance);

public static class ScenePicking
{
    public static PickingRay CreateRay(Camera camera, Vector2 normalizedViewport)
    {
        Matrix4x4 inverse = camera.ViewProjection.Invert();
        float ndcX = normalizedViewport.X * 2f - 1f;
        float ndcY = normalizedViewport.Y * 2f - 1f;
        Vector4 near = Vector4.Transform(new Vector4(ndcX, ndcY, 0f, 1f), inverse);
        Vector4 far = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), inverse);
        Vector3 nearPoint = new(near.X / near.W, near.Y / near.W, near.Z / near.W);
        Vector3 farPoint = new(far.X / far.W, far.Y / far.W, far.Z / far.W);
        return new PickingRay(nearPoint, (farPoint - nearPoint).Normalized);
    }

    public static PickingHit? Pick(PickingRay ray, IEnumerable<RenderItem> items)
    {
        PickingHit? closest = null;
        foreach (RenderItem item in items)
        {
            if (item.BoundTransform?.Owner is not GameObject gameObject)
                continue;
            Matrix4x4 inverse = item.ModelMatrix.Invert();
            Vector3 localOrigin = Vector3.TransformPoint(ray.Origin, inverse);
            Vector3 localDirection = Vector3.TransformDirection(ray.Direction, inverse).Normalized;
            if (!IntersectAabb(localOrigin, localDirection, item.Mesh.BoundsMin, item.Mesh.BoundsMax, out float localDistance))
                continue;
            Vector3 localHit = localOrigin + localDirection * localDistance;
            Vector3 worldHit = Vector3.TransformPoint(localHit, item.ModelMatrix);
            float distance = (worldHit - ray.Origin).Length;
            if (closest is null || distance < closest.Value.Distance)
                closest = new PickingHit(gameObject, distance);
        }
        return closest;
    }

    public static bool IntersectAabb(Vector3 origin, Vector3 direction, Vector3 minimum, Vector3 maximum, out float distance)
    {
        float minimumDistance = 0f;
        float maximumDistance = float.PositiveInfinity;
        if (!IntersectAxis(origin.X, direction.X, minimum.X, maximum.X, ref minimumDistance, ref maximumDistance) ||
            !IntersectAxis(origin.Y, direction.Y, minimum.Y, maximum.Y, ref minimumDistance, ref maximumDistance) ||
            !IntersectAxis(origin.Z, direction.Z, minimum.Z, maximum.Z, ref minimumDistance, ref maximumDistance))
        {
            distance = 0f;
            return false;
        }
        distance = minimumDistance;
        return maximumDistance >= 0f;
    }

    private static bool IntersectAxis(float origin, float direction, float minimum, float maximum, ref float near, ref float far)
    {
        if (MathF.Abs(direction) < MathUtil.Epsilon)
            return origin >= minimum && origin <= maximum;
        float inverse = 1f / direction;
        float first = (minimum - origin) * inverse;
        float second = (maximum - origin) * inverse;
        if (first > second)
            (first, second) = (second, first);
        near = MathF.Max(near, first);
        far = MathF.Min(far, second);
        return near <= far;
    }
}
