using f = FrooxEngine;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public partial class RootConverter
{
    private async Task<f::IComponent> CreateMeshRenderer(f::Slot parent, p::MeshRenderer component)
    {
        if (component.BlendshapeWeights.Count == 0 && component.Bones.Count == 0)
        {
            var mr = parent.AttachComponent<f.MeshRenderer>();

            if (component.Mesh != null) mr.Mesh.Value = AssetRefID<f.IAssetProvider<f.Mesh>>(component.Mesh);
            foreach (var matId in component.Materials)
            {
                BindMaterial(matId, mr.Materials.Add());
            }

            return mr;
        }
        else
        {
            var mr = parent.AttachComponent<f.SkinnedMeshRenderer>();

            if (component.Mesh != null) mr.Mesh.Value = AssetRefID<f.IAssetProvider<f.Mesh>>(component.Mesh);
            foreach (var matId in component.Materials)
            {
                BindMaterial(matId, mr.Materials.Add());
            }

            Defer(PHASE_RESOLVE_REFERENCES, "Register bones", () =>
            {
                foreach (var boneId in component.Bones)
                {
                    var obj = ObjectRefID<f.Slot>(boneId);
                    mr.Bones.Add().Value = obj;
                }
            });
            
            while (mr.BlendShapeWeights.Count < component.BlendshapeWeights.Count)
            {
                mr.BlendShapeWeights.Add();
            }
            
            for (int i = 0; i < component.BlendshapeWeights.Count; i++)
            {
                mr.BlendShapeWeights[i] = component.BlendshapeWeights[i];
            }

            // Temporary for debugging
            //parent.AttachComponent<f::MeshCollider>();

            return mr;
        }
    }
}