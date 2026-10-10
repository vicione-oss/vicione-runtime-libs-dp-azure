using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.Devices.Client;
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

public class AzureIotHubDataPortIncoming_ConnectAsync
{
    [Fact]
    public async Task Raises_the_desired_values_already_in_the_twin_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        deviceClient.GetTwinAsync(Arg.Any<CancellationToken>()).Returns(new Twin { Properties = new() { Desired = new("""{ "MyValue": 23, "$version": 4 }"""), }, });
        using DeviceClientPool pool = new((_, _) => deviceClient);
        var (incoming, channels) = CreateIncoming(pool, NullLogger<AzureIotHubDataPortIncoming>.Instance, "MyDevice");
        await using var _ = incoming;
        List<ExternalValue> received = [];
        incoming.Received += received.AddRange;

        await incoming.ConnectAsync(TestContext.Current.CancellationToken);

        var value = received.Should().ContainSingle().Which;
        value.Channel.Should().Be(channels["MyValue"]);
        value.Value.Should().Be(23);
    }

    [Fact]
    public async Task Applies_the_twin_and_then_a_newer_patch_that_arrives_during_the_read_Async()
    {
        await using var deviceClient = CreateDeviceClientWithPatchDuringTwinRead(
            twin: """{ "MyValue": 1, "OtherValue": 7, "$version": 4 }""",
            patch: """{ "MyValue": 2, "$version": 5 }""");
        using DeviceClientPool pool = new((_, _) => deviceClient);
        var (incoming, channels) = CreateIncoming(pool, NullLogger<AzureIotHubDataPortIncoming>.Instance, "MyDevice");
        await using var _ = incoming;
        List<ExternalValue> received = [];
        incoming.Received += received.AddRange;

        await incoming.ConnectAsync(TestContext.Current.CancellationToken);

        received.Should().SatisfyRespectively(
            first => first.Should().Match<ExternalValue>(v => v.Channel == channels["MyValue"] && (int)v.Value! == 1),
            second => second.Should().Match<ExternalValue>(v => v.Channel == channels["OtherValue"] && (int)v.Value! == 7),
            third => third.Should().Match<ExternalValue>(v => v.Channel == channels["MyValue"] && (int)v.Value! == 2));
    }

    [Fact]
    public async Task Skips_a_patch_older_than_the_twin_that_arrives_during_the_read_Async()
    {
        await using var deviceClient = CreateDeviceClientWithPatchDuringTwinRead(
            twin: """{ "MyValue": 1, "$version": 4 }""",
            patch: """{ "MyValue": 2, "$version": 3 }""");
        using DeviceClientPool pool = new((_, _) => deviceClient);
        var (incoming, _) = CreateIncoming(pool, NullLogger<AzureIotHubDataPortIncoming>.Instance, "MyDevice");
        await using var _ = incoming;
        List<ExternalValue> received = [];
        incoming.Received += received.AddRange;

        await incoming.ConnectAsync(TestContext.Current.CancellationToken);

        received.Should().ContainSingle().Which.Value.Should().Be(1);
    }

    [Fact]
    public async Task Does_not_read_the_twin_of_a_device_without_desired_properties_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        var (incoming, _) = CreateIncoming(pool, NullLogger<AzureIotHubDataPortIncoming>.Instance, "OtherDevice");
        await using var _ = incoming;

        await incoming.ConnectAsync(TestContext.Current.CancellationToken);

        await deviceClient.DidNotReceive().GetTwinAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Connects_and_logs_if_the_twin_cannot_be_read_Async()
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        deviceClient.GetTwinAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new TimeoutException());
        FakeLogger<AzureIotHubDataPortIncoming> logger = new();
        using DeviceClientPool pool = new((_, _) => deviceClient);
        var (incoming, _) = CreateIncoming(pool, logger, "MyDevice");
        await using var _ = incoming;

        await incoming.ConnectAsync(TestContext.Current.CancellationToken);

        await deviceClient.Received(1).OpenAsync(Arg.Any<CancellationToken>());
        logger.LatestRecord.Level.Should().Be(LogLevel.Error);
        logger.LatestRecord.Message.Should().Match("*twin*'MyDevice'*");
    }

    private static IDeviceClient CreateDeviceClientWithPatchDuringTwinRead(string twin, string patch)
    {
        var deviceClient = Substitute.For<IDeviceClient>();
        DesiredPropertyUpdateCallback? onPatch = null;
        object? patchContext = null;
        deviceClient.When(c => c.SetDesiredPropertyUpdateCallbackAsync(Arg.Any<DesiredPropertyUpdateCallback>(), Arg.Any<object>(), Arg.Any<CancellationToken>())).Do(call =>
        {
            onPatch = call.ArgAt<DesiredPropertyUpdateCallback>(0);
            patchContext = call.ArgAt<object>(1);
        });
        deviceClient.GetTwinAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await onPatch!(new(patch), patchContext!);
            return new Twin { Properties = new() { Desired = new(twin), }, };
        });

        return deviceClient;
    }

    private static (AzureIotHubDataPortIncoming Incoming, Dictionary<string, string> Channels) CreateIncoming(DeviceClientPool pool, ILogger<AzureIotHubDataPortIncoming> logger, string connectedDevice)
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = "MyDevice", };
        Node otherDeviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = "OtherDevice", };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        var desiredNodeId = Guid.NewGuid();
        Node[] valueNodes = [CreateValueNode(desiredNodeId, "MyValue"), CreateValueNode(desiredNodeId, "OtherValue"),];
        Node desiredNode = new()
        {
            Id = desiredNodeId,
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Desired,
            TransferredChannels = [.. valueNodes.Select(n => n.Id.ToString()),],
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, otherDeviceNode, propertiesNode, desiredNode, .. valueNodes,], };
        var connectedNode = connectedDevice == deviceNode.Name ? deviceNode : otherDeviceNode;

        AzureIotHubDataPortIncoming incoming = new(
            communication,
            AssemblyLoadContext.Default,
            async cancellationToken => new Dictionary<Guid, VirtualDeviceClient>
            {
                [connectedNode.Id] = await pool.Register("hub", connectedNode.Name, Substitute.For<IAuthenticationMethod>(), cancellationToken),
            },
            logger);

        return (incoming, valueNodes.ToDictionary(n => n.Name, n => n.Id.ToString()));
    }

    private static Node CreateValueNode(Guid parentId, string name)
    {
        var id = Guid.NewGuid();

        return new() { Id = id, ParentId = parentId, DesignId = AzureIotHubNodeDesignId.JsonStructure, Name = name, AffectedChannels = [id.ToString(),], ValueType = typeof(int), };
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
