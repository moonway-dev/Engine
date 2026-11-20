using System.Reflection;
using Engine.Core;
using Engine.Mathematics;
using Engine.Platform;
using Engine.Scripting;
using Xunit;

namespace Engine.Tests.Scripting;

public sealed class ScriptInputTests : IDisposable
{
    public void Dispose()
    {
        ScriptInput.RemoveAxis("Boost");
        ScriptInput.Window = null;
    }

    private static IWindow CreateWindow(HashSet<KeyCode> keys, Vector2 mouseDelta = default, Vector2 scroll = default)
    {
        KeyboardState keyboard = new();
        HashSet<KeyCode> current = (HashSet<KeyCode>)typeof(KeyboardState).GetField("_current", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(keyboard)!;
        foreach (KeyCode key in keys)
            current.Add(key);
        IWindow window = DispatchProxy.Create<IWindow, PlayerControllerTests.InputWindowProxy>();
        ((PlayerControllerTests.InputWindowProxy)window).Keyboard = keyboard;
        typeof(MouseState).GetProperty(nameof(MouseState.Delta))!.SetValue(window.Mouse, mouseDelta);
        typeof(MouseState).GetProperty(nameof(MouseState.ScrollDelta))!.SetValue(window.Mouse, scroll);
        return window;
    }

    [Theory]
    [InlineData(KeyCode.D, 1f)]
    [InlineData(KeyCode.A, -1f)]
    [InlineData(KeyCode.Right, 1f)]
    [InlineData(KeyCode.Left, -1f)]
    public void BuiltInHorizontalAxisFollowsBothKeyPairs(KeyCode key, float expected)
    {
        ScriptInput.Window = CreateWindow([key]);
        Assert.Equal(expected, ScriptInput.GetAxis("Horizontal"), 4);
    }

    [Theory]
    [InlineData(KeyCode.W, 1f)]
    [InlineData(KeyCode.S, -1f)]
    [InlineData(KeyCode.Up, 1f)]
    [InlineData(KeyCode.Down, -1f)]
    public void BuiltInVerticalAxisFollowsBothKeyPairs(KeyCode key, float expected)
    {
        ScriptInput.Window = CreateWindow([key]);
        Assert.Equal(expected, ScriptInput.GetAxis("Vertical"), 4);
    }

    [Fact]
    public void MouseAxesScaleDeltas()
    {
        ScriptInput.Window = CreateWindow([], new Vector2(25f, -10f), new Vector2(0f, 3f));
        Assert.Equal(2.5f, ScriptInput.GetAxis("Mouse X"), 4);
        Assert.Equal(-1f, ScriptInput.GetAxis("Mouse Y"), 4);
        Assert.Equal(0.3f, ScriptInput.GetAxis("Mouse ScrollWheel"), 4);
    }

    [Fact]
    public void CustomAxisAppliesDeadZoneAndSensitivity()
    {
        ScriptInput.DefineAxis("Boost", KeyCode.K, KeyCode.L, sensitivity: 2f, deadZone: 0.25f);
        Assert.Equal(0f, ScriptInput.GetAxis("Boost"));
        ScriptInput.Window = CreateWindow([KeyCode.L]);
        Assert.Equal(2f, ScriptInput.GetAxis("Boost"), 4);
        ScriptInput.RemoveAxis("Boost");
        Assert.Equal(0f, ScriptInput.GetAxis("Boost"));
    }

    [Fact]
    public void TryGetAxisReportsKnownAndUnknownNames()
    {
        Assert.True(ScriptInput.TryGetAxis("Horizontal", out float horizontal));
        Assert.Equal(0f, horizontal, 4);
        Assert.True(ScriptInput.TryGetAxis("Mouse X", out _));
        Assert.False(ScriptInput.TryGetAxis("Missing", out _));
    }

    [Fact]
    public void CursorVisibilityPreservesLockState()
    {
        IWindow window = CreateWindow([]);
        ScriptInput.Window = window;
        ScriptInput.SetCursorLocked(true);
        Assert.True(ScriptInput.IsCursorLocked);
        Assert.False(ScriptInput.IsCursorVisible);
        ScriptInput.SetCursorVisible(true);
        Assert.True(ScriptInput.IsCursorLocked);
        ScriptInput.SetCursorLocked(false);
        ScriptInput.SetCursorVisible(false);
        Assert.False(ScriptInput.IsCursorVisible);
        ScriptInput.SetCursorVisible(true);
        Assert.True(ScriptInput.IsCursorVisible);
    }
}
