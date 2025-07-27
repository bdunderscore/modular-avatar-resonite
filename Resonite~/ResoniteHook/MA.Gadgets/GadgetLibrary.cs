using System.Reflection;
using Elements.Assets;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.Store;
using SkyFrost.Base;

namespace nadena.dev.resonity.gadgets;

public class GadgetLibrary(FrooxEngine.Engine engine)
{
    public IGadget LoadingStandin => new ResonitePackageGadget(engine, "loading_standin");
    public IGadget CoreSystems => new ResonitePackageGadget(engine, "coresys");
    
    public IGadget AimConstraint => new ProtographGadget(engine, "AimConstraint");
    public IGadget AimSource => new ProtographGadget(engine, "AimSource");
    public IGadget AimWorldUp => new ProtographGadget(engine, "AimWorldUp");
}

public class ProtographGadget(Engine engine, string name) : IGadget
{
    public Task Spawn(Slot targetSlot)
    {
        var asm = Assembly.GetExecutingAssembly();
        using System.IO.Stream? s = asm.GetManifestResourceStream("nadena.dev.resonity.gadgets.resources.protograph." + name + ".pg.brson");
        if (s == null)
        {
            throw new ArgumentException("Resource not found: " + name);
        }

        var dataTree = DataTreeConverter.LoadAuto(s);
        targetSlot.LoadObject(dataTree, null!);

        return Task.CompletedTask;
    }
}

public class ResonitePackageGadget(Engine engine, string name) : IGadget
{
    public async Task Spawn(Slot targetSlot)
    {
        var asm = Assembly.GetExecutingAssembly();
        using System.IO.Stream? s = asm.GetManifestResourceStream("nadena.dev.resonity.gadgets.resources." + name + ".resonitepackage");
        if (s == null)
        {
            throw new ArgumentException("Resource not found: " + name);
        }

        var package = RecordPackage.Decode(s);
        await PackageImporter.ImportPackage(package, targetSlot);
    }
}

public interface IGadget
{
    public Task Spawn(Slot targetSlot);
}
