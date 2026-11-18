using Engine.Mathematics;

namespace Engine.Scene;

public sealed class Transform
{
    private Vector3 _localPosition = Vector3.Zero;
    private Quaternion _localRotation = Quaternion.Identity;
    private Vector3 _localScale = Vector3.One;
    private Matrix4x4 _localMatrix = Matrix4x4.Identity;
    private Matrix4x4 _worldMatrix = Matrix4x4.Identity;
    private Vector3 _renderPosition;
    private Quaternion _renderRotation = Quaternion.Identity;
    private bool _hasRenderPose;
    private bool _localDirty = true;
    private bool _worldDirty = true;

    public GameObject Owner { get; internal set; } = null!;

    public Transform? Parent => Owner.Parent?.Transform;

    public Vector3 LocalPosition
    {
        get => _localPosition;
        set
        {
            if (_localPosition == value)
                return;
            _localPosition = value;
            MarkDirty();
        }
    }

    public Quaternion LocalRotation
    {
        get => _localRotation;
        set
        {
            if (_localRotation == value)
                return;
            _localRotation = value;
            MarkDirty();
        }
    }

    public Vector3 LocalScale
    {
        get => _localScale;
        set
        {
            if (_localScale == value)
                return;
            _localScale = value;
            MarkDirty();
        }
    }

    public Vector3 Position
    {
        get
        {
            UpdateWorld();
            return new Vector3(_worldMatrix.M14, _worldMatrix.M24, _worldMatrix.M34);
        }
        set
        {
            if (Parent is { } parent)
            {
                Matrix4x4 parentInverse = parent.WorldMatrix.Invert();
                LocalPosition = Vector3.TransformPoint(value, parentInverse);
            }
            else
            {
                LocalPosition = value;
            }
        }
    }

    public Quaternion Rotation
    {
        get
        {
            UpdateWorld();
            Matrix4x4 world = _worldMatrix;
            world.Decompose(out _, out Quaternion rotation, out _);
            return rotation;
        }
        set
        {
            Quaternion parentRotation = Parent?.Rotation ?? Quaternion.Identity;
            LocalRotation = parentRotation.Inverse * value;
        }
    }

    public Vector3 Scale
    {
        get
        {
            UpdateWorld();
            _worldMatrix.Decompose(out Vector3 scale, out _, out _);
            return scale;
        }
    }

    public Vector3 Forward
    {
        get
        {
            UpdateWorld();
            return Vector3.TransformDirection(Vector3.Forward, _worldMatrix).Normalized;
        }
    }

    public Vector3 Right
    {
        get
        {
            UpdateWorld();
            return Vector3.TransformDirection(Vector3.Right, _worldMatrix).Normalized;
        }
    }

    public Vector3 Up
    {
        get
        {
            UpdateWorld();
            return Vector3.TransformDirection(Vector3.Up, _worldMatrix).Normalized;
        }
    }

    public Matrix4x4 LocalMatrix
    {
        get
        {
            UpdateLocal();
            return _localMatrix;
        }
    }

    public Matrix4x4 WorldMatrix
    {
        get
        {
            UpdateWorld();
            return _worldMatrix;
        }
    }

    public Matrix4x4 RenderWorldMatrix
    {
        get
        {
            UpdateWorld();
            if (!_hasRenderPose)
                return _worldMatrix;
            _worldMatrix.Decompose(out Vector3 scale, out _, out _);
            return Matrix4x4.CreateTRS(_renderPosition, _renderRotation, scale);
        }
    }

    public void SetRenderPose(Vector3 position, Quaternion rotation)
    {
        _renderPosition = position;
        _renderRotation = rotation;
        _hasRenderPose = true;
    }

    public void ClearRenderPose() => _hasRenderPose = false;

    public void SetLocalTRS(Vector3 position, Quaternion rotation, Vector3 scale)
    {
        _localPosition = position;
        _localRotation = rotation;
        _localScale = scale;
        MarkDirty();
    }

    public void Translate(Vector3 offset) => LocalPosition += offset;

    public void Rotate(Quaternion rotation) => LocalRotation = rotation * _localRotation;

    public void LookAt(Vector3 target, Vector3 up)
    {
        Vector3 forward = (target - Position).Normalized;
        if (forward.Length < MathUtil.Epsilon)
            return;
        Rotation = Quaternion.LookRotation(forward, up);
    }

    internal void MarkDirty()
    {
        _localDirty = true;
        _worldDirty = true;
        foreach (GameObject child in Owner.Children)
            child.Transform.PropagateDirty();
    }

    private void PropagateDirty()
    {
        _worldDirty = true;
        foreach (GameObject child in Owner.Children)
            child.Transform.PropagateDirty();
    }

    private void UpdateLocal()
    {
        if (!_localDirty)
            return;
        _localMatrix = Matrix4x4.CreateTRS(_localPosition, _localRotation, _localScale);
        _localDirty = false;
    }

    private void UpdateWorld()
    {
        UpdateLocal();
        if (!_worldDirty)
            return;
        Transform? parent = Parent;
        _worldMatrix = parent is not null ? parent.WorldMatrix * _localMatrix : _localMatrix;
        _worldDirty = false;
    }
}
