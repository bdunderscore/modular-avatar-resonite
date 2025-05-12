// See https://aka.ms/new-console-template for more information

using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Remoting;
using Assimp.Unmanaged;
using nadena.dev.resonity.remote.puppeteer.rpc;

[assembly: InternalsVisibleTo("Launcher")]

namespace nadena.dev.resonity.remote.puppeteer;

using Elements.Core;
using FrooxEngine;
using nadena.dev.resonity.remote.puppeteer.logging;

internal class Program
{
    public static void Main(string[] args)
    {
        throw new Exception("Puppeteer cannot be launched directly; use launcher.exe");
    }
    
    // ReSharper disable once UnusedMember.Global
    internal static async Task Launch(
        StartupArgs args
    )
    {
        var resoDirectory = args.resoniteInstallDirectory;
        var pipeName = args.pipeName;
        var autoShutdownTimeout = args.autoShutdownTimeout;
        
        var logStreamEntryPoint = new LogStreamEntryPoint();

        var pendingEP = new PendingEntryPoint();
        new RPCServer(pipeName).Start(pendingEP, logStreamEntryPoint);
        try
        {
            var engineController = new EngineController(resoDirectory);
            if (args.dataAndCacheRoot != null) engineController.TempDirectory = args.dataAndCacheRoot;
            await engineController.Start();

            pendingEP.SetBackend(new EntryPoint(engineController, autoShutdownTimeout));

            // wait forever; the RPC server will do a hard shutdown when needed
            await new TaskCompletionSource().Task;
        }
        catch (Exception e)
        {
            LogController.Log(LogController.LogLevel.Error, e.ToString());
        }
    }

    private static void InitAssimp(string resoDirectory)
    {
        AssimpLibrary.Instance.LoadLibrary(null, resoDirectory + "\\assimp.dll");
    }
}