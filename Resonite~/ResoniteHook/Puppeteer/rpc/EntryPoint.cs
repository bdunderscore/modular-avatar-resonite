using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using FrooxEngine;
using FrooxEngine.Store;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using JetBrains.Annotations;
using nadena.dev.ndmf.proto.rpc;
using ProtoFlux.Runtimes.Execution.Nodes.Math.Constants;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public class EntryPoint : nadena.dev.ndmf.proto.rpc.ResoPuppeteer.ResoPuppeteerBase
{
    private static readonly Empty Empty = new Empty();
    private readonly Engine _engine;
    private readonly World _world;
    private readonly TickController _tickController;
    
    private HashSet<Slot> _slots = new();

    private TimeSpan? _autoShutdownTimeout;
    private DateTime _lastPing = DateTime.UtcNow;
    
    public EntryPoint(EngineController controller, int? autoShutdownTimeout)
    {
        _engine = controller.Engine;
        _world = controller.World;
        _tickController = controller.TickController;
        _autoShutdownTimeout = autoShutdownTimeout != null ? TimeSpan.FromSeconds(autoShutdownTimeout.Value) : null;

        if (_autoShutdownTimeout != null) Task.Run(Watchdog);
    }

    [SuppressMessage("ReSharper", "FunctionNeverReturns")]
    private async Task Watchdog()
    {
        _tickController.OnRPCCompleted += () =>
        {
            _lastPing = DateTime.UtcNow;
        };
        
        while (true)
        {
            var sinceLastPing = DateTime.UtcNow - _lastPing;
            var remaining = _autoShutdownTimeout!.Value - sinceLastPing;
            
            if (remaining < TimeSpan.Zero)
            {
                if (_tickController.ActiveRPCs > 0)
                {
                    _lastPing = DateTime.UtcNow;
                    continue;
                }
                
                Console.WriteLine("Auto-shutdown triggered");
                Process.GetCurrentProcess().Kill();
            }
            else
            {
                await Task.Delay(remaining);
            }
        }
    }

    Task<T> RunAsync<T>(Func<Task<T>> func)
    {
        TaskCompletionSource<T> result = new();

        IDisposable disposable = _tickController.StartRPC();
        _world.Coroutines.StartTask(async f =>
        {
            Func<Task<T>> func = f!;
            try
            {
                result.SetResult(await func());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                result.SetException(e);
            }
            finally
            {
                disposable.Dispose();
            }
        }, func);
        _tickController.RequestFrameNow();
        
        return result.Task;
    }
    
    [MustUseReturnValue]
    Task<T> Run<T>(Func<T> func)
    {
        TaskCompletionSource<T> result = new();
        
        IDisposable disposable = _tickController.StartRPC();
        _world.Coroutines.Post(f =>
        {
            Func<T> func = (Func<T>)f!;
            try
            {
                result.SetResult(func());
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                result.SetException(e);
            }
            finally
            {
                disposable.Dispose();
            }
        }, func);
        _tickController.RequestFrameNow();

        return result.Task;
    }

    public override async Task ConvertObject(ConvertObjectRequest request, IServerStreamWriter<ConversionStatusMessage> responseStream, ServerCallContext context)
    {
        using var tick = _tickController.StartRPC();
        await using var statusStream = new StatusStream(responseStream);
        using var converter = new RootConverter(_engine, _world, statusStream);

        try
        {
            await converter.Convert(request.Root);
        }
        catch (Exception e)
        {
            statusStream.SendUnlocalizedError(e.ToString());
        }
    }

    public override Task<Empty> Ping(Empty request, ServerCallContext context)
    {
        Console.WriteLine("===== PING =====");
        _lastPing = DateTime.UtcNow;
        return Task.FromResult(new Empty());
    }

    public override Task<Empty> Shutdown(Empty request, ServerCallContext context)
    {
        Process.GetCurrentProcess().Kill();
        return Task.FromResult(new Empty());
    }
}