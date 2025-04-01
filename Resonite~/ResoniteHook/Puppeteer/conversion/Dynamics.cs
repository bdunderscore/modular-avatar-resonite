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
        posOffset -= collider.Height / 2f * float3.Up;
        
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
                    host.LocalPosition = (interval * i) * float3.Up;
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
        var root = Object<f.Slot>(bone.RootTransform) ?? throw new Exception("Dynamic bone root not found");

        var ignored = bone.IgnoreTransforms.Select(Object<f.Slot>)
            .Where(t => t != null)
            .Select(t => t!)
            .ToHashSet();
        ignored.Add(boneChild);
        
        var db = boneChild.AttachComponent<f.DynamicBoneChain>();
        
        Defer(PHASE_RESOLVE_REFERENCES, async () =>
        {
            db.SetupFromChildren(root, false, slot => !ignored.Contains(slot));

            db.BaseBoneRadius.Value = bone.BaseRadius;
            db.IsGrabbable.Value = bone.IsGrabbable;
            db.StaticColliders.AddRange(
                bone.Colliders.SelectMany(
                    colliderId => (IEnumerable<f.IDynamicBoneCollider>?)_colliderMap.GetValueOrDefault(colliderId)
                                  ?? Array.Empty<f.DynamicBoneSphereCollider>()
                )
            );
        
            GenerateTemplateControls(db, bone.TemplateName);
        });
        
        return db;
    }

    private void GenerateTemplateControls(f.DynamicBoneChain db, string templateName)
    {
        if (_dynamicBoneTemplateRoot == null) _dynamicBoneTemplateRoot = _root.AddSlot("DB Templates");

        string prefix = "NDMF/DB Template.";
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
                var variable = templateRoot.AttachComponent<f.DynamicValueVariable<T>>();
                //variable.VariableName.Value = variableName;
                StringConcatNode(templateBindings!, templateRoot.NameField, field.Name, variable.VariableName);
                variable.Value.Value = field.Value;
                
                var templateField = templateChain!.TryGetField<T>(field.Name) ?? throw new Exception("Field not found");
                variable.Value.DriveFrom(templateField);
            }

            var fieldBindingNode = bindingInternalsNode.AddSlot(field.Name);
            
            var driver = fieldBindingNode.AttachComponent<f.DynamicValueVariableDriver<T>>();
            driver.DefaultValue.Value = field.Value;
            //driver.VariableName.Value = variableName;
            StringConcatNode(fieldBindingNode, templateNameField, field.Name, driver.VariableName);
            driver.Target.Value = field.ReferenceID;
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