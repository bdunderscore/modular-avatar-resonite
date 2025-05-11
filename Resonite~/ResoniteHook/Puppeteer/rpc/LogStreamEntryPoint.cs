using System.Diagnostics.CodeAnalysis;
using Elements.Core;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using nadena.dev.ndmf.proto.rpc;
using nadena.dev.resonity.remote.puppeteer.logging;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public class LogStreamEntryPoint :  nadena.dev.ndmf.proto.rpc.LogStream.LogStreamBase
{
    LogListener _logListener;
    
    public LogStreamEntryPoint()
    {
        _logListener = LogController.StartLogListening();

        UniLog.OnLog += (s) => LogController.Log(LogController.LogLevel.Debug, s);
        UniLog.OnError += (s) => LogController.Log(LogController.LogLevel.Error, s);
        UniLog.OnWarning += (s) => LogController.Log(LogController.LogLevel.Warning, s);
    }
    
    // Our gRPC implementation does not support cancellation of stream writes
    [SuppressMessage("ReSharper", "MethodSupportsCancellation")]
    public override async Task Listen(FetchLogsRequest request, IServerStreamWriter<LogEntry> responseStream, ServerCallContext context)
    {
        var token = context.CancellationToken;
        
        try
        {
            while (!token.IsCancellationRequested)
            {
                var log = await _logListener.Poll(token);
                var protoLog = new LogEntry()
                {
                    Level = ConvertLevel(log.Level),
                    Message = log.Text,
                    Timestamp = log.Time.ToTimestamp(),
                };

                await responseStream.WriteAsync(protoLog);
            }
        }
        catch (Exception e)
        {
            LogController.Log(LogController.LogLevel.Error, "!!! Error writing log message to gRPC stream: " + e);
            throw;
        }
    }

    public override Task<Empty> CheckConnected(Empty request, ServerCallContext context)
    {
        return Task.FromResult(new Empty());
    }

    private LogLevel ConvertLevel(LogController.LogLevel level)
    {
        switch (level)
        {
            case LogController.LogLevel.Debug: return LogLevel.Debug;
            case LogController.LogLevel.Info: return LogLevel.Info;
            case LogController.LogLevel.Warning: return LogLevel.Warning;
            case LogController.LogLevel.Error: return LogLevel.Error;
        }

        return LogLevel.Error;
    }
}