namespace Engine.Mathematics;

public struct Matrix4x4 : IEquatable<Matrix4x4>
{
    public float M11;
    public float M12;
    public float M13;
    public float M14;
    public float M21;
    public float M22;
    public float M23;
    public float M24;
    public float M31;
    public float M32;
    public float M33;
    public float M34;
    public float M41;
    public float M42;
    public float M43;
    public float M44;

    public static Matrix4x4 Identity
    {
        get
        {
            Matrix4x4 result = default;
            result.M11 = 1f;
            result.M22 = 1f;
            result.M33 = 1f;
            result.M44 = 1f;
            return result;
        }
    }

    public bool IsIdentity =>
        M11 == 1f && M22 == 1f && M33 == 1f && M44 == 1f &&
        M12 == 0f && M13 == 0f && M14 == 0f &&
        M21 == 0f && M23 == 0f && M24 == 0f &&
        M31 == 0f && M32 == 0f && M34 == 0f &&
        M41 == 0f && M42 == 0f && M43 == 0f;

    public Vector3 Translation => new(M14, M24, M34);

    public static Matrix4x4 CreateTranslation(Vector3 position)
    {
        Matrix4x4 result = Identity;
        result.M14 = position.X;
        result.M24 = position.Y;
        result.M34 = position.Z;
        return result;
    }

    public static Matrix4x4 CreateScale(float scale) => CreateScale(new Vector3(scale));

    public static Matrix4x4 CreateScale(Vector3 scale)
    {
        Matrix4x4 result = Identity;
        result.M11 = scale.X;
        result.M22 = scale.Y;
        result.M33 = scale.Z;
        return result;
    }

    public static Matrix4x4 CreateRotationX(float radians)
    {
        Matrix4x4 result = Identity;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        result.M22 = cos;
        result.M23 = -sin;
        result.M32 = sin;
        result.M33 = cos;
        return result;
    }

    public static Matrix4x4 CreateRotationY(float radians)
    {
        Matrix4x4 result = Identity;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        result.M11 = cos;
        result.M13 = sin;
        result.M31 = -sin;
        result.M33 = cos;
        return result;
    }

    public static Matrix4x4 CreateRotationZ(float radians)
    {
        Matrix4x4 result = Identity;
        float cos = MathF.Cos(radians);
        float sin = MathF.Sin(radians);
        result.M11 = cos;
        result.M12 = -sin;
        result.M21 = sin;
        result.M22 = cos;
        return result;
    }

    public static Matrix4x4 CreateFromYawPitchRoll(float yaw, float pitch, float roll) =>
        CreateRotationY(yaw) * CreateRotationX(pitch) * CreateRotationZ(roll);

    public static Matrix4x4 CreateFromQuaternion(Quaternion rotation) => rotation.ToMatrix();

    public static Matrix4x4 CreateTRS(Vector3 position, Quaternion rotation, Vector3 scale) =>
        CreateTranslation(position) * CreateFromQuaternion(rotation) * CreateScale(scale);

    public static Matrix4x4 CreateLookAt(Vector3 eye, Vector3 target, Vector3 up)
    {
        Vector3 zAxis = (eye - target).Normalized;
        Vector3 xAxis = Vector3.Cross(up, zAxis).Normalized;
        Vector3 yAxis = Vector3.Cross(zAxis, xAxis);
        Matrix4x4 result = default;
        result.M11 = xAxis.X;
        result.M12 = xAxis.Y;
        result.M13 = xAxis.Z;
        result.M14 = -Vector3.Dot(xAxis, eye);
        result.M21 = yAxis.X;
        result.M22 = yAxis.Y;
        result.M23 = yAxis.Z;
        result.M24 = -Vector3.Dot(yAxis, eye);
        result.M31 = zAxis.X;
        result.M32 = zAxis.Y;
        result.M33 = zAxis.Z;
        result.M34 = -Vector3.Dot(zAxis, eye);
        result.M44 = 1f;
        return result;
    }

    public static Matrix4x4 CreatePerspectiveFieldOfView(float fieldOfViewY, float aspectRatio, float nearPlaneDistance, float farPlaneDistance)
    {
        if (fieldOfViewY <= 0f || fieldOfViewY >= MathF.PI)
            throw new ArgumentOutOfRangeException(nameof(fieldOfViewY));
        if (nearPlaneDistance <= 0f)
            throw new ArgumentOutOfRangeException(nameof(nearPlaneDistance));
        if (farPlaneDistance <= nearPlaneDistance)
            throw new ArgumentOutOfRangeException(nameof(farPlaneDistance));
        if (aspectRatio <= 0f)
            throw new ArgumentOutOfRangeException(nameof(aspectRatio));

        float yScale = 1f / MathF.Tan(fieldOfViewY * 0.5f);
        float xScale = yScale / aspectRatio;
        Matrix4x4 result = default;
        result.M11 = xScale;
        result.M22 = yScale;
        result.M33 = farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
        result.M34 = nearPlaneDistance * farPlaneDistance / (nearPlaneDistance - farPlaneDistance);
        result.M43 = -1f;
        return result;
    }

    public static Matrix4x4 CreateOrthographic(float width, float height, float nearPlaneDistance, float farPlaneDistance)
    {
        if (width <= 0f)
            throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0f)
            throw new ArgumentOutOfRangeException(nameof(height));
        Matrix4x4 result = Identity;
        result.M11 = 2f / width;
        result.M22 = 2f / height;
        result.M33 = 1f / (nearPlaneDistance - farPlaneDistance);
        result.M34 = nearPlaneDistance / (nearPlaneDistance - farPlaneDistance);
        return result;
    }

    public float GetDeterminant()
    {
        float kpLo = M33 * M44 - M34 * M43;
        float jpLn = M32 * M44 - M34 * M42;
        float joKn = M32 * M43 - M33 * M42;
        float ipLm = M31 * M44 - M34 * M41;
        float ioKm = M31 * M43 - M33 * M41;
        float inJm = M31 * M42 - M32 * M41;
        return
            M11 * (M22 * kpLo - M23 * jpLn + M24 * joKn) -
            M12 * (M21 * kpLo - M23 * ipLm + M24 * ioKm) +
            M13 * (M21 * jpLn - M22 * ipLm + M24 * inJm) -
            M14 * (M21 * joKn - M22 * ioKm + M23 * inJm);
    }

    public Matrix4x4 Invert()
    {
        float a1 = M11;
        float a2 = M12;
        float a3 = M13;
        float a4 = M14;
        float b1 = M21;
        float b2 = M22;
        float b3 = M23;
        float b4 = M24;
        float c1 = M31;
        float c2 = M32;
        float c3 = M33;
        float c4 = M34;
        float d1 = M41;
        float d2 = M42;
        float d3 = M43;
        float d4 = M44;

        float determinant = GetDeterminant();
        if (MathF.Abs(determinant) < MathUtil.Epsilon)
            throw new InvalidOperationException("Matrix is not invertible.");

        float invDet = 1f / determinant;
        Matrix4x4 result;
        result.M11 = (b2 * (c3 * d4 - c4 * d3) - b3 * (c2 * d4 - c4 * d2) + b4 * (c2 * d3 - c3 * d2)) * invDet;
        result.M12 = -(a2 * (c3 * d4 - c4 * d3) - a3 * (c2 * d4 - c4 * d2) + a4 * (c2 * d3 - c3 * d2)) * invDet;
        result.M13 = (a2 * (b3 * d4 - b4 * d3) - a3 * (b2 * d4 - b4 * d2) + a4 * (b2 * d3 - b3 * d2)) * invDet;
        result.M14 = -(a2 * (b3 * c4 - b4 * c3) - a3 * (b2 * c4 - b4 * c2) + a4 * (b2 * c3 - b3 * c2)) * invDet;
        result.M21 = -(b1 * (c3 * d4 - c4 * d3) - b3 * (c1 * d4 - c4 * d1) + b4 * (c1 * d3 - c3 * d1)) * invDet;
        result.M22 = (a1 * (c3 * d4 - c4 * d3) - a3 * (c1 * d4 - c4 * d1) + a4 * (c1 * d3 - c3 * d1)) * invDet;
        result.M23 = -(a1 * (b3 * d4 - b4 * d3) - a3 * (b1 * d4 - b4 * d1) + a4 * (b1 * d3 - b3 * d1)) * invDet;
        result.M24 = (a1 * (b3 * c4 - b4 * c3) - a3 * (b1 * c4 - b4 * c1) + a4 * (b1 * c3 - b3 * c1)) * invDet;
        result.M31 = (b1 * (c2 * d4 - c4 * d2) - b2 * (c1 * d4 - c4 * d1) + b4 * (c1 * d2 - c2 * d1)) * invDet;
        result.M32 = -(a1 * (c2 * d4 - c4 * d2) - a2 * (c1 * d4 - c4 * d1) + a4 * (c1 * d2 - c2 * d1)) * invDet;
        result.M33 = (a1 * (b2 * d4 - b4 * d2) - a2 * (b1 * d4 - b4 * d1) + a4 * (b1 * d2 - b2 * d1)) * invDet;
        result.M34 = -(a1 * (b2 * c4 - b4 * c2) - a2 * (b1 * c4 - b4 * c1) + a4 * (b1 * c2 - b2 * c1)) * invDet;
        result.M41 = -(b1 * (c2 * d3 - c3 * d2) - b2 * (c1 * d3 - c3 * d1) + b3 * (c1 * d2 - c2 * d1)) * invDet;
        result.M42 = (a1 * (c2 * d3 - c3 * d2) - a2 * (c1 * d3 - c3 * d1) + a3 * (c1 * d2 - c2 * d1)) * invDet;
        result.M43 = -(a1 * (b2 * d3 - b3 * d2) - a2 * (b1 * d3 - b3 * d1) + a3 * (b1 * d2 - b2 * d1)) * invDet;
        result.M44 = (a1 * (b2 * c3 - b3 * c2) - a2 * (b1 * c3 - b3 * c1) + a3 * (b1 * c2 - b2 * c1)) * invDet;
        return result;
    }

    public Matrix4x4 Transpose()
    {
        Matrix4x4 result;
        result.M11 = M11;
        result.M12 = M21;
        result.M13 = M31;
        result.M14 = M41;
        result.M21 = M12;
        result.M22 = M22;
        result.M23 = M32;
        result.M24 = M42;
        result.M31 = M13;
        result.M32 = M23;
        result.M33 = M33;
        result.M34 = M43;
        result.M41 = M14;
        result.M42 = M24;
        result.M43 = M34;
        result.M44 = M44;
        return result;
    }

    public bool Decompose(out Vector3 scale, out Quaternion rotation, out Vector3 translation)
    {
        translation = new Vector3(M14, M24, M34);
        scale = new Vector3(
            new Vector3(M11, M21, M31).Length,
            new Vector3(M12, M22, M32).Length,
            new Vector3(M13, M23, M33).Length);

        if (scale.X < MathUtil.Epsilon || scale.Y < MathUtil.Epsilon || scale.Z < MathUtil.Epsilon)
        {
            rotation = Quaternion.Identity;
            return false;
        }

        Matrix4x4 rotationMatrix = Identity;
        rotationMatrix.M11 = M11 / scale.X;
        rotationMatrix.M21 = M21 / scale.X;
        rotationMatrix.M31 = M31 / scale.X;
        rotationMatrix.M12 = M12 / scale.Y;
        rotationMatrix.M22 = M22 / scale.Y;
        rotationMatrix.M32 = M32 / scale.Y;
        rotationMatrix.M13 = M13 / scale.Z;
        rotationMatrix.M23 = M23 / scale.Z;
        rotationMatrix.M33 = M33 / scale.Z;
        rotation = Quaternion.FromRotationMatrix(rotationMatrix);
        return GetDeterminant() > 0f;
    }

    public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b)
    {
        Matrix4x4 result;
        result.M11 = a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31 + a.M14 * b.M41;
        result.M12 = a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32 + a.M14 * b.M42;
        result.M13 = a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33 + a.M14 * b.M43;
        result.M14 = a.M11 * b.M14 + a.M12 * b.M24 + a.M13 * b.M34 + a.M14 * b.M44;
        result.M21 = a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31 + a.M24 * b.M41;
        result.M22 = a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32 + a.M24 * b.M42;
        result.M23 = a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33 + a.M24 * b.M43;
        result.M24 = a.M21 * b.M14 + a.M22 * b.M24 + a.M23 * b.M34 + a.M24 * b.M44;
        result.M31 = a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31 + a.M34 * b.M41;
        result.M32 = a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32 + a.M34 * b.M42;
        result.M33 = a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33 + a.M34 * b.M43;
        result.M34 = a.M31 * b.M14 + a.M32 * b.M24 + a.M33 * b.M34 + a.M34 * b.M44;
        result.M41 = a.M41 * b.M11 + a.M42 * b.M21 + a.M43 * b.M31 + a.M44 * b.M41;
        result.M42 = a.M41 * b.M12 + a.M42 * b.M22 + a.M43 * b.M32 + a.M44 * b.M42;
        result.M43 = a.M41 * b.M13 + a.M42 * b.M23 + a.M43 * b.M33 + a.M44 * b.M43;
        result.M44 = a.M41 * b.M14 + a.M42 * b.M24 + a.M43 * b.M34 + a.M44 * b.M44;
        return result;
    }

    public static bool operator ==(Matrix4x4 a, Matrix4x4 b) => a.Equals(b);
    public static bool operator !=(Matrix4x4 a, Matrix4x4 b) => !a.Equals(b);

    public bool Equals(Matrix4x4 other) =>
        M11 == other.M11 && M12 == other.M12 && M13 == other.M13 && M14 == other.M14 &&
        M21 == other.M21 && M22 == other.M22 && M23 == other.M23 && M24 == other.M24 &&
        M31 == other.M31 && M32 == other.M32 && M33 == other.M33 && M34 == other.M34 &&
        M41 == other.M41 && M42 == other.M42 && M43 == other.M43 && M44 == other.M44;

    public override bool Equals(object? obj) => obj is Matrix4x4 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(M11, M12, M13, M14, M21, M22, M23, M24);
    public override string ToString() => $"[{M11}, {M12}, {M13}, {M14}; {M21}, {M22}, {M23}, {M24}; {M31}, {M32}, {M33}, {M34}; {M41}, {M42}, {M43}, {M44}]";
}
