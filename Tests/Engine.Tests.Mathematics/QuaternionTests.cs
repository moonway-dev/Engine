using Engine.Mathematics;
using Xunit;

namespace Engine.Tests.Mathematics;

public class QuaternionTests
{
    [Fact]
    public void Identity_HasNoRotation()
    {
        MathAssert.Equal(new Vector3(0f, 0f, -1f), Quaternion.Identity.Rotate(Vector3.Forward));
        Assert.True(Quaternion.Identity.IsIdentity);
    }

    [Fact]
    public void AxisAngle_QuarterTurnAroundY_MatchesConvention()
    {
        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi);
        MathAssert.Equal(new Vector3(0f, 0f, -1f), rotation.Rotate(Vector3.UnitX));
        MathAssert.Equal(new Vector3(-1f, 0f, 0f), rotation.Rotate(Vector3.Forward));
    }

    [Fact]
    public void Multiplication_ComposesRotationInOrder()
    {
        Quaternion first = Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi);
        Quaternion second = Quaternion.FromAxisAngle(Vector3.UnitZ, MathUtil.HalfPi);
        MathAssert.Equal(first.Rotate(second.Rotate(Vector3.UnitX)), (first * second).Rotate(Vector3.UnitX));
    }

    [Fact]
    public void ToMatrix_RoundTripsThroughFromRotationMatrix()
    {
        Quaternion original = Quaternion.FromYawPitchRoll(0.3f, -0.7f, 1.2f);
        Quaternion recovered = Quaternion.FromRotationMatrix(original.ToMatrix());
        if (Quaternion.Dot(original, recovered) < 0f)
            recovered = -recovered;
        MathAssert.Equal(original, recovered);
    }

    [Fact]
    public void QuaternionFromYawPitchRoll_MatchesMatrixComposition()
    {
        float yaw = 0.4f;
        float pitch = -0.25f;
        float roll = 0.9f;
        Matrix4x4 fromQuaternion = Quaternion.FromYawPitchRoll(yaw, pitch, roll).ToMatrix();
        Matrix4x4 fromMatrix = Matrix4x4.CreateFromYawPitchRoll(yaw, pitch, roll);
        MathAssert.Equal(
            new Vector3(fromQuaternion.M11, fromQuaternion.M22, fromQuaternion.M33),
            new Vector3(fromMatrix.M11, fromMatrix.M22, fromMatrix.M33));
        MathAssert.Equal(
            new Vector3(fromQuaternion.M13, fromQuaternion.M31, fromQuaternion.M21),
            new Vector3(fromMatrix.M13, fromMatrix.M31, fromMatrix.M21));
    }

    [Fact]
    public void ToYawPitchRoll_RoundTrips()
    {
        Vector3 angles = new(0.45f, 0.3f, -0.6f);
        Quaternion rotation = Quaternion.FromYawPitchRoll(angles.X, angles.Y, angles.Z);
        Vector3 recovered = rotation.ToYawPitchRoll();
        Assert.Equal(angles.X, recovered.X, 5);
        Assert.Equal(angles.Y, recovered.Y, 5);
        Assert.Equal(angles.Z, recovered.Z, 5);
    }

    [Fact]
    public void Slerp_InterpolatesEndpoints()
    {
        Quaternion a = Quaternion.FromAxisAngle(Vector3.UnitY, 0f);
        Quaternion b = Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi);
        MathAssert.Equal(a, Quaternion.Slerp(a, b, 0f));
        MathAssert.Equal(b, Quaternion.Slerp(a, b, 1f));
        MathAssert.Equal(Quaternion.FromAxisAngle(Vector3.UnitY, MathUtil.HalfPi * 0.5f), Quaternion.Slerp(a, b, 0.5f));
    }

    [Fact]
    public void FromToRotation_AlignsVectors()
    {
        Vector3 target = new Vector3(1f, 1f, 0f).Normalized;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.UnitX, target);
        MathAssert.Equal(target, rotation.Rotate(Vector3.UnitX));
    }

    [Fact]
    public void LookRotation_ProducesRequestedForward()
    {
        Vector3 forward = new(0f, 0f, -1f);
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.Up);
        MathAssert.Equal(forward, rotation.Rotate(Vector3.Forward));
        MathAssert.Equal(Vector3.Up, rotation.Rotate(Vector3.Up));
    }

    [Fact]
    public void Inverse_UndoesRotation()
    {
        Quaternion rotation = Quaternion.FromYawPitchRoll(1.1f, 0.4f, -0.2f);
        Vector3 original = new(0.2f, 0.7f, -0.4f);
        MathAssert.Equal(original, rotation.Rotate(original).RotateBy(rotation.Inverse));
    }
}
