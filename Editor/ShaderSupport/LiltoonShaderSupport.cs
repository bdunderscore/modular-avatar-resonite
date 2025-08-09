#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using nadena.dev.ndmf.proto;
using UnityEditor;
using UnityEngine;
using Material = UnityEngine.Material;
using Texture = UnityEngine.Texture;

namespace nadena.dev.ndmf.platform.resonite
{
    [SuppressMessage("ReSharper", "Unity.PreferAddressByIdToGraphicsParams")]
    internal partial class LiltoonShaderSupport : GenericShaderTranslator
    {
        public LiltoonShaderSupport(TextureAssetImporter textureImporter) : base(textureImporter)
        {
        }
        
        #if MA_LILTOON_PRESENT

        public override bool TryTranslateMaterial(Material material, out proto.Material? protoMat)
        {
            protoMat = null;
            if (!IsLiltoonShader(material.shader)) return false;

            if (material.shader.name.Contains("FakeShadow"))
            {
                protoMat = new()
                {
                    Category = MaterialCategory.FakeShadow
                };
                return true;
            }

            // Clone material as the bake operations are destructive
            material = new Material(material);
            _tempObjects.Add(material);
            
            Material = material;
            foreach (var field in _matPropFields)
            {
                ((lilMaterialProperty)field.GetValue(this)).Bind(this);
            }

            if (!base.TryTranslateMaterial(material, out protoMat)) return false;
            
            if (smoothnessTex.textureValue != null || metallicGlossMap.textureValue != null || reflectionColorTex.textureValue != null)
            {
                BakeMetallicMap(material, protoMat);
            }

            protoMat.Metallic = metallic.floatValue;
            if (material.GetFloat("_Reflection") == 0)
            {
                protoMat.Smoothness = 0;
            }
            else
            {
                protoMat.Smoothness = smoothness.floatValue;
            }
            protoMat.Reflectivity = reflectance.floatValue;
            
            // Update culling/etc settings based on liltoon config
            // TODO: lilToonMulti

            var hasCustomVRCOverride = !string.IsNullOrEmpty(material.GetTag("VRCFallback", false));

            if (!hasCustomVRCOverride)
            {
                if (material.shader.name.Contains("Cutout")) protoMat.BlendMode = BlendMode.Cutout;
                else if (material.shader.name.Contains("Transparent")) protoMat.BlendMode = BlendMode.Alpha;
                else protoMat.BlendMode = BlendMode.Opaque;
            }

            switch ((int)Math.Round(material.GetFloat("_Cull")))
            {
                case 1: protoMat.CullMode = CullMode.Front; break;
                case 2: protoMat.CullMode = CullMode.Back; break;
                default: /* case 0: */ protoMat.CullMode = CullMode.None; break;
            }

            return true;
        }

        private void BakeMetallicMap(Material material, proto.Material protoMat)
        {
            Material tmpMat = new Material(Shader.Find("Hidden/NDMF/BakeMetallicGlossReflectionMap"));
            _tempObjects.Add(tmpMat);

            tmpMat.SetTexture("_Smoothness", smoothnessTex.textureValue);
            tmpMat.SetTexture("_Metallic", metallicGlossMap.textureValue);
            tmpMat.SetTexture("_ReflectionColorTex", reflectionColorTex.textureValue);
            tmpMat.SetTextureOffset("_Smoothness", material.GetTextureOffset(smoothnessTex.propertyName));
            tmpMat.SetTextureScale("_Smoothness", material.GetTextureScale(smoothnessTex.propertyName));
            tmpMat.SetTextureOffset("_Metallic", material.GetTextureOffset(metallicGlossMap.propertyName));
            tmpMat.SetTextureScale("_Metallic", material.GetTextureScale(metallicGlossMap.propertyName));
            tmpMat.SetTextureOffset("_ReflectionColorTex", material.GetTextureOffset(reflectionColorTex.propertyName));
            tmpMat.SetTextureScale("_ReflectionColorTex", material.GetTextureScale(reflectionColorTex.propertyName));
            tmpMat.SetColor("_ReflectionColor", reflectionColor.colorValue);

            Texture2D? newTex = null;
            RunBake(ref newTex, tmpMat, smoothnessTex.textureValue, metallicGlossMap.textureValue,
                reflectionColorTex.textureValue);
            newTex.name = $"{material.name}_MetallicGlossReflectionMap";
            if (textureImporter(newTex, null, out var id, out _))
            {
                protoMat.SmoothnessMetallicReflectionMap = id;
            }
            _tempObjects.Add(newTex);
        }

        protected override bool GetOrBakeMainTexture(Material mat, out Texture? mainTex, out Texture? importerReference, out Vector2 scale,
            out Vector2 offset)
        {
            importerReference = this.mainTex.textureValue;
            scale = offset = Vector2.zero;
            
            mainTex = AutoBakeMainTexture(mat);
            if (mainTex == null) return false;
            
            if (mainTex != null && importerReference != null) mainTex.name = importerReference.name;
            scale = mat.GetTextureScale(this.mainTex.propertyName);
            offset = mat.GetTextureOffset(this.mainTex.propertyName);

            var alphaMask = mat.GetTexture("_AlphaMask");
            if (alphaMask != null)
            {
                var newTex = BakeMainTexAlpha(mat, mainTex, alphaMask);
                if (newTex != null)
                {
                    mainTex = newTex;
                    mainTex.name = alphaMask.name;
                }
            }

            return mainTex != null;
        }

        protected override bool GetMatcapTexture(Material mat, out Texture? matcapTex, out Texture? importerReference)
        {
            // If a matcap mask is enabled, we can't replicate it with XSToon, so just disable matcap entirely
            if (mat.HasTexture("_MatCapBlendMask") && mat.GetTexture("_MatCapBlendMask") != null)
            {
                matcapTex = importerReference = null;
                return false;
            }
            
            return base.GetMatcapTexture(mat, out matcapTex, out importerReference);
        }
        #endif
    }
}