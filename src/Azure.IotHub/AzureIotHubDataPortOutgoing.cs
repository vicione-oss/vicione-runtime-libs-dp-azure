using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;
using Message = Microsoft.Azure.Devices.Client.Message;

namespace ViciOne.Suite.DataPort;

public sealed class AzureIotHubDataPortOutgoing : IExternalOutgoingCommunication<AzureIotHubDataPortCommunication>, IAsyncDisposable
{
    internal static TimeSpan DisconnectFlushTimeout { get; } = TimeSpan.FromSeconds(10);

    private readonly Func<CancellationToken, Task<IDictionary<Guid, VirtualDeviceClient>>> _createDeviceClients;
    private readonly TelemetryHandler _telemetryHandler;
    private readonly ReportedPropertyHandler _reportedPropertyHandler;
    private readonly ILogger _logger;
    private readonly TimeProvider _timeProvider;
    private readonly int _queueSize;
    private readonly int _bulkSize;
    private ManagedQueue<IReadOnlyCollection<ExternalValue>>? _queue;
    private Task? _queueTask;
    private CancellationTokenSource? _deliveryCancellation;

    private IDictionary<Guid, VirtualDeviceClient>? _nodeDeviceClients;

    public AzureIotHubDataPortOutgoing(AzureIotHubDataPortCommunication communication, ILogger<AzureIotHubDataPortOutgoing> logger)
        : this(communication, cancellationToken => AzureSetup.CreateDeviceClients(communication, DeviceClientPool.Instance, cancellationToken), logger)
    { }

    internal AzureIotHubDataPortOutgoing(AzureIotHubDataPortCommunication communication, Func<CancellationToken, Task<IDictionary<Guid, VirtualDeviceClient>>> createDeviceClients, ILogger<AzureIotHubDataPortOutgoing> logger, TimeProvider? timeProvider = null)
    {
        _createDeviceClients = createDeviceClients;
        _telemetryHandler = new(communication, SendEventsToDevice);
        _reportedPropertyHandler = new(communication, UpdateReportedProperties, logger);
        _queueSize = communication.MaxPendingMessages;
        _bulkSize = communication.BulkSize;
        _logger = logger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is not null)
            return;

        _nodeDeviceClients = await _createDeviceClients(cancellationToken).ConfigureAwait(false);

        foreach (var (_, deviceClient) in _nodeDeviceClients)
            await deviceClient.Connect(cancellationToken).ConfigureAwait(false);

        _queue ??= new(_queueSize, _logger, string.Empty);
        _deliveryCancellation ??= new();
        _queueTask ??= Task.Run(ProcessQueueAsync, cancellationToken);
    }

    private async Task ProcessQueueAsync()
    {
        if (_queue is null || _deliveryCancellation is null)
            throw new InvalidOperationException("Queue must be initialized.");

        var cancellationToken = _deliveryCancellation.Token;

        await foreach (var batch in _queue.GetAsyncEnumerator(_bulkSize))
        {
            foreach (var values in batch)
            {
                if (await PublishAsync(values, cancellationToken).ConfigureAwait(false))
                    continue;

                _queue.Clear();
                break;
            }
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken)
    {
        _queue?.CompleteAdding();

        if (_queueTask is not null)
        {
            await FlushQueueAsync(_queueTask, cancellationToken).ConfigureAwait(false);
            _queueTask.Dispose();
            _queueTask = null;
        }

        _queue?.Dispose();
        _queue = null;
        _deliveryCancellation?.Dispose();
        _deliveryCancellation = null;

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

    // The SDK retries transient errors without end, so a send towards an unreachable hub only returns once its
    // token is cancelled.
    private async Task FlushQueueAsync(Task queueTask, CancellationToken cancellationToken)
    {
        using CancellationTokenSource flushTimeout = new(DisconnectFlushTimeout, _timeProvider);
        await using var onFlushTimeout = flushTimeout.Token.Register(CancelDelivery).ConfigureAwait(false);
        await using var onCancellation = cancellationToken.Register(CancelDelivery).ConfigureAwait(false);

        await queueTask.ConfigureAwait(false);
    }

    private void CancelDelivery()
        => _deliveryCancellation?.Cancel();

    public async ValueTask DisposeAsync()
    {
        if (_nodeDeviceClients is not null)
        {
            _queue?.CompleteAdding();
            CancelDelivery();

            foreach (var deviceClient in _nodeDeviceClients.Values)
                await deviceClient.DisposeAsync().ConfigureAwait(false);

            _nodeDeviceClients.Clear();
            _nodeDeviceClients = null;
        }
    }

    private Task SendEventsToDevice(Guid deviceNodeId, Func<Message> createMessage, CancellationToken cancellationToken)
        => DeliverAsync(deviceNodeId, async deviceClient =>
        {
            using var message = createMessage();
            await deviceClient.SendEventAsync(message, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    private Task UpdateReportedProperties(Guid deviceNodeId, TwinCollection collection, CancellationToken cancellationToken)
        => DeliverAsync(deviceNodeId, deviceClient => deviceClient.UpdateReportedPropertiesAsync(collection, cancellationToken), cancellationToken);

    // Failures are retried until the queue completes; nothing goes back into the queue, so data the hub already
    // accepted is never sent twice. A rejection of the data itself is permanent, and retrying it would block every
    // later value.
    private async Task DeliverAsync(Guid deviceNodeId, Func<IDeviceClient, Task> send, CancellationToken cancellationToken)
    {
        if (_nodeDeviceClients is null)
            throw new InvalidOperationException("Device clients not initialized.");

        var deviceClient = _nodeDeviceClients[deviceNodeId];

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await send(deviceClient.InternalDeviceClient).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (IsRejection(ex))
            {
                _logger.DataRejected(deviceClient.DeviceId, ex);
                return;
            }
            catch (Exception ex) when (cancellationToken.IsCancellationRequested || _queue is null or { IsAddingCompleted: true })
            {
                throw new OperationCanceledException("Delivery stopped because the DataPort disconnects.", ex);
            }
            catch (Exception ex)
            {
                var delay = GetRetryDelay(attempt);
                _logger.PublishingFailed(deviceClient.DeviceId, delay, ex);
                await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    // The SDK reports a refused message or patch as one of these; a plain IotHubException carries the 4xx response
    // to a twin request. Its subclasses for a suspended hub, the daily quota, an unknown device or a refused key are
    // about the hub or the device, not the data, and the data stays queued until the hub accepts it again.
    private static bool IsRejection(Exception exception)
        => exception switch
        {
            MessageTooLargeException or ArgumentException => true,
            IotHubException { IsTransient: false } iotHubException => iotHubException.GetType() == typeof(IotHubException),
            _ => false,
        };

    private static TimeSpan GetRetryDelay(int attempt)
        => attempt switch
        {
            0 => TimeSpan.FromSeconds(1),
            1 => TimeSpan.FromSeconds(5),
            _ => TimeSpan.FromSeconds(10),
        };

    public Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        _queue?.Append(values);

        return Task.CompletedTask;
    }

    private async Task<bool> PublishAsync(IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        try
        {
            await _telemetryHandler.Process(values, cancellationToken).ConfigureAwait(false);
            await _reportedPropertyHandler.Process(values, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            _logger.PublishWithCompletedQueueFailed(ex.InnerException ?? ex);
            return false;
        }
        catch (Exception ex)
        {
            _logger.ProcessingValuesFailed(ex);
        }

        return true;
    }
}
