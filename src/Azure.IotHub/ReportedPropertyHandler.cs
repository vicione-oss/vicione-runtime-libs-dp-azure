using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;

namespace ViciOne.Suite.DataPort;

internal sealed class ReportedPropertyHandler : UnspecificTreeHandlerOutgoing<ReportedPropertyHandler.DeviceData>
{
    // IoT Hub rejects a reported patch with error 400004 when an integer lies outside this range.
    internal const long TwinIntegerMinimum = -4_503_599_627_370_496;
    internal const long TwinIntegerMaximum = 4_503_599_627_370_495;

    private readonly AzureIotHubDataPortCommunication _communication;
    private readonly Func<Guid, TwinCollection, CancellationToken, Task> _updateReportedProperties;
    private readonly ILogger _logger;
    private readonly Dictionary<Guid, Guid> _transferredDeviceNodes = [];

    internal ReportedPropertyHandler(AzureIotHubDataPortCommunication communication, Func<Guid, TwinCollection, CancellationToken, Task> updateReportedProperties, ILogger logger)
    {
        _communication = communication;
        _updateReportedProperties = updateReportedProperties;
        _logger = logger;

        Initialize();
    }

    public override void Initialize()
    {
        var linkedNodes = LinkedNodeFactory.Create(_communication.Nodes);
        var nodes = linkedNodes.SelectMany(ln => ln.GetAllChildren(new()
        {
            { 1, n => n.DesignId == AzureIotHubNodeDesignId.Properties },
            { 2, n => n.DesignId == AzureIotHubNodeDesignId.Reported },
        })).ToArray();
        Initialize(
            nodes,
            handleTransferNode: node =>
            {
                var transferredNode = (LinkedNode)node;
                var deviceNodeId = transferredNode.Parent?.Parent?.Id ?? throw new InvalidOperationException("Device node not found.");

                _transferredDeviceNodes.Add(transferredNode.Id, deviceNodeId);
            });
    }

    protected override DeviceData HandleGroupNode(INode node, JsonObject data, JsonObject meta, IReadOnlyCollection<ExternalValue> values)
        => new(_transferredDeviceNodes[node.Id], new(JsonSerializer.Serialize(data, JsonSetup.OutgoingOptions)));

    protected override DeviceData HandleSingleNode(INode node, ExternalValue value)
        => throw new NotImplementedException();

    protected override async Task SendValue(DeviceData value, CancellationToken cancellationToken)
        => await _updateReportedProperties(value.DeviceId, value.Data, cancellationToken).ConfigureAwait(false);

    internal async Task Process(IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
        => await Send(values.Where(v => ChannelTransferredNodes.ContainsKey(v.Channel) && IsAcceptedByTwin(v)), cancellationToken);

    private bool IsAcceptedByTwin(ExternalValue value)
    {
        if (IsWithinTwinIntegerRange(value.Value))
            return true;

        _logger.ReportedIntegerOutOfRange(string.Join('.', ChannelJsonNodeRoutes[value.Channel].Select(n => n.Name)), value.Value);
        return false;
    }

    private static bool IsWithinTwinIntegerRange(object? value)
        => value switch
        {
            long l => l is >= TwinIntegerMinimum and <= TwinIntegerMaximum,
            ulong u => u <= TwinIntegerMaximum,
            _ => true,
        };

    internal sealed record class DeviceData(Guid DeviceId, TwinCollection Data);
}
