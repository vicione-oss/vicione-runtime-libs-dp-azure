using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ViciOne.Suite.DataPort;

internal class ManagedQueueBufferEnumerator<T> : IAsyncEnumerator<List<T>>, IAsyncEnumerable<List<T>> where T : notnull
{
    private readonly ManagedQueue<T> _queue;
    private readonly int _count;
    private readonly AutoResetEvent _gate = new(false);
    private readonly CancellationTokenSource _cancellationTokenSource;
    private List<T>? _current;
    private readonly PeriodicTimer? _timer;
    private readonly Task? _timerTask;

    internal ManagedQueueBufferEnumerator(ManagedQueue<T> queue, int count, TimeSpan? timeSpan, CancellationToken[] cancellationTokens, TimeProvider timeProvider)
    {
        _queue = queue;
        _count = count;
        _queue.ItemAdded += SetGate;
        _cancellationTokenSource = cancellationTokens.Length == 0 ? new() : CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens);

        if (timeSpan.HasValue)
        {
            _timer = new(timeSpan.Value, timeProvider);
            _timerTask = Task.Run(TimerTickAsync);
        }

        if (_queue.Count > 0)
            _gate.Set();
    }

    internal ManagedQueueBufferEnumerator(ManagedQueue<T> queue, int count, TimeSpan? timeSpan, params CancellationToken[] cancellationTokens)
        : this(queue, count, timeSpan, cancellationTokens, TimeProvider.System)
    { }

    private async Task TimerTickAsync()
    {
        if (_timer is null)
            throw new InvalidOperationException("Timer mus be initialized.");

        while (await _timer.WaitForNextTickAsync(_cancellationTokenSource.Token).ConfigureAwait(false))
            _gate.Set();
    }

    private void SetGate(int count)
    {
        if (count >= _count)
            _gate.Set();
    }

    public List<T> Current => _current ?? throw new InvalidOperationException();

    public async ValueTask<bool> MoveNextAsync()
    {
        await _gate.WaitOneAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

        if (_queue.Take(_count, out var items))
        {
            _current = items;
            return true;
        }
        else
        {
            if (_queue.IsWorking)
            {
                _current = [];
                return true;
            }
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _timer?.Dispose();

        if (_timerTask is not null)
        {
            try
            {
                await _timerTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) { }
        }

        _queue.ItemAdded -= SetGate;
        _gate.Dispose();
        _cancellationTokenSource.Dispose();
    }

    public IAsyncEnumerator<List<T>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => this;
}
