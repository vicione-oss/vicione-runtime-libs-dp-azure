using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.Devices.Shared;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class ReportedPropertyHandler_
{
    [Fact]
    public async Task Acceptance()
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), ParentId = null, DesignId = AzureIotHubNodeDesignId.Device, };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        var reportedValueNodeId = Guid.NewGuid();
        Node reportedPropertiesNode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Reported,
            TransferredChannels = [reportedValueNodeId.ToString(),],
        };
        Node reportedValueNode = new()
        {
            Id = reportedValueNodeId,
            ParentId = reportedPropertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.JsonStructure,
            Name = "MyValue",
            AffectedChannels = [reportedValueNodeId.ToString(),],
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, propertiesNode, reportedPropertiesNode, reportedValueNode,], };
        List<(Guid DeviceId, TwinCollection)> values = [];
        ReportedPropertyHandler reportedPropertyHandler = new(communication, (d, c, _) =>
        {
            values.Add((d, c));
            return Task.CompletedTask;
        });

        await reportedPropertyHandler.Process(
        [
            new()
            {
                Channel = reportedValueNodeId.ToString(),
                Value = 23,
                Timestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        ], TestContext.Current.CancellationToken);

        var (deviceId, collection) = values.Should().ContainSingle().Which;
        deviceId.Should().Be(deviceNode.Id);
        collection.ToJson().Should().Be("{\"MyValue\":23}");
    }
}

public class ReportedPropertyHandler_Process
{
    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(float.NegativeInfinity, "-Infinity")]
    public async Task Writes_non_finite_numbers_as_strings_Async(object value, string expected)
    {
        var (handler, channel, collections) = CreateHandler();

        await handler.Process([new() { Channel = channel, Value = value, }], TestContext.Current.CancellationToken);

        collections.Should().ContainSingle().Which.ToJson().Should().Be($"{{\"MyValue\":\"{expected}\"}}");
    }

    private static (ReportedPropertyHandler Handler, string Channel, List<TwinCollection> Collections) CreateHandler()
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), ParentId = null, DesignId = AzureIotHubNodeDesignId.Device, };
        Node propertiesNode = new() { Id = Guid.NewGuid(), ParentId = deviceNode.Id, DesignId = AzureIotHubNodeDesignId.Properties, };
        var reportedValueNodeId = Guid.NewGuid();
        Node reportedPropertiesNode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = propertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.Reported,
            TransferredChannels = [reportedValueNodeId.ToString(),],
        };
        Node reportedValueNode = new()
        {
            Id = reportedValueNodeId,
            ParentId = reportedPropertiesNode.Id,
            DesignId = AzureIotHubNodeDesignId.JsonStructure,
            Name = "MyValue",
            AffectedChannels = [reportedValueNodeId.ToString(),],
        };
        List<TwinCollection> collections = [];
        ReportedPropertyHandler handler = new(new() { Nodes = [deviceNode, propertiesNode, reportedPropertiesNode, reportedValueNode,], }, (_, c, _) =>
        {
            collections.Add(c);
            return Task.CompletedTask;
        });

        return (handler, reportedValueNodeId.ToString(), collections);
    }
}
