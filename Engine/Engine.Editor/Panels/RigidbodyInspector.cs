using System.Reflection;
using Engine.Editor.Inspection;
using Engine.Physics;
using Engine.Scene;
using ImGuiNET;

namespace Engine.Editor.Panels;

internal static class RigidbodyInspector
{
    private static readonly string[] Properties =
    [
        nameof(RigidBodyComponent.Kind),
        nameof(RigidBodyComponent.Mass),
        nameof(RigidBodyComponent.LinearDamping),
        nameof(RigidBodyComponent.AngularDamping),
        nameof(RigidBodyComponent.UseGravity),
        nameof(RigidBodyComponent.IsKinematic),
        nameof(RigidBodyComponent.DetectCollisions),
        nameof(RigidBodyComponent.Interpolation),
        nameof(RigidBodyComponent.CollisionDetection),
    ];

    internal static void Register() => ComponentInspectorHooks.Register<RigidBodyComponent>((context, body) =>
    {
        Draw(context, body);
        return true;
    });

    private static void Draw(EditorContext context, RigidBodyComponent body)
    {
        foreach (string property in Properties)
            ComponentPropertyInspector.DrawProperty(context, body, typeof(RigidBodyComponent).GetProperty(property)!);
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Constraints", ImGuiTreeNodeFlags.DefaultOpen))
        {
            DrawAxis(context, body, "Freeze Position X", RigidbodyConstraints.FreezePositionX);
            DrawAxis(context, body, "Freeze Position Y", RigidbodyConstraints.FreezePositionY);
            DrawAxis(context, body, "Freeze Position Z", RigidbodyConstraints.FreezePositionZ);
            DrawAxis(context, body, "Freeze Rotation X", RigidbodyConstraints.FreezeRotationX);
            DrawAxis(context, body, "Freeze Rotation Y", RigidbodyConstraints.FreezeRotationY);
            DrawAxis(context, body, "Freeze Rotation Z", RigidbodyConstraints.FreezeRotationZ);
        }
        ImGui.Spacing();
        ImGui.TextUnformatted(body.IsSleeping ? "Sleeping" : body.IsAwake ? "Awake" : "Inactive");
        InspectorEditing.DrawItemTooltip("Sleep may be interrupted by contact or forces. Disable the component to remove the object from physics.");
        if (ImGui.SmallButton("Sleep"))
            body.Sleep();
        ImGui.SameLine();
        if (ImGui.SmallButton("Wake Up"))
            body.WakeUp();
    }

    private static void DrawAxis(EditorContext context, RigidBodyComponent body, string label, RigidbodyConstraints flag)
    {
        bool frozen = (body.Constraints & flag) != 0;
        if (ImGui.Checkbox(label, ref frozen))
        {
            RigidbodyConstraints constraints = frozen ? body.Constraints | flag : body.Constraints & ~flag;
            InspectorEditing.SetComponentProperty(context, body.Id, nameof(RigidBodyComponent.Constraints),
                current => ((RigidBodyComponent)current).Constraints,
                (current, value) => ((RigidBodyComponent)current).Constraints = value, constraints, false);
        }
        InspectorEditing.DrawItemTooltip($"Prevent physics motion along this axis. {label}.");
    }
}
