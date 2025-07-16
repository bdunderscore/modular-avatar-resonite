#nullable enable

using Google.Protobuf;
using UnityEngine;
using UnityEngine.Animations;
using static UnityEngine.Vector3;
using p = nadena.dev.ndmf.proto;

namespace nadena.dev.ndmf.platform.resonite
{
    internal partial class AvatarSerializer
    {
        private IMessage? TranslateConstraint(IConstraint constraint)
        {
            switch (constraint)
            {
                case AimConstraint aim:
                    return TranslateAimConstraint(aim);
                default:
                    return null;
            }
        }

        private IMessage? TranslateAimConstraint(AimConstraint aim)
        {
            p.Constraint constraint = new();
            constraint.Type = p.ConstraintType.LookAtOrAimConstraint;
            constraint.IsActive = aim.constraintActive;
            constraint.Weight = aim.weight;

            constraint.AimVector = aim.aimVector.ToRPC();
            switch (aim.worldUpType)
            {
                case AimConstraint.WorldUpType.SceneUp:
                    constraint.RollConfiguration = new()
                    {
                        UpDirection = up.ToRPC()
                    };
                    break;
                case AimConstraint.WorldUpType.Vector:
                    constraint.RollConfiguration = new()
                    {
                        UpDirection = aim.upVector.ToRPC()
                    };
                    break;
                case AimConstraint.WorldUpType.ObjectRotationUp:
                    constraint.RollConfiguration = new()
                    {
                        ReferenceObject = MapObject(aim.gameObject),
                        UpDirection = aim.upVector.ToRPC()
                    };
                    break;
                // TODO: ObjectUp
                
                case AimConstraint.WorldUpType.None:
                default:
                    break;
            }

            constraint.RotationAtRest = Quaternion.Euler(aim.rotationAtRest).ToRPC();
            constraint.RotationOffset = Quaternion.Euler(aim.rotationOffset).ToRPC();
            
            var sourceCount = aim.sourceCount;
            for (int i = 0; i < sourceCount; i++)
            {
                var source = aim.GetSource(i);
                constraint.Sources.Add(new p.ConstraintSource()
                {
                    Transform = MapObject(source.sourceTransform),
                    Weight = source.weight
                });
            }

            return constraint;
        }
    }
}