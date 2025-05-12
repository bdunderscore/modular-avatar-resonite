using FrooxEngine;
using FrooxEngine.CommonAvatar;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class AvatarPoseNodeRefFilter(TranslateContext context)
{
    public void Apply()
    {
        foreach (var apn in context.Root.GetComponentsInChildren<AvatarPoseNode>())
        {
            var dv = apn.Slot.AttachComponent<DynamicReferenceVariable<Slot>>();
            var refField = apn.Slot.AttachComponent<ReferenceField<Slot>>();

            refField.Reference.Target = apn.Slot;
            dv.Reference.DriveFrom(refField.Reference);
            dv.VariableName.Value = ResoNamespaces.AvatarPoseNodePrefix + apn.Node.Value;
        }
    }
}