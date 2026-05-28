using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;

namespace ViciOne.Suite.DataPort;

internal sealed class VirtualDeviceClient(string deviceId, IDeviceClient deviceClient, DeviceClientPool pool) : IAsyncDisposable
{
    internal event Action<VirtualDeviceClient, TwinCollection>? DesiredPropertyUpdate;

    internal IDeviceClient InternalDeviceClient => deviceClient;
    internal string DeviceId => deviceId;

    internal void InvokeDesiredPropertyUpdate(TwinCollection twinCollection)
        => DesiredPropertyUpdate?.Invoke(this, twinCollection);

    internal Task Connect(CancellationToken cancellationToken)
        => pool.Connect(this, cancellationToken);

    internal Task Disconnect(CancellationToken cancellationToken)
        => pool.Disconnect(this, cancellationToken);

    public async ValueTask DisposeAsync()
        => await pool.Unregister(this);
}
