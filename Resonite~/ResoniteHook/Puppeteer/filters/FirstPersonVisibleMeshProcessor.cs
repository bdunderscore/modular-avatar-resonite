using System.Diagnostics;
using Elements.Assets;
using FrooxEngine.Store;
using nadena.dev.resonity.remote.puppeteer.misc;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer.filters;

using F = FrooxEngine;
using PR = Google.Protobuf.Reflection;
using P = nadena.dev.ndmf.proto;
using PM = nadena.dev.ndmf.proto.mesh;

internal partial class FirstPersonVisibleFilter
{
    private const float EPSILON = 0.01f;
    
    struct MeshProcessingResults
    {
        public List<int> AlwaysInvisibleSubmeshes;
        public List<int> CloneToOriginalIndex;
    }

    void CheckMeshBoneIndices(MeshX mesh)
    {
        int boneCount = mesh.BoneCount;
        int indexCount = mesh.VertexCount;

        for (int i = 0; i < indexCount; i++)
        {
            var rawBinding = mesh.RawBoneBindings[i];

            for (int b = 0; b < 4; b++)
            {
                rawBinding.GetBinding(b, out var boneIndex, out var weight);
                if (weight > 0.0)
                {
                    if (boneIndex < 0 || boneIndex >= boneCount)
                    {
                        throw new ArgumentOutOfRangeException(
                            $"Mesh has invalid bone index { boneIndex } for vertex {i} bone {b}" +
                                    $"Bone count is {boneCount}.");
                    }
                }
            }
        }
    }
    
    private async Task<MeshProcessingResults> ProcessFPVMesh(F.StaticMesh mesh, List<bool> boneToVisible)
    {
        var meshName = mesh.Slot.Name;
        
        var results = new MeshProcessingResults
        {
            AlwaysInvisibleSubmeshes = new List<int>(),
            CloneToOriginalIndex = new List<int>()
        };
        
        // Await mesh loading
        Stopwatch timer = new();
        timer.Start();
        while (!mesh.IsAssetAvailable && timer.ElapsedMilliseconds < 5000)
        {
            await new F.NextUpdate();
        }

        if (!mesh.IsAssetAvailable)
        {
            throw new Exception("Failed to load MeshX asset for mesh " + mesh.Slot.Name);
        }

        var asset = mesh.Asset;

        await new F.ToBackground();
        var meshx = new MeshX(); 
        // meshx.Copy(asset.Data); - this doesn't copy some data for some reason

        using (var stream = new MemoryStream())
        {
            var tmp = new BlockClosureStream(stream);
            asset.Data.Encode(tmp);

            stream.Seek(0, SeekOrigin.Begin);
            meshx.Decode(stream);
        }
        
        // determine which submeshes need to be split by looking at which vertices they touch
        bool[] isVertexInvisible = new bool[meshx.VertexCount];

        for (int i = 0; i < meshx.VertexCount; i++)
        {
            var bindings = meshx.RawBoneBindings[i];
            bool isInvisible = false;

            try
            {
                isInvisible = isInvisible || (bindings.boneIndex0 >= 0 && bindings.boneIndex0 < boneToVisible.Count 
                                                && !boneToVisible[bindings.boneIndex0] && bindings.weight0 > EPSILON);
                isInvisible = isInvisible || (bindings.boneIndex1 >= 0 && bindings.boneIndex1 < boneToVisible.Count
                                                && !boneToVisible[bindings.boneIndex1] && bindings.weight1 > EPSILON);
                isInvisible = isInvisible || (bindings.boneIndex2 >= 0 && bindings.boneIndex2 < boneToVisible.Count
                                                && !boneToVisible[bindings.boneIndex2] && bindings.weight2 > EPSILON);
                isInvisible = isInvisible || (bindings.boneIndex3 >= 0 && bindings.boneIndex3 < boneToVisible.Count
                                                && !boneToVisible[bindings.boneIndex3] && bindings.weight3 > EPSILON);
            }
            catch (ArgumentOutOfRangeException)
            {
                // One or more bone bindings were invalid; we'll just assume we want to keep this vertex
                isInvisible = false;
            }

            isVertexInvisible[i] = isInvisible;
        }

        CheckMeshBoneIndices(meshx);
        
        int originalSubmeshCount = meshx.SubmeshCount;
        for (int submeshIndex = 0; submeshIndex < originalSubmeshCount; submeshIndex++)
        {
            var submesh = meshx.GetSubmesh(submeshIndex);
            var newSubmesh = meshx.AddSubmesh(submesh.Topology);
            newSubmesh.Append(submesh);

            int indicesPerElement = submesh.IndiciesPerElement;
            int indiceCount = submesh.IndicieCount;
            
            bool hasVisible = false;
            bool hasInvisible = false;

            int oldWritePointer = 0;
            int newWritePointer = 0;
            
            for (int readPointer = 0; readPointer < indiceCount; readPointer += indicesPerElement)
            {
                bool isVisibleElement = true;
                for (int j = 0; j < indicesPerElement; j++)
                {
                    if (isVertexInvisible[submesh.RawIndicies[readPointer + j]])
                    {
                        isVisibleElement = false;
                    }
                }

                if (isVisibleElement)
                {
                    // Keep in old submesh
                    Array.Copy(submesh.RawIndicies, readPointer, submesh.RawIndicies, oldWritePointer, indicesPerElement);
                    oldWritePointer += indicesPerElement;
                }
                else
                {
                    Array.Copy(newSubmesh.RawIndicies, readPointer, newSubmesh.RawIndicies, newWritePointer, indicesPerElement);
                    newWritePointer += indicesPerElement;
                }
            }

            if (newWritePointer == 0)
            {
                // All elements are fully visible, we can drop the new submesh
                meshx.RemoveSubmesh(newSubmesh);
            }
            else if (oldWritePointer == 0)
            {
                // All elements are fully invisible; we can still drop the new submesh, but mark the submesh as always invisible
                meshx.RemoveSubmesh(newSubmesh);
                results.AlwaysInvisibleSubmeshes.Add(submeshIndex);
            }
            else
            {
                // Trim both submeshes to size
                submesh.SetCount(oldWritePointer / indicesPerElement);
                newSubmesh.SetCount(newWritePointer / indicesPerElement);
                results.CloneToOriginalIndex.Add(submeshIndex);
            }
        }
        
        CheckMeshBoneIndices(meshx);
        
        // Save meshX and reimport
        string tempFilePath = ctx.Engine.LocalDB.GetTempFilePath(".mesh");
        meshx.SaveToFile(tempFilePath);

        Uri uri = await ctx.Engine.LocalDB.ImportLocalAssetAsync(tempFilePath, LocalDB.ImportLocation.Move);

        await new F::ToWorld();
        
        mesh.URL.Value = uri;

        return results;
    }
}