using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure.Messaging.EventGrid;
using Microsoft.Extensions.Time.Testing;
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
        result.EventTime.Should().Be(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }
}

public class EventGridEventHandler_Process
{
    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    public async Task Sends_timestamp_as_utc_event_time_Async(DateTimeKind kind)
    {
        var (handler, channel, events) = CreateHandler();

        await handler.Process([new() { Channel = channel, Value = 1, Timestamp = new(2024, 1, 1, 12, 0, 0, kind), }], TestContext.Current.CancellationToken);

        events.Should().ContainSingle().Which.EventTime.Should().BeExactly(new DateTimeOffset(2024, 1, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Sends_current_time_as_event_time_if_values_have_no_timestamp_Async()
    {
        FakeTimeProvider timeProvider = new(new DateTimeOffset(2025, 6, 1, 8, 0, 0, TimeSpan.Zero));
        var (handler, channel, events) = CreateHandler(timeProvider);

        await handler.Process([new() { Channel = channel, Value = 1, Timestamp = DateTime.MinValue, }], TestContext.Current.CancellationToken);

        events.Should().ContainSingle().Which.EventTime.Should().BeExactly(timeProvider.Start);
    }

    [Theory]
    [InlineData(double.NaN, "NaN")]
    [InlineData(double.PositiveInfinity, "Infinity")]
    [InlineData(float.NegativeInfinity, "-Infinity")]
    public async Task Writes_non_finite_numbers_as_strings_Async(object value, string expected)
    {
        var (handler, channel, events) = CreateHandler();

        await handler.Process([new() { Channel = channel, Value = value, }], TestContext.Current.CancellationToken);

        events.Should().ContainSingle().Which.Data.ToObjectFromJson<string>().Should().Be($"{{\"value\":\"{expected}\"}}");
    }

    private static (EventGridEventHandler Handler, string Channel, List<EventGridEvent> Events) CreateHandler(TimeProvider? timeProvider = null)
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
                { "EventType", new() { Value = "testType" } },
                { "DataVersion", new() { Value = "1.0" } },
            },
        };
        Node valueNode = new()
        {
            Name = "value",
            DesignId = AzureEventGridTopicDataPortDesignId.JsonStructure,
            ParentId = eventId,
            Id = Guid.NewGuid(),
            AffectedChannels = [channel,],
        };
        List<EventGridEvent> events = [];
        EventGridEventHandler handler = new(new() { Nodes = [eventNode, valueNode,], }, (e, _) =>
        {
            events.AddRange(e);
            return Task.CompletedTask;
        }, timeProvider);

        return (handler, channel, events);
    }
}
