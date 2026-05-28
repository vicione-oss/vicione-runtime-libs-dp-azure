using System;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal static partial class AzureEventGridTopicLog
{
    [LoggerMessage(0, LogLevel.Error, "Cannot publish values. Items will be enqueued again.")]
    internal static partial void PublishBatchFailed(this ILogger logger);

    [LoggerMessage(1, LogLevel.Error, "Cannot publish data to Azure EventGrid.")]
    internal static partial void PublishingFailed(this ILogger logger, Exception exception);

    [LoggerMessage(2, LogLevel.Error, "Cannot publish values. Queue will be cleared.")]
    internal static partial void PublishWithCompletedQueueFailed(this ILogger logger);
}
