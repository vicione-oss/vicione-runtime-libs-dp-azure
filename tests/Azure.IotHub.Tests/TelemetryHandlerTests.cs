using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.Devices.Client;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class TelemetryHandler_
{
    [Fact]
    public async Task Acceptance()
    {
        Node deviceNode = new() { Id = Guid.NewGuid(), ParentId = null, DesignId = AzureIotHubNodeDesignId.Device, };
        var telemetryValueNodeId = Guid.NewGuid();
        Node telemetryNode = new()
        {
            Id = Guid.NewGuid(),
            ParentId = deviceNode.Id,
            DesignId = AzureIotHubNodeDesignId.Telemetry,
            TransferredChannels = [telemetryValueNodeId.ToString(),],
        };
        Node telemetryValueNode = new()
        {
            Id = telemetryValueNodeId,
            ParentId = telemetryNode.Id,
            DesignId = AzureIotHubNodeDesignId.JsonStructure,
            Name = "MyValue",
            AffectedChannels = [telemetryValueNodeId.ToString(),],
        };
        AzureIotHubDataPortCommunication communication = new() { Nodes = [deviceNode, telemetryNode, telemetryValueNode], };
        List<(Guid DeviceId, Message)> values = [];
        TelemetryHandler telemetryHandler = new(communication, (d, m, _) =>
        {
            values.Add((d, m));
            return Task.CompletedTask;
        });

        await telemetryHandler.Process(
        [
            new()
            {
                Channel = telemetryValueNode.Id.ToString(),
                Value = 23,
            },
        ], TestContext.Current.CancellationToken);

        var (deviceId, message) = values.Should().ContainSingle().Which;
        deviceId.Should().Be(deviceNode.Id);
    }
}
