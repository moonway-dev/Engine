namespace Engine.RHI;

public static class TextureViewValidation
{
    public static TextureViewDescription ValidateAndNormalize(ITexture texture, in TextureViewDescription description)
    {
        ArgumentNullException.ThrowIfNull(texture);
        if (description.Type is < TextureViewType.Texture2D or > TextureViewType.Texture3D)
            throw new ArgumentOutOfRangeException(nameof(description), "Texture view type is not supported.");
        if (description.BaseMipLevel >= texture.MipLevels)
            throw new ArgumentOutOfRangeException(nameof(description), "Base mip level is outside the texture.");
        if (description.BaseArrayLayer >= texture.ArrayLayers)
            throw new ArgumentOutOfRangeException(nameof(description), "Base array layer is outside the texture.");

        uint mipCount = description.MipLevelCount == 0
            ? texture.MipLevels - description.BaseMipLevel
            : description.MipLevelCount;
        uint layerCount = description.ArrayLayerCount == 0
            ? texture.ArrayLayers - description.BaseArrayLayer
            : description.ArrayLayerCount;
        if (mipCount == 0 || mipCount > texture.MipLevels - description.BaseMipLevel)
            throw new ArgumentOutOfRangeException(nameof(description), "Mip range is outside the texture.");
        if (layerCount == 0 || layerCount > texture.ArrayLayers - description.BaseArrayLayer)
            throw new ArgumentOutOfRangeException(nameof(description), "Array layer range is outside the texture.");

        switch (description.Type)
        {
            case TextureViewType.Texture2D when layerCount != 1:
                throw new ArgumentException("2D texture views must select exactly one array layer.", nameof(description));
            case TextureViewType.Texture2D when texture.Dimension == TextureDimension.Texture2D && description.BaseArrayLayer != 0:
                throw new ArgumentException("A 2D texture view must select array layer zero.", nameof(description));
            case TextureViewType.Texture2DArray when texture.Dimension is not (TextureDimension.Texture2DArray or TextureDimension.Cube):
                throw new ArgumentException("Array views require a 2D array or cube texture.", nameof(description));
            case TextureViewType.Texture3D when texture.Dimension != TextureDimension.Texture3D:
                throw new ArgumentException("3D views require a 3D texture.", nameof(description));
            case TextureViewType.Cube when texture.Dimension != TextureDimension.Cube || description.BaseArrayLayer != 0 || layerCount != 6:
                throw new ArgumentException("Cube views must select all six layers of a cube texture.", nameof(description));
        }

        return description with { MipLevelCount = mipCount, ArrayLayerCount = layerCount };
    }
}
