using System.ComponentModel;
using FrooxEngine;
using FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using nadena.dev.resonity.gadgets;

namespace nadena.dev.resonity.remote.puppeteer.dynamic_flux;

public interface IFluxGroup
{
    public T Spawn<T>(string? slotName = null) where T : ProtoFluxNode, new();
    public IFluxGroup Horizontal(string groupName = "Horizontal");
    public IFluxGroup Vertical(string groupName = "Vertical");
    public Slot SpawnSlot(string slotName = "Slot");
    
    public ElementSource<T> ElementSource<T>(out GlobalReference<T> globalRef, string? slotName = null)
        where T : class, IChangeable
    {
        var source = Spawn<ElementSource<T>>(slotName);
        globalRef = source.Slot.AttachComponent<GlobalReference<T>>();

        source.Source.Target = globalRef;

        return source;
    }

    public ValueInput<T> ValueInput<T>(T value, string? slotName = null)
        where T: unmanaged
    {
        var valueInput = Spawn<ValueInput<T>>(slotName);
        valueInput.Value.Value = value;
        
        return valueInput;
    }
    
    public ValueObjectInput<T> ValueObjectInput<T>(T value, string? slotName = null)
    {
        var valueInput = Spawn<ValueObjectInput<T>>(slotName);
        valueInput.Value.Value = value;
        
        return valueInput;
    }

    public INodeValueOutput<T> Relay<T>(INodeValueOutput<T> node) where T: unmanaged
    {
        var relay = Spawn<ValueRelay<T>>();
        relay.Input.Target = node;
        return relay;
    }
    
    public ValueFieldDrive<T> ValueFieldDrive<T>(string? slotName = null)
        where T : unmanaged
    {
        var drive = Spawn<ValueFieldDrive<T>>(slotName);
        return drive;
    }

    public async Task<FluxGadget> Gadget(IGadget gadget, string? name = null)
    {
        Slot slot = SpawnSlot();
        Slot tmp = slot.AddSlot("tmp");

        await gadget.Spawn(tmp);

        var fluxNode = tmp.FindChild(s => s.Name == "Flux", 2)[0];
        slot.Name = name ?? fluxNode.Name;

        foreach (var child in fluxNode.Children.ToList())
        {
            if (child.Name.StartsWith("Meta: ")) continue;
            child.SetParent(slot, false);
        }

        tmp.Destroy();

        foreach (var pfnode in slot.GetComponentsInChildren<ProtoFluxNode>())
        {
            pfnode.EnsureVisual();
        }

        return new FluxGadget(slot);
    }
}