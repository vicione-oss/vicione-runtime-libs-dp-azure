using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Func<CancellationToken, Task<IDictionary<Guid, VirtualDeviceClient>>> _createDeviceClients;
    private readonly DesiredPropertyHandler _desiredPropertyHandler;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly ILogger _logger;
    private readonly Lock _desiredLock = new();
    private readonly Dictionary<string, long> _appliedDesiredVersions = [];
    private readonly Dictionary<string, List<TwinCollection>> _patchesDuringTwinRead = [];

    private IDictionary<Guid, VirtualDeviceClient>? _nodeDeviceClients;

    public event Action<IReadOnlyCollection<ExternalValue>>? Received;

    public AzureIotHubDataPortIncoming(AzureIotHubDataPortCommunication communication, AssemblyLoadContext assemblyLoadContext, ILogger<AzureIotHubDataPortIncoming> logger)
        : this(communication, assemblyLoadContext, cancellationToken => AzureSetup.CreateDeviceClients(communication, DeviceClientPool.Instance, cancellationToken), logger)
    { }

    internal AzureIotHubDataPortIncoming(AzureIotHubDataPortCommunication communication, AssemblyLoadContext assemblyLoadContext, Func<CancellationToken, Task<IDictionary<Guid, VirtualDeviceClient>>> createDeviceClients, ILogger<AzureIotHubDataPortIncoming> logger, TimeProvider? timeProvider = null)
    {
        _createDeviceClients = createDeviceClients;
        _serializerOptions = JsonSetup.CreatePreserveTypeOptions(assemblyLoadContext);
        _desiredPropertyHandler = new(communication, timeProvider, _serializerOptions, logger);
        _logger = logger;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is not null)
            return;

        _nodeDeviceClients = await _createDeviceClients(cancellationToken).ConfigureAwait(false);

        lock (_desiredLock)
        {
            foreach (var deviceClient in _nodeDeviceClients.Values.Where(c => _desiredPropertyHandler.HasDesiredProperties(c.DeviceId)))
                _patchesDuringTwinRead.TryAdd(deviceClient.DeviceId, []);
        }

        foreach (var (_, deviceClient) in _nodeDeviceClients)
        {
            deviceClient.DesiredPropertyUpdate += OnDesiredPropertyUpdate;
            await deviceClient.Connect(cancellationToken).ConfigureAwait(false);
        }

        foreach (var deviceClient in _nodeDeviceClients.Values)
            await ReceiveTwinDesiredPropertiesAsync(deviceClient, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReceiveTwinDesiredPropertiesAsync(VirtualDeviceClient deviceClient, CancellationToken cancellationToken)
    {
        if (!_desiredPropertyHandler.HasDesiredProperties(deviceClient.DeviceId))
            return;

        TwinCollection? desired = null;

        try
        {
            desired = (await deviceClient.InternalDeviceClient.GetTwinAsync(cancellationToken).ConfigureAwait(false)).Properties.Desired;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.ReadingTwinFailed(deviceClient.DeviceId, ex);
        }

        CompleteTwinRead(deviceClient.DeviceId, desired);
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

        lock (_desiredLock)
        {
            _appliedDesiredVersions.Clear();
            _patchesDuringTwinRead.Clear();
        }
    }

    private void OnDesiredPropertyUpdate(VirtualDeviceClient deviceClient, TwinCollection twinCollection)
    {
        if (_nodeDeviceClients is null)
            throw new InvalidOperationException("Device clients are not initialized.");

        lock (_desiredLock)
        {
            if (_patchesDuringTwinRead.TryGetValue(deviceClient.DeviceId, out var patches))
                patches.Add(twinCollection);
            else
                ApplyIfNewer(deviceClient.DeviceId, twinCollection);
        }
    }

    // A patch carries only the changed keys, so the full twin goes first and the patches that arrived while it was
    // read follow in $version order; those the twin already contains are skipped.
    private void CompleteTwinRead(string deviceId, TwinCollection? desired)
    {
        lock (_desiredLock)
        {
            _patchesDuringTwinRead.Remove(deviceId, out var patches);

            if (desired is not null)
                Apply(deviceId, desired);

            foreach (var patch in (patches ?? []).OrderBy(p => p.Version))
                ApplyIfNewer(deviceId, patch);
        }
    }

    // A collection without $version reports 0 and is always applied.
    private void ApplyIfNewer(string deviceId, TwinCollection desired)
    {
        if (desired.Version > 0 && _appliedDesiredVersions.TryGetValue(deviceId, out var appliedVersion) && desired.Version <= appliedVersion)
            return;

        Apply(deviceId, desired);
    }

    private void Apply(string deviceId, TwinCollection desired)
    {
        Received?.Invoke(_desiredPropertyHandler.HandleDesiredProperty(deviceId, desired));
        _appliedDesiredVersions[deviceId] = desired.Version;
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
