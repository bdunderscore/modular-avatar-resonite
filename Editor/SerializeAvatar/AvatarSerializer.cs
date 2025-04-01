#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using nadena.dev.modular_avatar.core;
using nadena.dev.ndmf.multiplatform.components;
using nadena.dev.ndmf.proto.mesh;
using nadena.dev.ndmf.proto.rpc;
using ResoPuppetSchema;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using BoneWeight = nadena.dev.ndmf.proto.mesh.BoneWeight;
using Mesh = UnityEngine.Mesh;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.ndmf.platform.resonite
{
    internal partial class AvatarSerializer
    {
        private ulong nextAssetID = 1;
        private ulong nextObjectID = 1;

        private Dictionary<UnityEngine.Object, p.AssetID> _unityToAsset = new();
        private Dictionary<UnityEngine.Object, p.ObjectID> _unityToObject = new();
        private Dictionary<Mesh, SkinnedMeshRenderer> _referenceRenderer = new();

        private Queue<UnityEngine.Object> _unprocessedAssets = new();

        private p.ExportRoot _exportRoot = new();

        private p.AssetID MintAssetID()
        {
            return new p.AssetID() { Id = nextAssetID++ };
        }

        private p.ObjectID MintObjectID()
        {
            return new p.ObjectID() { Id = nextObjectID++ };
        }

        private p.AssetID MapAsset(UnityEngine.Object? asset)
        {
            if (asset == null) return new p.AssetID() { Id = 0 };
            if (_unityToAsset.TryGetValue(asset, out var id)) return id;

            _unityToAsset[asset] = id = MintAssetID();
            _unprocessedAssets.Enqueue(asset);

            return id;
        }

        private p.ObjectID MapObject(UnityEngine.Object? obj)
        {
            if (obj == null) return new p.ObjectID() { Id = 0 };
            if (obj is Transform t) obj = t.gameObject;
            if (_unityToObject.TryGetValue(obj, out var id)) return id;

            _unityToObject[obj] = id = MintObjectID();

            return id;
        }

        internal p.ExportRoot Export(GameObject go, CommonAvatarInfo info)
        {
            _exportRoot.Root = CreateTransforms(go.transform);

            if (go.TryGetComponent<Animator>(out var animator))
            {
                _exportRoot.Root.Components.Add(new p.Component()
                {
                    Enabled = true,
                    Id = MintObjectID(),
                    Component_ = Any.Pack(new p.RigRoot()
                        { })
                });
                _exportRoot.Root.Components.Add(new p.Component()
                {
                    Enabled = true,
                    Id = MintObjectID(),
                    Component_ = Any.Pack(TranslateAvatarDescriptor(animator, info))
                });
            }

            ProcessAssets();

            return _exportRoot;
        }

        private p.GameObject CreateTransforms(Transform t)
        {
            var protoObject = new p.GameObject();
            protoObject.Name = t.gameObject.name;
            protoObject.Id = MapObject(t.gameObject);
            protoObject.Enabled = t.gameObject.activeSelf;
            protoObject.LocalTransform = new p.Transform()
            {
                Position = t.localPosition.ToRPC(),
                Rotation = t.localRotation.ToRPC(),
                Scale = t.localScale.ToRPC()
            };

            foreach (Component c in t.gameObject.GetComponents<Component>())
            {
                IMessage? protoComponent;
                switch (c)
                {
                    case MeshRenderer mr:
                        protoComponent = TranslateMeshRenderer(mr);
                        break;
                    case SkinnedMeshRenderer smr:
                        protoComponent = TranslateSkinnedMeshRenderer(smr);
                        break;
                    case PortableDynamicCollider collider:
                        protoComponent = TranslateDynamicCollider(collider);
                        break;
                    case PortableDynamicBone pdb:
                        protoComponent = TranslateDynamicBone(pdb);
                        break;
                        
                    default: continue;
                }

                if (protoComponent == null) continue;

                p.Component wrapper = new p.Component()
                {
                    Enabled = (c as Behaviour)?.enabled ?? true,
                    Id = MapObject(c),
                    Component_ = Any.Pack(protoComponent)
                };

                protoObject.Components.Add(wrapper);
            }

            foreach (Transform child in t)
            {
                protoObject.Children.Add(CreateTransforms(child));
            }

            return protoObject;
        }

        private IMessage TranslateAvatarDescriptor(Animator uAnimator, CommonAvatarInfo avDesc)
        {
            var avatarDesc = new p.AvatarDescriptor();

            avatarDesc.EyePosition = avDesc.EyePosition?.ToRPC() ?? throw new Exception("Unable to determine viewpoint position");

            TransferHumanoidBones(avatarDesc, uAnimator);

            if (avDesc.VisemeRenderer != null)
            {
                var visemes = new p.VisemeConfig();
                avatarDesc.VisemeConfig = visemes;
                visemes.VisemeMesh = MapObject(avDesc.VisemeRenderer);
                
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_Silence, (s) => visemes.ShapeSilence = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_PP, (s) => visemes.ShapePP = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_FF, (s) => visemes.ShapeFF = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_TH, (s) => visemes.ShapeTH = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_DD, (s) => visemes.ShapeDD = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_kk, (s) => visemes.ShapeKk = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_CH, (s) => visemes.ShapeCH = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_SS, (s) => visemes.ShapeSS = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_nn, (s) => visemes.ShapeNn = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_RR, (s) => visemes.ShapeRR = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_aa, (s) => visemes.ShapeAa = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_E, (s) => visemes.ShapeE = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_ih, (s) => visemes.ShapeIh = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_oh, (s) => visemes.ShapeOh = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_ou, (s) => visemes.ShapeOu = s);
                SetVisemeShape(avDesc.VisemeBlendshapes, CommonAvatarInfo.Viseme_laugh, (s) => visemes.ShapeLaugh = s);
            }

            return avatarDesc;
            
            
            void SetVisemeShape(Dictionary<string, string> visemeBlendshapes, string name, Action<string> setter)
            {
                if (visemeBlendshapes.TryGetValue(name, out var shape))
                {
                    setter(shape);
                }
            }
        }

        private void TransferHumanoidBones(p.AvatarDescriptor avatarDesc, Animator uAnimator)
        {
            var bones = avatarDesc.Bones = new();
            bones.Head = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Head).gameObject);

            bones.LeftArm = new();
            bones.LeftArm.Shoulder = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder).gameObject);
            bones.LeftArm.UpperArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm).gameObject);
            bones.LeftArm.LowerArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm).gameObject);
            bones.LeftArm.Hand = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftHand).gameObject);

            bones.LeftArm.Index = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexDistal).gameObject)
                }
            };

            bones.LeftArm.Middle = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal).gameObject)
                }
            };

            bones.LeftArm.Ring = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingDistal).gameObject)
                }
            };

            bones.LeftArm.Pinky = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleDistal).gameObject)
                }
            };

            bones.LeftArm.Thumb = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbDistal).gameObject)
                }
            };

            bones.RightArm = new();

            bones.RightArm.Shoulder = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightShoulder).gameObject);
            bones.RightArm.UpperArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm).gameObject);
            bones.RightArm.LowerArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm).gameObject);
            bones.RightArm.Hand = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightHand).gameObject);

            bones.RightArm.Index = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexDistal).gameObject)
                }
            };

            bones.RightArm.Middle = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleDistal).gameObject)
                }
            };

            bones.RightArm.Ring = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingDistal).gameObject)
                }
            };

            bones.RightArm.Pinky = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleDistal).gameObject)
                }
            };

            bones.RightArm.Thumb = new()
            {
                Bones =
                {
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbProximal).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate).gameObject),
                    MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbDistal).gameObject)
                }
            };
        }

        private IMessage TranslateMeshRenderer(MeshRenderer r)
        {
            var meshFilter = r.GetComponent<MeshFilter>();

            var protoMeshRenderer = new p.MeshRenderer();

            var sharedMesh = meshFilter.sharedMesh;

            TranslateRendererCommon(r, sharedMesh, protoMeshRenderer);

            return protoMeshRenderer;
        }

        private IMessage TranslateSkinnedMeshRenderer(SkinnedMeshRenderer r)
        {
            var protoMeshRenderer = new p.MeshRenderer();

            var sharedMesh = r.sharedMesh;

            if (sharedMesh != null) _referenceRenderer[sharedMesh] = r;

            TranslateRendererCommon(r, sharedMesh, protoMeshRenderer);

            foreach (Transform bone in r.bones)
            {
                protoMeshRenderer.Bones.Add(MapObject(bone.gameObject));
            }

            var blendshapes = sharedMesh.blendShapeCount;
            for (int i = 0; i < blendshapes; i++)
            {
                protoMeshRenderer.BlendshapeWeights.Add(r.GetBlendShapeWeight(i) / 100.0f);
            }

            return protoMeshRenderer;
        }

        private void TranslateRendererCommon(Renderer r, Mesh? sharedMesh, p.MeshRenderer proto)
        {
            proto.Mesh = MapAsset(sharedMesh);
            foreach (var mat in r.sharedMaterials)
            {
                proto.Materials.Add(MapAsset(mat));
            }
        }


        private void ProcessAssets()
        {
            while (_unprocessedAssets.Count > 0)
            {
                var asset = _unprocessedAssets.Dequeue()!;
                var assetID = _unityToAsset[asset];

                IMessage protoAsset;
                switch (asset)
                {
                    case Texture2D tex2d: protoAsset = TranslateTexture2D(tex2d); break;
                    case Material mat: protoAsset = TranslateMaterial(mat); break;
                    case Mesh mesh: protoAsset = TranslateMesh(mesh); break;
                    default: continue;
                }

                p.Asset wrapper = new()
                {
                    Name = asset.name,
                    Id = assetID,
                    Asset_ = Any.Pack(protoAsset)
                };

                _exportRoot.Assets.Add(wrapper);
            }
        }

        private IMessage TranslateTexture2D(Texture2D tex2d)
        {
            var protoTex = new p.Texture();
            var filePath = AssetDatabase.GetAssetPath(tex2d);

            protoTex.Bytes = new() { Inline = ByteString.CopyFrom(File.ReadAllBytes(filePath)) };

            if (filePath.ToLowerInvariant().EndsWith(".png")) protoTex.Format = p.TextureFormat.Png;
            else if (filePath.ToLowerInvariant().EndsWith(".jpg") || filePath.ToLowerInvariant().EndsWith(".jpeg"))
                protoTex.Format = p.TextureFormat.Jpeg;
            else throw new System.Exception("Unsupported texture format: " + filePath);

            return protoTex;
        }

        private IMessage TranslateMaterial(Material material)
        {
            var protoMat = new p.Material();

            protoMat.MainTexture = MapAsset(material.mainTexture);
            protoMat.MainColor = material.color.ToRPC();

            return protoMat;
        }


        private p.mesh.Mesh TranslateMesh(UnityEngine.Mesh mesh)
        {
            SkinnedMeshRenderer? referenceSMR = _referenceRenderer.GetValueOrDefault(mesh);

            var msgMesh = new p.mesh.Mesh();
            msgMesh.Positions.AddRange(mesh.vertices.Select(v => new p.Vector() { X = v.x, Y = v.y, Z = v.z }));
            msgMesh.Normals.AddRange(mesh.normals.Select(v => new p.Vector() { X = v.x, Y = v.y, Z = v.z }));
            msgMesh.Tangents.AddRange(mesh.tangents.Select(v => new p.Vector() { X = v.x, Y = v.y, Z = v.z, W = v.w }));
            msgMesh.Colors.AddRange(mesh.colors.Select(c => new p.Color() { R = c.r, G = c.g, B = c.b, A = c.a }));

            // only copy UV0 for now
            var uv0 = new p.mesh.UVChannel();
            uv0.Uvs.AddRange(mesh.uv.Select(v => new p.Vector() { X = v.x, Y = v.y }));
            msgMesh.Uvs.Add(uv0);

            var smc = mesh.subMeshCount;
            var indexBuf = mesh.triangles;
            for (int i = 0; i < smc; i++)
            {
                var submesh = new p.mesh.Submesh();
                var desc = mesh.GetSubMesh(i);

                submesh.Triangles = new();

                for (int v = 0; v < desc.indexCount; v += 3)
                {
                    var tri = new p.mesh.Triangle()
                    {
                        V0 = indexBuf[desc.indexStart + v] + desc.baseVertex,
                        V1 = indexBuf[desc.indexStart + v + 1] + desc.baseVertex,
                        V2 = indexBuf[desc.indexStart + v + 2] + desc.baseVertex
                    };
                    submesh.Triangles.Triangles.Add(tri);

                    //Debug.Log("Triangle coordinates: " + msgMesh.Positions[tri.V0] + " " + msgMesh.Positions[tri.V1] + " " + msgMesh.Positions[tri.V2]);
                }

                msgMesh.Submeshes.Add(submesh);
            }

            var refBones = referenceSMR?.bones;
            var bindposes = mesh.bindposes;
            for (int i = 0; i < bindposes.Length; i++)
            {
                var boneName = refBones?[i].gameObject.name ?? "Bone" + i;
                var mat = new p.Matrix();
                var pose = bindposes[i];

                mat.Values.Capacity = 16;
                mat.Values.Add(pose.m00);
                mat.Values.Add(pose.m01);
                mat.Values.Add(pose.m02);
                mat.Values.Add(pose.m03);
                mat.Values.Add(pose.m10);
                mat.Values.Add(pose.m11);
                mat.Values.Add(pose.m12);
                mat.Values.Add(pose.m13);
                mat.Values.Add(pose.m20);
                mat.Values.Add(pose.m21);
                mat.Values.Add(pose.m22);
                mat.Values.Add(pose.m23);
                mat.Values.Add(pose.m30);
                mat.Values.Add(pose.m31);
                mat.Values.Add(pose.m32);
                mat.Values.Add(pose.m33);

                msgMesh.Bones.Add(new p.mesh.Bone() { Name = boneName, Bindpose = mat });
            }

            var boneWeights = mesh.boneWeights;

            if (boneWeights.Length > 0)
            {
                for (int v = 0; v < mesh.vertexCount; v++)
                {
                    var vbw = new VertexBoneWeights();
                    var weights = boneWeights[v];
                    if (weights.weight0 > 0)
                        vbw.BoneWeights.Add(new BoneWeight()
                            { Weight = weights.weight0, BoneIndex = (uint)weights.boneIndex0 });
                    if (weights.weight1 > 0)
                        vbw.BoneWeights.Add(new BoneWeight()
                            { Weight = weights.weight1, BoneIndex = (uint)weights.boneIndex1 });
                    if (weights.weight2 > 0)
                        vbw.BoneWeights.Add(new BoneWeight()
                            { Weight = weights.weight2, BoneIndex = (uint)weights.boneIndex2 });
                    if (weights.weight3 > 0)
                        vbw.BoneWeights.Add(new BoneWeight()
                            { Weight = weights.weight3, BoneIndex = (uint)weights.boneIndex3 });

                    msgMesh.VertexBoneWeights.Add(vbw);
                }
            }

            Vector3[] delta_position = new Vector3[mesh.vertexCount];
            Vector3[] delta_normal = new Vector3[mesh.vertexCount];
            Vector3[] delta_tangent = new Vector3[mesh.vertexCount];

            int blendshapeCount = mesh.blendShapeCount;
            for (int i = 0; i < blendshapeCount; i++)
            {
                var name = mesh.GetBlendShapeName(i);
                var frames = mesh.GetBlendShapeFrameCount(i);

                var rpcBlendshape = new p.mesh.Blendshape()
                {
                    Name = name,
                    Frames = { }
                };

                for (int f = 0; f < frames; f++)
                {
                    var frame = new p.mesh.BlendshapeFrame();
                    frame.Weight = mesh.GetBlendShapeFrameWeight(i, f) / 100.0f;
                    mesh.GetBlendShapeFrameVertices(i, f, delta_position, delta_normal, delta_tangent);

                    frame.DeltaPositions.AddRange(delta_position.Select(v => v.ToRPC()));
                    if (delta_normal.Any(n => n.sqrMagnitude > 0.0001f))
                    {
                        frame.DeltaNormals.AddRange(delta_normal.Select(v => v.ToRPC()));
                    }

                    if (delta_tangent.Any(t => t.sqrMagnitude > 0.0001f))
                    {
                        frame.DeltaTangents.AddRange(delta_tangent.Select(v => v.ToRPC()));
                    }

                    rpcBlendshape.Frames.Add(frame);
                }

                msgMesh.Blendshapes.Add(rpcBlendshape);
            }

            return msgMesh;
        }
    }

}