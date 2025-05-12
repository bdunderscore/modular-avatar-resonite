using FrooxEngine;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class MiscRefFilter(TranslateContext context)
{
    public void Apply()
    {
        // We don't use the avatar space, but other gimmicks do, so create it for them to use.
        var avatarSpace = context.Root.AttachComponent<DynamicVariableSpace>();
        avatarSpace.SpaceName.Value = "Avatar";
        avatarSpace.OnlyDirectBinding.Value = true;
    }
}