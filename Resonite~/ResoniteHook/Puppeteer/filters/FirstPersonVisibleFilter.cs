using System.Diagnostics;
using Elements.Core;
using FrooxEngine;
using nadena.dev.resonity.remote.puppeteer.logging;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

using F = FrooxEngine;
using PR = Google.Protobuf.Reflection;
using P = nadena.dev.ndmf.proto;
using PM = nadena.dev.ndmf.proto.mesh;

internal partial class FirstPersonVisibleFilter(TranslateContext ctx)
{
    Dictionary<F.Slot, bool> IsVisibleInFirstPerson = new();
    private Dictionary<F.IWorldElement, int> meshSlotReferences = new();
    private F.IAssetProvider<F.Material>? invisibleMaterial;
    
    public async Task Apply()
    {
        ResolveVisibility();

        foreach (var mr in ctx.Root!.GetComponentsInChildren<F.MeshRenderer>())
        {
            var mesh = mr.Mesh.Target;
            if (mesh == null) continue;
            meshSlotReferences[mesh] = meshSlotReferences.GetValueOrDefault(mesh) + 1;
        }

        List<Task> tasks = new();
        foreach (var smr in ctx.Root.GetComponentsInChildren<F.SkinnedMeshRenderer>())
        {
            var task = ProcessRenderer(smr);
            await task; // for debugging
            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
    }

    private async Task<F.IAssetProvider<F.Material>> GetInvisibleMaterial()
    {
        await new F.ToWorld();
        if (invisibleMaterial != null) return invisibleMaterial;
        
        var slot = ctx.AssetRoot!.AddSlot("Invisible Material");
        var mat = slot.AttachComponent<F.OverlayUnlitMaterial>();
        mat.BlendMode.Value = F.BlendMode.Cutout;
        mat.AlphaCutoff.Value = 1;
        mat.TintColor = colorX.Clear;

        return invisibleMaterial = mat;
    }

    private void ResolveVisibility()
    {
        if (!ctx.ProtoComponents.TryGetValue(P.VisibleInFirstPerson.Descriptor.FullName, out var componentList))
        {
            componentList = new();
        }
        
        foreach ((var slotId, var component) in componentList)
        {
            var visibleComponent = component.Component_.Unpack<P.VisibleInFirstPerson>();
            var slot = ctx.Object<F.Slot>(slotId);

            if (slot != null)
            {
                IsVisibleInFirstPerson[slot] = visibleComponent.Visible;
            }
        }

        if (!IsVisibleInFirstPerson.ContainsKey(ctx.Root!)) IsVisibleInFirstPerson[ctx.Root!] = true;
        
        ResolveVisibility(ctx.Root!);
    }

    private void ResolveVisibility(F.Slot slot)
    {
        if (!IsVisibleInFirstPerson.ContainsKey(slot))
        {
            IsVisibleInFirstPerson[slot] = IsVisibleInFirstPerson[slot.Parent];
        }

        foreach (var child in slot.Children)
        {
            ResolveVisibility(child);
        }
    }

    private async Task ProcessRenderer(F.SkinnedMeshRenderer smr)
    {
        await new F.ToWorld();
        
        // Skip broken renderers
        if (smr.Mesh.Target == null) return;
        
        // Skip fully visible renderers
        if (smr.Bones.All(b => b == null || !IsVisibleInFirstPerson.TryGetValue(b, out var visible) || visible))
        {
            return;
        }
        
        // If this mesh is shared, duplicate it first.
        if (meshSlotReferences[smr.Mesh.Target] > 1)
        {
            var meshSlot = (smr.Mesh.Target as F.IComponent).Slot; 
            meshSlot = meshSlot.Duplicate();
            smr.Mesh.Target = meshSlot.GetComponent<F.IAssetProvider<F.Mesh>>();
        }

        var boneToVisible =
            smr.Bones.Select(bone =>
                bone == null || (IsVisibleInFirstPerson.TryGetValue(bone, out var visible) && visible)
            ).ToList();

        var results = await ProcessFPVMesh((F.StaticMesh)smr.Mesh.Target, boneToVisible);

        int firstNewIndex = smr.Materials.Count;
        foreach (var oldIndex in results.CloneToOriginalIndex)
        {
            var newMatEntry = smr.Materials.Add();
            newMatEntry.DriveFrom(smr.Materials.GetElement(oldIndex));
        }

        var rmo = smr.Slot.AttachComponent<F.RenderMaterialOverride>();
        rmo.Renderer.Target = smr;
        rmo.Context.Value = RenderingContext.UserView;
        for (int i = firstNewIndex; i < smr.Materials.Count; i++)
        {
            var mo = rmo.Overrides.Add();
            mo.Index.Value = i;
            mo.Material.Target = await GetInvisibleMaterial();
        }
        
        foreach (var alwaysInvisibleIndex in results.AlwaysInvisibleSubmeshes)
        {
            var mo = rmo.Overrides.Add();
            mo.Index.Value = alwaysInvisibleIndex;
            mo.Material.Target = await GetInvisibleMaterial();
        }
        
        var avatarWorn = smr.Slot.AttachComponent<F.DynamicValueVariableDriver<bool>>();
        avatarWorn.VariableName.Value = ResoNamespaces.AvatarWornLocal;
        avatarWorn.Target.Target = rmo.EnabledField;
    }
}