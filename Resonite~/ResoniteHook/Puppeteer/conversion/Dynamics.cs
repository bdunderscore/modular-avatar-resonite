using System.Numerics;
using System.Reflection;
using Assimp = Assimp;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.CoreNodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Strings;
using FrooxEngine.Store;
using Google.Protobuf.Collections;
using SkyFrost.Base;
using Record = SkyFrost.Base.Record;
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

namespace nadena.dev.resonity.remote.puppeteer.rpc;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public partial class RootConverter
{
    private f.Slot? _dynamicBoneTemplateRoot = null;
    private HashSet<string> _generatedDynamicBoneTemplates = new();
    private Dictionary<p.ObjectID, List<f.DynamicBoneSphereCollider>> _colliderMap = new();
    
    private async Task<f.IComponent?> ProcessDynamicCollider(f.Slot parent, p.DynamicCollider collider, p.ObjectID componentID)
    {
        float height;
        switch (collider.Type)
        {
            case p.ColliderType.Sphere:
                height = 0;
                break;
            case p.ColliderType.Capsule:
                height = Math.Max(0, collider.Height - collider.Radius);
                break;
            default:
                // unsupported
                return null;
        }
        
        int colliderCount = 1 + (int)Math.Ceiling(height / (collider.Radius / 1f));

        float interval = collider.Height / colliderCount;
        float3 posOffset = collider.PositionOffset.Vec3();
        float3 heightOffset = -(collider.Height / 2f * float3.Up);
        
        Defer(PHASE_BUILD_COLLIDERS, async () =>
        {
            var root = Object<f.Slot>(collider.TargetTransform);
            var sub = root.AddSlot("DB Collider");
            sub.LocalPosition = posOffset;
            sub.LocalRotation = collider.RotationOffset.Quat();
            
            List<f.DynamicBoneSphereCollider> colliders = new();

            for (int i = 0; i < colliderCount; i++)
            {
                f.Slot host;
                if (i == 0)
                {
                    host = sub;
                }
                else
                {
                    host = sub.AddSlot("Capsule subcollider");
                    host.LocalPosition = heightOffset + (interval * i) * float3.Up;
                }

                var fCollider = host.AttachComponent<f.DynamicBoneSphereCollider>();
                fCollider.Radius.Value = collider.Radius;
                colliders.Add(fCollider);
            }
            
            _colliderMap[componentID] = colliders;
        });

        return null;
    }
    
    private async Task<f.IComponent?> ProcessDynamicBone(f.Slot parent, p.DynamicBone bone, p.ObjectID _)
    {
        var boneChild = parent.AddSlot("Dynamic Bone");

        Defer(PHASE_RESOLVE_REFERENCES, async () =>
        {
            var db = boneChild.AttachComponent<f.DynamicBoneChain>();

            var base_radius = bone.Bones.Select(b => b.Radius).Max();
            
            foreach ((var slot, var radius) in bone.Bones
                         .Select(b => (Object<f.Slot>(b.Bone), b.Radius))
                         .Where(b => b.Item1 != null)
                         .OrderBy(kv => BonePath(kv.Item1))
                    )
            {
                var entry = db.Bones.Add();
                entry.Assign(slot!);
                entry.RadiusModifier.Value = radius / base_radius;
            }

            db.BaseBoneRadius.Value = base_radius;
            db.IsGrabbable.Value = bone.IsGrabbable;
            db.StaticColliders.AddRange(
                bone.Colliders.SelectMany(
                    colliderId => (IEnumerable<f.IDynamicBoneCollider>?)_colliderMap.GetValueOrDefault(colliderId)
                                  ?? Array.Empty<f.DynamicBoneSphereCollider>()
                )
            );
        
            GenerateTemplateControls(db, bone.TemplateName);
        });
        
        return null;

        string BonePath(f.Slot? bone)
        {
            if (bone == null) return "";

            string path = "";
            foreach (var slot in bone.EnumerateParents())
            {
                path = $"{slot.ReferenceID}/{path}";
            }

            return path;
        }
    }

    private void GenerateTemplateControls(f.DynamicBoneChain db, string templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            templateName = "unnamed";
        }
        
        if (_dynamicBoneTemplateRoot == null)
        {
            _dynamicBoneTemplateRoot = CreateSettingsNode().AddSlot("Dynamic Bone Settings");
        }

        string prefix = ResoNamespaces.DynBoneTemplates;
        string intron = ".";
        
        f.Slot? templateRoot = null;
        f.DynamicBoneChain? templateChain = null;
        f.Slot? templateBindings = null;
        IField<string> templateNameField;
        if (!_generatedDynamicBoneTemplates.Contains(templateName))
        {
            templateRoot = _dynamicBoneTemplateRoot.AddSlot(templateName);
            templateChain = templateRoot.AttachComponent<f.DynamicBoneChain>();
            templateChain.Enabled = false;
            
            _generatedDynamicBoneTemplates.Add(templateName);

            templateBindings = templateRoot.AddSlot("(Internal) Bindings");
        }

        var templateNameNode = db.Slot.AddSlot("Template Name");
        templateNameField = templateNameNode.AttachComponent<ValueField<string>>().Value;
        templateNameField.Value = templateName;
        
        var bindingInternalsNode = db.Slot.AddSlot("Bindings");

        BindField(db.Inertia);
        BindField(db.InertiaForce);
        BindField(db.Damping);
        BindField(db.Elasticity);
        BindField(db.Stiffness);


        void BindField<T>(f.Sync<T> field)
        {
            if (templateRoot != null)
            {
                var templateFieldBinding = templateRoot.AttachComponent<f.DynamicField<T>>();
                //variable.VariableName.Value = variableName;
                StringConcatNode(templateBindings!, templateRoot.NameField, field.Name, templateFieldBinding.VariableName);

                var templateField = templateChain!.TryGetField<T>(field.Name) ?? throw new Exception("Field not found");
                templateFieldBinding.OverrideOnLink.Value = true;
                templateFieldBinding.TargetField.Value = templateField.ReferenceID;
            }

            var fieldBindingNode = bindingInternalsNode.AddSlot(field.Name);
            
            var driver = fieldBindingNode.AttachComponent<f.DynamicField<T>>();
            driver.TargetField.Value = field.ReferenceID;
            driver.OverrideOnLink.Value = false;
            //driver.VariableName.Value = variableName;
            StringConcatNode(fieldBindingNode, templateNameField, field.Name, driver.VariableName);
        }

        void StringConcatNode(Slot internalsNode, IField<string> templateName, string fieldName, IField<string> target)
        {
            var concat = CreateProtofluxNode<ConcatenateMultiString>(internalsNode);
            var prefixNode = CreateProtofluxNode<ValueObjectInput<string>>(internalsNode);
            var fieldSource = CreateProtofluxSource<string>(internalsNode);
            var suffixNode = CreateProtofluxNode<ValueObjectInput<string>>(internalsNode);

            prefixNode.Value.Value = prefix;
            suffixNode.Value.Value = intron + fieldName;
            
            fieldSource.TrySetRootSource(templateName);

            concat.Inputs.Add(prefixNode);
            concat.Inputs.Add((INodeObjectOutput<string>) fieldSource);
            concat.Inputs.Add(suffixNode);
            
            var driver = CreateProtofluxNode<FrooxEngine.FrooxEngine.ProtoFlux.CoreNodes.ObjectFieldDrive<string>>(internalsNode);
            driver.TrySetRootTarget(target);
            driver.Value.Target = concat;
        }

        ISource CreateProtofluxSource<T>(Slot parent)
        {
            var ty = ProtoFluxHelper.GetSourceNode(typeof(T));
            var node = parent.AddSlot(ty.Name);
            node.LocalPosition = -parent.LocalPosition;
            parent.LocalPosition += float3.Up * 0.1f;
            var component = node.AttachComponent(ty);

            return (ISource)component;
        }

        ProtoFluxNode CreateProtofluxNodeGeneric(Slot parent, Type t)
        {
            var node = parent.AddSlot(t.Name);
            node.LocalPosition = -parent.LocalPosition;
            parent.LocalPosition += float3.Up * 0.1f;
            var component = node.AttachComponent(t);

            return (ProtoFluxNode)component;
        }
        
        T CreateProtofluxNode<T>(Slot parent) where T : ProtoFluxNode, new()
        {
            var node = parent.AddSlot(typeof(T).Name);
            node.LocalPosition = -parent.LocalPosition;
            parent.LocalPosition += float3.Up * 0.1f;
            
            var component = node.AttachComponent<T>();

            return (T)component;
        }
    }
}