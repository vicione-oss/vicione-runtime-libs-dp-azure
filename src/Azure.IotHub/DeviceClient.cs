using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;

namespace ViciOne.Suite.DataPort;

internal sealed class DeviceClient(Microsoft.Azure.Devices.Client.DeviceClient deviceClient) : IDeviceClient
{
    [SuppressMessage("Reliability", "CA2000:Objekte verwerfen, bevor Bereich verloren geht")]
    internal static DeviceClient Create(string hostname, IAuthenticationMethod authenticationMethod)
        => new(Microsoft.Azure.Devices.Client.DeviceClient.Create(hostname, authenticationMethod));

    public Task OpenAsync(CancellationToken cancellationToken)
        => deviceClient.OpenAsync(cancellationToken);

    public Task CloseAsync(CancellationToken cancellationToken)
        => deviceClient.CloseAsync(cancellationToken);

    public Task SendEventAsync(Message message, CancellationToken cancellationToken)
        => deviceClient.SendEventAsync(message, cancellationToken);

    public Task UpdateReportedPropertiesAsync(TwinCollection reportedProperties, CancellationToken cancellationToken)
        => deviceClient.UpdateReportedPropertiesAsync(reportedProperties, cancellationToken);

    public Task SetDesiredPropertyUpdateCallbackAsync(DesiredPropertyUpdateCallback callback, object userContext, CancellationToken cancellationToken)
        => deviceClient.SetDesiredPropertyUpdateCallbackAsync(callback, userContext, cancellationToken);

    public ValueTask DisposeAsync()
        => deviceClient.DisposeAsync();
}
