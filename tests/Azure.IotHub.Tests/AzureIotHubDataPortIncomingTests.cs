using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class AzureIotHubDataPortIncoming_ReceiveTwinDesiredPropertiesAsync
{
    [Fact]
    public async Task Raises_the_desired_values_already_in_the_twin_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        deviceClient.GetTwinAsync(Arg.Any<CancellationToken>()).Returns(new Twin { Properties = new() { Desired = new("""{ "MyValue": 23, "$version": 4 }"""), }, });
        var (incoming, channel) = CreateIncoming(NullLogger<AzureIotHubDataPortIncoming>.Instance);
        List<ExternalValue> received = [];
        incoming.Received += received.AddRange;
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using VirtualDeviceClient virtualClient = new("MyDevice", deviceClient, pool);

        await incoming.ReceiveTwinDesiredPropertiesAsync(virtualClient, TestContext.Current.CancellationToken);

        var value = received.Should().ContainSingle().Which;
        value.Channel.Should().Be(channel);
        value.Value.Should().Be(23);
    }

    [Fact]
    public async Task Does_not_read_the_twin_of_a_device_without_desired_properties_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        var (incoming, _) = CreateIncoming(NullLogger<AzureIotHubDataPortIncoming>.Instance);
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using VirtualDeviceClient virtualClient = new("OtherDevice", deviceClient, pool);

        await incoming.ReceiveTwinDesiredPropertiesAsync(virtualClient, TestContext.Current.CancellationToken);

        await deviceClient.DidNotReceive().GetTwinAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Logs_a_failed_twin_read_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        deviceClient.GetTwinAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());
        FakeLogger<AzureIotHubDataPortIncoming> logger = new();
        var (incoming, _) = CreateIncoming(logger);
        using DeviceClientPool pool = new((_, _) => deviceClient);
        await using VirtualDeviceClient virtualClient = new("MyDevice", deviceClient, pool);

        await incoming.ReceiveTwinDesiredPropertiesAsync(virtualClient, TestContext.Current.CancellationToken);

        logger.LatestRecord.Level.Should().Be(LogLevel.Error);
        logger.LatestRecord.Message.Should().Match("*twin*'MyDevice'*");
    }

    private static (AzureIotHubDataPortIncoming Incoming, string Channel) CreateIncoming(ILogger<AzureIotHubDataPortIncoming> logger)
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = "MyDevice", };
        Node otherDeviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = "OtherDevice", };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        var valueNodeId = Guid.NewGuid();
        Node desiredNode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Desired,
            TransferredChannels = [valueNodeId.ToString(),],
        };
        Node valueNode = new()
        {
            Id = valueNodeId,
            ParentId = desiredNode.Id,
            DesignId = AzureIotHubNodeDesignId.JsonStructure,
            Name = "MyValue",
            AffectedChannels = [valueNodeId.ToString(),],
            ValueType = typeof(int),
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, otherDeviceNode, propertiesNode, desiredNode, valueNode,], };

        return (new(communication, AssemblyLoadContext.Default, Substitute.For<IDeviceClientPool>(), logger), valueNodeId.ToString());
    }
}

public class AzureIotHubDataPortIncoming_
{
    [Fact]
    public async Task DependencyInjectionProviderFactory_can_create_instance_Async()
    {
        await using var providerFactory = new ConstructorProviderFactory();

        var instance = providerFactory.CreateExternalIncoming(
            new AzureIotHubDataPortCommunication(),
            new(),
            Substitute.For<ILoggerFactory>(),
            Substitute.For<INameResolver>(),
            AssemblyLoadContext.Default);

        instance.Should().NotBeNull().And.BeOfType<AzureIotHubDataPortIncoming>();
    }

    [Collection(AzureIotHubTestEnvironment.Name)]
    public class Interoperability(AzureIotHubTestSystem system)
    {
        [Fact(Skip = "credentials required")]
        [Trait("Category", "Interoperability")]
        [SuppressMessage("Usage", "xUnit1004:Test methods should not be skipped")]
        public async Task Can_get_desired_value_from_device_twin()
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
                    DesignId = AzureIotHubNodeDesignId.Desired,
                    ParentId = propertiesNodeId,
                    Name = "Desired",
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

            await using AzureIotHubDataPortIncoming incoming = new(system.Communication with { Nodes = nodes, }, AssemblyLoadContext.Default, Substitute.For<ILogger<AzureIotHubDataPortIncoming>>());

            ConcurrentBag<ExternalValue> externalValues = [];
            incoming.Received += v =>
            {
                foreach (var value in v)
                    externalValues.Add(value);
            };
            await incoming.ConnectAsync(TestContext.Current.CancellationToken);
            var start = DateTime.UtcNow;

            try
            {
                await system.UpdateDesiredProperties(deviceName, new()
                {
                    ["property"] = new JsonObject
                    {
                        ["value"] = 112,
                    },
                });
            }
            finally
            {
                await incoming.DisconnectAsync(TestContext.Current.CancellationToken);
                await system.DeleteDevice(deviceName);
            }

            var value = externalValues.Should().ContainSingle().Which;
            value.Channel.Should().Be("value");
            value.Value.Should().Be(112);
            value.Validity.Should().Be(1);
            value.Timestamp.Should().BeAfter(start).And.BeBefore(DateTime.UtcNow);
        }
    }
}
