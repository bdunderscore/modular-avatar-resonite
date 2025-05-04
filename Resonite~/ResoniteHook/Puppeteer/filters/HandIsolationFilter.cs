using FrooxEngine;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class HandIsolationFilter(TranslateContext context)
{
    public void Apply()
    {
        var slots = context.Root.GetComponentsInChildren<AvatarFacetAnchor>().Select(c => c.Slot)
            .Concat(context.Root.GetComponentsInChildren<AvatarFacetAnchor>().Select(c => c.Slot))
            .Distinct();

        foreach (var slot in slots)
        {
            slot.AttachComponent<DynamicVariableSpace>().SpaceName.Value = ResoNamespaces.ModularAvatarNamespace;
        }
    }
}