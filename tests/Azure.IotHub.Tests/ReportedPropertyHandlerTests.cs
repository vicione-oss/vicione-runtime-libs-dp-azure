using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
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
        }, NullLogger.Instance);

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

    [Theory]
    [InlineData(ReportedPropertyHandler.TwinIntegerMinimum - 1)]
    [InlineData(ReportedPropertyHandler.TwinIntegerMaximum + 1)]
    [InlineData(long.MinValue)]
    [InlineData(ulong.MaxValue)]
    public async Task Skips_and_logs_an_integer_outside_the_twin_range_Async(object value)
    {
        FakeLogger<ReportedPropertyHandler> logger = new();
        var (handler, channel, collections) = CreateHandler(logger);

        await handler.Process([new() { Channel = channel, Value = value, }], TestContext.Current.CancellationToken);

        collections.Should().BeEmpty();
        logger.LatestRecord.Level.Should().Be(LogLevel.Warning);
        logger.LatestRecord.Message.Should().Match($"*'MyValue'*{value}*");
    }

    [Theory]
    [InlineData(ReportedPropertyHandler.TwinIntegerMinimum)]
    [InlineData(ReportedPropertyHandler.TwinIntegerMaximum)]
    [InlineData((ulong)ReportedPropertyHandler.TwinIntegerMaximum)]
    public async Task Sends_an_integer_at_the_edge_of_the_twin_range_Async(object value)
    {
        var (handler, channel, collections) = CreateHandler();

        await handler.Process([new() { Channel = channel, Value = value, }], TestContext.Current.CancellationToken);

        collections.Should().ContainSingle().Which.ToJson().Should().Be($"{{\"MyValue\":{value}}}");
    }

    private static (ReportedPropertyHandler Handler, string Channel, List<TwinCollection> Collections) CreateHandler(ILogger? logger = null)
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
        }, logger ?? NullLogger.Instance);

        return (handler, reportedValueNodeId.ToString(), collections);
    }
}
