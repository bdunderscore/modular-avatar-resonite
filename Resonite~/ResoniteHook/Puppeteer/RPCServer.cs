using GrpcDotNetNamedPipes;
using nadena.dev.ndmf.proto.rpc;
using nadena.dev.resonity.remote.puppeteer.rpc;

namespace nadena.dev.resonity.remote.puppeteer;

public class RPCServer
{
    private readonly string pipeName;
    
    public RPCServer(string pipeName)
    {
        this.pipeName = pipeName;
    }
    
    public void Start(
        nadena.dev.ndmf.proto.rpc.ResoPuppeteer.ResoPuppeteerBase entryPoint,
        LogStreamEntryPoint logEntryPoint
    )
    {
        var server = new NamedPipeServer(pipeName, new() {
            CurrentUserOnly = true,
        });

        LogStream.BindService(server.ServiceBinder, logEntryPoint);
        nadena.dev.ndmf.proto.rpc.ResoPuppeteer.BindService(server.ServiceBinder, entryPoint);
        
        server.Start();
    }
}