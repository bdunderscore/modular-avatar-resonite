using Elements.Core;
using FrooxEngine;
using nadena.dev.ndmf.proto;
using Finger = nadena.dev.ndmf.proto.Finger;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal static class RigNaming
{
    static List<(ObjectID, string)> GenerateHumanoidBoneNames(AvatarDescriptor desc)
    {
        var bones = desc.Bones;
        List<(ObjectID, string)> names = new();

        NameBone("hips", bones.Hips);
        NameBone("spine", bones.Spine);
        NameBone("chest", bones.Chest);
        // no upper chest
        NameBone("neck", bones.Neck);
        NameBone("head", bones.Head);

        NameArm("left_", bones.LeftArm);
        NameArm("right_", bones.RightArm);
        
        NameLeg("left_", bones.LeftLeg);
        NameLeg("right_", bones.RightLeg);
        
        NameBone("left_eye", desc.EyelookConfig?.LeftEyeTransform);
        NameBone("right_eye", desc.EyelookConfig?.RightEyeTransform);

        return names;
        
        void NameArm(string prefix, Arm arm)
        {
            NameBone(prefix + "shoulder", arm.Shoulder);
            NameBone(prefix + "upper_arm", arm.UpperArm);
            NameBone(prefix + "lower_arm", arm.LowerArm);
            NameBone(prefix + "hand", arm.Hand);

            NameFinger(prefix, "index", arm.Index);
            NameFinger(prefix, "middle", arm.Middle);
            NameFinger(prefix, "ring", arm.Ring);
            NameFinger(prefix, "pinky", arm.Pinky);
            NameFinger(prefix, "thumb", arm.Thumb);
        }
        
        void NameFinger(string prefix, string fingerName, Finger finger)
        {
            prefix += "finger_" + fingerName + "_";
            
            NameBone(prefix + "metacarpal", finger.Metacarpal);
            NameBone(prefix + "proximal", finger.Proximal);
            NameBone(prefix + "intermediate", finger.Intermediate);
            NameBone(prefix + "distal", finger.Distal);
            NameBone(prefix + "tip", finger.Tip);
        }

        void NameLeg(string prefix, Leg leg)
        {
            NameBone(prefix + "upperleg", leg.UpperLeg);
            NameBone(prefix + "lowleg", leg.LowerLeg);
            NameBone(prefix + "foot", leg.Foot);
            NameBone(prefix + "toe", leg.Toe);
        }

        void NameBone(string name, ObjectID? id)
        {
            if (id != null) names.Add((id, name));
        }
    }

    public static IDisposable Scope(RootConverter converter, Slot root, AvatarDescriptor avDesc)
    {
        List<Action> revertActions = new();

        VisitSlot(root);

        HashSet<Slot> humanoidBones = new();

        foreach (var (id, name) in GenerateHumanoidBoneNames(avDesc))
        {
            var slot = converter.Object<Slot>(id);
            if (slot != null) 
            {
                slot.Name = name;
                humanoidBones.Add(slot);
            }
        }

        HashSet<Slot> retainSlots = new(humanoidBones);
        
        // Make sure all parents of humanoid bones don't get reparented
        foreach (var slot in humanoidBones.ToList())
        {
            foreach (var parent in slot.EnumerateParents())
            {
                if (parent is Slot s) retainSlots.Add(s);
            }
        }

        foreach (var slot in humanoidBones)
        {
            foreach (var child in slot.Children.ToList())
            {
                if (child.Parent != root && !retainSlots.Contains(child))
                {
                    var priorParent = child.Parent;
                    var priorLocalPos = child.LocalPosition;
                    var priorLocalRot = child.LocalRotation;
                    var priorLocalScale = child.LocalScale;
                    
                    revertActions.Add(() => {
                        child.SetParent(priorParent, false);
                        child.LocalPosition = priorLocalPos;
                        child.LocalRotation = priorLocalRot;
                        child.LocalScale = priorLocalScale;
                    });
                    
                    child.SetParent(child.World.RootSlot, false);
                }
            }
        }
        
        return new Revert(revertActions);

        void VisitSlot(Slot slot)
        {
            var priorName = slot.Name;
            revertActions.Add(() => { slot.Name = priorName; });
            
            var tmpName = "tmp";
            foreach (char c in slot.Name)
            {
                tmpName += "_" + c;
            }

            slot.Name = tmpName;
            
            foreach (var child in slot.Children)
            {
                VisitSlot(child);
            }
        }
    }
    
    
    private class Revert(List<Action> actions) : IDisposable
    {
        public void Dispose()
        {
            foreach (var action in actions)
            {
                action();
            }
        }
    }
}
