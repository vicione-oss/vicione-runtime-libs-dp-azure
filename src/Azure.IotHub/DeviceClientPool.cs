using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;

namespace ViciOne.Suite.DataPort;

internal sealed class DeviceClientPool : IDeviceClientPool, IDisposable
{
    private readonly Dictionary<Key, (IDeviceClient DeviceClient, List<VirtualDeviceClient> VirtualClients)> _internalPool = [];
    private readonly Dictionary<VirtualDeviceClient, Key> _virtualDeviceClientKeys = [];
    private readonly Dictionary<VirtualDeviceClient, bool> _conntectedClients = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Func<string, IAuthenticationMethod, IDeviceClient> _createClient;

    internal DeviceClientPool() : this(DeviceClient.Create)
    { }

    internal DeviceClientPool(Func<string, IAuthenticationMethod, IDeviceClient> createClient)
        => _createClient = createClient;

    internal static DeviceClientPool Instance { get; } = new();

    public void Dispose()
        => _lock.Dispose();

    public async Task<VirtualDeviceClient> Register(string hostname, string deviceId, IAuthenticationMethod authenticationMethod, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            var key = new Key(hostname, deviceId);

            if (!_internalPool.TryGetValue(key, out var value))
            {
                var deviceClient = _createClient(hostname, authenticationMethod);
                await deviceClient.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyUpdate, key, cancellationToken);
                value = (deviceClient, VirtualClients: []);
                _internalPool.Add(key, value);
            }

            VirtualDeviceClient virtualClient = new(deviceId, value.DeviceClient, this);
            value.VirtualClients.Add(virtualClient);
            _virtualDeviceClientKeys.Add(virtualClient, key);

            return virtualClient;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task Connect(VirtualDeviceClient deviceClient, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (!_virtualDeviceClientKeys.ContainsKey(deviceClient))
                throw new InvalidOperationException("Device client not registered.");

            if (_conntectedClients.TryAdd(deviceClient, true))
                await deviceClient.InternalDeviceClient.OpenAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task Disconnect(VirtualDeviceClient deviceClient, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);

        try
        {
            if (!_virtualDeviceClientKeys.ContainsKey(deviceClient))
                throw new InvalidOperationException("Device client not registered.");

            if (_conntectedClients.Remove(deviceClient))
                await deviceClient.InternalDeviceClient.CloseAsync(cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task Unregister(VirtualDeviceClient deviceClient)
    {
        await _lock.WaitAsync();

        try
        {
            if (!_virtualDeviceClientKeys.TryGetValue(deviceClient, out var key))
                return;

            if (_internalPool.TryGetValue(key, out var value))
            {
                if (value.VirtualClients.Count == 1)
                {
                    _internalPool.Remove(key);
                    await value.DeviceClient.DisposeAsync();
                }
                else
                {
                    value.VirtualClients.Remove(deviceClient);
                }

                _virtualDeviceClientKeys.Remove(deviceClient);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task OnDesiredPropertyUpdate(TwinCollection twinCollection, object userContext)
    {
        var key = (Key)userContext;

        if (_internalPool.TryGetValue(key, out var value))
        {
            foreach (var virtualClient in value.VirtualClients)
                virtualClient.InvokeDesiredPropertyUpdate(twinCollection);
        }

        return Task.CompletedTask;
    }

    internal record Key(string Hostname, string DeviceId);
}
