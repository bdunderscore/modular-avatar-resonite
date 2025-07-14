using System.ComponentModel;
using FrooxEngine.ProtoFlux;

namespace nadena.dev.resonity.remote.puppeteer.dynamic_flux;

public interface IFluxGroup
{
    public T Spawn<T>(string? slotName = null) where T : ProtoFluxNode, new();
    public IFluxGroup Horizontal(string groupName = "Horizontal");
    public IFluxGroup Vertical(string groupName = "Vertical");
}