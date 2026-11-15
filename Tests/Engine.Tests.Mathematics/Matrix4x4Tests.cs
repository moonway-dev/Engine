using Engine.Mathematics;
using Xunit;

namespace Engine.Tests.Mathematics;

public class Matrix4x4Tests
{
    [Fact]
    public void Identity_LeavesPointUnchanged()
    {
        Vector3 point = new(1.5f, -2f, 3f);
        MathAssert.Equal(point, Vector3.TransformPoint(point, Matrix4x4.Identity));
        Assert.True(Matrix4x4.Identity.IsIdentity);
    }

    [Fact]
    public void TRS_Decompose_RoundTrip()
    {
        Vector3 position = new(1f, 2f, 3f);
        Quaternion rotation = Quaternion.FromYawPitchRoll(0.5f, 0.2f, -0.4f);
        Vector3 scale = new(2f, 3f, 4f);
        Matrix4x4 trs = Matrix4x4.CreateTRS(position, rotation, scale);
        bool decomposed = trs.Decompose(out Vector3 outScale, out Quaternion outRotation, out Vector3 outTranslation);
        Assert.True(decomposed);
        MathAssert.Equal(position, outTranslation);
        Assert.Equal(scale.X, outScale.X, 5);
        Assert.Equal(scale.Y, outScale.Y, 5);
        Assert.Equal(scale.Z, outScale.Z, 5);
        if (Quaternion.Dot(rotation, outRotation) < 0f)
            outRotation = -outRotation;
        MathAssert.Equal(rotation, outRotation, 1e-4f);
    }

    [Fact]
    public void ScaleFirst_ThenRotation_TransformsCorrectly()
    {
        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi);
        Matrix4x4 trs = Matrix4x4.CreateTRS(Vector3.Zero, rotation, Vector3.One * 2f);
        MathAssert.Equal(new Vector3(0f, 0f, -2f), Vector3.TransformPoint(new Vector3(1f, 0f, 0f), trs));
    }

    [Fact]
    public void Invert_RoundTripRestoresPoint()
    {
        Matrix4x4 source = Matrix4x4.CreateTRS(
            new Vector3(4f, -2f, 7f),
            Quaternion.FromYawPitchRoll(0.3f, 0.8f, -1.1f),
            new Vector3(1.5f, 0.75f, 2.25f));
        Matrix4x4 inverse = source.Invert();
        Vector4 point = new(0.3f, -0.7f, 2.1f, 1f);
        Vector4 transformed = Vector4.Transform(point, source);
        Vector4 restored = Vector4.Transform(transformed, inverse);
        Assert.Equal(point.X, restored.X, 4);
        Assert.Equal(point.Y, restored.Y, 4);
        Assert.Equal(point.Z, restored.Z, 4);
        Assert.Equal(point.W, restored.W, 4);
    }

    [Fact]
    public void GetDeterminant_RotationMatrixIsOne()
    {
        Matrix4x4 rotation = Matrix4x4.CreateFromQuaternion(Quaternion.FromYawPitchRoll(0.7f, -0.3f, 0.15f));
        Assert.Equal(1f, rotation.GetDeterminant(), 5);
        Assert.Equal(8f, Matrix4x4.CreateScale(2f).GetDeterminant(), 5);
    }

    [Fact]
    public void CreatePerspective_MapsNearAndFarPlanes()
    {
        const float near = 0.1f;
        const float far = 100f;
        Matrix4x4 projection = Matrix4x4.CreatePerspectiveFieldOfView(MathUtil.HalfPi, 16f / 9f, near, far);

        Vector4 nearPoint = Vector4.Transform(new Vector4(0f, 0f, -near, 1f), projection);
        Assert.Equal(0f, nearPoint.Z / nearPoint.W, 5);
        Assert.Equal(near, nearPoint.W, 5);

        Vector4 farPoint = Vector4.Transform(new Vector4(0f, 0f, -far, 1f), projection);
        Assert.Equal(1f, farPoint.Z / farPoint.W, 5);

        Vector4 offCenter = Vector4.Transform(
            new Vector4(MathF.Tan(MathUtil.HalfPi * 0.5f) * (16f / 9f) * near * 0.5f, 0f, -near, 1f),
            projection);
        float ndcX = offCenter.X / offCenter.W;
        Assert.Equal(0.5f, ndcX, 3);
    }

    [Fact]
    public void CreateLookAt_CameraLookingDownZ_ProducesValidView()
    {
        Matrix4x4 view = Matrix4x4.CreateLookAt(new Vector3(0f, 0f, 5f), Vector3.Zero, Vector3.Up);
        MathAssert.Equal(new Vector3(0f, 0f, -5f), Vector3.TransformPoint(Vector3.Zero, view));
    }

    [Fact]
    public void Transpose_SwapsRowsAndColumns()
    {
        Matrix4x4 source = Matrix4x4.CreateTRS(new Vector3(1f, 2f, 3f), Quaternion.FromAxisAngle(Vector3.UnitZ, 0.5f), Vector3.One);
        Matrix4x4 transposed = source.Transpose();
        Assert.Equal(source.M12, transposed.M21, 6);
        Assert.Equal(source.M21, transposed.M12, 6);
        Assert.Equal(source.M34, transposed.M43, 6);
    }

    [Fact]
    public void Invert_NonInvertibleMatrix_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Matrix4x4.CreateScale(0f).Invert());
    }
}
