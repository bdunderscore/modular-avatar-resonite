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

internal class Program
{
    internal static async Task Main(string[] args)
    {
        throw new Exception();
    }
    
    // ReSharper disable once UnusedMember.Global
    internal static async Task Launch(
        string resoDirectory,
        string tempDirectory,
        string pipeName,
        int? autoShutdownTimeout
    ) {
        UniLog.OnLog += l => System.Console.WriteLine("[LOG] " + l);
        UniLog.OnError += l => System.Console.WriteLine("[ERROR] " + l);
        UniLog.OnWarning += l => System.Console.WriteLine("[WARN] " + l);

        var pendingEP = new PendingEntryPoint();
        new RPCServer(pipeName).Start(pendingEP);
        
        var engineController = new EngineController(resoDirectory);
        await engineController.Start();
        
        Console.WriteLine("==== Startup complete ====");
        pendingEP.SetBackend(new EntryPoint(engineController, autoShutdownTimeout));

        // wait forever; the RPC server will do a hard shutdown when needed
        await new TaskCompletionSource().Task;
    }

    private static void InitAssimp(string resoDirectory)
    {
        AssimpLibrary.Instance.LoadLibrary(null, resoDirectory + "\\assimp.dll");
    }
}