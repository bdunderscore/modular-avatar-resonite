using Elements.Core;
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
        var assets = context.AssetRoot;
        if (assets == null) return;

        var loadCheck = context.Root.AddSlot("<color=cyan>Modular Avatar</color> - Mesh loaded check");
        var loadStatus = loadCheck.AttachComponent<MeshRendererLoadStatus>();
        var loadStatusDV = loadCheck.AttachComponent<DynamicValueVariable<bool>>();
        loadStatusDV.Value.Value = false;
        var loadStatusBD = loadCheck.AttachComponent<BooleanValueDriver<string>>();
        loadStatusBD.FalseValue.Value = ResoNamespaces.LoadingGate_NotLoaded;
        loadStatusBD.TrueValue.Value = null;
        loadStatusBD.TargetField.Target = loadStatusDV.VariableName;
        loadStatusBD.State.DriveFrom(loadStatus.IsLoaded);

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

            var driver = renderer.Slot.AttachComponent<DynamicValueVariableDriver<bool>>();
            driver.VariableName.Value = ResoNamespaces.LoadingGate_NotLoaded;
            driver.DefaultValue.Value = true;
            driver.Target.Target = renderer.EnabledField;
            
            loadStatus.Renderers.Add().Target = renderer;
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