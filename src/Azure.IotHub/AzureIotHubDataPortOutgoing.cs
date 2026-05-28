using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;
using Message = Microsoft.Azure.Devices.Client.Message;

namespace ViciOne.Suite.DataPort;

public sealed class AzureIotHubDataPortOutgoing : IExternalOutgoingCommunication<AzureIotHubDataPortCommunication>, IAsyncDisposable
{
    private readonly TaskPipeline<bool> _taskPipeline;
    private readonly AzureIotHubDataPortCommunication _communication;
    private readonly IDeviceClientPool _deviceClientPool;
    private readonly TelemetryHandler _telemetryHandler;
    private readonly ReportedPropertyHandler _reportedPropertyHandler;
    private readonly ILogger _logger;
    private readonly int _queueSize;
    private readonly int _bulkSize;
    private ManagedQueue<IReadOnlyCollection<ExternalValue>>? _queue;
    private Task? _queueTask;

    private IDictionary<Guid, VirtualDeviceClient>? _nodeDeviceClients;

    public AzureIotHubDataPortOutgoing(AzureIotHubDataPortCommunication communication, ILogger<AzureIotHubDataPortOutgoing> logger)
        : this(communication, DeviceClientPool.Instance, logger)
    { }

    internal AzureIotHubDataPortOutgoing(AzureIotHubDataPortCommunication communication, IDeviceClientPool deviceClientPool, ILogger<AzureIotHubDataPortOutgoing> logger)
    {
        _communication = communication;
        _deviceClientPool = deviceClientPool;
        _telemetryHandler = new(communication, SendEventsToDevice);
        _reportedPropertyHandler = new(communication, UpdateReportedProperties);
        _queueSize = communication.MaxPendingMessages;
        _bulkSize = communication.BulkSize;
        _logger = logger;
        _taskPipeline = new(
            3,
            retrycount =>
            {
                return retrycount switch
                {
                    0 => TimeSpan.Zero,
                    1 => TimeSpan.FromSeconds(1),
                    2 => TimeSpan.FromSeconds(5),
                    _ => TimeSpan.FromSeconds(10),
                };
            },
            false);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is not null)
            return;

        _nodeDeviceClients = await AzureSetup.CreateDeviceClients(_communication, _deviceClientPool, cancellationToken).ConfigureAwait(false);

        foreach (var (_, deviceClient) in _nodeDeviceClients)
            await deviceClient.Connect(cancellationToken).ConfigureAwait(false);

        _queue ??= new(_queueSize, _logger, string.Empty);
        _queueTask ??= Task.Run(ProcessQueueAsync, cancellationToken);
    }

    private async Task ProcessQueueAsync()
    {
        if (_queue is null)
            throw new InvalidOperationException("Queue must be initialized.");

        await foreach (var items in _queue.GetAsyncEnumerator(_bulkSize))
            await ProcessBatchAsync(items, _queue, PublishAsync, _taskPipeline, _logger.PublishBatchFailed, _logger.PublishWithCompletedQueueFailed).ConfigureAwait(false);
    }

    private static async Task ProcessBatchAsync<T>(List<T> items, ManagedQueue<T> queue, Func<List<T>, CancellationToken, ValueTask<bool>> publishAsync, TaskPipeline<bool> resiliencePipeline, Action publishFailed, Action publishWithCompletedQueueFailed) where T : notnull
    {
        if (!await resiliencePipeline.ExecuteAsync(cancellationToken => publishAsync(items, cancellationToken), CancellationToken.None).ConfigureAwait(false))
        {
            if (queue.IsAddingCompleted)
            {
                publishWithCompletedQueueFailed();
                queue.Clear();
            }
            else
            {
                publishFailed();
                queue.Prepend(items);
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _queue?.CompleteAdding();

        if (_queueTask is not null)
        {
            await _queueTask.ConfigureAwait(false);
            _queueTask.Dispose();
            _queueTask = null;
        }

        _queue?.Dispose();
        _queue = null;

        if (_nodeDeviceClients is null)
            return;

        foreach (var deviceClient in _nodeDeviceClients.Values)
        {
            await deviceClient.Disconnect(cancellationToken).ConfigureAwait(false);
            await deviceClient.DisposeAsync().ConfigureAwait(false);
        }

        _nodeDeviceClients.Clear();
        _nodeDeviceClients = null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_nodeDeviceClients is not null)
        {
            _queue?.CompleteAdding();

            foreach (var deviceClient in _nodeDeviceClients.Values)
                await deviceClient.DisposeAsync().ConfigureAwait(false);

            _nodeDeviceClients.Clear();
            _nodeDeviceClients = null;
        }
    }

    private async Task SendEventsToDevice(Guid deviceId, Message message, CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is null)
            throw new InvalidOperationException("Device clients not initialized.");

        await _nodeDeviceClients[deviceId].InternalDeviceClient.SendEventAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task UpdateReportedProperties(Guid deviceId, TwinCollection collection, CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is null)
            throw new InvalidOperationException("Device clients not initialized.");

        await _nodeDeviceClients[deviceId].InternalDeviceClient.UpdateReportedPropertiesAsync(collection, cancellationToken).ConfigureAwait(false);
    }

    public Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        _queue?.Append(values);

        return Task.CompletedTask;
    }

    private async ValueTask<bool> PublishAsync(IReadOnlyCollection<IReadOnlyCollection<ExternalValue>> valuesCollection, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var values in valuesCollection)
            {
                await _telemetryHandler.Process(values, cancellationToken).ConfigureAwait(false);
                await _reportedPropertyHandler.Process(values, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.PublishingFailed(ex);
            return false;
        }

        return true;
    }
}
