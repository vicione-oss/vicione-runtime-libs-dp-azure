using System;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal static partial class AzureIotHubLog
{
    [LoggerMessage(0, LogLevel.Error, "Cannot publish values. Items will be enqueued again.")]
    internal static partial void PublishBatchFailed(this ILogger logger);

    [LoggerMessage(1, LogLevel.Error, "Cannot publish data to Azure Iot Hub.")]
    internal static partial void PublishingFailed(this ILogger logger, Exception exception);

    [LoggerMessage(2, LogLevel.Error, "Cannot publish values. Queue will be cleared.")]
    internal static partial void PublishWithCompletedQueueFailed(this ILogger logger);

    [LoggerMessage(3, LogLevel.Warning, "Cannot read desired property '{Property}' of device '{DeviceId}'. The value is skipped.")]
    internal static partial void DesiredPropertyUnreadable(this ILogger logger, string deviceId, string property, Exception exception);
}
