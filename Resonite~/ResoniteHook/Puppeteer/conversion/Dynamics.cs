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
    public const string DynBoneControllerTag = "modular_avatar/dynamic_bone_controller";
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
        boneChild.Tag = DynBoneControllerTag;
        var db = boneChild.AttachComponent<f.DynamicBoneChain>();

        Defer(PHASE_RESOLVE_REFERENCES, async () =>
        {
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
        
            var rootSlot = Object<f.Slot>(bone.RootTransform) ?? parent;
            var templateName = bone.HasTemplateName && !string.IsNullOrWhiteSpace(bone.TemplateName) 
                ? bone.TemplateName 
                : GuessTemplateName(rootSlot, parent);
            
            GenerateTemplateControls(db, templateName);
        });
        
        return db;

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

            SetTemplateDefaultConfig(templateChain, templateName);
        }

        var templateNameNode = db.Slot.AddSlot("Template Name");
        templateNameField = templateNameNode.AttachComponent<ValueField<string>>().Value;
        templateNameField.Value = templateName;
        
        var bindingInternalsNode = db.Slot.AddSlot("Bindings");

        BindField(chain => chain.Inertia);
        BindField(chain => chain.InertiaForce);
        BindField(chain => chain.Damping);
        BindField(chain => chain.Elasticity);
        BindField(chain => chain.Stiffness);
        BindField(chain => chain.SimulateTerminalBones);
        BindField(chain => chain.DynamicPlayerCollision);
        BindField(chain => chain.CollideWithOwnBody);
        BindField(chain => chain.HandCollisionVibration);
        BindField(chain => chain.CollideWithHead);
        BindField(chain => chain.CollideWithBody);
        BindField(chain => chain.Gravity);
        BindField(chain => chain.GravitySpace.UseParentSpace);
        BindField(chain => chain.GravitySpace.Default);
        BindField(chain => chain.UseUserGravityDirection);
        BindField(chain => chain.LocalForce);
        BindField(chain => chain.GrabSlipping);
        BindField(chain => chain.GrabRadiusTolerance);
        BindField(chain => chain.GrabTerminalBones);
        BindField(chain => chain.GrabVibration);


        void BindField<T>(Func<f.DynamicBoneChain, f.Sync<T>> getField)
        {
            var field = getField(db);
            if (templateRoot != null)
            {
                var templateFieldBinding = templateRoot.AttachComponent<f.DynamicField<T>>();
                //variable.VariableName.Value = variableName;
                StringConcatNode(templateBindings!, templateRoot.NameField, field.Name, templateFieldBinding.VariableName);

                var templateField = getField(templateChain!) ?? throw new Exception("Field not found");
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
            var driver = internalsNode.AttachComponent<f.StringConcatenationDriver>();
            driver.TargetString.Target = target;
            driver.Strings.Add().Value = prefix;
            driver.Strings.Add().DriveFrom(templateName);
            driver.Strings.Add().Value = intron + fieldName;
        }

    }

    private void SetTemplateDefaultConfig(DynamicBoneChain db, string templateName)
    {
        switch (templateName)
        {
            case "skirt":
            {
                // Skirts tend to have issues with clipping due to the lack of angle constraints, set a default
                // config that doesn't move too much.
                db.Inertia.Value = 0.8f;
                db.InertiaForce.Value = 2.0f;
                db.Damping.Value = 50f;
                db.Elasticity.Value = 600f;
                db.Stiffness.Value = 0.75f;
                break;
            }
            case "breast":
            {
                db.Inertia.Value = 0.9f;
                db.InertiaForce.Value = 2.0f;
                db.Damping.Value = 10f;
                db.Elasticity.Value = 100f;
                db.Stiffness.Value = 0.67f;
                break;
            }
            case "hair":
            case "long_hair":
            {
                db.Inertia.Value = 0.34f;
                db.InertiaForce.Value = 2.0f;
                db.Damping.Value = 16.2f;
                db.Elasticity.Value = 100f;
                db.Stiffness.Value = 0.2f;
                break;
            }
            case "ear":
            {
                db.Inertia.Value = 0.5f;
                db.InertiaForce.Value = 2.0f;
                db.Damping.Value = 12.43f;
                db.Elasticity.Value = 100f;
                db.Stiffness.Value = 0.2f;
                break;
            }
            case "tail":
            {
                db.Inertia.Value = 0.2f;
                db.InertiaForce.Value = 2.0f;
                db.Damping.Value = 5f;
                db.Elasticity.Value = 100f;
                db.Stiffness.Value = 0.2f;
                break;
            }
            default:
                // use resonite built-in defaults
                break;
        }
    }

    private static string GuessTemplateName(f.Slot pbRootSlot, f.Slot pbContainerSlot)
    {
        return GuessTemplateNameFromSlot(pbRootSlot)
               ?? GuessTemplateNameFromSlot(pbContainerSlot)
               ?? "generic";
    }

    private static string? GuessTemplateNameFromSlot(Slot pbRootSlot)
    {
        var segments = pbRootSlot.EnumerateParents().Reverse().Append(pbRootSlot)
            .Select(s => s.Name)
            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            .Where(s => s != null)
            .ToList();
            
        foreach (var segment in segments)
        {
            var template = TemplateFromObjectName(segment);
            if (template != null)
            {
                return template;
            }
        }

        return null;
    }
    
    private static string? TemplateFromObjectName(string path)
    {
        path = path.ToLowerInvariant();
        if (path.Contains("pony") || path.Contains("twin")) return "long_hair";
            
        if (path.Contains("hair"))
        {
            return "hair";
        }

        if (path.Contains("tail")) return "tail";
        if (path.Contains("ear") || path.Contains("kemono") || path.Contains("mimi")) return "ear";
        if (path.Contains("breast")) return "breast";
        if (path.Contains("skirt")) return "skirt";

        return null;
    }
}