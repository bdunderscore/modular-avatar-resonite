using System.Diagnostics;
using System.Text.RegularExpressions;
using Elements.Assets;
using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Slots;
using PF = FrooxEngine.FrooxEngine.ProtoFlux;
using nadena.dev.resonity.remote.puppeteer.dynamic_flux;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

using F = FrooxEngine;
using PR = Google.Protobuf.Reflection;
using P = nadena.dev.ndmf.proto;
using PM = nadena.dev.ndmf.proto.mesh;

public class DynBoneAutoEnableFilter(TranslateContext context)
{
    private Dictionary<F.Slot, BoneGroup> boneSlotToEnableVar = new();
    private Dictionary<F.DynamicBoneChain, BoneGroup> DynamicChains = new();
    private HashSet<string> usedSlotNames;

    class BoneGroup
    {
        public string Name = "";
    }
    
    public async Task Apply()
    {
        FindDynamicBoneGroups();

        await AddRendererActiveStateRelays();
        
        BindDynamicBoneGroups();
    }

    void BindDynamicBoneGroups()
    {
        foreach ((var db, var group) in DynamicChains)
        {
            var driver = db.Slot.AttachComponent<F.DynamicValueVariableDriver<bool>>();
            driver.VariableName.Value = ResoNamespaces.DynBoneActivator + group.Name;
            driver.DefaultValue.Value = false;
            driver.Target.Target = db.EnabledField;
        }
    }

    private async Task AddRendererActiveStateRelays()
    {
        List<Task> tasks = new List<Task>();
        foreach (var smr in context.Root!.GetComponentsInChildren<F.SkinnedMeshRenderer>())
        {
            tasks.Add(ProcessRenderer(smr));
        }

        await Task.WhenAll(tasks);
    }
    
    private async Task ProcessRenderer(F.SkinnedMeshRenderer smr)
    {
        var mesh = smr.Mesh.Target;
        if (mesh == null) return;
        
        // Await mesh loading
        Stopwatch timer = new();
        timer.Start();

        var meshAsset = await context.WaitForAssetLoad(mesh);
        if (meshAsset == null)
        {
            return;
        }

        HashSet<int> usedBoneIndices = new HashSet<int>();
        foreach (var binding in mesh.Asset?.Data?.RawBoneBindings ?? [])
        {
            for (int i = 0; i < 4; i++)
            {
                if (binding[i].weight > 0) 
                {
                    usedBoneIndices.Add(binding[i].boneIndex);
                }
            }
        }

        HashSet<string> groups = new HashSet<string>();
        foreach (var index in usedBoneIndices)
        {
            if (index >= smr.Bones.Count) continue;
            var bone = smr.Bones[index];
            if (bone == null) continue;
            if (!boneSlotToEnableVar.TryGetValue(bone, out var group)) continue;

            groups.Add(group.Name);
        }

        if (groups.Count == 0) return;
        
        var slot = smr.Slot.AddSlot("DynBone Auto Enable");

        var f_true_multidriver = slot.AttachComponent<F.ValueMultiDriver<bool>>();
        f_true_multidriver.Value.Value = true;

        var f_active_multidriver = slot.AttachComponent<F.ValueMultiDriver<bool>>();
        f_active_multidriver.Value.DriveFrom(f_active_multidriver.Value, writeBack: true);
        await BuildActiveInHierarchySensor(slot, f_active_multidriver.Value);

        foreach (var group in groups)
        {
            var dynVar = slot.AttachComponent<F.DynamicValueVariable<bool>>();
            var boolDriver = slot.AttachComponent<F.BooleanValueDriver<string>>();
            boolDriver.TrueValue.Value = ResoNamespaces.DynBoneActivator + group;
            boolDriver.TargetField.Target = dynVar.VariableName;
            dynVar.OverrideOnLink.Value = true;

            f_true_multidriver.Drives.Add().Target = dynVar.Value;
            f_active_multidriver.Drives.Add().Target = boolDriver.State;
        }
    }

    private void FindDynamicBoneGroups()
    {
        foreach (var db in context.Root!.GetComponentsInChildren<F.DynamicBoneChain>())
        {
            var guid = Guid.NewGuid();
            var group = new BoneGroup();
            group.Name = "DBGroup_" + guid;

            bool hasOtherComponents = false;
            
            foreach (var bone in db.Bones)
            {
                var slot = bone.BoneSlot.Target;
                if (slot != null)
                {
                    boneSlotToEnableVar[slot] = group;
                }

                foreach (var component in slot.GetComponentsInChildren<F.IComponent>())
                {
                    if (component.Slot.IsChildOf(db.Slot, includeSelf:true)) continue;
                    // Some avatars have multiple DBC components on the same bone (with complex ignore configurations).
                    // Since the DBC component itself ignores it transform, we don't need to worry about it.
                    if (component.Slot.EnumerateParents().Append(component.Slot)
                        .Any(s => (s as F.Slot)?.Tag == RootConverter.DynBoneControllerTag))
                    {
                        continue;
                    }
                    
                    hasOtherComponents = true;
                    break;
                }
            }
            
            if (!hasOtherComponents)
            {
                DynamicChains.Add(db, group);
            }
        }
    }

    private async Task<(F.Slot, F.IField<bool>)> BuildActiveInHierarchySensor(F.Slot slot, IField<bool> field)
    {
        await using var builder = new FluxBuilder(slot);
        var vertical = builder.Vertical();
        var vs = vertical.Spawn<PF.CoreNodes.ValueSource<bool>>();
        var globalRef = vs.Slot.AttachComponent<GlobalReference<F.IValue<bool>>>();
        globalRef.Reference.Target = field;
        vs.Source.Target = globalRef;

        var activateGroup = vertical.Horizontal();
        {
            OnActivated onActivated;
            OnStart onStart;
            ValueInput<bool> vs_true;
            var subVert = activateGroup.Vertical();
            {
                onActivated = subVert.Spawn<OnActivated>();
                onStart = subVert.Spawn<OnStart>();
                vs_true = subVert.Spawn<ValueInput<bool>>();
                vs_true.Value.Value = true;
            }

            var write = activateGroup.Spawn<ValueWrite<FrooxEngineContext, bool>>();
            write.Variable.Target = vs;
            write.Value.Target = vs_true;

            onActivated.Trigger.Target = write;
            onStart.Trigger.Target = write;
        }

        var deactivateGroup = vertical.Horizontal();
        {
            OnDeactivated onDeactivated;
            ValueInput<bool> vs_false;
            var subVert = deactivateGroup.Vertical();
            {
                onDeactivated = subVert.Spawn<OnDeactivated>();
                vs_false = subVert.Spawn<ValueInput<bool>>();
                vs_false.Value.Value = false;
            }

            var write = deactivateGroup.Spawn<ValueWrite<FrooxEngineContext, bool>>();
            write.Variable.Target = vs;
            write.Value.Target = vs_false;

            onDeactivated.Trigger.Target = write;
        }

        return (slot, field);
    }
}