using System.Runtime.InteropServices;
using System.Text;
using Engine.Editor.Models;
using ImGuiNET;

namespace Engine.Editor.DragDrop;

public readonly record struct AssetDragPayload(AssetKind Kind, string RelativePath)
{
    public const string PayloadType = "ENGINE_ASSET";

    public string Serialize() => $"{(int)Kind}|{RelativePath}";

    public static bool TryParse(string value, out AssetDragPayload payload)
    {
        payload = default;
        int separator = value.IndexOf('|');
        if (separator <= 0 || !int.TryParse(value.AsSpan(0, separator), out int kind)
            || !Enum.IsDefined(typeof(AssetKind), kind))
            return false;
        string path = value[(separator + 1)..];
        if (string.IsNullOrWhiteSpace(path))
            return false;
        payload = new AssetDragPayload((AssetKind)kind, path);
        return true;
    }
}

public static class ImGuiDragPayloads
{
    public const string GameObjectPayloadType = "ENGINE_GAME_OBJECT";
    public const string SceneOrderPayloadType = "ENGINE_SCENE_ORDER";

    public static unsafe void SetAsset(AssetDragPayload payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(payload.Serialize());
        fixed (byte* data = bytes)
            ImGui.SetDragDropPayload(AssetDragPayload.PayloadType, (IntPtr)data, (uint)bytes.Length);
    }

    public static unsafe void SetSceneOrder(int index)
    {
        int* data = &index;
        ImGui.SetDragDropPayload(SceneOrderPayloadType, (IntPtr)data, sizeof(int));
    }

    public static unsafe int? AcceptSceneOrder()
    {
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(SceneOrderPayloadType);
        if (payload.NativePtr == null || !payload.IsDelivery() || payload.Data == IntPtr.Zero || payload.DataSize != sizeof(int))
            return null;
        return *(int*)payload.Data;
    }

    public static unsafe AssetDragPayload? AcceptAsset()
    {
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(AssetDragPayload.PayloadType);
        if (payload.NativePtr == null || !payload.IsDelivery() || payload.Data == IntPtr.Zero || payload.DataSize <= 0)
            return null;
        byte[] bytes = new byte[payload.DataSize];
        Marshal.Copy(payload.Data, bytes, 0, bytes.Length);
        return AssetDragPayload.TryParse(Encoding.UTF8.GetString(bytes), out AssetDragPayload parsed)
            ? parsed
            : null;
    }

    public static unsafe void SetGameObject(Guid id)
    {
        Span<byte> bytes = stackalloc byte[16];
        id.TryWriteBytes(bytes);
        fixed (byte* data = bytes)
            ImGui.SetDragDropPayload(GameObjectPayloadType, (IntPtr)data, 16);
    }

    public static unsafe Guid? AcceptGameObject()
    {
        ImGuiPayloadPtr payload = ImGui.AcceptDragDropPayload(GameObjectPayloadType);
        if (payload.NativePtr == null || !payload.IsDelivery() || payload.Data == IntPtr.Zero || payload.DataSize != 16)
            return null;
        Span<byte> bytes = stackalloc byte[16];
        new ReadOnlySpan<byte>((void*)payload.Data, 16).CopyTo(bytes);
        return new Guid(bytes);
    }
}
