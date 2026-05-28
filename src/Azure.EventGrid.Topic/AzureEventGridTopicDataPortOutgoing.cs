using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

public sealed class AzureEventGridTopicDataPortOutgoing : IExternalOutgoingCommunication<AzureEventGridTopicDataPortCommunication>
{
    private readonly TaskPipeline<bool> _taskPipeline;
    private readonly ILogger _logger;
    private readonly EventGridEventHandler _gridEventHandler;
    private readonly EventGridPublisherClient _client;
    private readonly int _queueSize;
    private readonly int _bulkSize;
    private ManagedQueue<EventGridEvent>? _queue;
    private Task? _queueTask;

    public AzureEventGridTopicDataPortOutgoing(AzureEventGridTopicDataPortCommunication communication, ILogger<AzureEventGridTopicDataPortOutgoing> logger)
    {
        _client = new(new Uri(communication.TopicEndpoint), new AzureKeyCredential(communication.AccessKey));
        _gridEventHandler = new(communication, SendGridEvents);
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

    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        _queue ??= new(_queueSize, _logger, string.Empty);
        _queueTask ??= Task.Run(ProcessQueueAsync, cancellationToken);

        return Task.CompletedTask;
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
    }

    private Task SendGridEvents(IEnumerable<EventGridEvent> events, CancellationToken cancellationToken)
    {
        _queue?.Append(events);

        return Task.CompletedTask;
    }

    public async Task SendAsync(ulong engineCycle, IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
        => await _gridEventHandler.Process(values, cancellationToken);

    private async ValueTask<bool> PublishAsync(IReadOnlyCollection<EventGridEvent> values, CancellationToken cancellationToken)
    {
        try
        {
            if (values.Count == 0)
                return true;

            var response = await _client.SendEventsAsync(values, cancellationToken);

            if (response.IsError)
                throw new InvalidOperationException($"Failed to send to Azure Topic: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            _logger.PublishingFailed(ex);
            return false;
        }

        return true;
    }
}
