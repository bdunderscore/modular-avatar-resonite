using Elements.Core;

namespace nadena.dev.resonity.remote.puppeteer;

public class TickController
{
    private int activeRPCs = 0;
    private AutoResetEvent requestFrame = new(false);
    
    public int ActiveRPCs => activeRPCs;
    public event Action? OnRPCCompleted;

    public void WaitFrame()
    {
        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();
        if (activeRPCs > 0)
        {
            Thread.Sleep(10);
        }
        else
        {
            requestFrame.WaitOne(1000);
        }
    }
    
    public IDisposable StartRPC()
    {
        activeRPCs++;
        return new RPCFinisher(this);
    }

    public void RequestFrameNow()
    {
        requestFrame.Set();
    }

    private class RPCFinisher : IDisposable
    {
        private readonly TickController controller;

        public RPCFinisher(TickController controller)
        {
            this.controller = controller;
        }

        void IDisposable.Dispose()
        {
            controller.OnRPCCompleted?.Invoke();
            controller.activeRPCs--;
        }
    }
}