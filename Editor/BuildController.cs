#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using nadena.dev.ndmf.preview;
using nadena.dev.ndmf.proto;
using nadena.dev.ndmf.proto.rpc;
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
        
        public Task<string> BuildAvatar(ClientHandle client, ExportRoot root)
        {
            if (!busyState.IsCompleted) throw new InvalidOperationException("Build is already in progress");

            var task = BuildAvatar0(client, root);
            busyState = task;

            return task;
        }

        private async Task<string?> BuildAvatar0(ClientHandle clientHandle, ExportRoot root)
        {
            var progressId = Progress.Start("Building resonite package");

            Progress.Report(progressId, 0, "Connecting to resonite backend");
            State = "Connecting to resonite backend";
            NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);

            try
            {
                var client = await clientHandle.GetClient();

                Progress.Report(progressId, 0, "Generating resonite package");
                var tempPath = System.IO.Path.Combine(Application.temporaryCachePath, "tmp.resonitepackage");

                using var stream = client.ConvertObject(new() { Root = root });
                var token = CancellationToken.None;
                
                State = "Generating resonite package";
                NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);

                bool successful = false;
                while (await stream.ResponseStream.MoveNext(token))
                {
                    var msg = stream.ResponseStream.Current;
                    if (msg.HasCompletedResonitePackage)
                    {
                        // Write to tempPath
                        using (var fs = System.IO.File.Create(tempPath))
                        {
                            await fs.WriteAsync(msg.CompletedResonitePackage.Memory);
                            successful = true;
                        }

                        break;
                    } else if (msg.HasProgressMessage)
                    {
                        State = msg.ProgressMessage;
                        NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);
                    } else if (msg.HasUnlocalizedError)
                    {
                        // TODO:  NDMF error reporting
                        Debug.LogError(msg.UnlocalizedError);
                        NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);
                    } else if (msg.StructuredError != null)
                    {
                        // TODO
                    }
                }

                if (successful)
                {
                    State = "Build finished!";
                    LastAvatarName = root.Root.Name;
                    LastTempPath = tempPath;
                    NDMFSyncContext.RunOnMainThread(_ => OnStateUpdate?.Invoke(), null);
                }
                else
                {
                    State = "Build failed; check console log for details";
                    LastAvatarName = null;
                    LastTempPath = null;
                }

                return tempPath;
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