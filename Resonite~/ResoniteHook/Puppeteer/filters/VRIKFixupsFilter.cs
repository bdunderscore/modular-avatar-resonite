using FrooxEngine;
using FrooxEngine.FinalIK;
using nadena.dev.resonity.remote.puppeteer.logging;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class VRIKFixupsFilter(TranslateContext context)
{
    public void Apply()
    {
        var vrik = context.Root.GetComponent<VRIKAvatar>();

        if (vrik == null)
        {
            LogController.Log(LogController.LogLevel.Warning, "VRIKAvatar not found, is this not a humanoid avatar?");
            return;
        }

        var configSlot = context.Root.AddSlot("<color=cyan>Modular Avatar</color> - VRIK Configuration");
        
        var headMax = vrik.HeadMaxFixDistance;
        var headMaxDriver = configSlot.AttachComponent<DynamicValueVariable<float>>();
        
        headMaxDriver.VariableName.Value = ResoNamespaces.VRIKHeadMaxFixDistance;
        headMaxDriver.Value.DriveFrom(headMax, writeBack: true);
    }
}