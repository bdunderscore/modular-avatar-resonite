﻿using Assimp.Configs;
using Elements.Core;
using FrooxEngine.Store;
using Google.Protobuf;
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
    private List<Action> _deferredConfiguration = new();

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

    private T? Object<T>(p::ObjectID? id) where T: class, f.IWorldElement
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

        _root = ConvertGameObject(exportRoot.Root, _world.RootSlot);
        foreach (var action in _deferredConfiguration)
        {
            action();
        }
        
        _assetRoot.SetParent(_root);
        
        // Temporary
        _root.AttachComponent<f.ObjectRoot>();
        _root.AttachComponent<f.Grabbable>();

        SavedGraph savedGraph = _root.SaveObject(f.DependencyHandling.CollectAssets);
        Record record = RecordHelper.CreateForObject<Record>(_root.Name, "", null);

        await new f.ToBackground();

        using (var stream = new FileStream(path, FileMode.Create))
        {
            await f.PackageCreator.BuildPackage(_engine, record, savedGraph, stream, false);
        }
    }

    private f.Slot ConvertGameObject(p.GameObject gameObject, f::Slot parent)
    {
        var slot = parent.AddSlot(gameObject.Name);

        slot.Position_Field.Value = gameObject.LocalTransform.Position.Vec3();
        slot.Rotation_Field.Value = gameObject.LocalTransform.Rotation.Quat();
        slot.Scale_Field.Value = gameObject.LocalTransform.Scale.Vec3();
        
        _objects[gameObject.Id] = slot;

        foreach (var component in gameObject.Components)
        {
            if (component != null) ConvertComponent(slot, component);
        }

        foreach (var child in gameObject.Children)
        {
            if (child != null) ConvertGameObject(child, slot);
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

    private async Task<f.IWorldElement?> CreateMaterial(f::Slot holder, p::Material material)
    {
        await new f::ToWorld();
        
        // TODO: handle other material types
        var materialComponent = holder.AttachComponent<f::PBS_Metallic>();
        _deferredConfiguration.Add(() =>
        {
            materialComponent.AlbedoTexture.Value = AssetRefID<f.IAssetProvider<f.Texture2D>>(material.MainTexture);    
        });
        materialComponent.AlbedoColor.Value = material.MainColor?.ColorX() ?? colorX.White;

        return materialComponent;
    }

    private async Task<f.IWorldElement?> CreateTexture(f::Slot holder, p::Texture texture)
    {
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
    
    private async Task<f.IWorldElement?> CreateMesh(f::Slot holder, p::mesh.Mesh mesh)
    {
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