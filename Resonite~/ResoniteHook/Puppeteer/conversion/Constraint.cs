using System.Numerics;
using System.Reflection;
using Assimp = Assimp;
using Elements.Core;
using FrooxEngine;
using FrooxEngine.CommonAvatar;
using FrooxEngine.FinalIK;
using FrooxEngine.ProtoFlux;
using FrooxEngine.ProtoFlux.Runtimes.Execution;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Slots;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.FrooxEngine.Transform;
using FrooxEngine.ProtoFlux.Runtimes.Execution.Nodes.Operators;
using FrooxEngine.Store;
using Google.Protobuf.Collections;
using nadena.dev.resonity.remote.puppeteer.dynamic_flux;
using nadena.dev.resonity.remote.puppeteer.filters;
using nadena.dev.resonity.remote.puppeteer.logging;
using ProtoFlux.Runtimes.Execution.Nodes.Math.Geometry3D;
using SkyFrost.Base;
using Record = SkyFrost.Base.Record;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

using f = FrooxEngine;
using pr = Google.Protobuf.Reflection;
using p = nadena.dev.ndmf.proto;
using pm = nadena.dev.ndmf.proto.mesh;

public partial class RootConverter
{
    const string ConstraintSlotNamePrefix = "<color=green>Constraint</color>: ";
    
    private async Task<f.IComponent?> ProcessConstraint(
        f.Slot parent,
        p.Constraint constraint,
        p.ObjectID _
    )
    {
        bool doPosition = false;
        bool doRotation = false;
        bool doAim = false;
        string constraintTypeName;

        switch (constraint.Type)
        {
            case p.ConstraintType.LookAtOrAimConstraint:
                doAim = true;
                constraintTypeName = ConstraintSlotNamePrefix + "LookAt/Aim";
                break;
            default:
                return null;
        }

        var slot = parent.AddSlot(constraintTypeName);
        var space = slot.AttachComponent<DynamicVariableSpace>();
        space.SpaceName.Value = "constraint";
        space.OnlyDirectBinding.Value = true;
        
        var dv_weight = slot.AttachComponent<f.DynamicValueVariable<float>>();
        dv_weight.VariableName.Value = "constraint/weight";
        dv_weight.Value.Value = constraint.Weight;
        
        // TODO: isActive support (field hook?)
        
        /*
        var dv_rot_offset = slot.AttachComponent<f.DynamicValueVariable<floatQ>>();
        dv_rot_offset.VariableName.Value = "constraint/rotationOffset";
        dv_rot_offset.Value.Value = constraint.RotationOffset.Quat();
        
        var dv_rot_at_rest = slot.AttachComponent<f.DynamicValueVariable<floatQ>>();
        dv_rot_at_rest.VariableName.Value = "constraint/rotationAtRest";
        dv_rot_at_rest.Value.Value = constraint.RotationAtRest.Quat();
        
        var dv_pos_offset = slot.AttachComponent<f.DynamicValueVariable<float3>>();
        dv_pos_offset.VariableName.Value = "constraint/positionOffset";
        dv_pos_offset.Value.Value = constraint.PositionOffset.Vec3();
        
        var dv_pos_at_rest = slot.AttachComponent<f.DynamicValueVariable<float3>>();
        dv_pos_at_rest.VariableName.Value = "constraint/positionAtRest";
        dv_pos_at_rest.Value.Value = constraint.PositionAtRest.Vec3();
        */
        
        await using var rootScope = new FluxBuilder(slot);
        IFluxGroup root = rootScope;
        
        // Sources in the local space of the parent of the target slot
        var posSources = new List<INodeValueOutput<float3>>();
        var rotSources = new List<INodeValueOutput<floatQ>>();
        
        INodeObjectOutput<f.Slot>? constraintSlotParent = null;
        INodeValueOutput<float3>? constraintParentPos = null;

        var setupVertical = root.Vertical();
        
        var constraintRef = setupVertical.ElementSource<f.Slot>(out var constraintRefRef, "Constraint");
        constraintRefRef.Reference.Target = parent;

        var sourceGroup = rootScope.Vertical("Sources");
        if (doAim)
        {
            foreach (var source in constraint.Sources)
            {
                posSources.Add(await BuildAimConstraintSource(
                    sourceGroup.Horizontal("PosSource"),
                    constraint,
                    source, 
                    constraintRef
                ));
            }
        }

        INodeValueOutput<floatQ>? rotationOutput = null;
        
        if (doAim)
        {
            rotationOutput = await BuildAimConstraint(
                root,
                parent,
                constraint,
                posSources
            );
        }

        var outputInputs = root.Vertical();

        if (rotationOutput != null)
        {
            var rotOutput = root.Horizontal();
            
            var rotOffset = outputInputs.ValueInput<floatQ>(constraint.RotationOffset.Quat());
            
            var rotMul = rotOutput.Spawn<ValueMul<floatQ>>();
            rotMul.A.Target = rotationOutput;
            rotMul.B.Target = rotOffset;
            
            // TODO - root weight blending
            
            var drive = rotOutput.ValueFieldDrive<floatQ>();
            drive.TrySetRootTarget(parent.Rotation_Field);
            drive.Value.Target = rotMul;
        }

        return null;
    }

    private async Task<INodeValueOutput<float3>> BuildAimConstraintSource(IFluxGroup horizontal, p.Constraint constraint, p.ConstraintSource source, INodeObjectOutput<Slot>? constraintSlot)
    {
        var inputs = horizontal.Vertical();
        
        var sourceSlot = inputs.ElementSource<f.Slot>(out var globalRef);
        Defer(PHASE_RESOLVE_REFERENCES, "Resolve constraint source references",
            () => globalRef.Reference.Target = Object<f.Slot>(source.Transform)!);

        var weight = inputs.ValueInput(constraint.Weight);

        var sourceLogic = await horizontal.Gadget(_context.Gadgets.AimSource);
        sourceLogic.ObjectRelay<Slot>("In_TargetSlot").Input.Target = sourceSlot;
        sourceLogic.ObjectRelay<Slot>("In_ConstrainedSlot").Input.Target = constraintSlot;
        sourceLogic.ValueRelay<float>("In_Weight").Input.Target = weight;

        return sourceLogic.ValueRelay<float3>("Out_AimVec");
    }

    private async Task<INodeValueOutput<floatQ>> BuildAimConstraint(
        IFluxGroup root,
        Slot constrainedObject, 
        p.Constraint constraint,
        List<INodeValueOutput<float3>> posSources
    )
    {
        var totalWeight = constraint.Sources.Select(s => s.Weight).Sum();
        if (totalWeight <= 0)
        {
            totalWeight = 1.0f; // Avoid division by zero
        }
        
        // Merge position sources
        var vert = root.Vertical("AimConstraintInputs");
        INodeValueOutput<float3> mergedPos;

        if (posSources.Count == 1)
        {
            mergedPos = posSources[0];
        }
        else
        {
            var multiAdd = vert.Spawn<ValueAddMulti<float3>>();
            foreach (var source in posSources)
            {
                multiAdd.Inputs.Add(source);
            }

            mergedPos = multiAdd;
        }

        var restRotation = vert.ValueInput(constraint.RotationAtRest.Quat());
        var offsetRotation = vert.ValueInput(constraint.RotationOffset.Quat());
        var weight = vert.ValueInput(constraint.Weight);
        
        // TODO
        var worldUpVec = vert.ValueInput(new float3(0, 1, 0));

        var aimVec = vert.ValueInput(new float3(0, 0, 1));
        var upVec = vert.ValueInput(new float3(0, 1, 0));

        var constraintLogic = await root.Gadget(_context.Gadgets.AimConstraint);
        constraintLogic.ValueRelay<float>("In_Weight").Input.Target = weight;
        constraintLogic.ValueRelay<float3>("In_AimVec").Input.Target = aimVec;
        constraintLogic.ValueRelay<float3>("In_UpVec").Input.Target = upVec;

        constraintLogic.ValueRelay<float3>("In_WorldUpVec").Input.Target = worldUpVec;
        constraintLogic.ValueRelay<float3>("In_TargetVec").Input.Target = mergedPos;
        
        constraintLogic.ValueRelay<floatQ>("In_RestRotation").Input.Target = restRotation;
        constraintLogic.ValueRelay<floatQ>("In_OffsetRotation").Input.Target = offsetRotation;

        return constraintLogic.ValueRelay<floatQ>("Out_Result");
    }

    private INodeValueOutput<float3x3> ComputeAimOrthonormalSpace(IFluxGroup flux, INodeValueOutput<float3> aimVector, INodeValueOutput<float3> upVector)
    {
        var normalize = flux.Vertical();
        
        var aim = normalize.Spawn<Normalized_Float3>();
        aim.A.Target = aimVector;

        var up = normalize.Spawn<Normalized_Float3>();
        up.A.Target = upVector;
        
        var outerGroup = flux.Vertical();
        var upPrep = outerGroup.Horizontal();
        var rightPrep = outerGroup.Horizontal();

        
        // Compute the orthonormal basis for the aim vector and up vector.
        
        // Project the up vector onto the plane defined by the aim vector
        var upDot = upPrep.Spawn<Dot_Float3>();
        upDot.A.Target = aim;
        upDot.B.Target = up;
        
        var upProj = upPrep.Spawn<Mul_Float3_Float>();
        upProj.A.Target = up;
        upProj.B.Target = upDot;
        
        var upPerp = upPrep.Spawn<ValueSub<float3>>();
        upPerp.A.Target = up;
        upPerp.B.Target = upProj;
        
        // Normalize the computed up vector
        var upNorm = upPrep.Spawn<Normalized_Float3>();
        upNorm.A.Target = upPerp;
        up = upNorm;
        
        // TODO: fallback if the up vector is parallel to the aim vector (e.g. use a default up vector)
        
        // Compute the right vector as the cross product of the aim and up vectors
        var right = rightPrep.Spawn<Cross_Float3>();
        right.A.Target = up;
        right.B.Target = aim;

        var packed = flux.Spawn<PackColumns_Float3x3>();
        packed.Column2.Target = aim;   // Z
        packed.Column1.Target = up;    // Y
        packed.Column0.Target = right; // X

        return packed;
    }

    /// <summary>
    ///  Constructs protoflux to obtain the effective position of a constraint source, with reference to the constrained
    /// object's reference space
    /// </summary>
    /// <param name="sourceGroup">The flux group to build nodes into</param>
    /// <param name="constraint">The constraint</param>
    /// <param name="source">The source</param>
    /// <param name="constraintSlotParent">The parent slot to the constraint, used as a reference space</param>
    /// <param name="constraintParentPos">The position of the constraint in its parent slot, used as an offset for aim constraints</param>
    /// <returns></returns>
    INodeValueOutput<float3> BuildPositionConstraintSource(
        IFluxGroup sourceGroup,
        p.Constraint constraint,
        p.ConstraintSource source,
        INodeObjectOutput<f.Slot>? constraintSlotParent,
        INodeValueOutput<float3>? constraintParentPos
    )
    {
        // TODO: parent constraint handling
        var targetSlot = sourceGroup.ElementSource<f.Slot>(out var targetSlotRef, "Target");
        Defer(PHASE_RESOLVE_REFERENCES, "Resolve constraint references",
            () => targetSlotRef.Reference.Target = Object<f.Slot>(source.Transform)!);

        if (constraintSlotParent == null || constraintParentPos == null)
        {
            var localPosNode = sourceGroup.Spawn<LocalTransform>();
            localPosNode.Instance.Target = targetSlot;

            return localPosNode.LocalPosition;
        }
        else
        {
            var worldPosNode = sourceGroup.Spawn<GlobalTransform>();
            worldPosNode.Instance.Target = targetSlot;
            
            var globalPointToLocal = sourceGroup.Spawn<GlobalPointToLocal>();
            globalPointToLocal.Instance.Target = constraintSlotParent;
            globalPointToLocal.GlobalPoint.Target = worldPosNode.GlobalPosition;

            if (constraint.Type == p.ConstraintType.LookAtOrAimConstraint)
            {
                var valueSub = sourceGroup.Spawn<ValueSub<float3>>();
                valueSub.A.Target = globalPointToLocal;
                valueSub.B.Target = constraintParentPos;
                
                var normalize = sourceGroup.Spawn<Normalized_Float3>();
                normalize.A.Target = valueSub;
                return normalize;
            }
            else
            {
                return globalPointToLocal;    
            }
        }
    }
}