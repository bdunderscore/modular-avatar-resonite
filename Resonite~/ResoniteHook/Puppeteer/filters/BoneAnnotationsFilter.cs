using Elements.Core;
using nadena.dev.ndmf.proto;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public class BoneAnnotationsFilter(TranslateContext context)
{
    public void Apply(AvatarDescriptor spec)
    {
        ConfigureBones(spec.Bones);
    }

    private void ConfigureBones(HumanoidBones bones)
    {
        ConfigureBone(bones.Head, "head");
        ConfigureBone(bones.Chest, "chest");
        ConfigureBone(bones.UpperChest, "upper_chest");
        ConfigureBone(bones.Neck, "neck");
        ConfigureBone(bones.Hips, "hips");
        ConfigureBone(bones.Spine, "spine");

        ConfigureArm(bones.LeftArm, "left_");
        ConfigureArm(bones.RightArm, "right_");

        ConfigureLeg(bones.LeftLeg, "left_");
        ConfigureLeg(bones.RightLeg, "right_");
    }

    private void ConfigureArm(Arm? arm, string prefix)
    {
        if (arm == null) return;
        
        ConfigureBone(arm.Shoulder, prefix + "shoulder");
        ConfigureBone(arm.UpperArm, prefix + "upper_arm");
        ConfigureBone(arm.LowerArm, prefix + "lower_arm");
        ConfigureBone(arm.Hand, prefix + "hand");
        
        ConfigureFinger(arm.Thumb, prefix + "thumb");
        ConfigureFinger(arm.Index, prefix + "index");
        ConfigureFinger(arm.Middle, prefix + "middle");
        ConfigureFinger(arm.Ring, prefix + "ring");
        ConfigureFinger(arm.Pinky, prefix + "pinky");
    }
    
    private void ConfigureFinger(Finger? finger, string prefix)
    {
        if (finger == null) return;
        
        ConfigureBone(finger.Metacarpal, prefix + "_metacarpal");
        ConfigureBone(finger.Proximal, prefix + "_proximal");
        ConfigureBone(finger.Intermediate, prefix + "_intermediate");
        ConfigureBone(finger.Distal, prefix + "_distal");
        ConfigureBone(finger.Tip, prefix + "_tip");
    }

    private void ConfigureLeg(Leg? leg, string prefix)
    {
        ConfigureBone(leg.UpperLeg, prefix + "upper_leg");
        ConfigureBone(leg.LowerLeg, prefix + "lower_leg");
        ConfigureBone(leg.Foot, prefix + "foot");
        ConfigureBone(leg.Toe, prefix + "toe");
    }

    private void ConfigureBone(ObjectID id, string name)
    {
        var bone = context.Object<f.Slot>(id);
        if (bone == null) return;

        var refVar = bone.AttachComponent<f.DynamicReferenceVariable<f.Slot>>();
        refVar.Reference.Target = bone;
        refVar.OverrideOnLink.Value = true;
        refVar.VariableName.Value = ResoNamespaces.HumanBoneRef + name;

        var poseVar = bone.AttachComponent<f.DynamicValueVariable<float4x4>>();
        poseVar.VariableName.Value = ResoNamespaces.HumanBonePose + name;
        poseVar.OverrideOnLink.Value = true;
        poseVar.Value.Value = bone.LocalToGlobal;
    }
}