#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace nadena.dev.ndmf.platform.resonite
{
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
            var alphaMask = mat.GetTextureSafe("_AlphaMask");

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
            
            emissionTex = mat.GetTextureSafe("_EmissionMap") as Texture2D;
            var emissionMask = mat.GetTextureSafe("_EmissionBlendMask") as Texture2D;

            importerReference = emissionTex;
            
            if (emissionTex != null && (emissionMask != null || GraphicsFormatUtility.HasAlphaChannel(emissionTex.graphicsFormat)))
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
            
            matcapTex = importerReference = mat.GetTextureSafe("_MatCapTex");
            return matcapTex != null;
        }

        public virtual bool GetNormalMapTexture(
            Material mat,
            out Texture? mainTex, 
            out Texture? importerReference,
            out Vector2 scale,
            out Vector2 offset
        )
        {
            mainTex = importerReference = mat.GetTextureSafe("_BumpMap");
            scale = mat.GetTextureScaleSafe("_BumpMap");
            offset = mat.GetTextureOffsetSafe("_BumpMap");

            return mainTex != null;
        }

        public virtual bool TryTranslateMaterial(Material material, out proto.Material? protoMat)
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
            
            protoMat.MainColor = material.GetColorSafe("_Color", Color.white).ToRPC();
            protoMat.EmissionColor = material.GetColorSafe("_EmissionColor", Color.black).ToRPC();
            protoMat.MatcapColor = material.GetColorSafe("_MatCapColor", Color.black).ToRPC();
            protoMat.AlphaClip = material.GetFloatSafe("_Cutoff", 0.5f).Value;
            
            var tag = material.GetTag("VRCFallback", false);
            proto.BlendMode blendMode = proto.BlendMode.Opaque;
            proto.CullMode cullMode = proto.CullMode.Back;
            
            if (tag.Contains("Cutout")) blendMode = proto.BlendMode.Cutout;
            else if (tag.Contains("Transparent")) blendMode = proto.BlendMode.Alpha;
            else if (tag.Contains("Fade")) blendMode = proto.BlendMode.Fade;
            
            if (tag.Contains("DoubleSided")) cullMode = proto.CullMode.None;
            
            protoMat.BlendMode = blendMode;
            protoMat.CullMode = cullMode;
            protoMat.UnityRenderQueue = material.renderQueue;

            return true;
        }
        
        private Texture2D BakeEmissionMask(Material material, Texture2D emissionMap, Texture2D? emissionMask)
        {
            var width = emissionMap.width;
            var height = emissionMap.height;
            
            var tempTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            _tempObjects.Add(tempTex);
            
            var tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var shader = Shader.Find("Hidden/NDMF/BakeEmission");
            
            var tmpMat = new Material(shader);
            _tempObjects.Add(tmpMat);
            
            tmpMat.SetTexture("_EmissionMap", emissionMap);
            tmpMat.SetTextureScale("_EmissionMap", material.GetTextureScaleSafe("_EmissionMap"));
            tmpMat.SetTextureOffset("_EmissionMap", material.GetTextureOffsetSafe("_EmissionMap"));
            
            tmpMat.SetTexture("_EmissionBlendMask", emissionMask);
            tmpMat.SetTextureScale("_EmissionBlendMask", material.GetTextureScaleSafe("_EmissionBlendMask"));
            tmpMat.SetTextureOffset("_EmissionBlendMask", material.GetTextureOffsetSafe("_EmissionBlendMask"));
            
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
            tmpMat.SetTextureScale("_AlphaMask", material.GetTextureScaleSafe("_AlphaMask"));
            tmpMat.SetTextureOffset("_AlphaMask", material.GetTextureOffsetSafe("_AlphaMask"));

            if (material.HasProperty("_AlphaMaskScale"))
            {
                tmpMat.SetFloat("_AlphaMaskScale", material.GetFloatSafe("_AlphaMaskScale", 1.0f).Value);
            }

            if (material.HasProperty("_AlphaMaskValue"))
            {
                tmpMat.SetFloat("_AlphaMaskValue", material.GetFloatSafe("_AlphaMaskValue", 1.0f).Value);
            }
            
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