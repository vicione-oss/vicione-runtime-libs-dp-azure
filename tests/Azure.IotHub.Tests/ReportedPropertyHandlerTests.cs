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
