#nullable enable

using FrooxEngine;
using Google.Protobuf;

using e = Elements.Core;
using f = FrooxEngine;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public partial class RootConverter
{
    private f.IAssetProvider<f.Material> _baseMaterial;
    
    private async Task<f.IWorldElement?> CreateMaterial(string name, p::Material material)
    {
        await new f::ToWorld();
        
        var holder = AssetSubslot("Materials", _assetRoot);

        return await CreateXSToonMaterial(name, holder, material);
        
        // TODO: handle other material types
        var materialComponent = holder.AttachComponent<f::PBS_Metallic>();
        Defer(PHASE_RESOLVE_REFERENCES, () =>
        {
            materialComponent.AlbedoTexture.Value = AssetRefID<f.IAssetProvider<f.Texture2D>>(material.MainTexture);
        });
        materialComponent.AlbedoColor.Value = material.MainColor?.ColorX() ?? e.colorX.White;

        return materialComponent;
    }

    private f::XiexeToonMaterial? _xsExemplar;
    
    private async Task<f.IWorldElement?> CreateXSToonMaterial(string name, f.Slot holder, p.Material src)
    {
        holder = AssetSubslot("XSToonMaterials", holder);
        if (_xsExemplar == null)
        {
            _xsExemplar = CreateXSToonExemplar(holder);
        }

        holder = holder.AddSlot(name);
        var mat = holder.AttachComponent<f.XiexeToonMaterial>();

        Defer(PHASE_RESOLVE_REFERENCES, () =>
        {
            mat.MainTexture.Value = AssetRefID<f.IAssetProvider<f.Texture2D>>(src.MainTexture);
            mat.NormalMap.Value = AssetRefID<f.IAssetProvider<f.Texture2D>>(src.NormalMap);
            mat.EmissionMap.Value = AssetRefID<f.IAssetProvider<f.Texture2D>>(src.EmissionMap);     
            mat.Matcap.Value = AssetRefID<f.IAssetProvider<f.Texture2D>>(src.MatcapTexture);
        });
        
        if (src.MainTextureScaleOffset != null)
        {
            mat.MainTextureScale.Value = src.MainTextureScaleOffset.Scale.Vec2();
            mat.MainTextureOffset.Value = src.MainTextureScaleOffset.Offset.Vec2();
        }
        mat.Color.Value = src.MainColor?.ColorX() ?? e.colorX.White;
        
        if (src.NormalMapScaleOffset != null)
        {
            mat.NormalMapScale.Value = src.NormalMapScaleOffset.Scale.Vec2();
            mat.NormalMapOffset.Value = src.NormalMapScaleOffset.Offset.Vec2();
        }
        
        mat.EmissionColor.Value = src.EmissionColor?.ColorX() ?? e.colorX.Clear;
        if (src.EmissionMapScaleOffset != null)
        {
            mat.EmissionMapScale.Value = src.EmissionMapScaleOffset.Scale.Vec2();
            mat.EmissionMapOffset.Value = src.EmissionMapScaleOffset.Offset.Vec2();
        }

        switch (src.BlendMode)
        {
            case p.BlendMode.Opaque: mat.BlendMode.Value = f.BlendMode.Opaque; break;
            case p.BlendMode.Cutout: mat.BlendMode.Value = f.BlendMode.Cutout; break;
            case p.BlendMode.Transparent: mat.BlendMode.Value = f.BlendMode.Transparent; break;
            case p.BlendMode.Additive: mat.BlendMode.Value = f.BlendMode.Additive; break;
            case p.BlendMode.Alpha: mat.BlendMode.Value = f.BlendMode.Alpha; break;
            case p.BlendMode.Multiply: mat.BlendMode.Value = f.BlendMode.Multiply; break;
            case p.BlendMode.Fade: mat.BlendMode.Value = f.BlendMode.Transparent; break;
            default: mat.BlendMode.Value = f.BlendMode.Opaque; break;
        }

        if (src.HasAlphaClip) mat.AlphaClip.Value = src.AlphaClip;

        switch (src.CullMode)
        {
            case p.CullMode.Back: mat.Culling.Value = f.Culling.Back; break;
            case p.CullMode.Front: mat.Culling.Value = f.Culling.Front; break;
            case p.CullMode.None: mat.Culling.Value = f.Culling.Off; break;
        }
        
        // TODO: matcap
        // TODO occlusion map?

        BindExemplarValues(mat, _xsExemplar);

        return mat;
    }

    private void BindExemplarValues(f.XiexeToonMaterial mat, f.XiexeToonMaterial xsExemplar)
    {
        var bindings = mat.Slot.AddSlot("Bindings");
        
        BindField(mat.Saturation);
        BindField(mat.Metallic);
        BindField(mat.Glossiness);
        BindField(mat.Reflectivity);
        BindField(mat.RimColor);
        BindField(mat.RimAlbedoTint);
        BindField(mat.RimIntensity);
        BindField(mat.RimRange);
        BindField(mat.RimThreshold);
        BindField(mat.RimSharpness);
        BindField(mat.SpecularIntensity);
        BindField(mat.SpecularArea);
        
        BindField(mat.Outline);
        BindField(mat.OutlineWidth);
        BindField(mat.OutlineColor);
        BindField(mat.OutlineAlbedoTint);
        // outline mask
        BindField(mat.ShadowRamp);
        // shadow ramp mask
        BindField(mat.ShadowRim);
        BindField(mat.ShadowSharpness);
        BindField(mat.ShadowRimRange);
        BindField(mat.ShadowRimThreshold);
        BindField(mat.ShadowRimSharpness);
        BindField(mat.ShadowRimAlbedoTint);
        BindField(mat.SubsurfaceColor);
        BindField(mat.SubsurfaceDistortion);
        BindField(mat.SubsurfacePower);
        BindField(mat.SubsurfaceScale);
        
        // BindField(mat.OffsetFactor);
        // BindField(mat.OffsetUnits);
        // various UV settings
        

        void BindField<T>(IField<T> matField)
        {
            var exemplarField = xsExemplar.TryGetField<T>(matField.Name) ?? throw new Exception($"Field {matField.Name} not found in exemplar");
            var variableName = "NDMF/XiexeToonMaterialTemplate." + matField.Name;
            
            if (!exemplarField.IsDriven)
            {
                var df = xsExemplar.Slot.AttachComponent<DynamicField<T>>();
                
                df.VariableName.Value = variableName;
                df.TargetField.Value = exemplarField.ReferenceID;
                df.OverrideOnLink.Value = true;
            }

            matField.Value = exemplarField.Value;
            
            var df2 = bindings.AttachComponent<DynamicField<T>>();
            df2.VariableName.Value = variableName;
            df2.TargetField.Value = matField.ReferenceID;
            df2.OverrideOnLink.Value = false;
        }
    }

    private f.XiexeToonMaterial CreateXSToonExemplar(f.Slot holder)
    {
        var slot = holder.AddSlot("__SharedConfig");
        var xs = slot.AttachComponent<f.XiexeToonMaterial>();

        var shadowRampHolder = slot.AddSlot("ShadowRamp");
        var shadowRamp = shadowRampHolder.AttachComponent<f.StaticTexture2D>();
        // TODO: don't hardcode this
        shadowRamp.URL.Value = new Uri("resdb:///213a4363379a81e99478e36f0aec87d65e68e3c3f42ec83b9ac3aa38bcd931ba.webp");

        xs.ShadowRamp.Value = shadowRamp.ReferenceID;
        xs.ShadowSharpness.Value = 0;

        return xs;
    }
}