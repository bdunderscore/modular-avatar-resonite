using System.Diagnostics;
using Elements.Core;
using FrooxEngine;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public sealed class TranslateContext : IDisposable
{
    private StatusStream _statusStream;
    public StatusStream StatusStream => _statusStream;
    
    private Dictionary<p::AssetID, f.IWorldElement> _assets = new();
    private Dictionary<p::ObjectID, f.IWorldElement> _objects = new();
    
    public Dictionary<p::AssetID, f.IWorldElement> Assets => _assets;
    public Dictionary<p::ObjectID, f.IWorldElement> Objects => _objects;
    
    public f.Slot? Root { get; set; }
    public f.Slot? AssetRoot { get; set; }
    public f.Slot? SettingsNode { get; set; }

    private readonly f::Engine _engine;
    private readonly f::World _world;
    
    public f::Engine Engine => _engine;
    public f::World World => _world;

    private IAssetProvider<Material>? _invisibleMaterial;

    public async Task<IAssetProvider<Material>> GetInvisibleMaterial()
    {
        if (_invisibleMaterial != null)
        {
            return _invisibleMaterial;
        }
        
        await new ToWorld();    
        var slot = AssetRoot!.AddSlot("Invisible Material");
        
        // PBS_RimSpecular is selected as it casts a shadow but otherwise is completely invisible.
        var mat = slot.AttachComponent<PBS_RimSpecular>();
        mat.AlbedoColor.Value = new colorX(0, 0, 0, 0);
        mat.RimColor.Value = new colorX(0, 0, 0, 0);
        mat.Transparent.Value = true;

        return _invisibleMaterial = mat;
    }
    
    public Dictionary<string, List<(p::ObjectID, p.Component)>> ProtoComponents { get; } = new();

    public TranslateContext(f::Engine engine, f::World world, StatusStream statusStream)
    {
        _engine = engine;
        _world = world;
        _statusStream = statusStream;
    }

    public void BuildComponentIndex(p.GameObject root)
    {
        foreach (var c in root.Components)
        {
            var typeName = Google.Protobuf.WellKnownTypes.Any.GetTypeName(c.Component_.TypeUrl);
            if (!ProtoComponents.TryGetValue(typeName, out var components))
            {
                ProtoComponents[typeName] = components = new List<(p::ObjectID, p.Component)>();
            }
            
            components.Add((root.Id, c));
        }

        foreach (var child in root.Children)
        {
            BuildComponentIndex(child);
        }
    }
    
    public T? Asset<T>(p::AssetID? id) where T : class
    {
        if (id == null) return null;
        
        if (!_assets.TryGetValue(id, out var elem))
        {
            return null;
        }

        if (elem is not T) throw new InvalidOperationException($"Expected {typeof(T).Name}, got {elem.GetType().Name}");

        return (T)elem;
    }
    
    public RefID AssetRefID<T>(p::AssetID? id) where T: class
    {
        return (Asset<T>(id) as f.IWorldElement)?.ReferenceID ?? RefID.Null;
    }

    public T? Object<T>(p::ObjectID? id) where T: class, f.IWorldElement
    {
        if (id == null) return null;
        
        if (!_objects.TryGetValue(id, out var elem))
        {
            return null;
        }

        if (elem is not T) throw new InvalidOperationException($"Expected {typeof(T).Name}, got {elem.GetType().Name}");

        return (T)elem;
    }
    
    public RefID ObjectRefID<T>(p::ObjectID? id) where T: class, f.IWorldElement
    {
        return Object<T>(id)?.ReferenceID ?? RefID.Null;
    }

    public void Dispose()
    {
        if (!_world.IsDestroyed)
        {
            _world.RunInUpdates(0, () =>
            {
                AssetRoot?.Destroy();
                Root?.Destroy();
            });
        }
    }

    public async Task<T?> WaitForAssetLoad<T>(f.IAssetProvider<T> assetProvider)
        where T : f.IAsset
    {
        Stopwatch sw = Stopwatch.StartNew();
        while (!assetProvider.IsAssetAvailable && sw.ElapsedMilliseconds < 5000)
        {
            await new f.NextUpdate();
        }
        
        var holder = assetProvider.FindNearestComponentOrSlot();
        if (holder is Component c)
        {
            holder = c.Slot;
        }
        
        if (assetProvider.Asset?.LoadState == AssetLoadState.Failed)
        {
            throw new Exception("Asset load failed: " + holder.Name);
        }
        
        if (assetProvider.IsAssetAvailable)
        {

            Console.WriteLine($"Loaded {holder.Name} in {sw.ElapsedMilliseconds}ms");
            return assetProvider.Asset;
        }
        else
        {
            Console.WriteLine($"[ERROR] Failed to load {holder.Name} in {sw.ElapsedMilliseconds}ms");

            return default;
        }
    }
}