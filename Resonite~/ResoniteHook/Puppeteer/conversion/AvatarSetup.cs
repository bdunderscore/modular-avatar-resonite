using System.Numerics;
using System.Reflection;
using Assimp = Assimp;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using FrooxEngine.Store;
using Google.Protobuf.Collections;
using nadena.dev.resonity.remote.puppeteer.filters;
using nadena.dev.resonity.remote.puppeteer.logging;
using SkyFrost.Base;
using Record = SkyFrost.Base.Record;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public partial class RootConverter
{
    private const bool FREEZE_AVATAR = false;

    private f.Slot? _settingsRoot;
    
    /// <summary>
    /// Ensures that we don't have any dynamic bone drives or similar things on humanoid bones.
    ///
    /// If we enter VRIK setup with driven bones, it will prevent VRIK from installing drives, which results in crazy
    /// bone wiggling. The main cause of this is if we have Dynamic Bones (eg - driving toes) on humanoid bones;
    /// as such, we handle this by creating an intermediate bone for VRIK's purposes, and letting DB drive the inner bone.
    ///
    /// However, if we have a humanoid bone that is a child of another humanoid bone, we violently break the drive of
    /// the parent instead.
    /// </summary>
    /// <param name="humanoidBones"></param>
    /// <exception cref="NotImplementedException"></exception>
    private static void EnsureHumanoidBonesNotDriven(HashSet<Slot> humanoidBones)
    {
        foreach (var bone in humanoidBones)
        {
            var anyDriven = bone.Position_Field.IsDriven || bone.Rotation_Field.IsDriven || bone.Scale_Field.IsDriven;
            if (!anyDriven) continue;

            if (humanoidBones.Any(b => b != bone && b.IsChildOf(bone)))
            {
                // Break all drives
                bone.Position_Field.ActiveLink?.ReleaseLink();
                bone.Rotation_Field.ActiveLink?.ReleaseLink();
                bone.Scale_Field.ActiveLink?.ReleaseLink();
            }
            else
            {
                // Create intermediate bones for DB usage
                var p1 = bone.Parent.AddSlot(bone.Name);
                var p2 = p1.AddSlot("ReverseTransform<NoIK>");
                var p3 = p2.AddSlot("ReverseTransform<NoIK>");

                p2.LocalRotation = p1.LocalRotation.Inverted;
                p2.LocalScale = 1/p1.LocalScale;
                p3.LocalPosition = -p1.LocalPosition;
                
                bone.SetParent(p3, keepGlobalTransform: false);
                bone.Name += "<NoIK>";
            }
        }
    }
    
    private async Task SetupRig(f.Slot parent, p.AvatarDescriptor avDesc)
    {
        // This is a virtual component that tags a slot as being the root of a rigged model
        await new f.ToWorld();
        
        foreach (f.SkinnedMeshRenderer smr in parent.GetComponentsInChildren<f.SkinnedMeshRenderer>())
        {
            if (await _context.WaitForAssetLoad(smr.Mesh.Target) == null)
            {
                throw new Exception("Failed to load skinned mesh renderer mesh: " + smr.Mesh.Target);
            }
        }

        var rig = parent.AttachComponent<f.Rig>();
        var relay = parent.AttachComponent<f.MeshRendererMaterialRelay>();
        
        foreach (f.SkinnedMeshRenderer smr in parent.GetComponentsInChildren<f.SkinnedMeshRenderer>())
        {
            smr.BoundsComputeMethod.Value = f.SkinnedBounds.Static;
            rig.Bones.AddRangeUnique(smr.Bones);
            relay.Renderers.Add(smr);
        }
        
        Defer(PHASE_RIG_SETUP, "Rig setup", async () =>
        {
            // Change all bone names to be what BipedRig expects (and any non-humanoid bones become temporary names)
            // We also need to move any children of humanoid bones in order to avoid breaking FingerPoser configuration

            using (var scope = RigNaming.Scope(this, _root, avDesc))
            {
                
                // Ensure any field drives are reflected before we start
                await new f.NextUpdate();

                EnsureHumanoidBonesNotDriven(scope.HumanoidBones);
                
                // Invoke ModelImporter to set up the rig
                var settings = new f.ModelImportSettings()
                {
                    //ForceTpose = true,
                    SetupIK = true,
                    GenerateColliders = true,
                    //GenerateSkeletonBoneVisuals = true,
                    //GenerateSnappables = true,
                };

                Console.WriteLine("Root position: " + parent.Position_Field.Value);

                Type ty_modelImportData =
                    typeof(f.ModelImporter).GetNestedType("ModelImportData", BindingFlags.NonPublic);
                var mid_ctor = ty_modelImportData.GetConstructors()[0];
                var modelImportData = mid_ctor.Invoke(new object[]
                {
                    "",
                    null,
                    parent,
                    _assetRoot,
                    settings,
                    new NullProgress()
                });

                ty_modelImportData.GetField("settings")!.SetValue(modelImportData, settings);

                typeof(f.ModelImporter).GetMethod(
                    "GenerateRigBones",
                    BindingFlags.Static | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(f.Rig), ty_modelImportData
                    },
                    null
                )!.Invoke(null, [
                    rig, modelImportData
                ]);

                // Avoid the rig moving while we're setting up the avatar by disabling IK
                rig.Slot.GetComponent<VRIK>().Enabled = false;
            } 
        });
        
        Defer(PHASE_ENABLE_RIG, "Enable VRIK", () =>
        {
            if (!FREEZE_AVATAR) rig.Slot.GetComponent<VRIK>().Enabled = true;
        });
    }

    private async Task<f.IComponent?> SetupAvatar(f.Slot slot, p.AvatarDescriptor spec)
    {
        await SetupRig(slot, spec);
        
        Console.WriteLine("=== TEST ===");
        
        Defer(PHASE_AVATAR_SETUP, "Deferred avatar setup", () => SetupAvatarDeferred(slot, spec));
        Defer(PHASE_POSTPROCESS, "Mesh Loading Filter", () => new MeshLoadingFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "DynBone auto-enable", () => new DynBoneAutoEnableFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "EyeSwing filter", () => new EyeSwingVariableFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "Add face mesh reference", () => new FaceMeshReferenceFilter(_context).Apply(spec));
        Defer(PHASE_POSTPROCESS, "Add thumbnail asset provider", () => new ThumbnailAssetProviderFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "Add render settings", () => new RenderSettingsFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "Add pose node references", () => new AvatarPoseNodeRefFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "Add misc references", () => new MiscRefFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "Add first person visible filter", () => new FirstPersonVisibleFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, "VRIK fixups", () => new VRIKFixupsFilter(_context).Apply());
        Defer(PHASE_RESOLVE_REFERENCES, "Adding bone annotations", () => new BoneAnnotationsFilter(_context).Apply(spec));

        return null;
    }

    private List<(f.SkinnedMeshRenderer, List<float>)> PreserveBlendshapes()
    {
        var list = new List<(f.SkinnedMeshRenderer, List<float>)>();
        
        foreach (var smr in _root.GetComponentsInChildren<f.SkinnedMeshRenderer>())
        {
            list.Add((smr, smr.BlendShapeWeights.ToList()));
        }

        return list;
    }

    private void RestoreBlendshapes(List<(f.SkinnedMeshRenderer, List<float>)> blendshapes)
    {
        foreach (var (smr, weights) in blendshapes)
        {
            while (smr.BlendShapeWeights.Count < weights.Count)
            {
                smr.BlendShapeWeights.Add();
            }
            
            for (int i = 0; i < weights.Count; i++)
            {
                if (!smr.BlendShapeWeights.GetField(i).IsDriven)
                {
                    smr.BlendShapeWeights[i] = weights[i];
                }
            }
        }
    }
    
    private async Task SetupAvatarDeferred(f.Slot slot, p.AvatarDescriptor spec)
    {
        var blendshapes = PreserveBlendshapes();

        await InvokeAvatarBuilder(slot, spec);

        if (FREEZE_AVATAR) return;

        CreateSettingsNode();
        
        // Setup visemes
        var driver = slot.GetComponentInChildren<f.DirectVisemeDriver>();
        driver?.Destroy();

        if (spec.VisemeConfig != null)
        {
            await SetupVisemes(spec);
        }
        
        RestoreBlendshapes(blendshapes);
    }

    private f.Slot CreateSettingsNode()
    {
        if (_settingsRoot != null) return _settingsRoot;
        
        _settingsRoot = _root.AddSlot("<color=#00ffff>Avatar Settings</color>");
        
        // Create core systems node
        var coreSys = _root.AddSlot("Core Systems");
        var task = coreSys.LoadObjectAsync(new Uri(CloudSpawnAssets.CoreSystems));
        
        var settingsField = _settingsRoot.AttachComponent<f.ReferenceField<f.Slot>>();
        settingsField.Reference.Target = _settingsRoot;
        
        var settingsVar = _settingsRoot.AttachComponent<f.DynamicReferenceVariable<f.Slot>>();
        settingsVar.VariableName.Value = ResoNamespaces.SettingsRoot;
        settingsVar.Reference.DriveFrom(settingsField.Reference);
        
        Defer(PHASE_AWAIT_CLOUD_SPAWN, "Waiting for cloud spawn...", () => task);
        Defer(PHASE_FINALIZE, "Finalizing avatar settings...", () =>
        {
            _settingsRoot.SetParent(_root, false);
            coreSys.SetParent(_root, false);
            coreSys.LocalPosition = default;
            coreSys.LocalRotation = Quaternion.Identity;
            coreSys.LocalScale = float3.One;
            
            _settingsRoot.LocalPosition = default;
            _settingsRoot.LocalRotation = Quaternion.Identity;
            _settingsRoot.LocalScale = float3.One;
        });

        return _settingsRoot;
    }

    private async Task SetupVisemes(p.AvatarDescriptor spec)
    {
        var vc = spec.VisemeConfig;
        var targetMesh = Object<f.SkinnedMeshRenderer>(spec.VisemeConfig.VisemeMesh);

        var blendshapeIndices = new Dictionary<string, int>();
        if (targetMesh == null) return;
        
        if (await _context.WaitForAssetLoad(targetMesh.Mesh.Target) == null)
        {
            throw new Exception("Failed to load viseme mesh: " + targetMesh.Mesh.Target);
        }
        
        foreach (var (bs, i) in targetMesh.Mesh.Asset.Data.BlendShapes.Select((bs, i) => (bs.Name, i)))
        {
            if (bs != null) blendshapeIndices[bs] = i;
        }

        var analyzer = _root.GetComponentInChildren<f.VisemeAnalyzer>();
        if (analyzer == null)
        {
            // Avatar creator won't create the analyzer if it can't detect visemes. Create it ourself
            var head = _root.FindChild("Head Proxy");
            analyzer = head.AttachComponent<f.VisemeAnalyzer>();
            head.AttachComponent<AvatarVoiceSourceAssigner>().TargetReference.Target = analyzer.Source;
        }
        
        var driver = targetMesh.Slot.AttachComponent<f.DirectVisemeDriver>();
        driver.Source.Target = analyzer;

      
        TryLinkViseme(driver.Silence, targetMesh, vc.ShapeSilence);
        TryLinkViseme(driver.PP, targetMesh, vc.ShapePP);
        TryLinkViseme(driver.FF, targetMesh, vc.ShapeFF);
        TryLinkViseme(driver.TH, targetMesh, vc.ShapeTH);
        TryLinkViseme(driver.DD, targetMesh, vc.ShapeDD);
        TryLinkViseme(driver.kk, targetMesh, vc.ShapeKk);
        TryLinkViseme(driver.CH, targetMesh, vc.ShapeCH);
        TryLinkViseme(driver.SS, targetMesh, vc.ShapeSS);
        TryLinkViseme(driver.nn, targetMesh, vc.ShapeNn);
        TryLinkViseme(driver.RR, targetMesh, vc.ShapeRR);
        TryLinkViseme(driver.aa, targetMesh, vc.ShapeAa);
        TryLinkViseme(driver.E, targetMesh, vc.ShapeE);
        TryLinkViseme(driver.ih, targetMesh, vc.ShapeIh);
        TryLinkViseme(driver.oh, targetMesh, vc.ShapeOh);
        TryLinkViseme(driver.ou, targetMesh, vc.ShapeOu);
        TryLinkViseme(driver.Laugh, targetMesh, vc.ShapeLaugh);
        
        
        void TryLinkViseme(f.FieldDrive<float> sourceField, f.SkinnedMeshRenderer targetMesh, string shapeName)
        {
            if (targetMesh == null) return;
            if (!blendshapeIndices.TryGetValue(shapeName, out var index)) return;
            if (targetMesh.BlendShapeWeights.Count <= index) return;
            
            var destinationField = targetMesh.BlendShapeWeights.GetElement(index);
            sourceField.ForceLink(destinationField);
        }
    }

    struct HandCoordinates
    {
        public float3 up;
        public float3 forward;
    }

    private HandCoordinates GetHandCoordinates(p.Arm arm)
    {
        // Sometimes, models can end up with weird hand positions as a result of VRIK processing. As such we need to
        // figure out if the hand is facing up or down. To do this, we take the cross product of the wrist-to-thumb and
        // wrist-to-index vectors. This gives us a vector that is perpendicular to the plane of the hand.

        var wrist = Object<f.Slot>(arm.Hand);
        var thumb = Object<f.Slot>(arm.Thumb.LastBone());
        var index = Object<f.Slot>(arm.Index.LastBone());

        if (wrist == null)
        {
            return new HandCoordinates()
            {
                // We have no hand, so just pick something and hope it works out :/
                up = float3.Up,
                forward = float3.Forward
            };
        }

        float3 forward;
        Vector3? fingertipGlobalPosition = null;

        // Use the arm-to-wrist vector as the forward direction.
        var wristNextParentId = arm.LowerArm;
        if (wristNextParentId == null || wristNextParentId.Id == 0)
        {
            wristNextParentId = arm.UpperArm;
        }
        if (wristNextParentId == null || wristNextParentId.Id == 0)
        {
            wristNextParentId = arm.Shoulder;
        }

        f.Slot parentSlot = (wristNextParentId != null && wristNextParentId.Id != 0)
            ? _context.Object<f.Slot>(wristNextParentId)!
            : wrist.Parent!;
        
        if (false && index != null)
        {
            // We use the wrist-to-index vector as the forward direction.
            //forward = AxisAlignDirection(wrist, index.GlobalPosition - wrist.GlobalPosition);
            forward = (index.GlobalPosition - wrist.GlobalPosition).Normalized;
        }
        else
        {
            forward = (wrist.GlobalPosition - parentSlot.GlobalPosition).Normalized;
        }
    
        float3 globalUp = float3.Up;
        
        // Remove all contribution on the forward direction from up
        globalUp -= Vector3.Dot(globalUp, forward) * forward;
        globalUp = globalUp.Normalized;
        
        return new HandCoordinates()
        {
            up = globalUp,
            forward = forward,
        };
    }

    private async Task InvokeAvatarBuilder(f.Slot slot, p.AvatarDescriptor spec)
    {
        var tmpSlot = slot.FindChild("CenteredRoot");
        
        var leftHandCoords = GetHandCoordinates(spec.Bones.LeftArm);
        var rightHandCoords = GetHandCoordinates(spec.Bones.RightArm);

        Console.WriteLine("Left hand up: " + leftHandCoords.up);
        Console.WriteLine("Left hand forward: " + leftHandCoords.forward);
        Console.WriteLine("Right hand up: " + rightHandCoords.up);
        Console.WriteLine("Right hand forward: " + rightHandCoords.forward);
        
        var avatarBuilderSlot = tmpSlot.AddSlot("Avatar Builder");
        var avatarCreator = avatarBuilderSlot.AttachComponent<f.AvatarCreator>();
        
        // Sleep one frame to ensure the avatar creator has time to initialize
        await new f.ToBackground();
        await new f.ToWorld();
        
        var ref_headset = field<f.SyncRef<f.Slot>>(avatarCreator, "_headsetReference");
        var ref_left_hand = field<f.SyncRef<f.Slot>>(avatarCreator, "_leftReference");
        var ref_right_hand = field<f.SyncRef<f.Slot>>(avatarCreator, "_rightReference");
        var ref_left_point = field<f.SyncRef<f.Slot>>(avatarCreator, "_leftPoint");
        var ref_right_point = field<f.SyncRef<f.Slot>>(avatarCreator, "_rightPoint");
        var b_useSymmetry = field<f.Sync<bool>>(avatarCreator, "_useSymmetry");
        
        var baseScale = 0.15f;
        
        var slot_headset = ref_headset.Target;
        var slot_left_hand = ref_left_hand.Target;
        var slot_right_hand = ref_right_hand.Target;
        var slot_left_point = ref_left_point.Target;
        var slot_right_point = ref_right_point.Target;

        var bone_head = Object<f.Slot>(spec.Bones.Head);
        var bone_left_hand = Object<f.Slot>(spec.Bones.LeftArm.Hand);
        var bone_right_hand = Object<f.Slot>(spec.Bones.RightArm.Hand);

        var handLength = MeasureHandSize(spec.Bones.LeftArm, out var leftFinger);
        MeasureHandSize(spec.Bones.RightArm, out var rightFinger);
        if (handLength < 0.01f) handLength = baseScale;
        var handScale = (2f/3f) * handLength / baseScale;
        
        var avCreatorScale = (f.Sync<float>) typeof(f.AvatarCreator).GetField("_scale", 
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(avatarCreator)!;
        avCreatorScale.Value = handScale;

        // Symmetry breaks on some avatars; since we compute left and right hand positions manually, we can disable
        // it to avoid issues
        b_useSymmetry.Value = false;
        
        // Sleep one frame
        await new f.ToBackground();
        await new f.ToWorld();
        
        slot_headset.LocalPosition= spec.EyePosition.Vec3(); // relative to avatar root
        Console.WriteLine("Local position in head space: " + bone_head.GlobalPointToLocal(slot_headset.GlobalPosition));
        slot_headset.GlobalRotation = _context.Root!.GlobalRotation;
        
        Console.WriteLine("Head fwd vector: " + bone_head.GlobalDirectionToLocal(Vector3.UnitZ));
        Console.WriteLine("Headset fwd vector: " + slot_headset.GlobalDirectionToLocal(Vector3.UnitZ));
        
        // Align hands. The resonite (right) hand model has the Z axis facing along the fingers, and Y up.

        
        var rightArm = bone_right_hand.Parent;
        var leftArm = bone_left_hand.Parent;
        float3 rightHandFwd = rightHandCoords.forward;
        float3 leftHandFwd = leftHandCoords.forward;

        float3 rightHandUp = rightHandCoords.up;
        float3 leftHandUp = leftHandCoords.up;
        
        var rightPointPosition = bone_right_hand.GlobalPosition + (rightHandFwd - rightHandUp) * (handLength / 2);
        var leftPointPosition = bone_left_hand.GlobalPosition + (leftHandFwd - leftHandUp) * (handLength / 2);
        
        
        // Now use a look rotation to align the model's hand with the resonite hand
        var rightHandRot = Quaternion.CreateFromRotationMatrix(
            Matrix4x4.CreateLookAt(Vector3.Zero, rightHandFwd, rightHandUp)
        );
        var leftHandRot = Quaternion.CreateFromRotationMatrix(
            Matrix4x4.CreateLookAt(Vector3.Zero, leftHandFwd, leftHandUp)
        );

        Console.WriteLine("Update wait...");
        await new f.NextUpdate();
        Console.WriteLine("Update wait...");
        await new f.NextUpdate();
        
        await new f.ToWorld();

        for (int i = 0; i < 10 && slot_right_hand.Position_Field.IsDriven; i++)
        {
            Console.WriteLine("Update wait... " + i);
            await new f.NextUpdate();
        
            await new f.ToWorld();
        }
        
                
        // Symmetry can break some avatars, so break the drives here.
        slot_right_hand.Position_Field.ActiveLink?.ReleaseLink();
        slot_right_hand.Rotation_Field.ActiveLink?.ReleaseLink();
        slot_right_point.Position_Field.ActiveLink?.ReleaseLink();
        slot_right_point.Rotation_Field.ActiveLink?.ReleaseLink();
        
        slot_right_hand.GlobalPosition = bone_right_hand.GlobalPosition;
        slot_right_hand.GlobalRotation = rightHandRot;
        
        slot_left_hand.GlobalPosition = bone_left_hand.GlobalPosition;
        slot_left_hand.GlobalRotation = leftHandRot;
       
        await new f.NextUpdate();
        await new f.NextUpdate();
        
        await new f.ToWorld();

        var marker = bone_right_hand.AddSlot("TMP Marker");
        marker.GlobalPosition = slot_right_hand.GlobalPosition;
        marker.GlobalRotation = slot_right_hand.GlobalRotation;
        
        await new f.NextUpdate();
        await new f.NextUpdate();
        
        await new f.ToWorld();
                
        Console.WriteLine("Right hand +Z vector: " + slot_right_hand.GlobalDirectionToLocal(Vector3.UnitZ));
        Console.WriteLine("Hand directional vector: " + rightArm.LocalDirectionToGlobal(bone_right_hand.LocalPosition.Normalized));
                
        Console.WriteLine("Left hand +Z vector: " + slot_left_hand.GlobalDirectionToLocal(Vector3.UnitZ));
        Console.WriteLine("Left hand directional vector: " + leftArm.LocalDirectionToGlobal(bone_left_hand.LocalPosition.Normalized));
        
        var m_alignAnchors = avatarCreator.GetType()
            .GetMethod("AlignAnchors", BindingFlags.NonPublic | BindingFlags.Instance);

        slot_left_point.Position_Field.ActiveLink?.ReleaseLink();
        slot_right_point.Position_Field.ActiveLink?.ReleaseLink();
        slot_left_point.GlobalPosition = leftPointPosition;
        slot_right_point.GlobalPosition = rightPointPosition;

        SetAnchorPositions(slot_left_hand, leftHandFwd, leftHandUp);
        SetAnchorPositions(slot_right_hand, rightHandFwd, rightHandUp);
        
        // TODO - scale adjustment
        
        if (!FREEZE_AVATAR) avatarCreator.GetType().GetMethod("RunCreate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(avatarCreator, null);

        var headProxy = _root.FindChild("Head Proxy");
        var target = headProxy?.FindChild("Target");
        if (target != null)
        {
            var origTargetPos = target.GlobalPosition;
            headProxy.LocalPosition = spec.EyePosition.Vec3();
            target.GlobalPosition = origTargetPos;
        }

        SetToolshelfPosition(bone_left_hand, leftHandFwd, leftHandUp);
        SetToolshelfPosition(bone_right_hand, rightHandFwd, rightHandUp);
        
        
        void SetAnchorPositions(f.Slot hand, float3 fwd, float3 up)
        {
            var tooltip = hand.FindChild("Tooltip");
            var grabber = hand.FindChild("Grabber");
            //var shelf = hand.FindChild("Shelf"); // TODO

            tooltip.Position_Field.ActiveLink?.ReleaseLink();
            grabber.Position_Field.ActiveLink?.ReleaseLink();
            tooltip.Rotation_Field.ActiveLink?.ReleaseLink();
            grabber.Rotation_Field.ActiveLink?.ReleaseLink();
            
            tooltip.GlobalPosition = hand.GlobalPosition + fwd * handLength * 1.2f;
            tooltip.GlobalRotation = floatQ.LookRotation(fwd, up);
            grabber.GlobalPosition = hand.GlobalPosition + fwd * (handLength / 2f) - up * (handLength / 2);
            grabber.GlobalRotation = tooltip.GlobalRotation;
        }
    }

    private void SetToolshelfPosition(f.Slot slotHand, float3 forward, float3 up)
    {
        // position: 0.05m above wrist (for now) 
        // Z+: up from the wrist
        // Y+: Towards elbow
        
        var toolAnchor = slotHand.GetComponentsInChildren<f.CommonAvatar.AvatarToolAnchor>()
            .FirstOrDefault(a => a.AnchorPoint.Value == AvatarToolAnchor.Point.Toolshelf)
            ?.Slot;
        if (toolAnchor == null) return;
        toolAnchor.GlobalRotation = floatQ.LookRotation(forward, up);
        
        toolAnchor.GlobalPosition = slotHand.GlobalPosition + up * 0.05f;
        
        // Undo the effects of the toolshelf's local position
        var toolshelfLocalPos = new float3(0.02f, 0.01f, -0.14f);
        toolAnchor.GlobalPosition = toolAnchor.LocalPointToGlobal(-toolshelfLocalPos);
        
        LogController.Log(LogController.LogLevel.Info, "[ToolAnchor] Under slot " + slotHand.Name +
            " @ " + slotHand.GlobalPosition + " anchor global pos " + toolAnchor.GlobalPosition +
            " anchor local pos " + toolAnchor.LocalPosition + " fwd " + forward + " up " + up);
    }

    private float MeasureHandSize(p.Arm bonesLeftArm, out f.Slot? longestFinger)
    {
        longestFinger = null;
        
        var hand = Object<f.Slot>(bonesLeftArm.Hand);
        if (hand == null) throw new Exception("Hand not found");

        float length = 0;
        MeasureFinger(bonesLeftArm.Index, ref length, ref longestFinger);
        MeasureFinger(bonesLeftArm.Middle, ref length, ref longestFinger);
        MeasureFinger(bonesLeftArm.Ring,ref length,  ref longestFinger);
        MeasureFinger(bonesLeftArm.Pinky, ref length, ref longestFinger);
        return length;

        void MeasureFinger(p.Finger finger, ref float length, ref f.Slot? longestFinger)
        {
            var boneRef = finger.LastBone();
            if (boneRef == null) return;
            var lastBone = Object<f.Slot>(boneRef);

            if (lastBone == null) return;

            var len = (hand.GlobalPosition - lastBone.GlobalPosition).Magnitude;
            if (len > length)
            {
                length = len;
                longestFinger = lastBone;
            }
        }
    }

    T? field<T>(object obj, string name)
    {
        return (T?)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj);
    }
}