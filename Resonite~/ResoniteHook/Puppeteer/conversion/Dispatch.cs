#nullable enable

using Google.Protobuf;

using e = Elements.Core;
using f = FrooxEngine;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public partial class RootConverter
{
    private const int PHASE_BUILD_COLLIDERS = -1;
    private const int PHASE_RESOLVE_REFERENCES = 0;
    private const int PHASE_RIG_SETUP = 100;
    private const int PHASE_AVATAR_SETUP = 200;
    private const int PHASE_ENABLE_RIG = 500;

    private const int PHASE_POSTPROCESS = 1000;
    private const int PHASE_AWAIT_CLOUD_SPAWN = 2000;
    private const int PHASE_FINALIZE = 10000;
    private const int PHASE_JUST_BEFORE_PACKAGING = 20000;
    
    private Dictionary<string, ComponentBuilder> ComponentTypes = new();

    private delegate Task<f::IComponent?> ComponentBuilder(f::Slot parent, p::Component component, p.ObjectID componentID);
    private delegate Task<f::IComponent> TypedComponentBuilder<M>(f::Slot parent, M component) where M : IMessage, new();
    private delegate Task<f::IComponent?> TypedComponentBuilder2<M>(f::Slot parent, M component, p.ObjectID componentID) where M : IMessage, new();

    private Dictionary<string, AssetBuilder> AssetTypes = new();
    
    private delegate Task<f.IWorldElement?> AssetBuilder(p::Asset name, p::Asset asset);
    private delegate Task<f.IWorldElement?> TypedAssetBuilder<M>(p::Asset outerAsset, M asset) where M : IMessage, new();

    private void RegisterComponentType<M>(TypedComponentBuilder<M> builder)
        where M : IMessage, new()
    {
        ComponentTypes[new M().Descriptor.FullName] =
            async (obj, untyped, _) => await builder(obj, untyped.Component_.Unpack<M>());
    }
    
    private void RegisterComponentType<M>(TypedComponentBuilder2<M> builder)
        where M : IMessage, new()
    {
        ComponentTypes[new M().Descriptor.FullName] =
            async (obj, untyped, id) => await builder(obj, untyped.Component_.Unpack<M>(), id);
    }
    
    private void RegisterAssetType<M>(TypedAssetBuilder<M> builder)
        where M : IMessage, new()
    {
        AssetTypes[new M().Descriptor.FullName] =
            (obj, untyped) => builder(obj, untyped.Asset_.Unpack<M>());
    }

    private SortedDictionary<int, List<Func<Task>>> _deferredActions = new();

    private void Defer(int phase, Func<Task> action)
    {
        if (!_deferredActions.TryGetValue(phase, out var actions))
        {
            actions = new List<Func<Task>>();
            _deferredActions[phase] = actions;
        }
        
        actions.Add(action);
    }
    
    private void Defer(int phase, Action action)
    {
        Defer(phase, () =>
        {
            action();
            return Task.CompletedTask;
        });
    }

    private async Task RunDeferred()
    {
        while (_deferredActions.Count > 0)
        {
            var firstKV = _deferredActions.First();
            _deferredActions.Remove(firstKV.Key);
            
            foreach (var action in firstKV.Value)
            {
                await action();
                await new f.ToWorld();
            }
        }
    }

    private void InitComponentTypes()
    {
        // Components
        RegisterComponentType<p::MeshRenderer>(CreateMeshRenderer);
        RegisterComponentType<p::AvatarDescriptor>(SetupAvatar);
        RegisterComponentType<p::DynamicCollider>(ProcessDynamicCollider);
        RegisterComponentType<p::DynamicBone>(ProcessDynamicBone);
        
        // Assets
        RegisterAssetType<p::Texture>(CreateTexture);
        RegisterAssetType<p::Material>(CreateMaterial);
        RegisterAssetType<p::mesh.Mesh>(CreateMesh);
    }
    
    
    private async Task ConvertComponent(f::Slot parent, p.Component component)
    {
        var typeName = Google.Protobuf.WellKnownTypes.Any.GetTypeName(component.Component_.TypeUrl);

        if (!ComponentTypes.TryGetValue(typeName, out var componentBuilder))
        {
            System.Console.WriteLine("Unknown component type: " + typeName);
            return;
        }
        
        var fComponent = await componentBuilder(parent, component, component.Id);
        if (fComponent != null)
        {
            fComponent.Enabled = component.Enabled;
            _objects[component.Id] = fComponent;
        }
    }

    private async Task ConvertAsset(p.Asset asset)
    {
        var typeName = Google.Protobuf.WellKnownTypes.Any.GetTypeName(asset.Asset_.TypeUrl);
        
        if (!AssetTypes.TryGetValue(typeName, out var assetBuilder))
        {
            System.Console.WriteLine("Unknown asset type: " + typeName);
            return;
        }
        
        var fAsset = await assetBuilder(asset, asset);
        await new f::ToWorld();
        if (fAsset != null) _assets[asset.Id] = fAsset;
    }
}