using System;
using System.Collections.Generic;
using System.Linq;
using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
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
        desiredPropertyHandler = new(communication, timeProvider ?? TimeProvider.System, new(), NullLogger.Instance);
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
        desiredPropertyHandler = new(communication, timeProvider, new(), NullLogger.Instance);
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

    [Fact]
    public void Skips_and_logs_a_value_that_does_not_match_its_type()
    {
        FakeLogger<DesiredPropertyHandler> logger = new();
        var (handler, channels) = CreateHandler(logger, "MyValue", "OtherValue");

        var values = handler.HandleDesiredProperty(DeviceName, new("""{ "MyValue": "abc", "OtherValue": 555 }"""));

        values.Should().ContainSingle().Which.Channel.Should().Be(channels["OtherValue"]);
        logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.LatestRecord.Message.Should().Match("*'MyValue'*'MyDevice'*");
    }

    [Theory]
    [InlineData("""{ "MyGroup": 5, "OtherValue": 555 }""")]
    [InlineData("""{ "MyGroup": [1], "OtherValue": 555 }""")]
    public void Skips_and_logs_a_value_or_array_in_place_of_a_structure(string patch)
    {
        FakeLogger<DesiredPropertyHandler> logger = new();
        var (handler, channels) = CreateHandler(logger, "MyGroup.MyValue", "OtherValue");

        var values = handler.HandleDesiredProperty(DeviceName, new(patch));

        values.Should().ContainSingle().Which.Channel.Should().Be(channels["OtherValue"]);
        logger.LatestRecord.Message.Should().Match("*'MyGroup.MyValue'*'MyDevice'*");
    }

    private static (DesiredPropertyHandler Handler, Dictionary<string, string> Channels) CreateHandler(params string[] valuePaths)
        => CreateHandler(NullLogger.Instance, valuePaths);

    private static (DesiredPropertyHandler Handler, Dictionary<string, string> Channels) CreateHandler(ILogger logger, params string[] valuePaths)
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = DeviceName, };
        Node otherDeviceNode = new() { Id = Guid.NewGuid(), DesignId = AzureIotHubNodeDesignId.Device, Name = "OtherDevice", };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        var desiredNodeId = Guid.NewGuid();
        Dictionary<string, Node> groupNodes = [];
        Dictionary<string, Node> valueNodes = [];

        foreach (var path in valuePaths)
        {
            var parts = path.Split('.');
            var parentId = desiredNodeId;

            foreach (var group in parts[..^1])
            {
                if (!groupNodes.TryGetValue(group, out var groupNode))
                {
                    groupNode = new() { Id = Guid.NewGuid(), ParentId = parentId, DesignId = AzureIotHubNodeDesignId.JsonStructure, Name = group, };
                    groupNodes.Add(group, groupNode);
                }

                parentId = groupNode.Id;
            }

            var id = Guid.NewGuid();
            valueNodes.Add(path, new() { Id = id, ParentId = parentId, DesignId = AzureIotHubNodeDesignId.JsonStructure, Name = parts[^1], AffectedChannels = [id.ToString(),], ValueType = typeof(int), });
        }

        Node desiredNode = new()
        {
            Id = desiredNodeId,
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Desired,
            TransferredChannels = [.. valueNodes.Values.Select(n => n.Id.ToString()),],
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, otherDeviceNode, propertiesNode, desiredNode, .. groupNodes.Values, .. valueNodes.Values,], };

        return (new(communication, TimeProvider.System, new(), logger), valueNodes.ToDictionary(n => n.Key, n => n.Value.Id.ToString()));
    }
}
