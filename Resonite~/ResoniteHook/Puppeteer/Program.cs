// See https://aka.ms/new-console-template for more information

using System.Reflection;
using System.Runtime.CompilerServices;
using Assimp.Unmanaged;
using nadena.dev.resonity.engine;
using nadena.dev.resonity.remote.puppeteer.rpc;

[assembly: InternalsVisibleTo("Launcher")]

namespace nadena.dev.resonity.remote.puppeteer;

using Elements.Core;
using FrooxEngine;
using Google.Protobuf;
using nadena.dev.resonity.remote.puppeteer.logging;
using p = nadena.dev.ndmf.proto;

internal class Program
{
    private const int DefaultShutdownTimeoutSeconds = 60;

    public static void Main(string[] args)
    {
        throw new Exception("Puppeteer cannot be launched directly; use launcher.exe");
    }

    // ReSharper disable once UnusedMember.Global
    internal static async Task<int> RunBatch(
        StartupArgs args
    )
    {
        if (args.inputPath == null) throw new ArgumentNullException(nameof(args.inputPath));
        if (args.outputPath == null) throw new ArgumentNullException(nameof(args.outputPath));

        UniLog.OnLog += s => LogController.Log(LogController.LogLevel.Debug, s);
        UniLog.OnError += s => LogController.Log(LogController.LogLevel.Error, s);
        UniLog.OnWarning += s => LogController.Log(LogController.LogLevel.Warning, s);

        EngineController? engineController = null;
        await using var statusStream = new StatusStream();

        try
        {
            engineController = new EngineController(args.resoniteInstallDirectory);
            if (args.dataAndCacheRoot != null) engineController.TempDirectory = args.dataAndCacheRoot;
            await engineController.Start();

            var exportRoot = p.ExportRoot.Parser.ParseFrom(File.ReadAllBytes(args.inputPath));

            using var tick = engineController.TickController.StartRPC();
            using var converter = new RootConverter(engineController, engineController.World, statusStream);

            var packageBytes = await converter.Convert(exportRoot);
            File.WriteAllBytes(args.outputPath, packageBytes.ToByteArray());
            Console.WriteLine(BatchProtocol.Done);
            return 0;
        }
        catch (Exception e)
        {
            statusStream.SendUnlocalizedError(e.ToString());
            return 1;
        }
        finally
        {
            if (engineController != null)
            {
                var timeoutSeconds = args.timeoutSeconds ?? DefaultShutdownTimeoutSeconds;
                var disposeTask = engineController.DisposeAsync().AsTask();
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

                var completed = await Task.WhenAny(disposeTask, timeoutTask);
                if (completed == timeoutTask)
                {
                    var message = $"Engine shutdown timed out after {timeoutSeconds}s; forcing termination.";
                    Console.Error.WriteLine(message);

                    // The engine may be wedged (e.g. a hung native call); wait indefinitely for a
                    // graceful shutdown risks never exiting, so terminate the process instead.
                    Environment.Exit(1);
                }
            }
        }
    }

}
