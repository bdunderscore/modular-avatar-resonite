using System.Numerics;
using System.Reflection;
using Assimp = Assimp;
using Elements.Core;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using FrooxEngine.Store;
using Google.Protobuf.Collections;
using nadena.dev.resonity.remote.puppeteer.filters;
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
    
    private async Task SetupRig(f.Slot parent, p.AvatarDescriptor avDesc)
    {
        // This is a virtual component that tags a slot as being the root of a rigged model
        await new f.ToWorld();
        
        foreach (f.SkinnedMeshRenderer smr in parent.GetComponentsInChildren<f.SkinnedMeshRenderer>())
        {
            while (!smr.Mesh.IsAssetAvailable)
            {
                await new f::ToBackground();
                await new f::ToWorld();
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
        
        Defer(PHASE_RIG_SETUP, () =>
        {
            // Change all bone names to be what BipedRig expects (and any non-humanoid bones become temporary names)

            using (var scope = RigNaming.Scope(this, _root, avDesc))
            {
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

            return Task.CompletedTask;
        });
        
        Defer(PHASE_ENABLE_RIG, () =>
        {
            if (!FREEZE_AVATAR) rig.Slot.GetComponent<VRIK>().Enabled = true;
        });
    }

    private async Task<f.IComponent?> SetupAvatar(f.Slot slot, p.AvatarDescriptor spec)
    {
        await SetupRig(slot, spec);
        
        Defer(PHASE_AVATAR_SETUP, () => SetupAvatarDeferred(slot, spec));
        Defer(PHASE_POSTPROCESS, () => new MeshLoadingFilter(_root).Apply());
        Defer(PHASE_POSTPROCESS, () => new EyeSwingVariableFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, () => new FaceMeshReferenceFilter(_context).Apply(spec));
        Defer(PHASE_POSTPROCESS, () => new ThumbnailAssetProviderFilter(_context).Apply());
        Defer(PHASE_POSTPROCESS, () => new HandIsolationFilter(_context).Apply());
        Defer(PHASE_RESOLVE_REFERENCES, () => new BoneAnnotationsFilter(_context).Apply(spec));

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
        var copier = _settingsRoot.AddSlot("Settings Copier");
        var task = copier.LoadObjectAsync(new Uri(CloudSpawnAssets.SettingsCopier));
        
        Defer(PHASE_AWAIT_CLOUD_SPAWN, () => task);
        Defer(PHASE_FINALIZE, () =>
        {
            _settingsRoot.SetParent(_root, false);
            _settingsRoot.Tag = "NDMFAvatarSettings";
            _root.Tag = "NDMFAvatarRoot";
        });

        return _settingsRoot;
    }

    private async Task SetupVisemes(p.AvatarDescriptor spec)
    {
        var vc = spec.VisemeConfig;
        var targetMesh = Object<f.SkinnedMeshRenderer>(spec.VisemeConfig.VisemeMesh);

        var blendshapeIndices = new Dictionary<string, int>();
        if (targetMesh == null) return;
        while (!targetMesh.Mesh.IsAssetAvailable)
        {
            await new f.NextUpdate();
            await new f.ToWorld();
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


    private async Task InvokeAvatarBuilder(f.Slot slot, p.AvatarDescriptor spec)
    {
        var tmpSlot = slot.FindChild("CenteredRoot");

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

        var baseScale = 0.15f;
        
        var slot_headset = ref_headset.Target;
        var slot_left_hand = ref_left_hand.Target;
        var slot_right_hand = ref_right_hand.Target;
        var slot_left_point = ref_left_point.Target;
        var slot_right_point = ref_right_point.Target;

        var bone_head = Object<f.Slot>(spec.Bones.Head);
        var bone_left_hand = Object<f.Slot>(spec.Bones.LeftArm.Hand);
        var bone_right_hand = Object<f.Slot>(spec.Bones.RightArm.Hand);

        var handLength = MeasureHandSize(spec.Bones.LeftArm);
        if (handLength < 0.01f) handLength = baseScale;
        var handScale = (2f/3f) * handLength / baseScale;
        
        var avCreatorScale = (f.Sync<float>) typeof(f.AvatarCreator).GetField("_scale", 
            BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(avatarCreator)!;
        avCreatorScale.Value = handScale;

        // Sleep one frame
        await new f.ToBackground();
        await new f.ToWorld();
        
        slot_headset.LocalPosition= spec.EyePosition.Vec3(); // relative to avatar root
        Console.WriteLine("Local position in head space: " + bone_head.GlobalPointToLocal(slot_headset.GlobalPosition));
        slot_headset.GlobalRotation = bone_head.GlobalRotation;
        
        Console.WriteLine("Head fwd vector: " + bone_head.GlobalDirectionToLocal(Vector3.UnitZ));
        Console.WriteLine("Headset fwd vector: " + slot_headset.GlobalDirectionToLocal(Vector3.UnitZ));
        
        // Align hands. The resonite (right) hand model has the Z axis facing along the fingers, and Y up.
        // First, find the corresponding axis for the model's hand.
        var rightArm = bone_right_hand.Parent;
        var leftArm = bone_left_hand.Parent;
        var rightHandFwd = rightArm.LocalDirectionToGlobal(bone_right_hand.LocalPosition.Normalized);
        var leftHandFwd = leftArm.LocalDirectionToGlobal(bone_left_hand.LocalPosition.Normalized);
        var rightPointPosition = bone_right_hand.GlobalPosition + (rightHandFwd - float3.Up) * (handLength / 2);
        var leftPointPosition = bone_left_hand.GlobalPosition + (leftHandFwd - float3.Up) * (handLength / 2);
        
        
        // Now use a look rotation to align the model's hand with the resonite hand
        var rightHandRot = Quaternion.CreateFromRotationMatrix(
            Matrix4x4.CreateLookAt(Vector3.Zero, rightHandFwd, Vector3.UnitY)
        );
        var leftHandRot = Quaternion.CreateFromRotationMatrix(
            Matrix4x4.CreateLookAt(Vector3.Zero, leftHandFwd, Vector3.UnitY)
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

        m_alignAnchors!.Invoke(avatarCreator, [slot_left_hand]);
        m_alignAnchors.Invoke(avatarCreator, [slot_right_hand]);

        slot_left_point.Position_Field.ActiveLink?.ReleaseLink();
        slot_right_point.Position_Field.ActiveLink?.ReleaseLink();
        slot_left_point.GlobalPosition = leftPointPosition;
        slot_right_point.GlobalPosition = rightPointPosition;

        SetAnchorPositions(slot_left_hand, leftHandFwd);
        SetAnchorPositions(slot_right_hand, rightHandFwd);
        
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
        
        
        void SetAnchorPositions(f.Slot hand, float3 fwd)
        {
            var tooltip = hand.FindChild("Tooltip");
            var grabber = hand.FindChild("Grabber");
            //var shelf = hand.FindChild("Shelf"); // TODO

            tooltip.GlobalPosition = hand.GlobalPosition + fwd * handLength * 1.2f;
            grabber.GlobalPosition = hand.GlobalPosition + fwd * (handLength / 2f) - float3.Up * (handLength / 2);
        }
    }

    private float MeasureHandSize(p.Arm bonesLeftArm)
    {
        var hand = Object<f.Slot>(bonesLeftArm.Hand);
        if (hand == null) throw new Exception("Hand not found");
        
        float length = MeasureFinger(bonesLeftArm.Index);
        length = Math.Max(length, MeasureFinger(bonesLeftArm.Middle));
        length = Math.Max(length, MeasureFinger(bonesLeftArm.Ring));
        return Math.Max(length, MeasureFinger(bonesLeftArm.Pinky));

        float MeasureFinger(p.Finger finger)
        {
            var boneRef = finger.LastBone();
            if (boneRef == null) return 0;
            var lastBone = Object<f.Slot>(boneRef);

            if (lastBone == null) return 0;

            return (hand.GlobalPosition - lastBone.GlobalPosition).Magnitude;
        }
    }

    T? field<T>(object obj, string name)
    {
        return (T?)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj);
    }
}