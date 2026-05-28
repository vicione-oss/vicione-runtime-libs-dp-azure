using System;
using AwesomeAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class DesiredPropertyHandler_
{
    [Fact]
    public void Can_handle_single_node()
    {
        CreateSingleNodeDesiredHandler(out var deviceNode, out var desiredValueNodeId, out var desiredPropertyHandler);

        var values = desiredPropertyHandler.HandleDesiredProperty(deviceNode.Name, new("""
        {
            "MyValue": 23,
            "$metadata": {
                "MyValue": {
                    "$lastUpdated": "2024-01-01"
                }
            }
        }
        """));

        var value = values.Should().ContainSingle().Which;
        value.Channel.Should().Be(desiredValueNodeId.ToString());
        value.Value.Should().Be(23);
        value.Validity.Should().Be(1);
        value.Timestamp.Should().HaveYear(2024).And.HaveMonth(1).And.HaveDay(1);
    }

    [Fact]
    public void Can_handle_single_node_without_timestamp()
    {
        FakeTimeProvider timeProvider = new(new DateTime(2020, 1, 1));
        CreateSingleNodeDesiredHandler(out var deviceNode, out _, out var desiredPropertyHandler, timeProvider);

        var values = desiredPropertyHandler.HandleDesiredProperty(deviceNode.Name, new("""
        {
            "MyValue": 23,
        }
        """));

        values.Should().ContainSingle().Which.Timestamp.Should().Be(timeProvider.Start.DateTime);
    }

    [Fact]
    public void Can_handle_group_node()
    {
        CreateGroupNodeDesiredHandler(out var deviceNode, out var desiredValueNodeId, out var desiredPropertyHandler);

        var values = desiredPropertyHandler.HandleDesiredProperty(deviceNode.Name, new("""
        {
            "MyGroup": {
                "MyValue": 23
            },
            "$metadata": {
                "MyGroup": {
                    "MyValue": {
                        "$lastUpdated": "2024-01-01"
                    }
                }
            }
        }
        """));

        var value = values.Should().ContainSingle().Which;
        value.Channel.Should().Be(desiredValueNodeId.ToString());
        value.Value.Should().Be(23);
        value.Validity.Should().Be(1);
        value.Timestamp.Should().HaveYear(2024).And.HaveMonth(1).And.HaveDay(1);
    }

    [Fact]
    public void Can_handle_group_node_without_timestamp()
    {
        FakeTimeProvider timeProvider = new(new DateTime(2020, 1, 1));
        CreateGroupNodeDesiredHandler(out var deviceNode, out _, out var desiredPropertyHandler, timeProvider);

        var values = desiredPropertyHandler.HandleDesiredProperty(deviceNode.Name, new("""
        {
            "MyGroup": {
                "MyValue": 23
            }
        }
        """));

        values.Should().ContainSingle().Which.Timestamp.Should().Be(timeProvider.Start.DateTime);
    }

    private static void CreateSingleNodeDesiredHandler(out Node deviceNode, out Guid desiredValueNodeId, out DesiredPropertyHandler desiredPropertyHandler, TimeProvider? timeProvider = null)
    {
        deviceNode = new() { Id = Guid.NewGuid(), ParentId = null, DesignId = AzureIotHubNodeDesignId.Device, Name = "MyDevice", };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        desiredValueNodeId = Guid.NewGuid();
        Node desiredPropertiesNode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Desired,
            TransferredChannels = [desiredValueNodeId.ToString(),],
        };
        Node desiredValueNode = new()
        {
            Id = desiredValueNodeId,
            ParentId = desiredPropertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.JsonStructure,
            Name = "MyValue",
            AffectedChannels = [desiredValueNodeId.ToString(),],
            ValueType = typeof(int),
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, propertiesNode, desiredPropertiesNode, desiredValueNode,], };
        desiredPropertyHandler = new(communication, timeProvider ?? TimeProvider.System, new());
    }

    private static void CreateGroupNodeDesiredHandler(out Node deviceNode, out Guid desiredValueNodeId, out DesiredPropertyHandler desiredPropertyHandler, TimeProvider? timeProvider = null)
    {
        deviceNode = new() { Id = Guid.NewGuid(), ParentId = null, DesignId = AzureIotHubNodeDesignId.Device, Name = "MyDevice", };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        desiredValueNodeId = Guid.NewGuid();
        Node desiredPropertiesNode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Desired,
            TransferredChannels = [desiredValueNodeId.ToString(),],
        };
        Node desiredGroupNode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = desiredPropertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.JsonStructure,
            Name = "MyGroup",
        };
        Node desiredValueNode = new()
        {
            Id = desiredValueNodeId,
            ParentId = desiredGroupNode.Id,
            DesignId = AzureIotHubNodeDesignId.JsonStructure,
            Name = "MyValue",
            AffectedChannels = [desiredValueNodeId.ToString(),],
            ValueType = typeof(int),
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, propertiesNode, desiredPropertiesNode, desiredGroupNode, desiredValueNode,], };
        desiredPropertyHandler = new(communication, timeProvider, new());
    }
}
