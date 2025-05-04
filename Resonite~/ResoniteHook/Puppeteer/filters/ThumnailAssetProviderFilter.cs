using Elements.Core;
using FrooxEngine;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class ThumbnailAssetProviderFilter(TranslateContext context)
{
    public void Apply()
    {
        var provider = context.Root!.AttachComponent<ItemTextureThumbnailSource>();
        var dynDriver = context.Root.AttachComponent<DynamicReferenceVariableDriver<IAssetProvider<Texture2D>>>();
        dynDriver.Target.Target = provider.Texture;
        dynDriver.VariableName.Value = ResoNamespaces.Config_ThumbnailAssetProvider;
        
        var rectDynDriver = context.Root.AttachComponent<DynamicValueVariableDriver<Rect?>>();
        rectDynDriver.Target.Target = provider.Crop;
        rectDynDriver.VariableName.Value = ResoNamespaces.Config_ThumbnailAssetProviderCrop;

        var configNode = context.SettingsNode!.AddSlot("Avatar Thumbnail");
        var templateRefBinding = configNode.AttachComponent<DynamicReferenceVariable<IAssetProvider<Texture2D>>>();
        templateRefBinding.OverrideOnLink.Value = true;
        templateRefBinding.VariableName.Value = ResoNamespaces.Config_ThumbnailAssetProvider;
        
        var templateRectBinding = configNode.AttachComponent<DynamicValueVariable<Rect?>>();
        templateRectBinding.OverrideOnLink.Value = true;
        templateRectBinding.VariableName.Value = ResoNamespaces.Config_ThumbnailAssetProviderCrop;
    }
}