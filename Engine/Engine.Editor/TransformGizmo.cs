using Camera = Engine.Rendering.Camera;
using Light = Engine.Rendering.Light;
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
    private static readonly uint[] AxisColors = [0xFF6464D8, 0xFF65B96A, 0xFFD68B59];
    private const uint ActiveColor = 0xFFB8D477;
    private const uint CenterColor = 0xFFD8D8D8;
    private const float AxisLineWidth = 2f;
    private const float ActiveAxisLineWidth = 3f;
    private const float AxisHitRadius = 13f;
    private const float RingLineWidth = 2f;
    private const float ActiveRingLineWidth = 3.5f;
    private const float RingHitRadius = 8f;
    private const int RingSegments = 64;

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
    private Vector3 _rotatePlaneCenter;
    private Vector3 _rotatePlaneAxis;
    private Vector3 _rotatePlaneGrab;
    private bool _rotatePlaneValid;

    public TransformGizmo(EditorContext context, RendererModule renderer)
    {
        _context = context;
        _renderer = renderer;
    }

    public void CancelDrag()
    {
        _objectId = null;
        _axisIndex = -1;
    }

    public bool Draw(ViewportPanel viewport)
    {
        if (!viewport.CanDrawOverlay)
        {
            CancelDrag();
            return false;
        }

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
                UpdateDrag(active.Transform, ImGui.GetMousePos(), viewport);
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
        ImDrawListPtr drawList = viewport.DrawList;
        if (!HasNativeDrawList(drawList))
            return false;
        drawList.PushClipRect(viewport.ImageMinimum, viewport.ImageMinimum + viewport.ImageSize, true);

        if (frame.Mode == TransformMode.Rotate)
        {
            NumericsVector2[][] rings = DrawRotationRings(drawList, viewport, transform.Position, frame, worldLength, origin);
            drawList.PopClipRect();
            consumed = HandleClick(viewport, mouse =>
            {
                int hit = FindRing(mouse, rings);
                if (hit < 0)
                    return false;
                BeginDrag(selected, frame, hit, origin, mouse, worldLength, mouse, viewport);
                return true;
            });
            return consumed || _objectId is not null;
        }

        Vector3[] axes = [frame.Axis(0), frame.Axis(1), frame.Axis(2)];
        NumericsVector2[] endpoints = new NumericsVector2[3];
        bool[] visible = new bool[3];
        for (int index = 0; index < axes.Length; index++)
        {
            visible[index] = TryProject(transform.Position + axes[index] * worldLength, viewport, out endpoints[index])
                && NumericsVector2.DistanceSquared(origin, endpoints[index]) >= 36f;
        }
        bool dragging = _objectId == selected.Id;
        for (int index = 0; index < axes.Length; index++)
        {
            if (!visible[index])
                continue;
            bool active = dragging && _axisIndex == index;
            uint color = active ? ActiveColor : AxisColors[index];
            NumericsVector2 direction = NumericsVector2.Normalize(endpoints[index] - origin);
            drawList.AddLine(origin, endpoints[index], color, active ? ActiveAxisLineWidth : AxisLineWidth);
            DrawHandle(drawList, endpoints[index], direction, color, frame.Mode);
        }
        drawList.PopClipRect();
        consumed = HandleClick(viewport, mouse =>
        {
            int hit = FindAxis(mouse, origin, endpoints, visible);
            if (hit < 0)
                return false;
            BeginDrag(selected, frame, hit, origin, endpoints[hit], worldLength, mouse, viewport);
            return true;
        });
        return consumed || _objectId is not null;
    }

    private bool HandleClick(ViewportPanel viewport, Func<NumericsVector2, bool> tryBegin)
    {
        if (_objectId is not null || !viewport.ImageClicked)
            return false;
        return tryBegin(ImGui.GetMousePos());
    }

    private NumericsVector2[][] DrawRotationRings(
        ImDrawListPtr drawList,
        ViewportPanel viewport,
        Vector3 center,
        GizmoFrame frame,
        float radius,
        NumericsVector2 origin)
    {
        NumericsVector2[][] rings = new NumericsVector2[3][];
        bool dragging = _objectId is not null;
        for (int index = 0; index < 3; index++)
        {
            Vector3 axis = frame.Axis(index);
            (Vector3 firstBasis, Vector3 secondBasis) = OrthonormalBasis(axis);
            NumericsVector2[] points = new NumericsVector2[RingSegments + 1];
            bool[] projected = new bool[RingSegments + 1];
            for (int segment = 0; segment <= RingSegments; segment++)
            {
                float angle = MathF.Tau * segment / RingSegments;
                Vector3 offset = firstBasis * MathF.Cos(angle) + secondBasis * MathF.Sin(angle);
                projected[segment] = TryProject(center + offset * radius, viewport, out points[segment]);
            }
            bool active = dragging && _axisIndex == index;
            uint color = active ? ActiveColor : (AxisColors[index] & 0xFFFFFF) | 0xB0000000;
            float width = active ? ActiveRingLineWidth : RingLineWidth;
            for (int segment = 0; segment < RingSegments; segment++)
            {
                if (projected[segment] && projected[segment + 1])
                    drawList.AddLine(points[segment], points[segment + 1], color, width);
            }
            rings[index] = points;
        }
        drawList.AddCircleFilled(origin, 4f, CenterColor);
        return rings;
    }

    private static (Vector3 First, Vector3 Second) OrthonormalBasis(Vector3 axis)
    {
        Vector3 helper = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        Vector3 first = Vector3.Cross(axis, helper).Normalized;
        Vector3 second = Vector3.Cross(axis, first).Normalized;
        return (first, second);
    }

    private static int FindRing(NumericsVector2 mouse, NumericsVector2[][] rings)
    {
        int result = -1;
        float closest = RingHitRadius;
        for (int index = 0; index < rings.Length; index++)
        {
            NumericsVector2[] points = rings[index];
            for (int segment = 0; segment + 1 < points.Length; segment++)
            {
                float distance = DistanceToSegment(mouse, points[segment], points[segment + 1]);
                if (distance >= closest)
                    continue;
                closest = distance;
                result = index;
            }
        }
        return result;
    }

    private static unsafe bool HasNativeDrawList(ImDrawListPtr drawList) => drawList.NativePtr != null;

    private void BeginDrag(
        GameObject selected,
        GizmoFrame frame,
        int axisIndex,
        NumericsVector2 origin,
        NumericsVector2 endpoint,
        float worldLength,
        NumericsVector2 mouse,
        ViewportPanel viewport)
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
        _rotatePlaneValid = false;
        if (frame.Mode == TransformMode.Rotate)
        {
            _rotatePlaneCenter = selected.Transform.Position;
            _rotatePlaneAxis = frame.Axis(axisIndex);
            _rotatePlaneValid = TryPlanePoint(viewport, mouse, _rotatePlaneCenter, _rotatePlaneAxis, out _rotatePlaneGrab);
        }
    }

    private void UpdateDrag(Transform transform, NumericsVector2 mouse, ViewportPanel viewport)
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
                float angle;
                if (_rotatePlaneValid && TryPlanePoint(viewport, mouse, _rotatePlaneCenter, _rotatePlaneAxis, out Vector3 current))
                {
                    Vector3 from = _rotatePlaneGrab - _rotatePlaneCenter;
                    Vector3 to = current - _rotatePlaneCenter;
                    angle = SignedAngle(from, to, _rotatePlaneAxis);
                }
                else
                {
                    NumericsVector2 startDirection = _startMouse - _originScreen;
                    NumericsVector2 currentDirection = mouse - _originScreen;
                    angle = startDirection.LengthSquared() > 16f && currentDirection.LengthSquared() > 16f
                        ? MathF.Atan2(
                            startDirection.X * currentDirection.Y - startDirection.Y * currentDirection.X,
                            NumericsVector2.Dot(startDirection, currentDirection))
                        : pixels * 0.01f;
                }
                transform.Rotation = _frame.Rotate(_startRotation, _axisIndex, angle);
                break;
            case TransformMode.Scale:
                Vector3 scale = _startScale;
                float value = GetAxis(scale, _axisIndex) + pixels / 80f;
                SetAxis(ref scale, _axisIndex, value);
                transform.LocalScale = EditorContext.ClampScale(scale);
                break;
        }
    }

    private bool TryPlanePoint(ViewportPanel viewport, NumericsVector2 mouseScreen, Vector3 center, Vector3 axis, out Vector3 point)
    {
        point = default;
        NumericsVector2 size = viewport.ImageSize;
        NumericsVector2 normalized = new(
            (mouseScreen.X - viewport.ImageMinimum.X) / MathF.Max(size.X, 1f),
            (mouseScreen.Y - viewport.ImageMinimum.Y) / MathF.Max(size.Y, 1f));
        PickingRay ray = ScenePicking.CreateRay(_renderer.EditorCamera, new Vector2(normalized.X, normalized.Y));
        float denominator = Vector3.Dot(ray.Direction, axis);
        if (MathF.Abs(denominator) < MathUtil.Epsilon)
            return false;
        float distance = Vector3.Dot(center - ray.Origin, axis) / denominator;
        if (distance <= MathUtil.Epsilon)
            return false;
        point = ray.Origin + ray.Direction * distance;
        return true;
    }

    private static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis) =>
        MathF.Atan2(Vector3.Dot(Vector3.Cross(from, to), axis), Vector3.Dot(from, to));

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
        float closest = AxisHitRadius;
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
                NumericsVector2 basePoint = endpoint - direction * 14f;
                drawList.AddTriangleFilled(endpoint, basePoint + perpendicular * 6f, basePoint - perpendicular * 6f, color);
                break;
            case TransformMode.Rotate:
                drawList.AddCircle(endpoint, 5f, color, 16, 2f);
                break;
            case TransformMode.Scale:
                NumericsVector2 half = new(6f, 6f);
                drawList.AddRectFilled(endpoint - half, endpoint + half, color);
                drawList.AddRect(endpoint - half, endpoint + half, 0xFF202020);
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
