using nadena.dev.modular_avatar.core;
using nadena.dev.modular_avatar.resonite;
using nadena.dev.modular_avatar.resonite.runtime;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;

[assembly: ExportsPlugin(typeof(ResoniteBuildPlugin))]

namespace nadena.dev.modular_avatar.resonite
{
    [RunsOnPlatforms(WellKnownPlatforms.Resonite)]
    class ResoniteBuildPlugin : Plugin<ResoniteBuildPlugin>
    {
        public override string DisplayName => "Modular Avatar - Resonite Build Support";

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming).WithCompatibleExtensions(new []{
                typeof(AnimatorServicesContext)
            }, seq =>
            {
                seq.BeforePlugin("nadena.dev.modular-avatar")
                    .Run(RecordVisibleHeadAccessoryPass.Instance);
            });
        }
    }
    
    class RecordVisibleHeadAccessoryPass : Pass<RecordVisibleHeadAccessoryPass>
    {
        public override string QualifiedName => "nadena.dev.modular-avatar.RecordVisibleHeadAccessoryPass";
        public override string DisplayName => "Record Visible Head Accessory components for Resonite";
        
        protected override void Execute(BuildContext context)
        {
            foreach (var vha in context.AvatarRootTransform
                         .GetComponentsInChildren<ModularAvatarVisibleHeadAccessory>(true))
            {
                vha.gameObject.AddComponent<TagVisibleInFirstPerson>();
            }
        }
    }
}