using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.EventGrid;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

internal sealed class EventGridEventHandler
{
    private readonly Dictionary<string, INode> _channelTransferredNodes;
    private readonly Dictionary<string, INode[]> _channelJsonNodeRoutes;
    private readonly Func<IEnumerable<EventGridEvent>, CancellationToken, Task> _sendGridEvents;

    internal EventGridEventHandler(
        AzureEventGridTopicDataPortCommunication communication,
        Func<IEnumerable<EventGridEvent>, CancellationToken, Task> sendGridEvents)
    {
        var linkedNodes = LinkedNodeFactory.Create(communication.Nodes);
        var eventSubjectNodes = linkedNodes.Where(n => n.DesignId == AzureEventGridTopicDataPortDesignId.EventGridEvent).SelectMany(n => n.GetAllChildren()).ToArray();
        (_channelJsonNodeRoutes, _channelTransferredNodes, _) = InitializeUnspecificTree.InitializeOutgoing(eventSubjectNodes);
        _sendGridEvents = sendGridEvents;
    }

    internal async Task Process(IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
    {
        var eventValues = values
            .Where(v => _channelTransferredNodes.ContainsKey(v.Channel))
            .GroupBy(v => _channelTransferredNodes[v.Channel]);

        await _sendGridEvents(GetEvents(eventValues), cancellationToken);
    }

    private IEnumerable<EventGridEvent> GetEvents(IEnumerable<IGrouping<INode, ExternalValue>> eventValues)
    {
        foreach (var value in eventValues)
            yield return GetEvent(value);
    }

    private EventGridEvent GetEvent(IGrouping<INode, ExternalValue> values)
    {
        var (data, _) = NodeValueFactory.CreateJsonStructure(_channelJsonNodeRoutes, values);

        return new(values.Key.Name, values.Key.GetProperty(AzureEventGridTopicProperty.EventType).Value.ToString(), values.Key.GetProperty(AzureEventGridTopicProperty.DataVersion).Value.ToString(), JsonSerializer.Serialize(data))
        {
            EventTime = values.Max(v => v.Timestamp)
        };
    }
}
