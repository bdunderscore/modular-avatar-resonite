using System.Numerics;
using System.Reflection;
using Elements.Assets;
using Assimp = Assimp;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using FrooxEngine.Store;
using Google.Protobuf;
using Google.Protobuf.Collections;
using nadena.dev.resonity.remote.puppeteer.misc;
using ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Time;
using SkyFrost.Base;
using Record = SkyFrost.Base.Record;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public partial class RootConverter : IDisposable
{
    private TranslateContext _context;

    private Dictionary<p::AssetID, f.IWorldElement> _assets => _context.Assets;
    private Dictionary<p::ObjectID, f.IWorldElement> _objects => _context.Objects;

    private f.Slot _root => _context.Root;
    private f.Slot _assetRoot => _context.AssetRoot;

    private f::Engine _engine => _context.Engine;
    private f::World _world => _context.World;

    private T? Asset<T>(p::AssetID? id) where T : class
    {
        return _context.Asset<T>(id);
    }
    
    private RefID AssetRefID<T>(p::AssetID? id) where T: class
    {
        return _context.AssetRefID<T>(id);
    }

    internal T? Object<T>(p::ObjectID? id) where T: class, f.IWorldElement
    {
        return _context.Object<T>(id);
    }
    
    private RefID ObjectRefID<T>(p::ObjectID? id) where T: class, f.IWorldElement
    {
        return _context.ObjectRefID<T>(id);
    }
    
    public RootConverter(f::Engine engine, f::World world, StatusStream stream)
    {
        InitComponentTypes();
        _context = new TranslateContext(engine, world, stream);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    public Task Convert(p.ExportRoot exportRoot)
    {
        if (_assetRoot != null) throw new InvalidOperationException("Already converted");
        
        return _world.Coroutines.StartTask(async () =>
        {
            await new f::ToWorld();

            await _ConvertSync(exportRoot);
        });
    }

    private async Task _ConvertSync(p.ExportRoot exportRoot)
    {
        // FrooxEngine will attempt to create an Assets slot only if one with that name does not already exist.
        // If it does, the assets slot becomes protected and cannot be reparented. To avoid this, we apply some color codes
        // to the Assets slot name, so it won't be treated as the default Assets slot.
        
        // Try to workaround missing assets issue - maybe a slot named "Assets" under the root is treated specially?
        _context.AssetRoot = _world.RootSlot.AddSlot("<color=cyan>Assets</color>");

        _context.StatusStream.SendProgressMessage("Converting assets...");
        await ConvertAssets(exportRoot.Assets);

        // temporarily put the settings in the asset root, until we build the real root
        // (this ensures it's cleaned up in dispose)
        _context.Root = _context.AssetRoot;
        _context.SettingsNode = CreateSettingsNode();
        
        _context.StatusStream.SendProgressMessage("Converting game objects...");
        _context.BuildComponentIndex(exportRoot.Root);
        _context.Root = await ConvertGameObject(exportRoot.Root, _world.RootSlot);

        _assetRoot.SetParent(_context.Root);
        _context.SettingsNode.SetParent(_context.Root);
        
        // Reset root transform
        _root.Position_Field.Value = new float3();
        _root.Rotation_Field.Value = new floatQ(0, 0, 0, 1);

        await RunDeferred();

        EmbedVersionInfo(exportRoot);

        //_root.AttachComponent<f.DestroyRoot>();
        _root.AttachComponent<SimpleAvatarProtection>();
        _root.AttachComponent<f.ObjectRoot>();
        var dynamicVariableSpace = _root.AttachComponent<f.DynamicVariableSpace>();
        dynamicVariableSpace.SpaceName.Value = ResoNamespaces.ModularAvatarNamespace;
        dynamicVariableSpace.OnlyDirectBinding.Value = true;

        var avatarRootVar = _root.AttachComponent<f.DynamicReferenceVariable<f.Slot>>();
        var avatarRootField = _root.AttachComponent<f.ReferenceField<f.Slot>>();
        avatarRootVar.VariableName.Value = ResoNamespaces.AvatarRoot;
        avatarRootField.Reference.Target = _root;
        avatarRootVar.Reference.DriveFrom(avatarRootField.Reference);

        // Move assets to the root (just to be sure)
        _assetRoot.SetParent(_root);

        _context.StatusStream.SendProgressMessage("Exporting avatar...");
        SavedGraph savedGraph = _root.SaveObject(f.DependencyHandling.CollectAssets);
        Record record = RecordHelper.CreateForObject<Record>(_root.Name, "", null);
        
        // Destroy the root object now so we don't keep on generating unnecessary asset variants
        _root.Destroy();

        await new f.ToBackground();

        using (var stream = new MemoryStream())
        {
            // BuildPackage tries to close the stream, which prevents us from streaming data back to unity;
            // filter out this close call.
            var wrapper = new BlockClosureStream(stream);
            await f.PackageCreator.BuildPackage(_engine, record, savedGraph, wrapper, true);
            stream.Flush();
            
            // ReSharper disable once MethodHasAsyncOverload
            var bytes = ByteString.CopyFrom(stream.GetBuffer(), 0, (int)stream.Length);
            _context.StatusStream.SendCompletedAvatar(bytes);
        }
    }

    private void EmbedVersionInfo(p.ExportRoot exportRoot)
    {
        if (exportRoot.Versions.Count == 0) return;
        
        var versionInfo = _root.AddSlot("<color=cyan>Modular Avatar</color> - Version Info");
        versionInfo.OrderOffset = 100;

        foreach (var vi in exportRoot.Versions)
        {
            var dynVar = versionInfo.AttachComponent<DynamicValueVariable<string>>();
            dynVar.VariableName.Value = ResoNamespaces.VersionNamespace + vi.PackageName;
            dynVar.Value.Value = vi.Version;
        }
    }

    private async Task<f.Slot> ConvertGameObject(p.GameObject gameObject, f::Slot parent)
    {
        var slot = parent.AddSlot(gameObject.Name);

        slot.ActiveSelf = gameObject.Enabled;
        slot.Position_Field.Value = gameObject.LocalTransform.Position.Vec3();
        slot.Rotation_Field.Value = gameObject.LocalTransform.Rotation.Quat();
        slot.Scale_Field.Value = gameObject.LocalTransform.Scale.Vec3();
        
        _objects[gameObject.Id] = slot;

        foreach (var component in gameObject.Components)
        {
            if (component != null) await ConvertComponent(slot, component);
        }

        foreach (var child in gameObject.Children)
        {
            if (child != null) await ConvertGameObject(child, slot);
        }

        return slot;
    }

    private async Task ConvertAssets(RepeatedField<p::Asset> exportRootAssets)
    {
        List<Task> assetImportTasks = new();
        foreach (var asset in exportRootAssets)
        {
            assetImportTasks.Add(ConvertAsset(asset));
        }

        foreach (var task in assetImportTasks)
        {
            await task; // propagate exceptions
        }
    }

    private f.Slot AssetSubslot(string subslotName, f.Slot root)
    {
        f.Slot? slot = root.FindChild(subslotName);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (slot == null)
        {
            slot = root.AddSlot(subslotName);
        }

        return slot;
    }

    private Stopwatch _textureBuildTimer = new();
    private async Task<f.IWorldElement?> CreateTexture(p::Asset asset, p::Texture texture)
    {
        var name = asset.Name;
        var holder = AssetSubslot("Textures", _assetRoot).AddSlot(name);

        await new f::ToBackground();
        
        string extension;

        switch (texture.Format)
        {
            case p.TextureFormat.Png:
                extension = ".png";
                break;
            case p.TextureFormat.Jpeg:
                extension = ".jpg";
                break;
            default:
                System.Console.WriteLine("Unknown texture format");
                return null;
        }
        
        // only support blob contents for now
        if (texture.Bytes == null)
        {
            System.Console.WriteLine("Texture has no blob");
            return null;
        }

        var path = _engine.LocalDB.GetTempFilePath(extension);
        
        await File.WriteAllBytesAsync(path, texture.Bytes.Inline.ToByteArray());
        Uri uri = await _engine.LocalDB.ImportLocalAssetAsync(path, LocalDB.ImportLocation.Move);

        await new f.ToWorld();

        var textureComponent = holder.AttachComponent<f.StaticTexture2D>();
        textureComponent.URL.Value = uri;
        textureComponent.IsNormalMap.Value = texture.IsNormalMap;

        // Initially, we generate asset variants up to a 512x512 size. This ensures that the textures are visible
        // immediately on import, without blowing up the size of the resonitepackage too much. We set the MaxSize back
        // to its final value just before we generate the package and destroy the StaticTexture2D.
        
        textureComponent.MaxSize.Value = 512;
        
        System.Diagnostics.Stopwatch timeout = new();
        timeout.Start();
        Defer(PHASE_FINALIZE, "Waiting for texture variant generation...", async () =>
        {
            if (_textureBuildTimer.ElapsedMilliseconds > 5000)
            {
                return; // don't spend a long time on this
            }
            _textureBuildTimer.Start();
            await _context.WaitForAssetLoad<ITexture2D>(textureComponent);
            _textureBuildTimer.Stop();
        });
        Defer(PHASE_JUST_BEFORE_PACKAGING, "Texture post-configuration", () =>
        {
            if (_textureBuildTimer.ElapsedMilliseconds > 0)
            {
                Console.WriteLine($"Waited {_textureBuildTimer.ElapsedMilliseconds}ms for textures to become available.");
                _textureBuildTimer.Reset();
            }
            textureComponent.MaxSize.Value = texture.HasMaxResolution ? (int) texture.MaxResolution : null;
        });

        return textureComponent;
    }

    private async Task<f.IWorldElement?> CreateMesh(p::Asset asset, p::mesh.Mesh mesh)
    {
        var name = asset.Name;
        var holder = AssetSubslot("Meshes", _assetRoot).AddSlot(name);

        await new f::ToBackground();

        var meshx = mesh.ToMeshX();
        
        string tempFilePath = _engine.LocalDB.GetTempFilePath(".mesh");
        meshx.SaveToFile(tempFilePath);
        
        Uri uri = await _engine.LocalDB.ImportLocalAssetAsync(tempFilePath, LocalDB.ImportLocation.Move);
        
        await new f::ToWorld();
        
        var meshComponent = holder.AttachComponent<f::StaticMesh>();
        meshComponent.URL.Value = uri;

        return meshComponent;
    }

}

class NullProgress : Elements.Core.IProgressIndicator
{
    public void UpdateProgress(float percent, LocaleString progressInfo, LocaleString detailInfo)
    {
    }

    public void ProgressDone(LocaleString message)
    {
    }

    public void ProgressFail(LocaleString message)
    {
    }
}