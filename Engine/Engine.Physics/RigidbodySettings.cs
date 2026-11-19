namespace Engine.Physics;

[Flags]
public enum RigidbodyConstraints
{
    None = 0,
    FreezePositionX = 1,
    FreezePositionY = 2,
    FreezePositionZ = 4,
    FreezeRotationX = 8,
    FreezeRotationY = 16,
    FreezeRotationZ = 32,
    FreezePosition = FreezePositionX | FreezePositionY | FreezePositionZ,
    FreezeRotation = FreezeRotationX | FreezeRotationY | FreezeRotationZ,
    FreezeAll = FreezePosition | FreezeRotation,
}

public enum RigidbodyInterpolation
{
    None,
    Interpolate,
    Extrapolate,
}

public enum RigidbodyCollisionDetection
{
    Discrete,
    Passive,
    Continuous,
}

public readonly record struct RigidbodySettings(
    float LinearDamping = 0.05f,
    float AngularDamping = 0.05f,
    bool UseGravity = true,
    bool DetectCollisions = true,
    RigidbodyConstraints Constraints = RigidbodyConstraints.None,
    RigidbodyCollisionDetection CollisionDetection = RigidbodyCollisionDetection.Passive)
{
    public static RigidbodySettings Default => new(0.05f, 0.05f, true, true);

    internal void Validate()
    {
        if (!float.IsFinite(LinearDamping) || LinearDamping < 0f)
            throw new ArgumentOutOfRangeException(nameof(LinearDamping));
        if (!float.IsFinite(AngularDamping) || AngularDamping < 0f)
            throw new ArgumentOutOfRangeException(nameof(AngularDamping));
        if ((Constraints & ~RigidbodyConstraints.FreezeAll) != 0)
            throw new ArgumentOutOfRangeException(nameof(Constraints));
        if (!Enum.IsDefined(CollisionDetection))
            throw new ArgumentOutOfRangeException(nameof(CollisionDetection));
    }
}
