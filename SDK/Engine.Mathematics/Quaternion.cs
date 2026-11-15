namespace Engine.Mathematics;

public struct Quaternion : IEquatable<Quaternion>
{
    public float X;
    public float Y;
    public float Z;
    public float W;

    public static Quaternion Identity => new(0f, 0f, 0f, 1f);

    public Quaternion(float x, float y, float z, float w)
    {
        X = x;
        Y = y;
        Z = z;
        W = w;
    }

    public Quaternion(Vector3 axis, float angle)
    {
        this = FromAxisAngle(axis, angle);
    }

    public float Length => MathF.Sqrt(LengthSquared);
    public float LengthSquared => X * X + Y * Y + Z * Z + W * W;
    public bool IsIdentity => X == 0f && Y == 0f && Z == 0f && W == 1f;

    public Quaternion Conjugate => new(-X, -Y, -Z, W);

    public Quaternion Inverse
    {
        get
        {
            float lengthSquared = LengthSquared;
            if (lengthSquared < MathUtil.Epsilon)
                return Identity;
            float invLengthSquared = 1f / lengthSquared;
            return new Quaternion(-X * invLengthSquared, -Y * invLengthSquared, -Z * invLengthSquared, W * invLengthSquared);
        }
    }

    public Quaternion Normalized
    {
        get
        {
            float length = Length;
            return length > MathUtil.Epsilon ? new Quaternion(X / length, Y / length, Z / length, W / length) : Identity;
        }
    }

    public static float Dot(Quaternion a, Quaternion b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

    public static Quaternion FromAxisAngle(Vector3 axis, float angle)
    {
        axis = axis.Normalized;
        float half = angle * 0.5f;
        float sin = MathF.Sin(half);
        return new Quaternion(axis.X * sin, axis.Y * sin, axis.Z * sin, MathF.Cos(half));
    }

    public static Quaternion FromYawPitchRoll(float yaw, float pitch, float roll)
    {
        float cy = MathF.Cos(yaw * 0.5f);
        float sy = MathF.Sin(yaw * 0.5f);
        float cp = MathF.Cos(pitch * 0.5f);
        float sp = MathF.Sin(pitch * 0.5f);
        float cr = MathF.Cos(roll * 0.5f);
        float sr = MathF.Sin(roll * 0.5f);
        return new Quaternion(
            cy * sp * cr + sy * cp * sr,
            sy * cp * cr - cy * sp * sr,
            cy * cp * sr - sy * sp * cr,
            cy * cp * cr + sy * sp * sr);
    }

    public static Quaternion FromRotationMatrix(in Matrix4x4 matrix)
    {
        float trace = matrix.M11 + matrix.M22 + matrix.M33;
        Quaternion result;
        if (trace > 0f)
        {
            float s = MathF.Sqrt(trace + 1f) * 2f;
            result = new Quaternion(
                (matrix.M32 - matrix.M23) / s,
                (matrix.M13 - matrix.M31) / s,
                (matrix.M21 - matrix.M12) / s,
                s * 0.25f);
        }
        else if (matrix.M11 > matrix.M22 && matrix.M11 > matrix.M33)
        {
            float s = MathF.Sqrt(1f + matrix.M11 - matrix.M22 - matrix.M33) * 2f;
            result = new Quaternion(
                s * 0.25f,
                (matrix.M12 + matrix.M21) / s,
                (matrix.M13 + matrix.M31) / s,
                (matrix.M32 - matrix.M23) / s);
        }
        else if (matrix.M22 > matrix.M33)
        {
            float s = MathF.Sqrt(1f + matrix.M22 - matrix.M11 - matrix.M33) * 2f;
            result = new Quaternion(
                (matrix.M12 + matrix.M21) / s,
                s * 0.25f,
                (matrix.M23 + matrix.M32) / s,
                (matrix.M13 - matrix.M31) / s);
        }
        else
        {
            float s = MathF.Sqrt(1f + matrix.M33 - matrix.M11 - matrix.M22) * 2f;
            result = new Quaternion(
                (matrix.M13 + matrix.M31) / s,
                (matrix.M23 + matrix.M32) / s,
                s * 0.25f,
                (matrix.M21 - matrix.M12) / s);
        }
        return result.Normalized;
    }

    public static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        from = from.Normalized;
        to = to.Normalized;
        Vector3 axis = Vector3.Cross(from, to);
        float dot = Vector3.Dot(from, to);
        if (axis.Length < MathUtil.Epsilon)
        {
            if (dot > 0f)
                return Identity;
            Vector3 perpendicular = MathF.Abs(from.X) < 0.9f
                ? Vector3.Cross(from, Vector3.UnitX)
                : Vector3.Cross(from, Vector3.UnitY);
            return FromAxisAngle(perpendicular.Normalized, MathUtil.Pi);
        }
        return new Quaternion(axis, MathF.Acos(MathUtil.Clamp(dot, -1f, 1f)));
    }

    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        forward = forward.Normalized;
        if (forward.Length < MathUtil.Epsilon)
            return Identity;
        Vector3 right = Vector3.Cross(forward, up);
        if (right.Length < MathUtil.Epsilon)
        {
            up = MathF.Abs(forward.Y) > 0.99f ? Vector3.UnitZ : Vector3.Up;
            right = Vector3.Cross(forward, up);
        }
        right = right.Normalized;
        Vector3 trueUp = Vector3.Cross(right, forward);
        Matrix4x4 basis = default;
        basis.M11 = right.X;
        basis.M21 = right.Y;
        basis.M31 = right.Z;
        basis.M12 = trueUp.X;
        basis.M22 = trueUp.Y;
        basis.M32 = trueUp.Z;
        basis.M13 = -forward.X;
        basis.M23 = -forward.Y;
        basis.M33 = -forward.Z;
        basis.M44 = 1f;
        return FromRotationMatrix(basis);
    }

    public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
    {
        float cos = Dot(a, b);
        if (cos < 0f)
        {
            cos = -cos;
            b = -b;
        }
        float k0;
        float k1;
        if (cos > 0.9995f)
        {
            k0 = 1f - t;
            k1 = t;
        }
        else
        {
            float theta = MathF.Acos(cos);
            float sin = MathF.Sin(theta);
            k0 = MathF.Sin((1f - t) * theta) / sin;
            k1 = MathF.Sin(t * theta) / sin;
        }
        return new Quaternion(
            a.X * k0 + b.X * k1,
            a.Y * k0 + b.Y * k1,
            a.Z * k0 + b.Z * k1,
            a.W * k0 + b.W * k1);
    }

    public static Quaternion Lerp(Quaternion a, Quaternion b, float t)
    {
        if (Dot(a, b) < 0f)
            b = -b;
        Quaternion result = new(
            a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t,
            a.W + (b.W - a.W) * t);
        return result.Normalized;
    }

    public static float Angle(Quaternion a, Quaternion b) => 2f * MathF.Acos(MathUtil.Clamp(MathF.Abs(Dot(a, b)), -1f, 1f));

    public Vector3 ToYawPitchRoll()
    {
        Matrix4x4 matrix = ToMatrix();
        float yaw = MathF.Atan2(matrix.M13, matrix.M33);
        float pitch = MathF.Asin(MathUtil.Clamp(-matrix.M23, -1f, 1f));
        float roll = MathF.Atan2(matrix.M21, matrix.M22);
        return new Vector3(yaw, pitch, roll);
    }

    public Matrix4x4 ToMatrix()
    {
        float xx = X * X;
        float yy = Y * Y;
        float zz = Z * Z;
        float xy = X * Y;
        float xz = X * Z;
        float yz = Y * Z;
        float wx = W * X;
        float wy = W * Y;
        float wz = W * Z;
        Matrix4x4 result = default;
        result.M11 = 1f - 2f * (yy + zz);
        result.M12 = 2f * (xy - wz);
        result.M13 = 2f * (xz + wy);
        result.M21 = 2f * (xy + wz);
        result.M22 = 1f - 2f * (xx + zz);
        result.M23 = 2f * (yz - wx);
        result.M31 = 2f * (xz - wy);
        result.M32 = 2f * (yz + wx);
        result.M33 = 1f - 2f * (xx + yy);
        result.M44 = 1f;
        return result;
    }

    public Vector3 Rotate(Vector3 value)
    {
        Vector3 u = new(X, Y, Z);
        return value + 2f * Vector3.Cross(u, Vector3.Cross(u, value) + W * value);
    }

    public static Quaternion operator *(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    public static Quaternion operator -(Quaternion value) => new(-value.X, -value.Y, -value.Z, -value.W);
    public static bool operator ==(Quaternion a, Quaternion b) => a.Equals(b);
    public static bool operator !=(Quaternion a, Quaternion b) => !a.Equals(b);

    public bool Equals(Quaternion other) => X == other.X && Y == other.Y && Z == other.Z && W == other.W;
    public override bool Equals(object? obj) => obj is Quaternion other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
    public override string ToString() => $"({X}, {Y}, {Z}, {W})";
}

public static class QuaternionExtensions
{
    public static Vector3 RotateBy(this Vector3 value, Quaternion rotation) => rotation.Rotate(value);
}
