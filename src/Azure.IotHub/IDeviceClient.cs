using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;

namespace ViciOne.Suite.DataPort;

internal interface IDeviceClient : IAsyncDisposable
{
    Task OpenAsync(CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
    Task SendEventAsync(Message message, CancellationToken cancellationToken);
    Task UpdateReportedPropertiesAsync(TwinCollection reportedProperties, CancellationToken cancellationToken);
    Task SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateCallback callback, object userContext, CancellationToken cancellationToken);
}
