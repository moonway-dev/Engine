using Engine.Editor;
using Engine.Mathematics;
using Engine.Editor.Panels;
using System;
using Xunit;

namespace Engine.Tests.Rendering;

public sealed class GizmoFrameTests
{
    [Fact]
    public void LocalAxesFollowObjectWhileGlobalAxesStayFixed()
    {
        Quaternion rotation = Quaternion.FromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        GizmoFrame local = new(TransformMode.Translate, TransformSpace.Local, rotation);
        GizmoFrame global = new(TransformMode.Translate, TransformSpace.Global, rotation);
        Assert.True((local.Axis(0) - Vector3.UnitY).Length < 0.0001f);
        Assert.Equal(Vector3.UnitX, global.Axis(0));
        Assert.True((local.Translate(Vector3.Zero, 0, 2) - new Vector3(0, 2, 0)).Length < 0.0001f);
        Assert.Equal(new Vector3(2, 0, 0), global.Translate(Vector3.Zero, 0, 2));
    }

    [Fact]
    public void RotationUsesCapturedCoordinateSpace()
    {
        Quaternion start = Quaternion.FromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        GizmoFrame local = new(TransformMode.Rotate, TransformSpace.Local, start);
        GizmoFrame global = new(TransformMode.Rotate, TransformSpace.Global, start);
        Vector3 localResult = local.Rotate(start, 0, MathF.PI / 2).Rotate(Vector3.UnitZ);
        Vector3 globalResult = global.Rotate(start, 0, MathF.PI / 2).Rotate(Vector3.UnitZ);
        Assert.True((localResult - Vector3.UnitX).Length < 0.0001f);
        Assert.True((globalResult + Vector3.UnitY).Length < 0.0001f);
    }

    [Fact]
    public void ScaleExplicitlyUsesLocalAxes()
    {
        GizmoFrame frame = new(TransformMode.Scale, TransformSpace.Global,
            Quaternion.FromAxisAngle(Vector3.UnitZ, MathF.PI / 2));
        Assert.Equal(TransformSpace.Local, frame.EffectiveSpace);
        Assert.True((frame.Axis(0) - Vector3.UnitY).Length < 0.0001f);
    }

    [Fact]
    public void ClampScale_RepairsZeroOrNonFiniteComponentsWithoutChangingValidSigns()
    {
        Vector3 scale = EditorContext.ClampScale(new Vector3(2f, 0f, float.NaN));

        Assert.Equal(new Vector3(2f, EditorContext.MinimumScaleMagnitude, EditorContext.MinimumScaleMagnitude), scale);
        Assert.Equal(new Vector3(-EditorContext.MinimumScaleMagnitude, -2f, 3f),
            EditorContext.ClampScale(new Vector3(-0.001f, -2f, 3f)));
    }
}
