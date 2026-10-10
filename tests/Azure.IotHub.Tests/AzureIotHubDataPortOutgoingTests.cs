using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure.Messaging.EventHubs.Consumer;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Client.Exceptions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class AzureIotHubDataPortOutgoing_SendAsync
{
    private const string TelemetryChannel = "telemetry";
    private const string ReportedChannel = "reported";

    public static TheoryData<Exception> Rejections { get; } =
    [
        new IotHubException("Argument 'reported' is out of range."),
        new MessageTooLargeException("Message too large."),
        new ArgumentException("Argument error."),
    ];

    public static TheoryData<Exception> Unavailabilities { get; } =
    [
        new IotHubSuspendedException("IoT Hub suspended."),
        new DeviceMaximumQueueDepthExceededException("Daily quota exceeded."),
    ];

    public static TheoryData<Exception> Refusals { get; } =
    [
        new DeviceNotFoundException("Device not found."),
        new UnauthorizedException("Key refused."),
        new InvalidOperationException("amqp:not-allowed"),
        new NotSupportedException("amqp:not-implemented"),
    ];

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Drops_the_data_of_a_refused_device_without_blocking_and_logs_once_Async(Exception refusal)
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        deviceClient.UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(refusal), Task.FromException(refusal), Task.CompletedTask, Task.FromException(refusal));
        FakeLogger<AzureIotHubDataPortOutgoing> logger = new();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using var outgoing = CreateOutgoing(pool, logger);
        await outgoing.ConnectAsync(TestContext.Current.CancellationToken);

        for (var cycle = 1; cycle <= 4; cycle++)
            await outgoing.SendAsync((ulong)cycle, CreateCycle(cycle), TestContext.Current.CancellationToken);
        await outgoing.DisconnectAsync(TestContext.Current.CancellationToken);

        await deviceClient.Received(4).SendEventAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        await deviceClient.Received(4).UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>());
        logger.Collector.GetSnapshot().Should().Satisfy(
            r => r.Message.Contains("refuses", StringComparison.Ordinal),
            r => r.Message.Contains("refuses", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Rejections))]
    public async Task Drops_a_rejected_reported_patch_and_delivers_the_next_cycle_Async(Exception rejection)
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        deviceClient.UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>()).Returns(Task.FromException(rejection), Task.CompletedTask);
        FakeLogger<AzureIotHubDataPortOutgoing> logger = new();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using var outgoing = CreateOutgoing(pool, logger);
        await outgoing.ConnectAsync(TestContext.Current.CancellationToken);

        await outgoing.SendAsync(1, CreateCycle(1), TestContext.Current.CancellationToken);
        await outgoing.SendAsync(2, CreateCycle(2), TestContext.Current.CancellationToken);
        await outgoing.DisconnectAsync(TestContext.Current.CancellationToken);

        await deviceClient.Received(2).SendEventAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        await deviceClient.Received(2).UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>());
        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error && r.Message.Contains("rejected", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Unavailabilities))]
    public async Task Retries_only_the_failed_message_while_the_hub_is_unavailable_Async(Exception unavailability)
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        TaskCompletionSource retried = new();
        var attempts = 0;
        deviceClient.UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (++attempts == 1)
                return Task.FromException(unavailability);

            retried.TrySetResult();
            return Task.CompletedTask;
        });
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using var outgoing = CreateOutgoing(pool, NullLogger<AzureIotHubDataPortOutgoing>.Instance);
        await outgoing.ConnectAsync(TestContext.Current.CancellationToken);

        await outgoing.SendAsync(1, CreateCycle(1), TestContext.Current.CancellationToken);
        await retried.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        await outgoing.DisconnectAsync(TestContext.Current.CancellationToken);

        await deviceClient.Received(1).SendEventAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        await deviceClient.Received(2).UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Drops_a_cycle_that_cannot_be_processed_and_delivers_the_next_cycle_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        FakeLogger<AzureIotHubDataPortOutgoing> logger = new();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using var outgoing = CreateOutgoing(pool, logger);
        await outgoing.ConnectAsync(TestContext.Current.CancellationToken);

        await outgoing.SendAsync(1, [new() { Channel = TelemetryChannel, Value = new Unserializable(), }, new() { Channel = ReportedChannel, Value = 1, },], TestContext.Current.CancellationToken);
        await outgoing.SendAsync(2, CreateCycle(2), TestContext.Current.CancellationToken);
        await outgoing.DisconnectAsync(TestContext.Current.CancellationToken);

        await deviceClient.Received(1).SendEventAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>());
        await deviceClient.Received(1).UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>());
        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Error && r.Message.Contains("dropped", StringComparison.Ordinal));
    }

    private sealed class Unserializable
    {
        public int Value => throw new InvalidOperationException("Value cannot be read.");
    }

    internal static ExternalValue[] CreateCycle(int value)
        => [new() { Channel = TelemetryChannel, Value = value, }, new() { Channel = ReportedChannel, Value = value, },];

    internal static AzureIotHubDataPortOutgoing CreateOutgoing(DeviceClientPool pool, ILogger<AzureIotHubDataPortOutgoing> logger, TimeProvider? timeProvider = null)
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = "MyDevice", };
        Node telemetryNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Telemetry, TransferredChannels = [TelemetryChannel,], };
        Node telemetryValueNode = new() { Id = Guid.NewGuid(), ParentId = telemetryNode.Id, DesignId = AzureIotHubNodeDesignId.JsonStructure, Name = "value", AffectedChannels = [TelemetryChannel,], };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        Node reportedNode = new() { Id = Guid.NewGuid(), ParentId = propertiesNode.Id, DesignId = AzureIotHubNodeDesignId.Reported, TransferredChannels = [ReportedChannel,], };
        Node reportedValueNode = new() { Id = Guid.NewGuid(), ParentId = reportedNode.Id, DesignId = AzureIotHubNodeDesignId.JsonStructure, Name = "value", AffectedChannels = [ReportedChannel,], };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, telemetryNode, telemetryValueNode, propertiesNode, reportedNode, reportedValueNode,], };

        return new(
            communication,
            async cancellationToken => new Dictionary<Guid, VirtualDeviceClient>
            {
                [deviceNode.Id] = await pool.Register("hub", deviceNode.Name, Substitute.For<IAuthenticationMethod>(), cancellationToken),
            },
            logger,
            timeProvider);
    }
}

public class AzureIotHubDataPortOutgoing_DisconnectAsync
{
    [Fact]
    public async Task Stops_retrying_and_clears_the_queue_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        TaskCompletionSource attempted = new();
        deviceClient.SendEventAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            attempted.TrySetResult();
            return Task.FromException(new IotHubSuspendedException("IoT Hub suspended."));
        });
        FakeLogger<AzureIotHubDataPortOutgoing> logger = new();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using var outgoing = AzureIotHubDataPortOutgoing_SendAsync.CreateOutgoing(pool, logger);
        await outgoing.ConnectAsync(TestContext.Current.CancellationToken);
        await outgoing.SendAsync(1, AzureIotHubDataPortOutgoing_SendAsync.CreateCycle(1), TestContext.Current.CancellationToken);
        await outgoing.SendAsync(2, AzureIotHubDataPortOutgoing_SendAsync.CreateCycle(2), TestContext.Current.CancellationToken);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await outgoing.DisconnectAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await deviceClient.DidNotReceive().UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>());
        logger.LatestRecord.Message.Should().Be("Cannot publish values. Queue will be cleared.");
    }

    [Fact]
    public async Task Stops_a_send_that_does_not_return_after_the_flush_timeout_Async()
    {
        var (deviceClient, attempted) = CreateUnreachableDeviceClient();
        FakeTimeProvider timeProvider = new();
        FakeLogger<AzureIotHubDataPortOutgoing> logger = new();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using var outgoing = AzureIotHubDataPortOutgoing_SendAsync.CreateOutgoing(pool, logger, timeProvider);
        await outgoing.ConnectAsync(TestContext.Current.CancellationToken);
        await outgoing.SendAsync(1, AzureIotHubDataPortOutgoing_SendAsync.CreateCycle(1), TestContext.Current.CancellationToken);
        await outgoing.SendAsync(2, AzureIotHubDataPortOutgoing_SendAsync.CreateCycle(2), TestContext.Current.CancellationToken);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var disconnect = outgoing.DisconnectAsync(TestContext.Current.CancellationToken);
        var completedBeforeTimeout = disconnect.IsCompleted;
        timeProvider.Advance(AzureIotHubDataPortOutgoing.DisconnectFlushTimeout);
        await disconnect.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        completedBeforeTimeout.Should().BeFalse();
        await deviceClient.DidNotReceive().UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>());
        logger.LatestRecord.Message.Should().Be("Cannot publish values. Queue will be cleared.");
    }

    [Fact]
    public async Task Stops_a_send_that_does_not_return_when_cancelled_Async()
    {
        var (deviceClient, attempted) = CreateUnreachableDeviceClient();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using var outgoing = AzureIotHubDataPortOutgoing_SendAsync.CreateOutgoing(pool, NullLogger<AzureIotHubDataPortOutgoing>.Instance, new FakeTimeProvider());
        await outgoing.ConnectAsync(TestContext.Current.CancellationToken);
        await outgoing.SendAsync(1, AzureIotHubDataPortOutgoing_SendAsync.CreateCycle(1), TestContext.Current.CancellationToken);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        using CancellationTokenSource stop = new();

        var disconnect = outgoing.DisconnectAsync(stop.Token);
        await stop.CancelAsync();
        var waitForDisconnect = () => disconnect.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        await waitForDisconnect.Should().ThrowAsync<OperationCanceledException>();
        await deviceClient.DidNotReceive().UpdateReportedPropertiesAsync(Arg.Any<TwinCollection>(), Arg.Any<CancellationToken>());
    }

    // The SDK retries a hub it cannot reach without end and returns only when the token is cancelled.
    private static (IDeviceClient DeviceClient, TaskCompletionSource Attempted) CreateUnreachableDeviceClient()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        TaskCompletionSource attempted = new();
        deviceClient.SendEventAsync(Arg.Any<Message>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            attempted.TrySetResult();
            return Task.Delay(Timeout.Infinite, call.ArgAt<CancellationToken>(1));
        });

        return (deviceClient, attempted);
    }
}

public class AzureIotHubDataPortOutgoing_
{
    [Fact]
    public async Task DependencyInjectionProviderFactory_can_create_instance_Async()
    {
        await using var providerFactory = new ConstructorProviderFactory();

        var instance = providerFactory.CreateExternalOutgoing(
            new AzureIotHubDataPortCommunication(),
            new(),
            Substitute.For<ILoggerFactory>(),
            Substitute.For<INameResolver>(),
            AssemblyLoadContext.Default);

        instance.Should().NotBeNull().And.BeOfType<AzureIotHubDataPortOutgoing>();
    }

    [Collection(AzureIotHubTestEnvironment.Name)]
    public class Interoperability(AzureIotHubTestSystem system)
    {
        [Fact(Skip = "credentials required")]
        [Trait("Category", "Interoperability")]
        [SuppressMessage("Usage", "xUnit1004:Test methods should not be skipped")]
        public async Task Can_update_reported_value_in_device_twin()
        {
            var deviceNodeId = Guid.NewGuid();
            var deviceName = deviceNodeId.ToString().Replace("-", string.Empty, StringComparison.InvariantCulture);
            var propertiesNodeId = Guid.NewGuid();
            var reportedNodeId = Guid.NewGuid();
            var jsonPropertyNodeId = Guid.NewGuid();
            var valueNodeId = Guid.NewGuid();

            Node[] nodes =
            [
                new()
                {
                    Id = deviceNodeId,
                    DesignId = AzureIotHubNodeDesignId.Device,
                    Name = deviceName,
                },
                new()
                {
                    Id = propertiesNodeId,
                    DesignId = AzureIotHubNodeDesignId.Properties,
                    ParentId = deviceNodeId,
                    Name = "Properties",
                },
                new()
                {
                    Id = reportedNodeId,
                    DesignId = AzureIotHubNodeDesignId.Reported,
                    ParentId = propertiesNodeId,
                    Name = "Reported",
                    TransferredChannels = [ "value" ],
                },
                new()
                {
                    Id = jsonPropertyNodeId,
                    DesignId = AzureIotHubNodeDesignId.JsonStructure,
                    ParentId = reportedNodeId,
                    Name = "property",
                },
                new()
                {
                    Id = valueNodeId,
                    DesignId = AzureIotHubNodeDesignId.JsonStructure,
                    ParentId = jsonPropertyNodeId,
                    Name = "value",
                    ValueType = typeof(int),
                    AffectedChannels = [ "value" ],
                },
            ];

            await using AzureIotHubDataPortOutgoing outgoing = new(system.Communication with { Nodes = nodes, }, Substitute.For<ILogger<AzureIotHubDataPortOutgoing>>());

            await outgoing.ConnectAsync(TestContext.Current.CancellationToken);

            try
            {
                (await system.GetDeviceTwin(deviceName))
                    ["properties"]!
                    ["reported"]!
                    ["property"]!
                    ["value"]
                    .Should().BeNull();

                await outgoing.SendAsync(1, [
                        new()
                        {
                            Channel = "value",
                            Value = 112,
                        },
                    ], TestContext.Current.CancellationToken);

                (await system.GetDeviceTwin(deviceName))
                    ["properties"]!
                    ["reported"]!
                    ["property"]!
                    ["value"]!
                    .GetValue<int>()
                    .Should().Be(112);
            }
            finally
            {
                await outgoing.DisconnectAsync(TestContext.Current.CancellationToken);
                await system.DeleteDevice(deviceName);
            }
        }

        [Fact(Skip = "credentials required")]
        [Trait("Category", "Interoperability")]
        [SuppressMessage("Usage", "xUnit1004:Test methods should not be skipped")]
        public async Task Can_send_device_telemetry()
        {
            var deviceNodeId = Guid.NewGuid();
            var deviceName = deviceNodeId.ToString().Replace("-", string.Empty, StringComparison.InvariantCulture);
            var telemetryNodeId = Guid.NewGuid();
            var jsonPropertyNodeId = Guid.NewGuid();
            var valueNodeId = Guid.NewGuid();

            Node[] nodes =
            [
                new()
                {
                    Id = deviceNodeId,
                    DesignId = AzureIotHubNodeDesignId.Device,
                    Name = deviceName,
                },
                new()
                {
                    Id = telemetryNodeId,
                    DesignId = AzureIotHubNodeDesignId.Telemetry,
                    ParentId = deviceNodeId,
                    Name = "Telemetry",
                    TransferredChannels = [ "value" ],
                },
                new()
                {
                    Id = jsonPropertyNodeId,
                    DesignId = AzureIotHubNodeDesignId.JsonStructure,
                    ParentId = telemetryNodeId,
                    Name = "property",
                },
                new()
                {
                    Id = valueNodeId,
                    DesignId = AzureIotHubNodeDesignId.JsonStructure,
                    ParentId = jsonPropertyNodeId,
                    Name = "value",
                    ValueType = typeof(int),
                    AffectedChannels = [ "value" ],
                },
            ];

            await using AzureIotHubDataPortOutgoing outgoing = new(system.Communication with { Nodes = nodes, }, Substitute.For<ILogger<AzureIotHubDataPortOutgoing>>());

            await using EventHubConsumerClient client = new(EventHubConsumerClient.DefaultConsumerGroupName, system.EventHubConnectionString);
            ConcurrentBag<PartitionEvent> values = [];
            using CancellationTokenSource readCancellationTokenSource = new();
            var start = DateTimeOffset.UtcNow;
            var readTask = Task.Run(async () =>
            {
                await foreach (var @event in client.ReadEventsAsync(readCancellationTokenSource.Token))
                {
                    if (@event.Data.EnqueuedTime > start)
                        values.Add(@event);
                }
            }, TestContext.Current.CancellationToken);
            DateTime timestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            await outgoing.ConnectAsync(TestContext.Current.CancellationToken);

            try
            {
                await Task.Delay(1_000, TestContext.Current.CancellationToken);
                await outgoing.SendAsync(1, [
                        new()
                        {
                            Channel = "value",
                            Value = 112,
                            Validity = 11,
                            Timestamp = timestamp,
                        },
                    ], TestContext.Current.CancellationToken);
                await Task.Delay(1_000, TestContext.Current.CancellationToken);
            }
            finally
            {
                await outgoing.DisconnectAsync(TestContext.Current.CancellationToken);
                await system.DeleteDevice(deviceName);
                await readCancellationTokenSource.CancelAsync();
                try
                {
                    await readTask;
                }
                catch (OperationCanceledException)
                { }
            }

            var value = values.Should().ContainSingle().Which;
            var data = await JsonSerializer.DeserializeAsync<JsonObject>(value.Data.EventBody.ToStream(), cancellationToken: TestContext.Current.CancellationToken);
            data!["property"]!["value"]!.GetValue<int>().Should().Be(112);
            value.Data.Properties["type"].Should().Be("{\"property\":{\"value\":\"System.Int32, System.Private.CoreLib, Version=10.0.0.0, Culture=neutral, PublicKeyToken=7cec85d7bea7798e\"}}");
            value.Data.Properties["timestamp"].Should().Be(timestamp.ToString("O"));
            value.Data.Properties["validity"].Should().Be("11");
        }
    }
}
