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
            
        var getParent = setupVertical.Spawn<GetParentSlot>();
        getParent.Instance.Target = constraintRef;
        constraintSlotParent = getParent;
        
        
        if (!constraint.LocalSpace)
        {
            var globalTransform = setupVertical.Spawn<GlobalTransform>();
            globalTransform.Instance.Target = constraintSlotParent;
            
            var globalPointToLocal = root.Spawn<GlobalPointToLocal>();
            globalPointToLocal.Instance.Target = constraintSlotParent;
            globalPointToLocal.GlobalPoint.Target = globalTransform.GlobalPosition;
            
            constraintParentPos = globalPointToLocal;
        }

        var sourceGroup = rootScope.Vertical("Sources");
        if (doPosition || doAim)
        {
            foreach (var source in constraint.Sources)
            {
                posSources.Add(BuildPositionConstraintSource(
                    sourceGroup.Horizontal("PosSource"),
                    constraint,
                    source, 
                    constraintSlotParent, 
                    constraintParentPos)
                );
            }
        }

        INodeValueOutput<floatQ>? rotationOutput = null;
        
        if (doAim)
        {
            rotationOutput = BuildAimConstraint(
                root,
                parent,
                constraint,
                posSources
            );
        }

        var outputInputs = root.Vertical();
        var outputGroup = root.Vertical();
        
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

    private INodeValueOutput<floatQ> BuildAimConstraint(
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
        
        // Compute the effective target points
        var scaling = root.Vertical();
        var adders = root.Vertical();
        
        var targetMultiAdd = adders.Spawn<ValueAddMulti<float3>>();
        var weightMultiAdd = adders.Spawn<ValueAddMulti<float>>();
        
        foreach ((var targetPoint, var config) in posSources.Zip(constraint.Sources))
        {
            var horz = scaling.Horizontal();

            var staging = horz.Vertical();
            
            var relay = staging.Spawn<ValueRelay<float3>>();
            relay.Input.Target = targetPoint;
            var factor = staging.ValueInput(config.Weight);
            
            var mul = horz.Spawn<Mul_Float3_Float>();
            mul.A.Target = relay;
            mul.B.Target = factor;

            targetMultiAdd.Inputs.Add(mul);
            weightMultiAdd.Inputs.Add(factor);
        }

        var flux = root.Vertical();
        
        var valueDiv = flux.Spawn<Div_Float3_Float>();
        valueDiv.A.Target = targetMultiAdd;
        valueDiv.B.Target = weightMultiAdd;
        
        var self = flux.ElementSource<f.Slot>(out var selfRef, "SelfObject");
        selfRef.Reference.Target = constrainedObject;
        
        var parent = flux.Spawn<GetParentSlot>();
        parent.Instance.Target = self;
        
        // Compute aim vector.
        INodeValueOutput<float3> aimVector = valueDiv;
        if (!constraint.LocalSpace)
        {
            flux = root.Vertical();
            
            // Move aim target to parent local space, and subtract the local position of the constrained object
            var globalPointToLocal = flux.Spawn<GlobalPointToLocal>();
            globalPointToLocal.Instance.Target = parent;
            globalPointToLocal.GlobalPoint.Target = valueDiv;
            
            var localTransform = flux.Spawn<LocalTransform>();
            localTransform.Instance.Target = self;
            
            flux = root.Vertical();
            var valueSub = flux.Spawn<ValueSub<float3>>();
            valueSub.A.Target = globalPointToLocal;
            valueSub.B.Target = localTransform.LocalPosition;
            
            aimVector = valueSub;
        }
        
        // TODO: Support aim constraints with no up vector
        INodeValueOutput<float3>? upVector = null;
        var rollConfiguration = constraint.RollConfiguration;
        if (rollConfiguration != null)
        {
            var inputs = root.Vertical();
            upVector = inputs.ValueInput<float3>(rollConfiguration.WorldUpDirection?.Vec3() ?? float3.Up);

            if (rollConfiguration.ReferenceObject != null)
            {
                var refObject = inputs.ElementSource<f.Slot>(out var refObjectRef, "ReferenceObject");
                Defer(PHASE_RESOLVE_REFERENCES, "Resolve reference object for aim constraint",
                    () => refObjectRef.Reference.Target = Object<f.Slot>(rollConfiguration.ReferenceObject)!);
                
                var selfObject = inputs.ElementSource<f.Slot>(out var selfObjectRef, "SelfObject");
                selfObjectRef.Reference.Target = constrainedObject;
                
                var getParent = flux.Spawn<GetParentSlot>();
                getParent.Instance.Target = selfObject;

                flux = root.Vertical();
                
                var globalUp = flux.Spawn<LocalVectorToGlobal>();
                globalUp.Instance.Target = refObject;
                globalUp.LocalVector.Target = upVector;

                flux = root.Vertical();

                var localUp = flux.Spawn<GlobalVectorToLocal>();
                localUp.Instance.Target = getParent;
                localUp.GlobalVector.Target = globalUp;
                
                upVector = localUp;
            }
        }

        var relays = root.Vertical();
        aimVector = relays.Relay(aimVector);
        upVector = upVector != null ? relays.Relay(upVector) : relays.ValueInput<float3>(float3.Up);

        var originAimVector = relays.ValueInput<float3>(constraint.AimVector.Vec3());
        var originUpVector = relays.ValueInput<float3>(
            constraint.RollConfiguration?.LocalUpDirection?.Vec3() ?? float3.Up
        );
        
        // Compute the rotation quaternion by computing the original and target orthonormal bases directly.
        flux = root.Vertical();
        var goalSpace = ComputeAimOrthonormalSpace(flux.Horizontal(), aimVector, upVector);

        var originSpace = ComputeAimOrthonormalSpace(flux.Horizontal(), originAimVector, originUpVector);

        flux = root.Vertical();
        goalSpace = flux.Relay(goalSpace);
        var transpose = flux.Spawn<Transpose_Float3x3>();
        transpose.A.Target = originSpace;
        
        var rotationMul = flux.Spawn<ValueMul<float3x3>>();
        rotationMul.A.Target = goalSpace;
        rotationMul.B.Target = transpose;

        var rotation = flux.Spawn<Decomposed_Rotation_Float3x3>();
        rotation.A.Target = rotationMul;

        return rotation;
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