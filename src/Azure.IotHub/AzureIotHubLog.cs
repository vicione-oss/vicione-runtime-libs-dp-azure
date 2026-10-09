using System;
using Microsoft.Extensions.Logging;

namespace ViciOne.Suite.DataPort;

internal static partial class AzureIotHubLog
{
    [LoggerMessage(1, LogLevel.Error, "Cannot publish data for device '{DeviceId}' to Azure IoT Hub. Retrying in {Delay}.")]
    internal static partial void PublishingFailed(this ILogger logger, string deviceId, TimeSpan delay, Exception exception);

    [LoggerMessage(2, LogLevel.Error, "Cannot publish values. Queue will be cleared.")]
    internal static partial void PublishWithCompletedQueueFailed(this ILogger logger, Exception exception);

    [LoggerMessage(3, LogLevel.Warning, "Cannot read desired property '{Property}' of device '{DeviceId}'. The value is skipped.")]
    internal static partial void DesiredPropertyUnreadable(this ILogger logger, string deviceId, string property, Exception exception);

    [LoggerMessage(4, LogLevel.Error, "Cannot read the device twin of '{DeviceId}'. Desired properties apply from the next patch.")]
    internal static partial void ReadingTwinFailed(this ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(5, LogLevel.Error, "Azure IoT Hub rejected data for device '{DeviceId}'. The data is dropped.")]
    internal static partial void DataRejected(this ILogger logger, string deviceId, Exception exception);

    [LoggerMessage(6, LogLevel.Error, "Cannot process values for Azure IoT Hub. The values of this engine cycle are dropped.")]
    internal static partial void ProcessingValuesFailed(this ILogger logger, Exception exception);

    [LoggerMessage(7, LogLevel.Warning, "Reported property '{Property}' is skipped: device twins accept integers only between -4503599627370496 and 4503599627370495, but the value is {Value}.")]
    internal static partial void ReportedIntegerOutOfRange(this ILogger logger, string property, object? value);
}
