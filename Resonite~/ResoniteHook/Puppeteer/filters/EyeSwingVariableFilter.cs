using FrooxEngine;
using FrooxEngine.CommonAvatar;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class EyeSwingVariableFilter(TranslateContext context)
{
    public void Apply()
    {
        var eyeSwing = context.Root.GetComponentInChildren<EyeRotationDriver>();
        if (eyeSwing == null) return;

        var swingConfigSlot = context.SettingsNode.AddSlot("Eye Swing");
        var dvv = swingConfigSlot.AttachComponent<DynamicValueVariable<float>>();
        dvv.Value.Value = 10; // reasonable default
        dvv.VariableName.Value = ResoNamespaces.Config_EyeSwing;

        var driver = eyeSwing.Slot.AttachComponent<DynamicValueVariableDriver<float>>();
        driver.VariableName.Value = ResoNamespaces.Config_EyeSwing;
        driver.Target.Target = eyeSwing.MaxSwing;
    }
}