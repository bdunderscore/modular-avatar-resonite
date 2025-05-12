#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using GrpcDotNetNamedPipes;
using JetBrains.Annotations;
using nadena.dev.ndmf.proto.rpc;
using NUnit.Framework.Internal;
using UnityEngine;
using Debug = UnityEngine.Debug;
using OSPlatform = System.Runtime.InteropServices.OSPlatform;

namespace nadena.dev.ndmf.platform.resonite
{
    internal class RPCClientController
    {

        private static ResoPuppeteer.ResoPuppeteerClient? _client;
        private static Task<ResoPuppeteer.ResoPuppeteerClient>? _clientTask = null;
        private static PipeManager _pipePathManager = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsPipeManager() : new LinuxPipeManager();
        private static string _executableBinaryExtension = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";
        private static Process? _lastProcess;
        private static bool _isDebugBackend;

        private static CancellationTokenSource _logStreamCancellationToken = new();
        internal const string RESOPUPPET_DIR = "Packages/nadena.dev.modular-avatar.resonite/ResoPuppet~";

        private static ResoPuppeteer.ResoPuppeteerClient OpenChannel(string pipeName)
        {
            var channel = new NamedPipeChannel(".", pipeName, new NamedPipeChannelOptions()
            {
                ImpersonationLevel = TokenImpersonationLevel.None,
            });

            var logStream = new LogStream.LogStreamClient(channel);
            _logStreamCancellationToken?.Cancel();
            _logStreamCancellationToken = new CancellationTokenSource();
            var token = _logStreamCancellationToken.Token;
            Task.Run(() => ForwardLogs(logStream, token));
            
            return new ResoPuppeteer.ResoPuppeteerClient(channel);
        }

        private static async Task ForwardLogs(LogStream.LogStreamClient client, CancellationToken token)
        {
            using var stream = client.Listen(new() { });
            int lastSeq = -2;
            while (!token.IsCancellationRequested && await stream.ResponseStream.MoveNext(token))
            {
                var log = stream.ResponseStream.Current;
                if (log.Seq == lastSeq)
                {
                    // Due to a bug in gRPCNamedPipes, if the server disconnects we end up in a infinite loop processing
                    // the last seen log message. Work around this by aborting if we see a message twice.
                    break;
                }

                lastSeq = log.Seq;
                
                switch (log.Level)
                {
                    case LogLevel.Debug:
                        #if NDMF_DEBUG
                            UnityEngine.Debug.Log("[MA-Resonite] " + log.Message);
                        #endif
                            break;
                    case LogLevel.Info:
                        UnityEngine.Debug.Log("[MA-Resonite] " + log.Message);
                        break;
                    case LogLevel.Warning:
                        UnityEngine.Debug.LogWarning("[MA-Resonite] " + log.Message);
                        break;
                    default:
                    case LogLevel.Error:
                        UnityEngine.Debug.LogError("[MA-Resonite] " + log.Message);
                        break;
                }
            }
        } 

        public static ClientHandle ClientHandle()
        {
            var client = GetClient();

            return new ClientHandle(client);
        }

        public static Task<ResoPuppeteer.ResoPuppeteerClient> GetClient()
        {
            if (_clientTask != null && !_clientTask.IsCompleted)
            {
                return _clientTask;
            }

            return _clientTask = Task.Run(GetClient0);
        }

        internal static CancellationToken CancelAfter(int timeoutMs)
        {
            var token = new CancellationTokenSource();
            token.CancelAfter(timeoutMs);

            return token.Token;
        }

        private static async Task<ResoPuppeteer.ResoPuppeteerClient> GetClient0()
        {
            if (_client != null && (_isDebugBackend || _lastProcess?.HasExited == false))
            {
                try
                {
                    await _client.PingAsync(new(), cancellationToken: CancelAfter(2000));
                    return _client;
                }
                catch (Exception)
                {
                    // continue
                }
            }

            var activePipes = _pipePathManager.ActivePipes();
            var devPipeName = _pipePathManager.DevPipePath();
            if (activePipes.Contains(devPipeName))
            {
                _isDebugBackend = true;
                return _client = OpenChannel(devPipeName);
            }

            _isDebugBackend = false;
            var pipeName = _pipePathManager.GetPipePath();

            // if there is already a server running, try to shut it down (since we've lost the process handle)
            if (activePipes.Contains(pipeName))
            {
                try
                {
                    var preexisting = OpenChannel(pipeName);
                    await preexisting.ShutdownAsync(new(), cancellationToken: CancelAfter(2000));
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Failed to shut down existing server: " + e);
                }

                await Task.Delay(250); // give it some time to exit
            }

            // Launch production server
            if (_lastProcess?.HasExited == false)
            {
                _lastProcess?.Kill();
                await Task.Delay(250); // give it some time to exit
            }

            var cwd = Path.GetFullPath(RESOPUPPET_DIR);
            var exe = Path.Combine(cwd, "Launcher" + _executableBinaryExtension);

            if (!File.Exists(exe))
            {
                throw new FileNotFoundException("Resonite Launcher not found", exe);
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                await ResolveSharedObject.DoResolve();

            var libraryPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Library");
            var tempDir = Path.Combine(libraryPath, "ResonitePuppet");
            Directory.CreateDirectory(tempDir);

            var logPath = Path.Combine(tempDir, "puppeteer.log.txt");

            var args = new string[]
            {
                "--pipe-name", pipeName,
                "--temp-directory", tempDir,
                "--auto-shutdown-timeout", "30",
                "--log-path", "\"" + logPath + "\""
            };

            var startInfo = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Join(" ", args),
                WorkingDirectory = cwd,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _lastProcess = new Process
            {
                StartInfo = startInfo,
                EnableRaisingEvents = true
            };
            _lastProcess.Exited += (sender, e) =>
            {
                Console.WriteLine("Resonite Launcher exited");

                // ResonitePuppeteer から NamedPipeServer.Dispose を呼ぶ良い手段がなかったので仕方がなくこっちで強制的に削除します by Reina_Sakiria
                _pipePathManager.ForceRemovePipe(pipeName);

                _client = null;
            };

            if (!_lastProcess.Start())
            {
                throw new Exception("Failed to start Resonite Launcher");
            }

            // Register domain reload hook to shut down the server
            AppDomain.CurrentDomain.DomainUnload += (sender, e) =>
            {
                try
                {
                    _lastProcess.Kill();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to kill Resonite Launcher: {ex}");
                }
            };

            // Also register the process exit hook
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                try
                {
                    _lastProcess.Kill();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to kill Resonite Launcher: {ex}");
                }
            };

            var tmpClient = OpenChannel(pipeName);

            // Wait for the server to start
            await tmpClient.PingAsync(new(), cancellationToken: CancelAfter(60_000));
            _client = tmpClient;

            return _client;
        }
    }

    internal class ClientHandle : IDisposable
    {
        private readonly Task<ResoPuppeteer.ResoPuppeteerClient> _clientTask;
        private bool _isDisposed = false;

        public ClientHandle(Task<ResoPuppeteer.ResoPuppeteerClient> client)
        {
            _clientTask = client;

            var currentContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            Task.Run(async () =>
            {
                while (!_isDisposed)
                {
                    await Task.Delay(1000);
                    await (await _clientTask).PingAsync(new());
                }
            });
            SynchronizationContext.SetSynchronizationContext(currentContext);
        }

        public Task<ResoPuppeteer.ResoPuppeteerClient> GetClient()
        {
            return _clientTask;
        }

        public void Dispose()
        {
            _isDisposed = true;
        }
    }


    internal abstract class PipeManager
    {
        private const string DevPipeName = "MA_RESO_PUPPETEER_DEV";
        private const string ProdPipePrefix = "ModularAvatarResonite_PuppetPipe_";

        internal abstract HashSet<string> ActivePipes();
        internal abstract void ForceRemovePipe(string pipePath);
        internal virtual string DevPipePath() => DevPipeName;
        internal virtual string GetPipePath() => ProdPipePrefix + Process.GetCurrentProcess().Id;
    }
    internal class WindowsPipeManager : PipeManager
    {
        private const string PipeRoot = "\\\\.\\pipe\\";
        internal override HashSet<string> ActivePipes()
        {
            return new HashSet<string>(System.IO.Directory.GetFiles(PipeRoot)
                .Select(p => p.Split("\\").Last())
            );
        }

        internal override void ForceRemovePipe(string pipePath)
        {
            // Windows ではこれをする必要はない可能性はかなり高いですが念の為 by Reina_Sakiria
            var pipeFullPath = Path.Combine(PipeRoot, pipePath);
            try { if (File.Exists(pipeFullPath)) { File.Delete(pipeFullPath); } }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
    internal class LinuxPipeManager : PipeManager
    {
        private const string PipeFolder = ".ResonitePuppetPipe";
        internal override HashSet<string> ActivePipes()
        {
            if (Directory.Exists(PipeFolder) is false) Directory.CreateDirectory(PipeFolder);
            return Directory.GetFiles(PipeFolder).Select(Path.GetFullPath).ToHashSet();
        }

        internal override void ForceRemovePipe(string pipePath)
        {
            try { if (File.Exists(pipePath)) { File.Delete(pipePath); } }
            catch (Exception e) { Debug.LogException(e); }
        }
        internal override string DevPipePath() { return Path.GetFullPath(Path.Combine(PipeFolder, base.DevPipePath())); }
        internal override string GetPipePath() { return Path.GetFullPath(Path.Combine(PipeFolder, base.GetPipePath())); }
    }
}
