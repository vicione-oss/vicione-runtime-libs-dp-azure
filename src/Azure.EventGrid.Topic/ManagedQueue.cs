using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal sealed class ManagedQueue<T> : IDisposable where T : notnull
{
    private const double PercentToHoldInList = 0.8;

    private readonly Lock _listLock = new();
    private readonly List<T> _list;
    private readonly int _capacity;
    private readonly ILogger? _logger;
    private readonly string _name = "unnamed";
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    internal event Action<int>? ItemAdded;

    internal ManagedQueue() : this([], int.MaxValue) { }
    internal ManagedQueue(int capacity) : this([], capacity) { }
    internal ManagedQueue(List<T> source) : this(source, int.MaxValue) { }
    internal ManagedQueue(ILogger logger, string name) : this([], int.MaxValue, logger, name) { }
    internal ManagedQueue(int capacity, ILogger logger, string name) : this([], capacity, logger, name) { }

    internal ManagedQueue(List<T> source, int capacity, ILogger logger, string name) : this(source, capacity)
    {
        _logger = logger;
        _name = name;
    }

    internal ManagedQueue(List<T> source, int capacity)
    {
        _list = source;
        _capacity = capacity;
    }

    internal bool IsWorking => GetIsWorking();

    internal bool IsAddingCompleted => _cancellationTokenSource.IsCancellationRequested;

    internal int Count => GetCount();

    internal IEnumerable<T> GetValues()
    {
        lock (_listLock)
            return [.. _list];
    }

    internal void Append(IEnumerable<T> items)
    {
        lock (_listLock)
        {
            _list.AddRange(items);
            ApplyCapacity();
            OnItemAdded();
        }
    }

    internal void Append(T item)
    {
        lock (_listLock)
        {
            _list.Add(item);
            ApplyCapacity();
            OnItemAdded();
        }
    }

    private void ApplyCapacity()
    {
        if (_list.Count > _capacity)
        {
            var itemsToRemove = Convert.ToInt32(_list.Count - (_capacity * PercentToHoldInList));
            _list.RemoveRange(0, itemsToRemove);
            _logger?.LogItemsRemoved(_name, itemsToRemove);
        }
    }

    internal bool Take(int count, out List<T> items)
    {
        items = Take(count);
        return items.Count > 0;
    }

    internal List<T> Take(int count)
    {
        lock (_listLock)
        {
            if (_list.Count == 0)
                return [];

            var items = _list.Take(count).ToList();
            _list.RemoveRange(0, items.Count);

            return items;
        }
    }

    internal bool TryTake([NotNullWhen(true)] out T? item)
    {
        lock (_listLock)
        {
            if (_list.Count > 0)
            {
                item = _list[0];
                _list.Remove(item);
                return true;
            }
            else
            {
                item = default;
                return false;
            }
        }
    }

    internal void Prepend(IEnumerable<T> items)
    {
        lock (_listLock)
        {
            _list.InsertRange(0, items);
            ApplyCapacity();
            OnItemAdded();
        }
    }

    internal void CompleteAdding()
        => _cancellationTokenSource.Cancel();

    internal void Clear()
    {
        lock (_listLock)
            _list.Clear();
    }

    private bool GetIsWorking()
    {
        lock (_listLock)
            return _list.Count > 0 || !IsAddingCompleted;
    }

    private int GetCount()
    {
        lock (_listLock)
            return _list.Count;
    }

    private void OnItemAdded()
        => ItemAdded?.Invoke(_list.Count);

    public void Dispose()
        => _cancellationTokenSource.Dispose();

    internal ManagedQueueBufferEnumerator<T> GetBufferEnumerator(int count, TimeSpan? timeSpan = null, CancellationToken cancellationToken = default)
        => new(this, count, timeSpan, _cancellationTokenSource.Token, cancellationToken);

    internal ManagedQueueEnumerator<T> GetAsyncEnumerator(int count, CancellationToken cancellationToken = default)
        => new(this, count, _cancellationTokenSource.Token, cancellationToken);
}
