#nullable enable

using System;
using JetBrains.Annotations;
using UnityEngine;

using p = nadena.dev.ndmf.proto;

namespace nadena.dev.ndmf.platform.resonite
{
    delegate bool TextureAssetImporter(
        Texture? tex,
        Texture? importReference,
        out p.AssetID? assetID,
        out p.Texture? translatedTex
    );
    
    internal interface IShaderTranslator : IDisposable
    {
        bool TryTranslateMaterial(Material m, out p.Material? outMaterial);
    }
}