using System;
using System.Threading;
using System.Threading.Tasks;

namespace XboxControllerPowerOff;

internal static class WinRtAsync
{
    public static async Task<T> WithTimeoutAsync<T>(
        Task<T> task,
        TimeSpan timeout,
        string operationName)
    {
        using var cts = new CancellationTokenSource(timeout);
        Task completed = await Task.WhenAny(task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
        if (completed != task)
        {
            throw new TimeoutException($"{operationName} timed out after {timeout.TotalSeconds:0} seconds.");
        }

        return await task.ConfigureAwait(false);
    }
}
