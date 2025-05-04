#nullable enable

using System;
using System.Collections.Generic;
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

    internal class GenericShaderTranslator : IShaderTranslator
    {
        protected readonly TextureAssetImporter textureImporter;
        protected List<UnityEngine.Object> _tempObjects = new();

        public GenericShaderTranslator(TextureAssetImporter textureImporter)
        {
            this.textureImporter = textureImporter;
        }


        public void Dispose()
        {
            foreach (var obj in _tempObjects)
            {
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }

        protected virtual bool GetOrBakeMainTexture(
            Material mat,
            out Texture? mainTex,
            out Texture? importerReference,
            out Vector2 scale, 
            out Vector2 offset
        )
        {
            scale = mat.mainTextureScale;
            offset = mat.mainTextureOffset;
            
            mainTex = mat.mainTexture;
            var alphaMask = mat.GetTexture("_AlphaMask");

            importerReference = mainTex;
            
            if (alphaMask != null && mainTex != null)
            {
                var newTex = BakeMainTexAlpha(mat, mainTex, alphaMask);
                if (newTex != null)
                {
                    mainTex = newTex;
                    _tempObjects.Add(newTex);
                }
            }

            return mainTex != null;
        }

        protected virtual bool GetOrBakeEmissionTexture(Material mat, out Texture? emissionTex, out Texture? importerReference, out Vector2 scale, out Vector2 offset)
        {
            scale = mat.GetTextureScale("_EmissionMap");
            offset = mat.GetTextureOffset("_EmissionMap");
            
            emissionTex = mat.GetTexture("_EmissionMap") as Texture2D;
            var emissionMask = mat.GetTexture("_EmissionBlendMask") as Texture2D;

            importerReference = emissionTex;
            
            if (emissionMask != null && emissionTex != null)
            {
                var newTex = BakeEmissionMask(mat, (Texture2D) emissionTex, emissionMask);
                if (newTex != null)
                {
                    emissionTex = newTex;
                    _tempObjects.Add(newTex);
                }
            }

            return emissionTex != null;
        }

        protected virtual bool GetMatcapTexture(Material mat, out Texture? matcapTex, out Texture? importerReference)
        {
            matcapTex = importerReference = null;
            if (!mat.HasTexture("_MatCapTex")) return false;
            
            matcapTex = importerReference = mat.GetTexture("_MatCapTex");
            return matcapTex != null;
        }

        public virtual bool GetNormalMapTexture(
            Material mat,
            out Texture mainTex, 
            out Texture importerReference,
            out Vector2 scale,
            out Vector2 offset
        )
        {
            mainTex = importerReference = mat.GetTexture("_BumpMap");
            scale = mat.GetTextureScale("_BumpMap");
            offset = mat.GetTextureOffset("_BumpMap");

            return mainTex != null;
        }

        public virtual bool TryTranslateMaterial(Material material, out p.Material? protoMat)
        {
            protoMat = new();

            if (GetOrBakeMainTexture(
                    material, 
                    out var mainTex, 
                    out var importerReference, 
                    out var scale,
                    out var offset
            ))
            {
                if (textureImporter(mainTex, importerReference, out var assetID, out var _))
                {
                    protoMat.MainTexture = assetID;
                }

                protoMat.MainTextureScaleOffset = new()
                {
                    Scale = scale.ToRPC(),
                    Offset = offset.ToRPC()
                };
            }

            if (GetOrBakeEmissionTexture(material, out var emissionTex, out importerReference, out scale, out offset))
            {
                if (textureImporter(emissionTex, importerReference, out var assetID, out var _))
                {
                    protoMat.EmissionMap = assetID;
                }
                
                protoMat.EmissionMapScaleOffset = new()
                {
                    Scale = scale.ToRPC(),
                    Offset = offset.ToRPC()
                };
            }

            if (GetMatcapTexture(material, out var matcapTex, out importerReference))
            {
                if (textureImporter(matcapTex, importerReference, out var assetID, out var _))
                {
                    protoMat.MatcapTexture = assetID;
                }
            }
            
            if (GetNormalMapTexture(material, out var normalMapTex, out importerReference, out scale, out offset))
            {
                if (textureImporter(normalMapTex, importerReference, out var assetID, out var _))
                {
                    protoMat.NormalMap = assetID;
                }
                
                protoMat.NormalMapScaleOffset = new()
                {
                    Scale = scale.ToRPC(),
                    Offset = offset.ToRPC()
                };
            }
            
            protoMat.MainColor = material.color.ToRPC();
            protoMat.EmissionColor = material.GetColor("_EmissionColor").ToRPC();
            protoMat.MatcapColor = material.GetColor("_MatCapColor").ToRPC();
            protoMat.AlphaClip = material.GetFloat("_Cutoff");
            
            var tag = material.GetTag("VRCFallback", false);
            p.BlendMode blendMode = p.BlendMode.Opaque;
            p.CullMode cullMode = p.CullMode.Back;
            
            if (tag.Contains("Cutout")) blendMode = p.BlendMode.Cutout;
            else if (tag.Contains("Transparent")) blendMode = p.BlendMode.Alpha;
            else if (tag.Contains("Fade")) blendMode = p.BlendMode.Fade;
            
            if (tag.Contains("DoubleSided")) cullMode = p.CullMode.None;
            
            protoMat.BlendMode = blendMode;
            protoMat.CullMode = cullMode;

            return true;
        }
        
        private Texture2D BakeEmissionMask(Material material, Texture2D emissionMap, Texture2D? emissionMask)
        {
            if (emissionMask == null)
            {
                return null;
            }
            
            var width = emissionMap.width;
            var height = emissionMap.height;
            
            var tempTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            _tempObjects.Add(tempTex);
            
            var tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var shader = Shader.Find("Hidden/NDMF/BakeEmission");
            
            var tmpMat = new Material(shader);
            _tempObjects.Add(tmpMat);
            
            tmpMat.SetTexture("_EmissionMap", emissionMap);
            tmpMat.SetTextureScale("_EmissionMap", material.GetTextureScale("_EmissionMap"));
            tmpMat.SetTextureOffset("_EmissionMap", material.GetTextureOffset("_EmissionMap"));
            
            tmpMat.SetTexture("_EmissionBlendMask", emissionMask);
            tmpMat.SetTextureScale("_EmissionBlendMask", material.GetTextureScale("_EmissionBlendMask"));
            tmpMat.SetTextureOffset("_EmissionBlendMask", material.GetTextureOffset("_EmissionBlendMask"));
            
            Graphics.Blit(tempTex, tempRT, tmpMat);
            
            // Read back to a texture2d
            var tmp = RenderTexture.active;
            RenderTexture.active = tempRT;
            tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = tmp;
                
            RenderTexture.ReleaseTemporary(tempRT);

            tempTex.name = emissionMap.name + " MaskBaked";
            return tempTex;
        }

        protected Texture BakeMainTexAlpha(Material material, Texture mainTex, Texture alphaMask)
        {
            // Bake alpha to a temporary texture
            var width = mainTex?.width ?? alphaMask.width;
            var height = mainTex?.height ?? alphaMask.height;
                
            var tempTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            _tempObjects.Add(tempTex);
            var tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var shader = mainTex != null
                ? Shader.Find("Hidden/NDMF/WriteColorTex")
                : Shader.Find("Hidden/NDMF/FillColor");
            var tmpMat = new Material(shader);
            _tempObjects.Add(tmpMat);
            if (mainTex != null)
            {
                tmpMat.SetTexture("_MainTex", mainTex);
                tmpMat.SetTextureScale("_MainTex", material.mainTextureScale);
                tmpMat.SetTextureOffset("_MainTex", material.mainTextureOffset);
            }
            Graphics.Blit(mainTex, tempRT, tmpMat);

            tmpMat.shader = Shader.Find("Hidden/NDMF/WriteAlpha");
            tmpMat.SetTexture("_AlphaMask", alphaMask);
            tmpMat.SetTextureScale("_AlphaMask", material.GetTextureScale("_AlphaMask"));
            tmpMat.SetTextureOffset("_AlphaMask", material.GetTextureOffset("_AlphaMask"));
            Graphics.Blit(alphaMask, tempRT, tmpMat);
                
            // Read back to a texture2d
            var tmp = RenderTexture.active;
            RenderTexture.active = tempRT;
            tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = tmp;
                
            RenderTexture.ReleaseTemporary(tempRT);

            tempTex.name = (mainTex?.name ?? alphaMask.name) + " AlphaBlend";
            mainTex = tempTex;
            return mainTex;
        }
    }
}