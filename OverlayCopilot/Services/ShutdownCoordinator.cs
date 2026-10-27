using System;
using System.Threading;
using System.Threading.Tasks;

namespace OverlayCopilot.Services;

public static class ShutdownCoordinator
{
    // A slow device/recognizer must not keep an explicit Exit waiting indefinitely.
    public static async Task CompleteWithinAsync(Task cleanup, TimeSpan budget)
    {
        try { await cleanup.WaitAsync(budget); }
        catch (TimeoutException)
        {
            _ = cleanup.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
        catch { /* Shutdown still proceeds when a device fails during cleanup. */ }
    }
}
