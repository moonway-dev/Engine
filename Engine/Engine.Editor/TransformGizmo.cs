using Engine.Editor.Commands;
using Engine.Editor.Panels;
using Engine.Mathematics;
using Engine.Rendering;
using Engine.Scene;
using ImGuiNET;
using NumericsVector2 = System.Numerics.Vector2;

namespace Engine.Editor;

internal sealed class TransformGizmo
{
    private static readonly uint[] AxisColors = [0xFF4D4DFF, 0xFF4DFF4D, 0xFFFF8040];
    private const uint ActiveColor = 0xFF40FFFF;

    private readonly EditorContext _context;
    private readonly RendererModule _renderer;
    private Guid? _objectId;
    private GizmoFrame _frame;
    private int _axisIndex = -1;
    private Vector3 _startPosition;
    private Quaternion _startRotation;
    private Vector3 _startLocalPosition;
    private Quaternion _startLocalRotation;
    private Vector3 _startScale;
    private NumericsVector2 _startMouse;
    private NumericsVector2 _originScreen;
    private NumericsVector2 _axisScreen;
    private float _worldLength;

    public TransformGizmo(EditorContext context, RendererModule renderer)
    {
        _context = context;
        _renderer = renderer;
    }

    public bool Draw(ViewportPanel viewport)
    {
        GameObject? selected = _context.Document.SelectedObject;
        bool consumed = _objectId is not null;
        if (_objectId is Guid activeId)
        {
            GameObject? active = _context.Document.Scene.FindById(activeId);
            if (active is null)
                _objectId = null;
            else if (selected?.Id != activeId || !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                EndDrag(active);
            else
                UpdateDrag(active.Transform, ImGui.GetMousePos());
        }
        if (selected is null || viewport.ImageSize.X <= 0f || viewport.ImageSize.Y <= 0f)
            return consumed;

        Transform transform = selected.Transform;
        if (!TryProject(transform.Position, viewport, out NumericsVector2 origin))
            return false;

        float distance = MathF.Max((_renderer.EditorCamera.Position - transform.Position).Length, 0.1f);
        float worldLength = MathF.Max(0.2f, distance * MathF.Tan(_renderer.EditorCamera.FieldOfViewY * 0.5f) * 180f / MathF.Max(viewport.ImageSize.Y, 1f));
        GizmoFrame frame = _objectId == selected.Id
            ? _frame
            : new GizmoFrame(_context.TransformMode, _context.TransformSpace, transform.Rotation);
        if (_objectId == selected.Id)
            worldLength = _worldLength;
        Vector3[] axes = [frame.Axis(0), frame.Axis(1), frame.Axis(2)];
        NumericsVector2[] endpoints = new NumericsVector2[3];
        bool[] visible = new bool[3];
        for (int index = 0; index < axes.Length; index++)
        {
            visible[index] = TryProject(transform.Position + axes[index] * worldLength, viewport, out endpoints[index])
                && NumericsVector2.DistanceSquared(origin, endpoints[index]) >= 36f;
        }

        ImDrawListPtr drawList = ImGui.GetForegroundDrawList();
        drawList.PushClipRect(viewport.ImageMinimum, viewport.ImageMinimum + viewport.ImageSize, true);
        for (int index = 0; index < axes.Length; index++)
        {
            if (!visible[index])
                continue;
            uint color = _objectId == selected.Id && _axisIndex == index ? ActiveColor : AxisColors[index];
            NumericsVector2 direction = NumericsVector2.Normalize(endpoints[index] - origin);
            drawList.AddLine(origin, endpoints[index], color, _objectId == selected.Id && _axisIndex == index ? 4f : 3f);
            DrawHandle(drawList, endpoints[index], direction, color, frame.Mode);
        }
        drawList.PopClipRect();

        if (_objectId is null && viewport.ImageClicked)
        {
            NumericsVector2 mouse = ImGui.GetMousePos();
            int hitAxis = FindAxis(mouse, origin, endpoints, visible);
            if (hitAxis >= 0)
            {
                BeginDrag(selected, frame, hitAxis, origin, endpoints[hitAxis], worldLength, mouse);
                consumed = true;
            }
        }

        return consumed || _objectId is not null;
    }

    private void BeginDrag(
        GameObject selected,
        GizmoFrame frame,
        int axisIndex,
        NumericsVector2 origin,
        NumericsVector2 endpoint,
        float worldLength,
        NumericsVector2 mouse)
    {
        _objectId = selected.Id;
        _frame = frame;
        _axisIndex = axisIndex;
        _startPosition = selected.Transform.Position;
        _startRotation = selected.Transform.Rotation;
        _startLocalPosition = selected.Transform.LocalPosition;
        _startLocalRotation = selected.Transform.LocalRotation;
        _startScale = selected.Transform.LocalScale;
        _startMouse = mouse;
        _originScreen = origin;
        _axisScreen = endpoint - origin;
        _worldLength = worldLength;
    }

    private void UpdateDrag(Transform transform, NumericsVector2 mouse)
    {
        NumericsVector2 delta = mouse - _startMouse;
        float screenLength = MathF.Max(_axisScreen.Length(), 1f);
        NumericsVector2 screenAxis = _axisScreen / screenLength;
        float pixels = NumericsVector2.Dot(delta, screenAxis);
        switch (_frame.Mode)
        {
            case TransformMode.Translate:
                transform.Position = _frame.Translate(_startPosition, _axisIndex, pixels * _worldLength / screenLength);
                break;
            case TransformMode.Rotate:
                NumericsVector2 startDirection = _startMouse - _originScreen;
                NumericsVector2 currentDirection = mouse - _originScreen;
                float angle = startDirection.LengthSquared() > 16f && currentDirection.LengthSquared() > 16f
                    ? MathF.Atan2(
                        startDirection.X * currentDirection.Y - startDirection.Y * currentDirection.X,
                        NumericsVector2.Dot(startDirection, currentDirection))
                    : pixels * 0.01f;
                transform.Rotation = _frame.Rotate(_startRotation, _axisIndex, angle);
                break;
            case TransformMode.Scale:
                Vector3 scale = _startScale;
                float value = MathF.Max(0.01f, GetAxis(scale, _axisIndex) + pixels / 80f);
                SetAxis(ref scale, _axisIndex, value);
                transform.LocalScale = scale;
                break;
        }
    }

    private void EndDrag(GameObject gameObject)
    {
        Guid id = gameObject.Id;
        switch (_frame.Mode)
        {
            case TransformMode.Translate when gameObject.Transform.LocalPosition != _startLocalPosition:
                Execute("Move", id, "position", value => value.Transform.LocalPosition, (value, next) => value.Transform.LocalPosition = next, _startLocalPosition, gameObject.Transform.LocalPosition);
                break;
            case TransformMode.Rotate when gameObject.Transform.LocalRotation != _startLocalRotation:
                Execute("Rotate", id, "rotation", value => value.Transform.LocalRotation, (value, next) => value.Transform.LocalRotation = next, _startLocalRotation, gameObject.Transform.LocalRotation);
                break;
            case TransformMode.Scale when gameObject.Transform.LocalScale != _startScale:
                Execute("Scale", id, "scale", value => value.Transform.LocalScale, (value, next) => value.Transform.LocalScale = next, _startScale, gameObject.Transform.LocalScale);
                break;
        }
        _objectId = null;
        _axisIndex = -1;
    }

    private void Execute<T>(
        string name,
        Guid objectId,
        string property,
        Func<GameObject, T> read,
        Action<GameObject, T> write,
        T before,
        T after)
    {
        _context.Document.Execute(new PropertyCommand<T>(
            name,
            (objectId, property),
            () => read(Resolve(objectId)),
            value => write(Resolve(objectId), value),
            before,
            after), false);
    }

    private GameObject Resolve(Guid id) => _context.Document.Scene.FindById(id)
        ?? throw new InvalidOperationException("The transformed GameObject no longer exists.");

    private bool TryProject(Vector3 world, ViewportPanel viewport, out NumericsVector2 screen)
    {
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), _renderer.EditorCamera.ViewProjection);
        if (clip.W <= MathUtil.Epsilon)
        {
            screen = default;
            return false;
        }
        float x = clip.X / clip.W;
        float y = clip.Y / clip.W;
        screen = viewport.ImageMinimum + new NumericsVector2(
            (x * 0.5f + 0.5f) * viewport.ImageSize.X,
            (y * 0.5f + 0.5f) * viewport.ImageSize.Y);
        return true;
    }

    private static int FindAxis(NumericsVector2 mouse, NumericsVector2 origin, NumericsVector2[] endpoints, bool[] visible)
    {
        int result = -1;
        float closest = 10f;
        for (int index = 0; index < endpoints.Length; index++)
        {
            if (!visible[index])
                continue;
            float distance = DistanceToSegment(mouse, origin, endpoints[index]);
            if (distance >= closest)
                continue;
            closest = distance;
            result = index;
        }
        return result;
    }

    private static float DistanceToSegment(NumericsVector2 point, NumericsVector2 start, NumericsVector2 end)
    {
        NumericsVector2 segment = end - start;
        float lengthSquared = segment.LengthSquared();
        if (lengthSquared <= float.Epsilon)
            return NumericsVector2.Distance(point, start);
        float amount = Math.Clamp(NumericsVector2.Dot(point - start, segment) / lengthSquared, 0f, 1f);
        return NumericsVector2.Distance(point, start + segment * amount);
    }

    private static void DrawHandle(ImDrawListPtr drawList, NumericsVector2 endpoint, NumericsVector2 direction, uint color, TransformMode mode)
    {
        switch (mode)
        {
            case TransformMode.Translate:
                NumericsVector2 perpendicular = new(-direction.Y, direction.X);
                NumericsVector2 basePoint = endpoint - direction * 11f;
                drawList.AddTriangleFilled(endpoint, basePoint + perpendicular * 5f, basePoint - perpendicular * 5f, color);
                break;
            case TransformMode.Rotate:
                drawList.AddCircle(endpoint, 6f, color, 16, 3f);
                break;
            case TransformMode.Scale:
                NumericsVector2 half = new(5f, 5f);
                drawList.AddRectFilled(endpoint - half, endpoint + half, color);
                break;
        }
    }

    private static float GetAxis(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z,
    };

    private static void SetAxis(ref Vector3 value, int axis, float component)
    {
        switch (axis)
        {
            case 0:
                value.X = component;
                break;
            case 1:
                value.Y = component;
                break;
            default:
                value.Z = component;
                break;
        }
    }
}
