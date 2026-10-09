using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ViciOne.Suite.DataPort;

internal class ManagedQueueEnumerator<T> : IAsyncEnumerator<List<T>>, IAsyncEnumerable<List<T>> where T : notnull
{
    private readonly ManagedQueue<T> _queue;
    private readonly AutoResetEvent _gate = new(false);
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly int _count;
    private List<T>? _current;

    internal ManagedQueueEnumerator(ManagedQueue<T> queue, int count, params CancellationToken[] cancellationTokens)
    {
        _queue = queue;
        _queue.ItemAdded += SetGate;
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokens);
        _count = count;

        if (_queue.Count > 0)
            _gate.Set();
    }

    private void SetGate(int count)
        => _gate.Set();

    public List<T> Current => _current ?? throw new InvalidOperationException();

    public async ValueTask<bool> MoveNextAsync()
    {
        await _gate.WaitOneAsync(_cancellationTokenSource.Token).ConfigureAwait(false);

        if (_queue.Take(_count, out var items))
        {
            if (_queue.Count > 0)
                _gate.Set();

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

    public ValueTask DisposeAsync()
    {
        _queue.ItemAdded -= SetGate;
        _gate.Dispose();
        _cancellationTokenSource.Dispose();
        return ValueTask.CompletedTask;
    }

    public IAsyncEnumerator<List<T>> GetAsyncEnumerator(CancellationToken cancellationToken = default)
        => this;
}
