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
    
    private async Task<MeshProcessingResults> ProcessFPVMesh(F.StaticMesh mesh, List<bool> boneToVisible, int minSubmeshes)
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

        var meshUrl = mesh.URL.Value;
        
        await new F.ToBackground();
        var meshx = new MeshX(); 
        // meshx.Copy(asset.Data); - this doesn't copy some data for some reason; instead, we reload the mesh from LocalDB

        var assetRecord = await ctx.Engine.LocalDB.TryFetchAssetRecordWithMetadataAsync(meshUrl);
        if (assetRecord == null)
        {
            return new MeshProcessingResults()
            {
                AlwaysInvisibleSubmeshes = [],
                CloneToOriginalIndex = []
            };
        }
        
        using (var stream = ctx.Engine.LocalDB.OpenAssetRead(assetRecord))
        {
            meshx.Decode(stream);
        }
        Console.WriteLine("[TIMING] Reload mesh: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);
        
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
        Console.WriteLine("[TIMING] Identify invisible vertices: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);

        CheckMeshBoneIndices(meshx);
        Console.WriteLine("[TIMING] Check mesh bone indices: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);
        
        
        while (meshx.SubmeshCount < minSubmeshes)
        {
            // Unity duplicates the last submesh if we try to add more than the original count. Since we'll be adding
            // new submeshes below, though, we need to make sure we don't rely on this hack. As such, we'll duplicate
            // the last submesh to fill the gap.
            var lastSubmesh = meshx.GetSubmesh(meshx.SubmeshCount - 1);
            var newSubmesh = meshx.AddSubmesh(lastSubmesh.Topology);
            newSubmesh.Append(lastSubmesh);
        }
        Console.WriteLine("[TIMING] Copy submeshes: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);
        
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
                    if (readPointer + j >= submesh.RawIndicies.Length || submesh.RawIndicies[readPointer + j] >= isVertexInvisible.Length)
                    {
                        throw new IndexOutOfRangeException(
                            $"Submesh {submeshIndex} has invalid index {submesh.RawIndicies[readPointer + j]} at readPointer {readPointer} and j {j}");
                    }
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
        Console.WriteLine("[TIMING] Adjust index buffers: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);
        
        CheckMeshBoneIndices(meshx);
        
        Console.WriteLine("[TIMING] Prior to save: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);
        
        
        // Save meshX and reimport
        string tempFilePath = ctx.Engine.LocalDB.GetTempFilePath(".mesh");
        meshx.SaveToFile(tempFilePath);
        Console.WriteLine("[TIMING] Save mesh: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);
        
        Uri uri = await ctx.Engine.LocalDB.ImportLocalAssetAsync(tempFilePath, LocalDB.ImportLocation.Move);
        Console.WriteLine("[TIMING] Import local asset async: " + timer.ElapsedMilliseconds + "ms for mesh " + meshName);

        await new F::ToWorld();
        
        mesh.URL.Value = uri;

        return results;
    }
}