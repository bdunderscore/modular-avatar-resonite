using Elements.Assets;
using Elements.Core;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public static class MeshTranslator
{
    public static MeshX ToMeshX(this pm::Mesh rpcMesh)
    {
        var meshx = new MeshX();
        
        var vertexCount = rpcMesh.Positions.Count;
        meshx.SetVertexCount(vertexCount);
        
        meshx.HasColors = rpcMesh.Colors.Count > 0;
        meshx.HasNormals = rpcMesh.Normals.Count > 0;
        meshx.HasTangents = rpcMesh.Tangents.Count > 0;
        meshx.HasUV0s = rpcMesh.Uvs.GetOrNull(0)?.Uvs?.Count > 0;
        meshx.HasUV1s = rpcMesh.Uvs.GetOrNull(1)?.Uvs?.Count >= 2;
        meshx.HasUV2s = rpcMesh.Uvs.GetOrNull(2)?.Uvs?.Count >= 2;
        meshx.HasUV3s = rpcMesh.Uvs.GetOrNull(3)?.Uvs?.Count >= 2;

        for (int i = 0; i < rpcMesh.Positions.Count; i++)
        {
            meshx.SetVertex(i, rpcMesh.Positions[i].Vec3());
            if (i < rpcMesh.Normals.Count) meshx.SetNormal(i, rpcMesh.Normals[i].Vec3());
            if (i < rpcMesh.Tangents.Count) meshx.SetTangent(i, rpcMesh.Tangents[i].Vec4());
            if (i < rpcMesh.Colors.Count) meshx.SetColor(i, rpcMesh.Colors[i].Color());
        }

        for (int uv = 0; uv < rpcMesh.Uvs.Count; uv++)
        {
            var uvData = rpcMesh.Uvs[uv]!;
            for (int v = 0; v < uvData.Uvs.Count; v++)
            {
                meshx.SetUV(v, uv, uvData.Uvs[v].Vec2());
            }
        }
        
        // TODO: support for point submeshes
        foreach (var submesh in rpcMesh.Submeshes)
        {
            var submeshx = meshx.AddSubmesh<TriangleSubmesh>();
            foreach (var tri in submesh.Triangles.Triangles)
            {
                submeshx.AddTriangle(tri.V0, tri.V1, tri.V2);
            }
        }
        
        meshx.HasBoneBindings = rpcMesh.Bones.Count > 0;

        if (meshx.HasBoneBindings)
        {
            foreach (var bone in rpcMesh.Bones)
            {
                meshx.AddBone(bone.Name).BindPose = bone.Bindpose.To4x4();
            }

            for (int i = 0; i < rpcMesh.VertexBoneWeights.Count; i++)
            {
                var vbw = rpcMesh.VertexBoneWeights[i];
                var binding = meshx.RawBoneBindings[i];

                var weight0 = vbw.BoneWeights.GetOrNull(0);
                var weight1 = vbw.BoneWeights.GetOrNull(1);
                var weight2 = vbw.BoneWeights.GetOrNull(2);
                var weight3 = vbw.BoneWeights.GetOrNull(3);

                binding.SetBinding(0, (int)(weight0?.BoneIndex ?? 0), weight0?.Weight ?? 0);
                binding.SetBinding(1, (int)(weight1?.BoneIndex ?? 0), weight1?.Weight ?? 0);
                binding.SetBinding(2, (int)(weight2?.BoneIndex ?? 0), weight2?.Weight ?? 0);
                binding.SetBinding(3, (int)(weight3?.BoneIndex ?? 0), weight3?.Weight ?? 0);

                meshx.RawBoneBindings[i] = binding;
            }

            meshx.SortTrimAndNormalizeBoneWeights();
            meshx.FillInEmptyBindings(0);
        }
        
        var blendshapeNames = new HashSet<string>();
        foreach (var blendshape in rpcMesh.Blendshapes)
        {
            if (!blendshapeNames.Add(blendshape.Name))
            {
                var index = 0;
                string newName;
                do
                {
                    newName = blendshape.Name + "." + (index++);
                } while (!blendshapeNames.Add(newName));

                blendshape.Name = newName;
            } 
            
            var blendshapeX = meshx.AddBlendShape(blendshape.Name);

            foreach (var frame in blendshape.Frames)
            {
                var frameX = blendshapeX.AddFrame(frame.Weight);
                frameX.SetPositionDeltas(frame.DeltaPositions
                    .Select(v => (float3)v.Vec3()).ToArray());
                if (frame.DeltaNormals.Count > 0)
                {
                    frameX.SetNormalDeltas(frame.DeltaNormals.Select(v => (float3)v.Vec3()).ToArray());
                }
                if (frame.DeltaTangents.Count > 0)
                {
                    frameX.SetTangentDeltas(frame.DeltaTangents.Select(v => (float3)v.Vec3()).ToArray());
                }
                
                if (frame.DeltaNormals.Count > 0) blendshapeX.HasNormals = true;
                if (frame.DeltaTangents.Count > 0) blendshapeX.HasTangents = true;
            }
        }
        
        // Workaround FrooxEngine bug: Set HasNormals/HasTangents on all blendshapes
        // https://github.com/Yellow-Dog-Man/Resonite-Issues/issues/4547
        bool anyHasNormals = meshx.BlendShapes.Any(bs => bs.HasNormals);
        bool anyHasTangents = meshx.BlendShapes.Any(bs => bs.HasTangents);
        foreach (var bs in meshx.BlendShapes)
        {
            bs.HasNormals = anyHasNormals;
            bs.HasTangents = anyHasTangents;
        }
        
        return meshx;
    }  
}