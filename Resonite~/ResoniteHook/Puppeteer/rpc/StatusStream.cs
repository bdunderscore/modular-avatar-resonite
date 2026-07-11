using System.Diagnostics;
using Google.Protobuf;
using nadena.dev.ndmf.proto.rpc;
using nadena.dev.resonity.remote.puppeteer.logging;

namespace nadena.dev.resonity.remote.puppeteer.rpc;

public class StatusStream : IAsyncDisposable
{
    private string _lastProgressMessage = "";
    private Stopwatch _progressTimer = new();

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

        Console.WriteLine(BatchProtocol.Progress + SingleLine(message));
    }

    public void SendUnlocalizedError(string message)
    {
        Console.Error.WriteLine(message);
        Console.WriteLine(BatchProtocol.Error + SingleLine(message));
    }

    public void SendStructuredError(NDMFError error)
    {
        Console.WriteLine(BatchProtocol.StructuredError + Convert.ToBase64String(error.ToByteArray()));
    }

    private static string SingleLine(string message)
    {
        var newline = message.IndexOfAny(new[] { '\r', '\n' });
        return newline < 0 ? message : message.Substring(0, newline);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
