﻿using System.Diagnostics;
using Google.Protobuf;
using Grpc.Core;
using nadena.dev.ndmf.proto.rpc;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public class StatusStream : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private readonly IServerStreamWriter<ConversionStatusMessage> _stream;
    private int _seq;
    private string _lastProgressMessage = "";
    private Stopwatch _progressTimer = new();

    private Task lastSendTask = Task.CompletedTask;
    
    public StatusStream(IServerStreamWriter<ConversionStatusMessage> stream)
    {
        _stream = stream;
    }
    
    private void SendMessage(ConversionStatusMessage msg, bool isFinal = false)
    {
        lock (_lock)
        {
            msg.Final = isFinal;
            msg.Seq = _seq++;

            lastSendTask = lastSendTask.ContinueWith(_ => _stream.WriteAsync(msg)).Unwrap();
        }
    }
    
    public void SendProgressMessage(string message)
    {
        if (message == _lastProgressMessage)
        {
            return;
        }

        var elapsed = _progressTimer.ElapsedMilliseconds;
        Console.WriteLine($"[Phase: {_lastProgressMessage}] Elapsed: {elapsed}ms");
        
        _lastProgressMessage = message;
        _progressTimer.Restart();

        SendMessage(new()
        {
            ProgressMessage = message
        });
    }

    public void SendCompletedAvatar(ByteString resonitepackage)
    {
        SendMessage(new()
        {
            CompletedResonitePackage = resonitepackage
        }, true);
    }

    public void SendUnlocalizedError(string message)
    {
        SendMessage(new()
        {
            UnlocalizedError = message
        });
    }

    public void SendStructuredError(NDMFError error)
    {
        SendMessage(new()
        {
            StructuredError = error
        });
    }

    public async ValueTask DisposeAsync()
    {
        await lastSendTask;
    }
}