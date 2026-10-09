using System;
using System.Collections.Generic;
using System.Runtime.Loader;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class AzureIotHubDataPortIncoming : IExternalIncomingCommunication<AzureIotHubDataPortCommunication>, IAsyncDisposable
{
    private readonly AzureIotHubDataPortCommunication _communication;
    private readonly IDeviceClientPool _deviceClientPool;
    private readonly DesiredPropertyHandler _desiredPropertyHandler;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger _logger;

    private IDictionary<Guid, VirtualDeviceClient>? _nodeDeviceClients;

    public event Action<IReadOnlyCollection<ExternalValue>>? Received;

    public AzureIotHubDataPortIncoming(AzureIotHubDataPortCommunication communication, AssemblyLoadContext assemblyLoadContext, ILogger<AzureIotHubDataPortIncoming> logger)
        : this(communication, assemblyLoadContext, DeviceClientPool.Instance, logger)
    { }

    internal AzureIotHubDataPortIncoming(AzureIotHubDataPortCommunication communication, AssemblyLoadContext assemblyLoadContext, IDeviceClientPool deviceClientPool, ILogger<AzureIotHubDataPortIncoming> logger, TimeProvider? timeProvider = null)
    {
        _communication = communication;
        _deviceClientPool = deviceClientPool;
        _serializerOptions = JsonSetup.CreatePreserveTypeOptions(assemblyLoadContext);
        _desiredPropertyHandler = new(communication, timeProvider, _serializerOptions, logger);
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is not null)
            return;

        _nodeDeviceClients = await AzureSetup.CreateDeviceClients(_communication, _deviceClientPool, cancellationToken).ConfigureAwait(false);

        foreach (var (_, deviceClient) in _nodeDeviceClients)
        {
            deviceClient.DesiredPropertyUpdate += OnDesiredPropertyUpdate;
            await deviceClient.Connect(cancellationToken).ConfigureAwait(false);
        }

        foreach (var deviceClient in _nodeDeviceClients.Values)
            await ReceiveTwinDesiredPropertiesAsync(deviceClient, cancellationToken).ConfigureAwait(false);
    }

    internal async Task ReceiveTwinDesiredPropertiesAsync(VirtualDeviceClient deviceClient, CancellationToken cancellationToken)
    {
        if (!_desiredPropertyHandler.HasDesiredProperties(deviceClient.DeviceId))
            return;

        try
        {
            var twin = await deviceClient.InternalDeviceClient.GetTwinAsync(cancellationToken).ConfigureAwait(false);
            Received?.Invoke(_desiredPropertyHandler.HandleDesiredProperty(deviceClient.DeviceId, twin.Properties.Desired));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.ReadingTwinFailed(deviceClient.DeviceId, ex);
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is null)
            return;

        foreach (var deviceClient in _nodeDeviceClients.Values)
        {
            await deviceClient.Disconnect(cancellationToken).ConfigureAwait(false);
            deviceClient.DesiredPropertyUpdate -= OnDesiredPropertyUpdate;
            await deviceClient.DisposeAsync().ConfigureAwait(false);
        }

        _nodeDeviceClients.Clear();
        _nodeDeviceClients = null;
    }

    private void OnDesiredPropertyUpdate(VirtualDeviceClient deviceClient, TwinCollection twinCollection)
    {
        if (_nodeDeviceClients is null)
            throw new InvalidOperationException("Device clients are not initialized.");

        Received?.Invoke(_desiredPropertyHandler.HandleDesiredProperty(deviceClient.DeviceId, twinCollection));
    }

    public async ValueTask DisposeAsync()
    {
        if (_nodeDeviceClients is not null)
        {
            foreach (var deviceClient in _nodeDeviceClients.Values)
                await deviceClient.DisposeAsync().ConfigureAwait(false);

            _nodeDeviceClients.Clear();
            _nodeDeviceClients = null;
        }
    }
}
