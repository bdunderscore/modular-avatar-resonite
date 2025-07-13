using System.Collections.Concurrent;

namespace nadena.dev.resonity.remote.puppeteer.logging;

public class LogListener : IDisposable
{
    private Queue<LogController.Message> _messages = new();

    private readonly Lock _lock = new();
    private TaskCompletionSource _tcs = new();
    
    internal LogListener()
    {
        
    }

    internal void Enqueue(LogController.Message message)
    {
        lock (_lock)
        {
            _messages.Enqueue(message);
            _tcs.TrySetResult();
        }
    }
    
    public async Task<LogController.Message> Poll(CancellationToken token)
    {
        do
        {
            TaskCompletionSource waiter;
            lock (_lock)
            {
                if (_messages.TryDequeue(out var msg)) return msg;
                
                if (_tcs.Task.IsCompleted) _tcs = new TaskCompletionSource();
                waiter = _tcs;
                token.Register(() => waiter.TrySetResult());
            }
            
            await waiter.Task;
        } while (!token.IsCancellationRequested);

        return new()
        {
            Level = LogController.LogLevel.Debug,
            Text = "(cancelled)",
            Time = DateTime.UtcNow,
        };
    }
    
    public void StopListening()
    {
        LogController.StopLogListening(this);
    }
    
    public void Dispose()
    {
        StopListening();
    }
}