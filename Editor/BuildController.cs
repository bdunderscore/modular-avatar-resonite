#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.proto;
using UnityEditor;
using UnityEngine;

namespace nadena.dev.ndmf.platform.resonite
{
    internal sealed class BuildController
    {
        public static BuildController Instance { get; } = new();
        
        private BuildController() {}

        // If not completed, we're busy; don't allow a new task to start
        private Task busyState = Task.CompletedTask;
        
        public string? LastTempPath, LastAvatarName;
        public string State = "Ready to Build";
        
        public event Action? OnStateUpdate;

        public bool IsBuilding => !busyState.IsCompleted;
        
        public Task<string> BuildAvatar(ExportRoot root)
        {
            if (!busyState.IsCompleted) throw new InvalidOperationException("Build is already in progress");

            var task = BuildAvatar0(root);
            busyState = task;

            return task;
        }

        private async Task<string?> BuildAvatar0(ExportRoot root)
        {
            var progressId = Progress.Start("Building resonite package");

            Progress.Report(progressId, 0, "Generating resonite package");
            var libraryPath = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath)!.FullName, "Library");
            var tempDir = System.IO.Path.Combine(libraryPath, "ResonitePuppet");
            System.IO.Directory.CreateDirectory(tempDir);
            var tempPath = System.IO.Path.Combine(tempDir, "tmp.resonitepackage");

            State = "Generating resonite package";
            NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);

            try
            {
                var successful = await ResoniteBackendRunner.RunBuild(root, tempPath, message =>
                {
                    State = message;
                    NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);
                }, CancellationToken.None);

                if (successful)
                {
                    State = "Build finished!";
                    LastAvatarName = root.Root.Name;
                    LastTempPath = tempPath;
                    NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);

                    return tempPath;
                }
                else
                {
                    State = "Build failed; check console log for details";
                    LastAvatarName = null;
                    LastTempPath = null;

                    return null;
                }
            }
            catch (Exception e)
            {
                State = e.ToString().Split("\n")[0];
                Debug.LogException(e);

                return null;
            }
            finally
            {
                Progress.Remove(progressId);
            }
        }
    }
}