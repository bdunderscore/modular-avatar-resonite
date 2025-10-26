using Elements.Core;
using FrooxEngine;
using nadena.dev.ndmf.proto;
using Renderite.Shared;
using Finger = nadena.dev.ndmf.proto.Finger;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal static class RigNaming
{
    public static List<(ObjectID, string, BodyNode)> GenerateHumanoidBoneNames(AvatarDescriptor desc)
    {
        var bones = desc.Bones;
        List<(ObjectID, string, BodyNode)> names = new();

        NameBone("hips", bones.Hips, BodyNode.Hips);
        NameBone("spine", bones.Spine, BodyNode.Spine);
        NameBone("chest", bones.Chest, BodyNode.Chest);
        // no upper chest
        NameBone("neck", bones.Neck, BodyNode.Neck);
        NameBone("head", bones.Head, BodyNode.Head);

        NameArm("left_", bones.LeftArm, BodyNode.LeftShoulder);
        NameArm("right_", bones.RightArm, BodyNode.RightShoulder);
        
        NameLeg("left_", bones.LeftLeg, BodyNode.LeftUpperLeg);
        NameLeg("right_", bones.RightLeg, BodyNode.RightUpperLeg);
        
        NameBone("left_eye", desc.EyelookConfig?.LeftEyeTransform, BodyNode.LeftEye);
        NameBone("right_eye", desc.EyelookConfig?.RightEyeTransform, BodyNode.RightEye);

        return names;
        
        void NameArm(string prefix, Arm arm, BodyNode baseNode)
        {
            var offset = (int)baseNode - (int)BodyNode.LeftShoulder;
            
            NameBone(prefix + "shoulder", arm.Shoulder, BodyNode.LeftShoulder + offset);
            NameBone(prefix + "upper_arm", arm.UpperArm, BodyNode.LeftUpperArm + offset);
            NameBone(prefix + "lower_arm", arm.LowerArm, BodyNode.LeftLowerArm + offset);
            NameBone(prefix + "hand", arm.Hand, BodyNode.LeftHand + offset);

            NameFinger(prefix, "index", arm.Index, baseNode == BodyNode.LeftShoulder, "IndexFinger");
            NameFinger(prefix, "middle", arm.Middle,  baseNode == BodyNode.LeftShoulder, "MiddleFinger");
            NameFinger(prefix, "ring", arm.Ring,  baseNode == BodyNode.LeftShoulder, "RingFinger");
            NameFinger(prefix, "pinky", arm.Pinky,  baseNode == BodyNode.LeftShoulder, "Pinky");
            NameThumb(prefix, arm.Thumb, baseNode == BodyNode.LeftShoulder);
        }

        void NameThumb(string prefix, Finger finger, bool leftChirality)
        {
            // Unity names the thumb bones Proximal/Intermediate/Distal, but when converting to resonite
            // the conversion is more natural if we map this to Metacarpal/Proximal/Distal. So we have a bit
            // of a hack here to get this mapping right.

            if (finger.Metacarpal != null)
            {
                // We actually have a metacarpal, so use the normal mapping
                NameFinger(prefix, "thumb", finger, leftChirality, "Thumb");
                return;
            }

            prefix += "finger_thumb_";
            string enumPrefix = (leftChirality ? "Left" : "Right") + "Thumb";
            
            NameBoneStr(prefix + "metacarpal", finger.Proximal, enumPrefix + "_Metacarpal");
            NameBoneStr(prefix + "proximal", finger.Intermediate, enumPrefix + "_Proximal");
            NameBoneStr(prefix + "distal", finger.Distal, enumPrefix + "_Distal");
            NameBoneStr(prefix + "tip", finger.Tip, enumPrefix + "_Tip");
        }
        
        void NameFinger(string prefix, string fingerName, Finger finger, bool leftChirality, string enumFingerName)
        {
            prefix += "finger_" + fingerName + "_";

            string enumPrefix = leftChirality ? "Left" : "Right";
            
            NameBoneStr(prefix + "metacarpal", finger.Metacarpal, enumPrefix + enumFingerName + "_Metacarpal");
            NameBoneStr(prefix + "proximal", finger.Proximal, enumPrefix + enumFingerName + "_Proximal");
            NameBoneStr(prefix + "intermediate", finger.Intermediate, enumPrefix + enumFingerName + "_Intermediate");
            NameBoneStr(prefix + "distal", finger.Distal, enumPrefix + enumFingerName + "_Distal");
            NameBoneStr(prefix + "tip", finger.Tip, enumPrefix + enumFingerName + "_Tip");
        }

        void NameLeg(string prefix, Leg leg, BodyNode baseNode)
        {
            var offset = (int)baseNode - (int)BodyNode.LeftUpperLeg;
            
            NameBone(prefix + "upperleg", leg.UpperLeg, BodyNode.LeftUpperLeg + offset);
            NameBone(prefix + "lowleg", leg.LowerLeg, BodyNode.LeftLowerLeg + offset);
            NameBone(prefix + "foot", leg.Foot, BodyNode.LeftFoot + offset);
            NameBone(prefix + "toe", leg.Toe, BodyNode.LeftToes + offset);
        }

        void NameBone(string name, ObjectID? id, BodyNode node)
        {
            if (id != null) names.Add((id, name, node));
        }
        
        void NameBoneStr(string name, ObjectID? id, string nodeName)
        {
            if (!Enum.TryParse<BodyNode>(nodeName, out var node))
            {
                return;
            }
            
            NameBone(name, id, node);
        }
    }
    
    public static HumanoidRigScope IsolateHumanoidRigScope(RootConverter converter, Slot root, AvatarDescriptor avDesc)
    {
        List<Action> revertActions = new();
        HashSet<Slot> humanoidBones = new();

        foreach (var (id, name, _) in GenerateHumanoidBoneNames(avDesc))
        {
            var slot = converter.Object<Slot>(id);
            
            if (slot != null) 
            {
                var currentOrderOffset = slot.OrderOffset;
                revertActions.Add(() => slot.OrderOffset = currentOrderOffset);
                // HandPoser requires that the humanoid bones sort before nonhumanoid bones to avoid incorrect
                // coordinate compensation calculations
                slot.OrderOffset = -1;
                humanoidBones.Add(slot);
            }
        }
        
        return new HumanoidRigScope(revertActions, humanoidBones);
    }

    public class HumanoidRigScope(List<Action> actions, HashSet<Slot> humanoidBones) : IDisposable
    {
        public HashSet<Slot> HumanoidBones => humanoidBones;
        
        public void Dispose()
        {
            foreach (var action in actions)
            {
                action();
            }
        }
    }
}
