using System;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Engine.Core;
using Engine.Graphics;
using Engine.Math;

namespace Engine.Editor;

public class FreeLookCameraController
{
    private Camera _camera;
    private float _yaw = -90.0f;
    private float _pitch = 0.0f;
    private float _movementSpeed = 5.0f;
    private float _rotationSensitivity = 0.1f;
    private float _zoomSensitivity = 0.3f;
    private float _speedScrollSensitivity = 2.0f;
    private bool _firstMouse = true;
    private bool _mouseCaptured = false;

    public float MovementSpeed
    {
        get => _movementSpeed;
        set => _movementSpeed = MathF.Max(0.1f, value);
    }

    public float RotationSensitivity
    {
        get => _rotationSensitivity;
        set => _rotationSensitivity = MathF.Max(0.01f, value);
    }

    public FreeLookCameraController(Camera camera)
    {
        _camera = camera;
    }

    public bool IsCapturing => _mouseCaptured;

    private const int RightMouseButton = 1;

    public void Update(GameWindow window, float deltaTime, bool canCaptureMouse = true)
    {
        if (!canCaptureMouse && !_mouseCaptured)
        {
            return;
        }

        if (!_mouseCaptured && canCaptureMouse && Input.GetMouseButtonDown(RightMouseButton))
        {
            _mouseCaptured = true;
            window.CursorState = CursorState.Grabbed;
            _firstMouse = true;
            Input.OverrideMousePosition(Input.MousePosition);
        }

        if (_mouseCaptured && Input.GetMouseButtonUp(RightMouseButton))
        {
            _mouseCaptured = false;
            window.CursorState = CursorState.Normal;
        }

        if (_mouseCaptured)
        {
            ApplyScrollSpeed();
            ProcessMouseMovement();
            ProcessKeyboardInput(deltaTime);
        }
    }

    private void ProcessMouseMovement()
    {
        var mouseDelta = Input.MouseDelta;

        if (_firstMouse)
        {
            _firstMouse = false;
            return;
        }

        bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);

        float xOffset = mouseDelta.X * _rotationSensitivity;
        float yOffset = mouseDelta.Y * _rotationSensitivity;

        if (altHeld)
        {
            AdjustFieldOfView(yOffset);
            return;
        }

        _yaw -= xOffset;
        _pitch += yOffset;

        if (_pitch > 89.0f)
            _pitch = 89.0f;
        if (_pitch < -89.0f)
            _pitch = -89.0f;

        float yawRad = _yaw * MathF.PI / 180.0f;
        float pitchRad = _pitch * MathF.PI / 180.0f;
        
        Quaternion yawRotation = Quaternion.FromAxisAngle(Vector3.Up, yawRad);
        Quaternion pitchRotation = Quaternion.FromAxisAngle(Vector3.Right, pitchRad);
        _camera.Rotation = yawRotation * pitchRotation;
    }

    private void AdjustFieldOfView(float mouseYOffset)
    {
        float fovDegrees = _camera.FOV * (180f / MathF.PI);
        fovDegrees -= mouseYOffset * _zoomSensitivity;
        fovDegrees = System.Math.Clamp(fovDegrees, 25f, 120f);
        _camera.FOV = fovDegrees * (MathF.PI / 180f);
    }

    private void ProcessKeyboardInput(float deltaTime)
    {
        bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        float speedMultiplier = shiftHeld ? 2.0f : 1.0f;
        float velocity = _movementSpeed * speedMultiplier * deltaTime;

        Vector3 position = _camera.Position;
        Vector3 forward = _camera.Forward;
        Vector3 right = _camera.Right;
        Vector3 up = _camera.Up;

        if (Input.GetKey(KeyCode.W))
            position += forward * velocity;
        if (Input.GetKey(KeyCode.S))
            position -= forward * velocity;
        if (Input.GetKey(KeyCode.A))
            position -= right * velocity;
        if (Input.GetKey(KeyCode.D))
            position += right * velocity;
        if (Input.GetKey(KeyCode.Q))
            position -= up * velocity;
        if (Input.GetKey(KeyCode.E))
            position += up * velocity;

        _camera.Position = position;
    }

    private void ApplyScrollSpeed()
    {
        float scroll = Input.ScrollDelta;
        if (MathF.Abs(scroll) < 0.0001f)
            return;

        _movementSpeed += scroll * _speedScrollSensitivity;
        _movementSpeed = System.Math.Clamp(_movementSpeed, 0.5f, 100f);
    }
}

