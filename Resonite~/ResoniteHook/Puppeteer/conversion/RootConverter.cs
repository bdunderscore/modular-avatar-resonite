﻿using System.Numerics;
using System.Reflection;
using Assimp = Assimp;
using Elements.Core;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using FrooxEngine.Store;
using Google.Protobuf.Collections;
using SkyFrost.Base;
using Record = SkyFrost.Base.Record;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public partial class RootConverter : IDisposable
{
    private Dictionary<p::AssetID, f.IWorldElement> _assets = new();
    private Dictionary<p::ObjectID, f.IWorldElement> _objects = new();

    private f.Slot _root;
    private f.Slot _assetRoot;

    private readonly f::Engine _engine;
    private readonly f::World _world;

    private T? Asset<T>(p::AssetID? id) where T : class
    {
        if (id == null) return null;
        
        if (!_assets.TryGetValue(id, out var elem))
        {
            return null;
        }

        if (elem is not T) throw new InvalidOperationException($"Expected {typeof(T).Name}, got {elem.GetType().Name}");

        return (T)elem;
    }
    
    private RefID AssetRefID<T>(p::AssetID? id) where T: class
    {
        return (Asset<T>(id) as f.IWorldElement)?.ReferenceID ?? RefID.Null;
    }

    internal T? Object<T>(p::ObjectID? id) where T: class, f.IWorldElement
    {
        if (id == null) return null;
        
        if (!_objects.TryGetValue(id, out var elem))
        {
            return null;
        }

        if (elem is not T) throw new InvalidOperationException($"Expected {typeof(T).Name}, got {elem.GetType().Name}");

        return (T)elem;
    }
    
    private RefID ObjectRefID<T>(p::ObjectID? id) where T: class, f.IWorldElement
    {
        return Object<T>(id)?.ReferenceID ?? RefID.Null;
    }
    
    public RootConverter(f::Engine engine, f::World world)
    {
        InitComponentTypes();
        _engine = engine;
        _world = world;
    }

    public void Dispose()
    {
        _world.Coroutines.StartTask(async () =>
        {
            await new f::ToWorld();

            _root?.Destroy();
            _assetRoot?.Destroy();
        });
    }

    public Task Convert(p.ExportRoot exportRoot, string path)
    {
        if (_assetRoot != null) throw new InvalidOperationException("Already converted");
        
        return _world.Coroutines.StartTask(async () =>
        {
            await new f::ToWorld();

            await _ConvertSync(exportRoot, path);
        });
    }

    private async Task _ConvertSync(p.ExportRoot exportRoot, string path)
    {
        _assetRoot = _world.RootSlot.AddSlot("__Assets");

        await ConvertAssets(exportRoot.Assets);

        _root = await ConvertGameObject(exportRoot.Root, _world.RootSlot);

        _assetRoot.SetParent(_root);
        
        // Reset root transform
        _root.Position_Field.Value = new float3();
        _root.Rotation_Field.Value = new floatQ(0, 0, 0, 1);

        await RunDeferred();

        //_root.AttachComponent<f.DestroyRoot>();
        _root.AttachComponent<SimpleAvatarProtection>();
        _root.AttachComponent<f.ObjectRoot>();
        var dynamicVariableSpace = _root.AttachComponent<f.DynamicVariableSpace>();
        dynamicVariableSpace.SpaceName.Value = "NDMF";
        dynamicVariableSpace.OnlyDirectBinding.Value = true;
        
        SavedGraph savedGraph = _root.SaveObject(f.DependencyHandling.CollectAssets);
        Record record = RecordHelper.CreateForObject<Record>(_root.Name, "", null);

        await new f.ToBackground();

        using (var stream = new FileStream(path, FileMode.Create))
        {
            await f.PackageCreator.BuildPackage(_engine, record, savedGraph, stream, false);
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

    private async Task<f.IWorldElement?> CreateTexture(string name, p::Texture texture)
    {
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
        
        return textureComponent;
    }
    
    private async Task<f.IWorldElement?> CreateMesh(string name, p::mesh.Mesh mesh)
    {
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