using Engine.Mathematics;
using Engine.Platform;
using Engine.Scripting;

public sealed class PlayerController : ScriptBehaviour
{
    [InspectorRange(0.5f, 20f)]
    public float Speed = 4f;

    public override void OnUpdate()
    {
        Vector3 movement = Vector3.Zero;
        if (ScriptInput.IsKeyDown(KeyCode.W))
            movement += Vector3.Forward;
        if (ScriptInput.IsKeyDown(KeyCode.S))
            movement -= Vector3.Forward;
        if (ScriptInput.IsKeyDown(KeyCode.A))
            movement -= Vector3.Right;
        if (ScriptInput.IsKeyDown(KeyCode.D))
            movement += Vector3.Right;
        if (ScriptInput.IsKeyDown(KeyCode.Space))
            movement += Vector3.UnitY;
        if (ScriptInput.IsKeyDown(KeyCode.LeftControl))
            movement -= Vector3.UnitY;
        if (movement.Length > MathUtil.Epsilon)
            Transform.Position += movement.Normalized * Speed * ScriptTime.DeltaTime;
    }
}
