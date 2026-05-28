using System;
using System.Threading;
using System.Threading.Tasks;

namespace ViciOne.Suite.DataPort;

internal static class WaitHandleExtensions
{
    extension(WaitHandle waitHandle)
    {
        internal async Task<bool> WaitOneAsync(int millisecondsTimeout, CancellationToken cancellationToken)
        {
            RegisteredWaitHandle? registeredHandle = null;
            CancellationTokenRegistration tokenRegistration = default;
            try
            {
                TaskCompletionSource<bool> tcs = new();
                registeredHandle = ThreadPool.RegisterWaitForSingleObject(
                    waitHandle,
                    (state, timedOut) => (state as TaskCompletionSource<bool>)?.TrySetResult(!timedOut),
                    tcs,
                    millisecondsTimeout,
                    true);
                tokenRegistration = cancellationToken.Register(
                    state => (state as TaskCompletionSource<bool>)?.TrySetCanceled(),
                    tcs);
                return await tcs.Task.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            finally
            {
                registeredHandle?.Unregister(null);
                await tokenRegistration.DisposeAsync().ConfigureAwait(false);
            }
        }

        internal Task<bool> WaitOneAsync(TimeSpan timeout, CancellationToken cancellationToken)
            => waitHandle.WaitOneAsync((int)timeout.TotalMilliseconds, cancellationToken);

        internal Task<bool> WaitOneAsync(CancellationToken cancellationToken)
            => waitHandle.WaitOneAsync(Timeout.Infinite, cancellationToken);
    }
}
