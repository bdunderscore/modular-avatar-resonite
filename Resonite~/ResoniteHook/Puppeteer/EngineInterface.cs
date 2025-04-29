using System.Reflection;
using Assimp.Unmanaged;
using FrooxEngine;

namespace nadena.dev.resonity.remote.puppeteer;

public class EngineController : IAsyncDisposable
{
    public const string DefaultResoniteDirectory = "C:\\Program Files (x86)\\Steam\\steamapps\\common\\Resonite";
    
    public string ResoniteDirectory = DefaultResoniteDirectory;
    public string TempDirectory = ".";

    private TaskCompletionSource _updateLoopReturned = new();
    private TaskCompletionSource _shutdownComplete = new();
    
    private bool _wantShutdown;
    
    private Engine _engine;
    private TickController _tickController;
    private World _world;

    public TickController TickController => _tickController;
    public Engine Engine => _engine;
    public World World => _world;
    
    public EngineController(string? ResoniteDirectory = null)
    {
        this.ResoniteDirectory = ResoniteDirectory ?? DefaultResoniteDirectory;
    }

    public async Task Start()
    {
        InitAssimp();

        Assembly.Load("ProtoFlux.Nodes.FrooxEngine");
        Assembly.Load("ProtoFluxBindings");
        
        _tickController = new TickController();
        StandaloneSystemInfo info = new StandaloneSystemInfo();
        LaunchOptions options = new LaunchOptions();
        options.DataDirectory = Path.Combine(TempDirectory, "Data");
        options.CacheDirectory = Path.Combine(TempDirectory, "Cache");
        _engine = new Engine();

        bool shutdownRequested = false;
        
        _engine.OnShutdownRequest += _ => shutdownRequested = true;
        _engine.OnShutdown += () => _shutdownComplete.TrySetResult();
        _engine.EnvironmentShutdownCallback = () => { };
        _engine.EnvironmentCrashCallback = () => { };

        await _engine.Initialize(ResoniteDirectory, options, info, null, new ConsoleEngineInitProgress()).ConfigureAwait(false);

        //World world = Userspace.SetupUserspace(engine);
        _world = _engine.WorldManager.StartLocal(w => { });

        var updateLoop = new Thread(() =>
        {
            while (!_wantShutdown)
            {
                _engine.RunUpdateLoop();
                info.FrameFinished();
                _tickController.WaitFrame();
            }

            _updateLoopReturned.TrySetResult();
        });
        
        updateLoop.Name = "Update loop";
        updateLoop.Start();
    }

    private void InitAssimp()
    {
        AssimpLibrary.Instance.LoadLibrary(null, ResoniteDirectory + "\\assimp.dll");
    }

    public async ValueTask DisposeAsync()
    {
        _wantShutdown = true;
        using var rpcLock = _tickController.StartRPC();
        
        _engine.RequestShutdown();

        await _shutdownComplete.Task;
        await _updateLoopReturned.Task;
        
        _engine.Dispose();
    }

    private void OnLog(string message)
    {
        Console.WriteLine("[LOG] " + message);
    }
    
    private void OnError(string message)
    {
        Console.WriteLine("[ERROR] " + message);
    }
    
    private void OnWarning(string message)
    {
        Console.WriteLine("[WARNING] " + message);
    }
}