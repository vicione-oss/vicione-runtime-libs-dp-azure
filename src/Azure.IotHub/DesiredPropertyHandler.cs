using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Azure.Devices.Shared;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

internal sealed class DesiredPropertyHandler
{
    private readonly Dictionary<Guid, IReadOnlyCollection<INode[]>> _transferredNodeJsonNodes;
    private readonly Dictionary<string, IReadOnlyCollection<INode>> _deviceTransferredNodes;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    internal DesiredPropertyHandler(AzureIotHubDataPortCommunication communication, TimeProvider? timeProvider, JsonSerializerOptions jsonSerializerOptions)
    {
        var linkedNodes = LinkedNodeFactory.Create(communication.Nodes);
        var nodes = linkedNodes.SelectMany(ln => ln.GetAllChildren(new()
        {
            { 1, n => n.DesignId == AzureIotHubNodeDesignId.Properties },
            { 2, n => n.DesignId == AzureIotHubNodeDesignId.Desired },
        })).ToArray();
        (_transferredNodeJsonNodes, _deviceTransferredNodes) = InitializeIncoming(nodes, n => ((LinkedNode)n).Parent?.Parent?.Name ?? throw new InvalidOperationException("Device node not found."));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _jsonSerializerOptions = jsonSerializerOptions;
    }

    /*
     * TwinCollection JSON
     * -------------------
     * {
     *   "$metadata": {
     *     "value": {
     *       "$lastUpdated": "2016-03-30T16:24:48.789Z"
     *     },
     *     "custom": {
     *       "temp": {
     *          "$lastUpdated": "2024-01-01"
     *       }
     *     }
     *   },
     *   "value": 10,
     *   "custom": {
     *      "temp": 100
     *   }
     * }
     */
    internal IReadOnlyCollection<ExternalValue> HandleDesiredProperty(string deviceId, TwinCollection twinCollection)
    {
        var transferredNodes = _deviceTransferredNodes[deviceId];
        List<ExternalValue> externalValues = [];
        var jsonNode = JsonNode.Parse(twinCollection.ToJson());

        foreach (var transferredNode in transferredNodes)
        {
            if (_transferredNodeJsonNodes.TryGetValue(transferredNode.Id, out var routes))
            {
                foreach (var route in routes)
                {
                    var valueNode = NodeValueFactory.GetValueJsonNode(route, jsonNode);
                    var timestamp = GetValueJsonNode(route, twinCollection);
                    var dataPortNode = route[^1];
                    var valueType = dataPortNode.ValueType ?? throw new InvalidOperationException("Type of value not found.");
                    var value = valueNode.Deserialize(valueType, _jsonSerializerOptions);
                    var channel = dataPortNode.AffectedChannels.First(transferredNode.TransferredChannels.Contains);

                    externalValues.Add(new()
                    {
                        Channel = channel,
                        Value = value,
                        Timestamp = timestamp ?? _timeProvider.GetUtcNow().DateTime,
                        Validity = 1,
                    });
                }
            }
        }

        return externalValues;
    }

    private static DateTime? GetValueJsonNode(Span<INode> structure, object? collection, int index = 0)
    {
        if (!((structure.Length - index) > 0) || collection is null)
        {
            return collection switch
            {
                TwinCollection c => c.GetMetadata().LastUpdated,
                TwinCollectionValue c => c.GetMetadata().LastUpdated,
                _ => null,
            };
        }
        else
        {
            var part = structure[index];

            if (collection is not TwinCollection c)
                return null;

            object token = c[part.Name];

            return GetValueJsonNode(structure, token, index + 1);
        }
    }

    private static (Dictionary<Guid, IReadOnlyCollection<INode[]>> TransferredNodeJsonNodes, Dictionary<string, IReadOnlyCollection<INode>> AddressNodes) InitializeIncoming(IReadOnlyCollection<INode> nodes, Func<INode, string> getDeviceId)
    {
        Dictionary<string, List<INode>> deviceTransferredNodes = [];
        List<INode> groupNodes = [];

        var nodesParentCache = nodes.CreateNodeParentsCache();

        foreach (var node in nodes.Where(n => n.TransferredChannels.Count != 0))
        {
            var deviceId = getDeviceId(node);

            if (deviceTransferredNodes.TryGetValue(deviceId, out var list))
                list.Add(node);
            else
                deviceTransferredNodes.Add(deviceId, [node,]);

            if (node.AffectedChannels.Count == 0)
                groupNodes.Add(node);
        }

        return (groupNodes.ToDictionary(n => n.Id, n => n.GetSuccessorRoutes(nodesParentCache)), deviceTransferredNodes.ToDictionary(_ => _.Key, _ => (IReadOnlyCollection<INode>)_.Value));
    }
}
