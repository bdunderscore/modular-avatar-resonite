#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using nadena.dev.modular_avatar.core;
using nadena.dev.modular_avatar.resonite.runtime;
using nadena.dev.ndmf.multiplatform.components;
using nadena.dev.ndmf.proto.mesh;
using nadena.dev.ndmf.proto.rpc;
using ResoPuppetSchema;
using UnityEditor;
using UnityEditor.Graphs;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using UnityEngine.Animations;
using PackageManager = UnityEditor.PackageManager;
using UnityEngine.UI;
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
        private Dictionary<UnityEngine.Object, IMessage?> _protoAssets = new();
        private Dictionary<UnityEngine.Object, p.ObjectID> _unityToObject = new();
        private Dictionary<Mesh, SkinnedMeshRenderer> _referenceRenderer = new();

        private Queue<(UnityEngine.Object, UnityEngine.Object)> _unprocessedAssets = new();

        private p.ExportRoot _exportRoot = new();

        private List<UnityEngine.Object> _tempObjects = new();

        private List<IShaderTranslator> _shaderTranslators;

        private Transform? _avatarHead;

        internal AvatarSerializer()
        {
            _shaderTranslators = new()
            {
                new LiltoonShaderSupport(ImportTexture),
                new GenericShaderTranslator(ImportTexture),
            };
        }

        private bool ImportTexture(Texture? tex, Texture? importReference, out p.AssetID? assetID, out p.Texture? translatedTex)
        {
            assetID = MapAsset(tex, importReference, out var protoAsset);
            translatedTex = protoAsset as p.Texture;
            return assetID.Id != 0;
        }
        
        private p.AssetID MintAssetID()
        {
            return new p.AssetID() { Id = nextAssetID++ };
        }

        private p.ObjectID MintObjectID()
        {
            return new p.ObjectID() { Id = nextObjectID++ };
        }

        private p.AssetID MapAsset(UnityEngine.Object? asset, UnityEngine.Object? referenceAsset = null)
        {
            return MapAsset(asset, referenceAsset, out _);
        }

        private p.AssetID MapAsset(UnityEngine.Object? asset, UnityEngine.Object? referenceAsset, out IMessage? protoAsset)
        {
            protoAsset = null;
            if (asset == null) return new p.AssetID() { Id = 0 };
            if (_unityToAsset.TryGetValue(asset, out var id))
            {
                protoAsset = _protoAssets.GetValueOrDefault(asset);
                return id;
            }

            referenceAsset = referenceAsset ?? asset;

            _unityToAsset[asset] = id = MintAssetID();

            switch (asset)
            {
                case Texture2D tex2d: protoAsset = TranslateTexture2D(tex2d, referenceAsset); break;
                case Material mat: protoAsset = TranslateMaterial(mat); break;
                case Mesh mesh: protoAsset = TranslateMesh(mesh); break;
                default: protoAsset = null; break;
            }

            if (protoAsset != null)
            {
                _protoAssets[asset] = protoAsset;
                
                p.Asset wrapper = new()
                {
                    Name = asset.name,
                    Id = id,
                    Asset_ = Any.Pack(protoAsset),
                };

                var base_asset = ObjectRegistry.GetReference(asset).Object;
                if (AssetDatabase.Contains(base_asset))
                {
                    var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(base_asset));
                    wrapper.StableId = guid;
                } 

                _exportRoot.Assets.Add(wrapper);
            }

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

        internal async Task<p.ExportRoot> Export(GameObject go, CommonAvatarInfo info)
        {
            try
            {
                if (go.TryGetComponent<Animator>(out var a))
                {
                    _avatarHead = a.GetBoneTransform(HumanBodyBones.Head);
                }
                
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

				await EmbedVersions();

                return _exportRoot;
            }
            finally
            {
                foreach (var obj in _tempObjects)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }

                foreach (var translator in _shaderTranslators)
                {
                    translator.Dispose();
                }
            }
        }

        private static string[] PackagesToEmbed = new[]
        {
            "nadena.dev.modular-avatar",
            "nadena.dev.ndmf",
            "nadena.dev.modular-avatar-resonite"
        };

        private async Task EmbedVersions()
        {
            var request = PackageManager.Client.List(true);

            while (!request.IsCompleted)
            {
                await Task.Delay(10);
            }

            var pkgList = request.Result;
            var packages = pkgList.ToDictionary(p => p.name, p => p);
            
            foreach (var pkg in PackagesToEmbed)
            {
                if (packages.TryGetValue(pkg, out var info))
                {
                    var version = info.version;
                    if (File.Exists(Path.Combine(info.assetPath, ".git", "HEAD")))
                    {
                        version += "+git";
                    }
                    
                    _exportRoot.Versions.Add(new p.VersionInfo()
                    {
                        PackageName = pkg,
                        Version = version
                    });
                }
            }
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

            bool hasVHA = false;
            
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
                    case PortableDynamicBoneCollider collider:
                        protoComponent = TranslateDynamicCollider(collider);
                        break;
                    case PortableDynamicBone pdb:
                        protoComponent = TranslateDynamicBone(pdb);
                        break;
                    case TagVisibleInFirstPerson:
                        protoComponent = new p.VisibleInFirstPerson() { Visible = true };
                        hasVHA = true;
                        break;
                    case IConstraint constraint:
                        protoComponent = TranslateConstraint(constraint);
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

            if (!hasVHA && _avatarHead == t)
            {
                protoObject.Components.Add(new p.Component()
                {
                    Enabled = true,
                    Id = MintObjectID(),
                    Component_ = Any.Pack(new p.VisibleInFirstPerson() { Visible = false })
                });
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
            if (avatarDesc.EyelookConfig == null) avatarDesc.EyelookConfig = new();
            avatarDesc.EyelookConfig.LeftEyeTransform = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftEye));
            avatarDesc.EyelookConfig.RightEyeTransform = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightEye));
            
            var bones = avatarDesc.Bones = new();
            bones.Head = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Head));
            bones.Chest = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Chest));
            bones.Spine = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Spine));
            bones.UpperChest = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.UpperChest));
            bones.Neck = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Neck));
            bones.Hips = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.Hips));

            bones.LeftArm = new();
            bones.LeftArm.Shoulder = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftShoulder));
            bones.LeftArm.UpperArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm));
            bones.LeftArm.LowerArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm));
            bones.LeftArm.Hand = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftHand));

            bones.LeftArm.Index = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftIndexDistal))
            };
            bones.LeftArm.Middle = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftMiddleDistal))
            };
            bones.LeftArm.Ring = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftRingDistal))
            };
            bones.LeftArm.Pinky = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLittleDistal))
            };
            bones.LeftArm.Thumb = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftThumbDistal))
            };
            
            bones.RightArm = new();
            bones.RightArm.Shoulder = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightShoulder));
            bones.RightArm.UpperArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm));
            bones.RightArm.LowerArm = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm));
            bones.RightArm.Hand = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightHand));
            bones.RightArm.Index = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightIndexDistal))
            };
            bones.RightArm.Middle = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightMiddleDistal))
            };
            bones.RightArm.Ring = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightRingDistal))
            };
            bones.RightArm.Pinky = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLittleDistal))
            };
            bones.RightArm.Thumb = new()
            {
                Proximal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbProximal)),
                Intermediate = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbIntermediate)),
                Distal = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightThumbDistal))
            };
            
            bones.LeftLeg = new()
            {
                UpperLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg)),
                LowerLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg)),
                Foot = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftFoot)),
                Toe = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.LeftToes))
            };
            
            bones.RightLeg = new()
            {
                UpperLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg)),
                LowerLeg = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg)),
                Foot = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightFoot)),
                Toe = MapObject(uAnimator.GetBoneTransform(HumanBodyBones.RightToes))
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
                protoMeshRenderer.Bones.Add(MapObject(bone?.gameObject));
            }

            if (sharedMesh != null)
            {
                var blendshapes = sharedMesh.blendShapeCount;
                for (int i = 0; i < blendshapes; i++)
                {
                    protoMeshRenderer.BlendshapeWeights.Add(r.GetBlendShapeWeight(i) / 100.0f);
                }
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
        
        private IMessage TranslateTexture2D(Texture2D tex2d, UnityEngine.Object refAsset)
        {
            var refTex = refAsset as Texture2D ?? tex2d;
            var protoTex = new p.Texture();
            
            // Get texture importer for this texture, if available
            var textureImporter = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(refTex)) as TextureImporter;
            if (textureImporter != null)
            {
                // Only set max resolution if the texture was not baked
                // (if we baked the texture, it's possible we might use a higher resolution after merging multiple
                // layers, and there's also no point going to a higher resolution than the baked texture)
                if (refTex == tex2d && textureImporter.maxTextureSize > 0)
                {
                    protoTex.MaxResolution = (uint) textureImporter.maxTextureSize;
                }

                protoTex.IsNormalMap = textureImporter.convertToNormalmap;
            }
            else
            {
                protoTex.IsNormalMap = !refTex.isDataSRGB;
            }

            if (AssetDatabase.IsMainAsset(tex2d))
            {
                var filePath = AssetDatabase.GetAssetPath(tex2d);

                protoTex.Bytes = new() { Inline = ByteString.CopyFrom(File.ReadAllBytes(filePath)) };

                bool supported = true;
                if (filePath.ToLowerInvariant().EndsWith(".png")) protoTex.Format = p.TextureFormat.Png;
                else if (filePath.ToLowerInvariant().EndsWith(".jpg") || filePath.ToLowerInvariant().EndsWith(".jpeg"))
                    protoTex.Format = p.TextureFormat.Jpeg;
                else supported = false;

                if (supported) return protoTex;
            }
        
            // Convert to PNG (blit if necessary)
            byte[]? png = null;
            try
            {
                png = tex2d.EncodeToPNG();
            }
            catch (ArgumentException)
            {
                // continue with blit path below.
            }
            if (png == null)
            {
                // Transform texture into an encodable format. We can't use CopyTexture as formats will be different
                // here.
                var tmpTex = new Texture2D(tex2d.width, tex2d.height, TextureFormat.ARGB32, false);
                var tmpRT = RenderTexture.GetTemporary(tex2d.width, tex2d.height, 0, RenderTextureFormat.ARGB32);
                var priorRT = RenderTexture.active;
                try
                {
                    Graphics.Blit(tex2d, tmpRT);
                    RenderTexture.active = tmpRT;
                    
                    tmpTex.ReadPixels(new Rect(0, 0, tex2d.width, tex2d.height), 0, 0);
                }
                finally
                {
                    RenderTexture.active = priorRT;
                }

                png = tmpTex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tmpTex);
            }
            protoTex.Bytes = new() { Inline = ByteString.CopyFrom(png) };
            protoTex.Format = p.TextureFormat.Png;

            return protoTex;
        
        }

        private IMessage? TranslateMaterial(Material material)
        {
            foreach (var handler in _shaderTranslators)
            {
                if (handler.TryTranslateMaterial(material, out var protoMat)) return protoMat;
            }

            return null;
        }

        private p.AssetID BakeEmissionMask(Material material, Texture2D emissionMap, Texture2D? emissionMask)
        {
            if (emissionMask == null)
            {
                return MapAsset(emissionMap);
            }
            
            var width = emissionMap.width;
            var height = emissionMap.height;
            
            var tempTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            _tempObjects.Add(tempTex);
            
            var tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var shader = Shader.Find("Hidden/NDMF/BakeEmission");
            
            var tmpMat = new Material(shader);
            _tempObjects.Add(tmpMat);
            
            tmpMat.SetTexture("_EmissionMap", emissionMap);
            tmpMat.SetTextureScale("_EmissionMap", material.GetTextureScale("_EmissionMap"));
            tmpMat.SetTextureOffset("_EmissionMap", material.GetTextureOffset("_EmissionMap"));
            
            tmpMat.SetTexture("_EmissionBlendMask", emissionMask);
            tmpMat.SetTextureScale("_EmissionBlendMask", material.GetTextureScale("_EmissionBlendMask"));
            tmpMat.SetTextureOffset("_EmissionBlendMask", material.GetTextureOffset("_EmissionBlendMask"));
            
            Graphics.Blit(tempTex, tempRT, tmpMat);
            
            // Read back to a texture2d
            var tmp = RenderTexture.active;
            RenderTexture.active = tempRT;
            tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = tmp;
                
            RenderTexture.ReleaseTemporary(tempRT);

            tempTex.name = emissionMap.name + " MaskBaked";
            return MapAsset(tempTex, emissionMap);
        }

        private Texture BakeMainTexAlpha(Material material, Texture mainTex, Texture alphaMask)
        {
            // Bake alpha to a temporary texture
            var width = mainTex?.width ?? alphaMask.width;
            var height = mainTex?.height ?? alphaMask.height;
                
            var tempTex = new Texture2D(width, height, TextureFormat.ARGB32, false);
            _tempObjects.Add(tempTex);
            var tempRT = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var shader = mainTex != null
                ? Shader.Find("Hidden/NDMF/WriteColorTex")
                : Shader.Find("Hidden/NDMF/FillColor");
            var tmpMat = new Material(shader);
            _tempObjects.Add(tmpMat);
            if (mainTex != null)
            {
                tmpMat.SetTexture("_MainTex", mainTex);
                tmpMat.SetTextureScale("_MainTex", material.mainTextureScale);
                tmpMat.SetTextureOffset("_MainTex", material.mainTextureOffset);
            }
            Graphics.Blit(mainTex, tempRT, tmpMat);

            tmpMat.shader = Shader.Find("Hidden/NDMF/WriteAlpha");
            tmpMat.SetTexture("_AlphaMask", alphaMask);
            tmpMat.SetTextureScale("_AlphaMask", material.GetTextureScale("_AlphaMask"));
            tmpMat.SetTextureOffset("_AlphaMask", material.GetTextureOffset("_AlphaMask"));
            Graphics.Blit(alphaMask, tempRT, tmpMat);
                
            // Read back to a texture2d
            var tmp = RenderTexture.active;
            RenderTexture.active = tempRT;
            tempTex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = tmp;
                
            RenderTexture.ReleaseTemporary(tempRT);

            tempTex.name = (mainTex?.name ?? alphaMask.name) + " AlphaBlend";
            mainTex = tempTex;
            return mainTex;
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
            var indexBuf = mesh.GetIndexBuffer();
            uint[] indices = new uint[indexBuf.count];
            if (indexBuf.stride == 2)
            {
                var tmp = new ushort[indexBuf.count];
                indexBuf.GetData(tmp);
                for (int i = 0; i < tmp.Length; i++)
                {
                    indices[i] = tmp[i];
                }
            }
            else
            {
                indexBuf.GetData(indices);
            }
            
            for (int i = 0; i < smc; i++)
            {
                var submesh = new p.mesh.Submesh();
                var desc = mesh.GetSubMesh(i);

                submesh.Triangles = new();
                
                for (int v = 0; v < desc.indexCount; v += 3)
                {
                    var tri = new p.mesh.Triangle()
                    {
                        V0 = (int)(indices[desc.indexStart + v] + desc.baseVertex),
                        V1 = (int)(indices[desc.indexStart + v + 1] + desc.baseVertex),
                        V2 = (int)(indices[desc.indexStart + v + 2] + desc.baseVertex)
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
                var refBone = (refBones != null && i < refBones.Length) ? refBones[i] : null;
                var boneName = refBone != null ? refBone.gameObject.name : "Bone" + i;
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