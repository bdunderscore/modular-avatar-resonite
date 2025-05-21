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

            msg.RootTransform = MapObject(root);
            msg.TemplateName = pdb.TemplateName;
            msg.BaseRadius = pdb.BaseRadius;
            msg.IgnoreTransforms.AddRange(pdb.IgnoreTransforms.Value.Where(t => t != null)
                .Select(MapObject));
            msg.IsGrabbable = pdb.IsGrabbable;
            //msg.IgnoreSelf = pdb.IgnoreSelf;
            msg.Colliders.AddRange(pdb.Colliders.Value
                .Where(c => c != null)
                .Select(MapObject));

            return msg;
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