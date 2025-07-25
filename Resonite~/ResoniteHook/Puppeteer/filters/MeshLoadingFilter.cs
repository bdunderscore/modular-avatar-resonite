﻿using Elements.Core;
using FrooxEngine;
using FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Casts;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Assets;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Async;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Slots;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Variables;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Math.Random;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;
using nadena.dev.resonity.remote.puppeteer.dynamic_flux;
using nadena.dev.resonity.remote.puppeteer.logging;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class MeshLoadingFilter(TranslateContext context)
{
    private Slot _avatarRoot => context.Root!;
    
    public async Task Apply()
    {
        var centeredRoot = context.Root!.FindChild("CenteredRoot");
        var assets = context.AssetRoot;
        if (assets == null) return;

        var settingsRoot = context.SettingsNode!;
        var gateRoot = settingsRoot.AddSlot("Avatar Loading Display");
        var spinnerTask = SpawnLoadingSpinner(gateRoot);

        var renderers = _avatarRoot.FindChild("CenteredRoot")
                            ?.GetComponentsInChildren<MeshRenderer>()
                        ?? new List<MeshRenderer>();
        foreach (var renderer in renderers)
        {
            if (renderer.EnumerateParents().Contains(gateRoot)) continue;
            if (renderer.Mesh.Target == null) continue;

            var slot = renderer.Slot.AddSlot("Loaded check");
            var meshMetadata = slot.AttachComponent<MeshAssetMetadata>();
            meshMetadata.Mesh.DriveFrom(renderer.Mesh);

            var eqDriver = slot.AttachComponent<ValueEqualityDriver<int>>();
            eqDriver.TargetValue.Target = meshMetadata.VertexCount;
            eqDriver.Reference.Value = -1;
            eqDriver.UseApproximateComparison.Value = false;

            var bvDriver = slot.AttachComponent<BooleanValueDriver<string>>();
            eqDriver.Target.Target = bvDriver.State;
            bvDriver.TrueValue.Value = ResoNamespaces.LoadingGate_NotLoaded;
            bvDriver.FalseValue.Value = null;

            var dynVar = slot.AttachComponent<DynamicValueVariable<bool>>();
            bvDriver.TargetField.Target = dynVar.VariableName;
            dynVar.Value.Value = false;
            dynVar.OverrideOnLink.Value = true;

            var driver = renderer.Slot.AttachComponent<DynamicValueVariableDriver<bool>>();
            driver.VariableName.Value = ResoNamespaces.LoadingGate_NotLoaded;
            driver.DefaultValue.Value = true;
            driver.Target.Target = renderer.EnabledField;
        }

        var spinner = await spinnerTask;
        var spinnerDriver = gateRoot.AttachComponent<DynamicValueVariableDriver<bool>>();
        var notDriver = gateRoot.AttachComponent<BooleanValueDriver<bool>>();
        notDriver.TargetField.Target = spinner.ActiveSelf_Field;
        notDriver.FalseValue.Value = true;
        notDriver.TrueValue.Value = false;
        spinnerDriver.VariableName.Value = ResoNamespaces.LoadingGate_NotLoaded;
        spinnerDriver.Target.Target = notDriver.State;
        spinnerDriver.DefaultValue.Value = true;
    }

    private async Task<Slot> SpawnLoadingSpinner(Slot parent)
    {
        var slot = parent.AddSlot("LoadingSpinner");

        await context.Gadgets.LoadingStandin.Spawn(slot);
        
        slot.LocalRotation = floatQ.Identity;
        slot.LocalScale = float3.One;

        return slot;
    }
}