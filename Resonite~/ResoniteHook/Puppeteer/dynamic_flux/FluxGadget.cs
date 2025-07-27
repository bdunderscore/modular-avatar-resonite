using FrooxEngine;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;

namespace nadena.dev.resonity.remote.puppeteer.dynamic_flux;

public class FluxGadget
{
    private readonly Slot _slot;
    
    public Slot Slot => _slot;
    
    public FluxGadget(Slot slot)
    {
        _slot = slot;

        foreach (var node in _slot.Children.ToList())
        {
            if (node.Name.StartsWith("Input:") || node.Name.StartsWith("Output:"))
            {
                node.Destroy();
            }
        }
    }

    public ValueRelay<T> ValueRelay<T>(string name) where T : unmanaged
    {
        return _slot.FindChild(name).GetComponent<ValueRelay<T>>();
    }
    
    public ObjectRelay<T> ObjectRelay<T>(string name)
    {
        return _slot.FindChild(name).GetComponent<ObjectRelay<T>>();
    }

    public INodeValueOutput<T> ValueOutput<T>(string name) where T : unmanaged
    {
        return _slot.FindChild(name).GetComponent<INodeValueOutput<T>>();
    }
    
    public INodeObjectOutput<T> ObjectOutput<T>(string name)
    {
        return _slot.FindChild(name).GetComponent<INodeObjectOutput<T>>();
    }
}