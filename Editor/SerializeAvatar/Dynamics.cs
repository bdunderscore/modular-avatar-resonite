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
        private IMessage? TranslateDynamicBone(PortableDynamicBone pdb)
        {
            var msg = new p.DynamicBone();

            var root = pdb.Root ? pdb.Root!.gameObject : pdb.gameObject;

            var bones = CollectBones(pdb, root.transform, new HashSet<Transform>(pdb.IgnoreTransforms.Value ?? new List<Transform>()));
            if (bones.Count == 0)
            {
                Debug.LogWarning($"PortableDynamicBone {pdb.name} has no bones to serialize. Skipping.");
                return null;
            }
            var maxDepth = bones.Max(b => b.Item2);

            foreach ((var bone, var depth) in bones)
            {
                var boneNode = new p.DynamicBoneNode();
                boneNode.Bone = MapObject(bone);
                boneNode.Radius = pdb.BaseRadius.Value;
                if (pdb.RadiusCurve.Value != null)
                {
                    var t = (float)depth / (float)maxDepth;
                    boneNode.Radius *= pdb.RadiusCurve.Value.Evaluate(t);
                }
                msg.Bones.Add(boneNode);
            }
            
            msg.TemplateName = pdb.TemplateName;
            msg.IsGrabbable = pdb.IsGrabbable;
            //msg.IgnoreSelf = pdb.IgnoreSelf;
            msg.Colliders.AddRange(pdb.Colliders.Value
                .Where(c => c != null)
                .Select(MapObject));

            return msg;
        }

        private List<(Transform, int)> CollectBones(PortableDynamicBone pdb, Transform root, HashSet<Transform> ignores)
        {
            List<(Transform, int)> bones = new();

            Traverse(root, 0);

            return bones;
            
            void Traverse(Transform t, int depth)
            {
                if (ignores.Contains(t))
                    return;

                int childCount = 0;
                foreach (Transform child in t)
                {
                    if (ignores.Contains(child)) continue;
                    childCount++;
                }
                
                if (childCount <= 1 || !pdb.IgnoreMultiChild.Value)
                {
                    bones.Add((t, depth));
                }
                
                foreach (Transform child in t)
                {
                    Traverse(child, depth + 1);
                }
            }
        }

        private IMessage? TranslateDynamicCollider(PortableDynamicBoneCollider boneCollider)
        {
            var msg = new p.DynamicCollider();
            
            var root = boneCollider.Root != null ? boneCollider.Root.gameObject : boneCollider.gameObject;
            msg.TargetTransform = MapObject(root);

            p.ColliderType ty;
            switch (boneCollider.ColliderType)
            {
                case PortableDynamicColliderType.Sphere: ty = p.ColliderType.Sphere; break;
                case PortableDynamicColliderType.Capsule: ty = p.ColliderType.Capsule; break;
                case PortableDynamicColliderType.Plane: ty = p.ColliderType.Plane; break;
                default:
                    // Unsupported
                    return null;
            }

            msg.Type = ty;
            msg.Radius = boneCollider.Radius;
            msg.Height = boneCollider.Height;
            msg.PositionOffset = boneCollider.PositionOffset.ToRPC();
            msg.RotationOffset = boneCollider.RotationOffset.ToRPC();

            return msg;
        }
    }
}