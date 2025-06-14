using nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

internal static class Helpers
{
    internal static ObjectID? LastBone(this Finger f)
    {
        if (f.Tip != null && f.Tip.Id != 0) return f.Tip;
        if (f.Distal != null && f.Distal.Id != 0) return f.Distal;
        if (f.Intermediate != null && f.Intermediate.Id != 0) return f.Intermediate;
        if (f.Proximal != null && f.Proximal.Id != 0) return f.Proximal;
        if (f.Metacarpal != null && f.Metacarpal.Id != 0) return f.Metacarpal;
        
        return null;
    }
}