using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure.Messaging.EventGrid;
using ViciOne.ManagedEngine.ExternalCommunication;
using Xunit;

namespace ViciOne.Suite.DataPort;

public class EventGridEventHandler_
{
    [Fact]
    public async Task Acceptance()
    {
        var channel = Guid.NewGuid().ToString();
        var eventId = Guid.NewGuid();
        Node eventNode = new()
        {
            Name = "Test",
            Id = eventId,
            DesignId = AzureEventGridTopicDataPortDesignId.EventGridEvent,
            TransferredChannels = [channel,],
            Properties = new()
            {
                { "EventType", new() { Value = "testType"} },
                { "DataVersion", new() { Value = "1.0" } },
            }
        };
        Node jsonStructure = new()
        {
            DesignId = AzureEventGridTopicDataPortDesignId.JsonStructure,
            ParentId = eventId,
            Id = Guid.Empty,
            AffectedChannels = [channel],
        };
        List<EventGridEvent> events = [];
        var communication = new AzureEventGridTopicDataPortCommunication() { Nodes = [jsonStructure, eventNode] };
        var handler = new EventGridEventHandler(communication, (l, _) =>
        {
            events.AddRange(l);
            return Task.CompletedTask;
        });
        List<ExternalValue> values =
        [
            new()
            {
                Channel = channel,
                Value = "test Text",
                Timestamp = new DateTime(2024, 1, 1),
            }
        ];

        await handler.Process(values, TestContext.Current.CancellationToken);

        var result = events.Should().ContainSingle().Which;
        result.Subject.Should().Be("Test");
        result.DataVersion.Should().Be("1.0");
        result.EventType.Should().Be("testType");
        result.EventTime.Should().Be(new DateTime(2024, 1, 1));
    }
}
