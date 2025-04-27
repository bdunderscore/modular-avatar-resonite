using Elements.Core;
using FrooxEngine;
using FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Assets;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Users;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;
using nadena.dev.resonity.remote.puppeteer.dynamic_flux;

namespace nadena.dev.resonity.remote.puppeteer.filters;

public class MeshLoadingFilter
{
    private Slot _avatarRoot;

    public MeshLoadingFilter(Slot avatarRoot)
    {
        this._avatarRoot = avatarRoot;
    }

    public async Task Apply()
    {
        var centeredRoot = _avatarRoot.FindChild("CenteredRoot");
        var assets = centeredRoot?.FindChild("__Assets");
        if (assets == null) return;

        var meshAssets = assets.GetComponentsInChildren<StaticMesh>();
        if (meshAssets.Count == 0) return;

        var loadingSpinner = await SpawnLoadingSpinner();
        var headProxy = _avatarRoot.FindChild("Head Proxy");
        loadingSpinner.LocalPosition = headProxy.LocalPosition;

        var checkRoot = _avatarRoot.AddSlot("Mesh loading gate");
        var checkRootValue = checkRoot.AttachComponent<ValueField<bool>>();
        centeredRoot.ActiveSelf_Field.DriveFrom(checkRootValue.Value);
        
        using var rootFlux = new FluxBuilder(checkRoot, "ProtoFlux");

        List<INodeValueOutput<bool>> assetLoadingStatus = new();
        using (var loadingCheckGroup = rootFlux.Vertical("Asset Checks"))
        {
            foreach (var mesh in meshAssets)
            {
                assetLoadingStatus.Add(CreateAssetLoadingStatusChecker(loadingCheckGroup, mesh));
            }
        }
        
        var boolAND = rootFlux.Spawn<AND_Multi_Bool>();
        foreach (var status in assetLoadingStatus)
        {
            boolAND.Operands.Add().Target = status;
        }

        using (var vertical = rootFlux.Vertical())
        {
            var driver = vertical.Spawn<ValueFieldDrive<bool>>();
            driver.TrySetRootTarget(checkRootValue.Value);
            driver.Value.Target = boolAND;

            using (var horizontal = vertical.Horizontal())
            {
                var notGate = horizontal.Spawn<NOT_Bool>();
                notGate.A.Target = boolAND;
                
                var spinnerDriver = horizontal.Spawn<ValueFieldDrive<bool>>();
                spinnerDriver.TrySetRootTarget(loadingSpinner.ActiveSelf_Field);
                spinnerDriver.Value.Target = notGate;
            }
        }
    }

    private async Task<Slot> SpawnLoadingSpinner()
    {
        var slot = _avatarRoot.AddSlot("LoadingSpinner");

        Console.WriteLine("==== Spawn loading spinner ==="); 
        bool result = await slot.LoadObjectAsync(new Uri(CloudSpawnAssets.LoadingSpinner));
        Console.WriteLine("Result: " + result);
        
        slot.LocalRotation = floatQ.Identity;
        slot.LocalScale = float3.One;

        return slot;
    }

    private INodeValueOutput<bool> CreateAssetLoadingStatusChecker(FluxBuilder loadingCheckGroup, StaticMesh mesh)
    {
        using var flux = loadingCheckGroup.Horizontal("Status Check: " + mesh.Slot.Name);

        UsersAssetLoadProgress<Mesh> userLoadingStatus = mesh.Slot.AttachComponent<UsersAssetLoadProgress<Mesh>>();
        userLoadingStatus.Asset.Target = mesh;

        ElementSource<UsersAssetLoadProgress<Mesh>> source;
        LocalUser localUser;
        using (var vertical = flux.Vertical())
        {
            source = flux.Spawn<ElementSource<UsersAssetLoadProgress<Mesh>>>();
            source.TrySetRootSource(userLoadingStatus);
            
            localUser = vertical.Spawn<LocalUser>();
        }

        AssetLoadProgress<Mesh> loadingStatus;
        ValueInput<AssetLoadState> fullyLoaded;
        using (var vertical = flux.Vertical())
        {
            loadingStatus = vertical.Spawn<AssetLoadProgress<Mesh>>();
            loadingStatus.Tracker.Target = source;
            loadingStatus.User.Target = localUser;

            fullyLoaded = vertical.Spawn<ValueInput<AssetLoadState>>();
            fullyLoaded.Value.Value = AssetLoadState.FullyLoaded;
        }

        var eq = flux.Spawn<ValueEquals<AssetLoadState>>();
        eq.A.Target = loadingStatus.LoadState;
        eq.B.Target = fullyLoaded;

        return eq;
    }
}