using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.ManagedEngine.Runtime;
using Xunit;

namespace ViciOne.Suite.DataPort;

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

            await using AzureIotHubDataPortIncoming incoming = new(system.Communication with { Nodes = nodes, }, AssemblyLoadContext.Default);

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
