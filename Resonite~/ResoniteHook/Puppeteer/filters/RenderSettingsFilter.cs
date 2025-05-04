using FrooxEngine.CommonAvatar;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class RenderSettingsFilter(TranslateContext context)
{
    public void Apply()
    {
        var settingsNode = context.SettingsNode.AddSlot("RenderSettings");
        settingsNode.AttachComponent<AvatarRenderSettings>();
    }
}