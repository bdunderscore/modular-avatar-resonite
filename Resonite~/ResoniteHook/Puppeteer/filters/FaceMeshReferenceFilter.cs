using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public class FaceMeshReferenceFilter(TranslateContext context)
{
    public void Apply(p.AvatarDescriptor avDesc)
    {
        var faceMeshId = avDesc?.VisemeConfig?.VisemeMesh;
        var skinnedMesh = context.Object<f.SkinnedMeshRenderer>(faceMeshId);
        if (skinnedMesh == null) return;

        var rendererRef = skinnedMesh.Slot.AttachComponent<f.DynamicReferenceVariable<f.SkinnedMeshRenderer>>();
        rendererRef.VariableName.Value = ResoNamespaces.FaceMeshRenderer;
        rendererRef.Reference.Target = skinnedMesh;
        rendererRef.OverrideOnLink.Value = true;

        var mesh = skinnedMesh.Mesh.Target;
        var meshRef = skinnedMesh.Slot.AttachComponent<f.DynamicReferenceVariable<f.IAssetProvider<f.Mesh>>>();
        meshRef.VariableName.Value = ResoNamespaces.FaceMesh;
        meshRef.Reference.Target = mesh;
        meshRef.OverrideOnLink.Value = true;
    }
}