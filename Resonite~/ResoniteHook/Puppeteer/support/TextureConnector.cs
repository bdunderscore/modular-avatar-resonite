using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using JetBrains.Annotations;

namespace nadena.dev.resonity.remote.puppeteer.support;

/// <summary>
/// The presence of this class triggers creation of texture asset variants.
/// </summary>
[UsedImplicitly]
public class TextureConnector : ITexture2DConnector
{
    public void Initialize(Asset asset)
    {
    }

    public void Unload()
    {
    }

    public void SetTexture2DFormat(int width, int height, int mipmaps, TextureFormat format, ColorProfile profile,
        AssetIntegrated onDone)
    {
        onDone?.Invoke(false);
    }

    public void SetTexture2DData(Bitmap2D data, int startMipLevel, TextureUploadHint hint, AssetIntegrated onSet)
    {
        onSet?.Invoke(false);
    }

    public void SetTexture2DProperties(TextureFilterMode filterMode, int anisoLevel, TextureWrapMode wrapU,
        TextureWrapMode wrapV,
        float mipmapBias, AssetIntegrated onSet)
    {
        onSet?.Invoke(false);
    }
}