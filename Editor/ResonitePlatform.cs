#nullable enable

using JetBrains.Annotations;
using nadena.dev.ndmf.model;
using nadena.dev.ndmf.proto.rpc;
using UnityEngine;

namespace nadena.dev.ndmf.platform.resonite
{
    [NDMFPlatformProvider]
    internal class ResonitePlatform : INDMFPlatformProvider
    {
        public static readonly INDMFPlatformProvider Instance = new ResonitePlatform();
        public string QualifiedName => WellKnownPlatforms.Resonite;
        public string DisplayName => "Resonite";
        public Texture2D? Icon => null;

        public BuildUIElement? CreateBuildUI()
        {
            return new ResoniteBuildUI();
        }

        public void InitBuildFromCommonAvatarInfo(BuildContext context, CommonAvatarInfo info)
        {
            context.GetState<ResoniteBuildState>().cai = info;
        }
    }
}