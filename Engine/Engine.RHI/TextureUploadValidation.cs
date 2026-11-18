namespace Engine.RHI;

public static class TextureUploadValidation
{
    public static void ValidateSubresources(ITexture texture, ReadOnlySpan<TextureSubresourceData> subresources)
    {
        ArgumentNullException.ThrowIfNull(texture);
        HashSet<(uint Mip, uint Layer)> written = [];
        foreach (TextureSubresourceData subresource in subresources)
        {
            if (subresource.MipLevel >= texture.MipLevels)
                throw new ArgumentOutOfRangeException(nameof(subresources), "Mip level is outside the texture.");
            if (subresource.ArrayLayer >= texture.ArrayLayers)
                throw new ArgumentOutOfRangeException(nameof(subresources), "Array layer is outside the texture.");
            if (!written.Add((subresource.MipLevel, subresource.ArrayLayer)))
                throw new ArgumentException("A mip/layer subresource may only be uploaded once per call.", nameof(subresources));

            uint width = Math.Max(1u, texture.Width >> checked((int)subresource.MipLevel));
            uint height = Math.Max(1u, texture.Height >> checked((int)subresource.MipLevel));
            ulong expectedSize = checked((ulong)width * height * (uint)BytesPerPixel(texture.Format));
            if ((ulong)subresource.Data.Length != expectedSize)
                throw new ArgumentException(
                    $"Mip {subresource.MipLevel}, layer {subresource.ArrayLayer} requires {expectedSize} bytes, got {subresource.Data.Length}.",
                    nameof(subresources));
        }
    }

    private static int BytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.R8Unorm => 1,
        TextureFormat.R8G8B8A8UNorm or TextureFormat.R8G8B8A8Srgb or TextureFormat.B8G8R8A8UNorm or TextureFormat.B8G8R8A8Srgb => 4,
        TextureFormat.R16G16B16A16Float => 8,
        TextureFormat.R32G32B32A32Float => 16,
        TextureFormat.R11G11B10Float => 4,
        TextureFormat.D16Unorm => 2,
        TextureFormat.D32Float or TextureFormat.D24UnormS8Uint => 4,
        TextureFormat.D32FloatS8Uint => 8,
        _ => throw new NotSupportedException($"Texture format '{format}' does not support data uploads."),
    };
}
