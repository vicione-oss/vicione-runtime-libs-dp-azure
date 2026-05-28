
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal static partial class ManagedQueueLog
{
    [LoggerMessage(0, LogLevel.Warning, "Limit of queue '{QueueName}' reached. {ItemsToRemove} items removed.")]
    internal static partial void LogItemsRemoved(this ILogger logger, string queueName, int itemsToRemove);
}
