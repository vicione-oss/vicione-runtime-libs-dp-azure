using System;
using System.Collections.Generic;
using System.Linq;
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

public class DesiredPropertyHandler_HandleDesiredProperty
{
    private const string DeviceName = "MyDevice";

    [Fact]
    public void Returns_only_the_values_present_in_the_patch()
    {
        var (handler, channels) = CreateHandler("MyValue", "OtherValue");

        var values = handler.HandleDesiredProperty(DeviceName, new("""{ "OtherValue": 555, "$version": 7 }"""));

        var value = values.Should().ContainSingle().Which;
        value.Channel.Should().Be(channels["OtherValue"]);
        value.Value.Should().Be(555);
    }

    [Fact]
    public void Skips_properties_removed_with_null()
    {
        var (handler, _) = CreateHandler("MyValue");

        var values = handler.HandleDesiredProperty(DeviceName, new("""{ "MyValue": null }"""));

        values.Should().BeEmpty();
    }

    [Fact]
    public void Returns_no_values_for_a_device_without_desired_properties()
    {
        var (handler, _) = CreateHandler("MyValue");

        var values = handler.HandleDesiredProperty("OtherDevice", new("""{ "MyValue": 23 }"""));

        values.Should().BeEmpty();
    }

    private static (DesiredPropertyHandler Handler, Dictionary<string, string> Channels) CreateHandler(params string[] valueNames)
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = DeviceName, };
        Node otherDeviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = "OtherDevice", };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        var desiredNodeId = Guid.NewGuid();
        var valueNodes = valueNames.Select(name =>
        {
            var id = Guid.NewGuid();
            return new Node
            {
                Id = id,
                ParentId = desiredNodeId,
                DesignId = AzureIotHubNodeDesignId.JsonStructure,
                Name = name,
                AffectedChannels = [id.ToString(),],
                ValueType = typeof(int),
            };
        }).ToArray();
        Node desiredNode = new()
        {
            Id = desiredNodeId,
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Desired,
            TransferredChannels = [.. valueNodes.Select(n => n.Id.ToString()),],
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, otherDeviceNode, propertiesNode, desiredNode, .. valueNodes,], };

        return (new(communication, TimeProvider.System, new()), valueNodes.ToDictionary(n => n.Name, n => n.Id.ToString()));
    }
}
