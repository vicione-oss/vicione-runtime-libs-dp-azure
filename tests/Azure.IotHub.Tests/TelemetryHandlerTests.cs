using System;
using System.Collections.Generic;
using System.Text;
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

public class TelemetryHandler_Process
{
    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(float.NegativeInfinity, "-Infinity")]
    public async Task Writes_non_finite_numbers_as_strings_Async(object value, string expected)
    {
        var (handler, channel, bodies) = CreateHandler();

        await handler.Process([new() { Channel = channel, Value = value, }], TestContext.Current.CancellationToken);

        bodies.Should().ContainSingle().Which.Should().Be($"{{\"MyValue\":\"{expected}\"}}");
    }

    private static (TelemetryHandler Handler, string Channel, List<string> Bodies) CreateHandler()
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
        List<string> bodies = [];
        TelemetryHandler handler = new(new() { Nodes = [deviceNode, telemetryNode, telemetryValueNode,], }, (_, m, _) =>
        {
            bodies.Add(Encoding.UTF8.GetString(m.GetBytes()));
            return Task.CompletedTask;
        });

        return (handler, telemetryValueNodeId.ToString(), bodies);
    }
}
