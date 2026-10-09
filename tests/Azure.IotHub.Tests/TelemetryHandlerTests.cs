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
        List<(Guid DeviceId, Func<Message>)> values = [];
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

        var (deviceId, _) = values.Should().ContainSingle().Which;
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
        var (handler, channel, createMessages) = CreateHandler();

        await handler.Process([new() { Channel = channel, Value = value, }], TestContext.Current.CancellationToken);

        ReadBody(createMessages.Should().ContainSingle().Which).Should().Be($"{{\"MyValue\":\"{expected}\"}}");
    }

    [Fact]
    public async Task Hands_out_a_new_message_with_the_same_content_for_every_send_attempt_Async()
    {
        var (handler, channel, createMessages) = CreateHandler();

        await handler.Process([new() { Channel = channel, Value = 23, Timestamp = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), }], TestContext.Current.CancellationToken);

        var createMessage = createMessages.Should().ContainSingle().Which;
        using var first = createMessage();
        using var second = createMessage();
        second.Should().NotBeSameAs(first);
        Encoding.UTF8.GetString(second.GetBytes()).Should().Be(Encoding.UTF8.GetString(first.GetBytes())).And.Be("{\"MyValue\":23}");
        second.Properties.Should().BeEquivalentTo(first.Properties).And.ContainKey("timestamp");
    }

    private static string ReadBody(Func<Message> createMessage)
    {
        using var message = createMessage();

        return Encoding.UTF8.GetString(message.GetBytes());
    }

    private static (TelemetryHandler Handler, string Channel, List<Func<Message>> CreateMessages) CreateHandler()
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
        List<Func<Message>> createMessages = [];
        TelemetryHandler handler = new(new() { Nodes = [deviceNode, telemetryNode, telemetryValueNode,], }, (_, createMessage, _) =>
        {
            createMessages.Add(createMessage);
            return Task.CompletedTask;
        });

        return (handler, telemetryValueNodeId.ToString(), createMessages);
    }
}
