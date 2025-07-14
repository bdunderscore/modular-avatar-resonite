using System.Numerics;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.ProtoFlux;

namespace nadena.dev.resonity.remote.puppeteer.dynamic_flux;

public class FluxBuilder : IAsyncDisposable, IFluxGroup
{
    private const float Padding = 0.05f;
    private readonly FluxGroup _root;
    private readonly Slot _packInto;
    
    public FluxBuilder(Slot packInto)
    {
        this._root = new FluxGroup(packInto, false);
        this._packInto = packInto;
    }

    public FluxBuilder(Slot parent, string name)
        : this(parent.AddSlot(name))
    {
    }
    
    public async ValueTask DisposeAsync()
    {
        await new NextUpdate();

        _root.Layout();
        
        foreach (var node in _packInto.GetComponentsInChildren<ProtoFluxNode>())
        {
            node.RemoveVisual();
        }
    }

    public IFluxGroup Horizontal(string groupName = "Horizontal") => _root.Horizontal(groupName);
    public IFluxGroup Vertical(string groupName = "Vertical") => _root.Vertical(groupName);
    public T Spawn<T>(string? slotName = null) where T : ProtoFluxNode, new() => _root.Spawn<T>(slotName);
}