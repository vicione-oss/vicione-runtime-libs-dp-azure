using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ViciOne.ManagedEngine.ExternalCommunication;
using Message = Microsoft.Azure.Devices.Client.Message;

namespace ViciOne.Suite.DataPort;

internal sealed class TelemetryHandler : UnspecificTreeHandlerOutgoing<TelemetryHandler.DeviceData>
{
    private readonly AzureIotHubDataPortCommunication _communication;
    private readonly Func<Guid, Message, CancellationToken, Task> _sendTelemetry;
    private readonly Dictionary<Guid, Guid> _transferredDeviceNodes = [];

    internal TelemetryHandler(AzureIotHubDataPortCommunication communication, Func<Guid, Message, CancellationToken, Task> sendTelemetry)
    {
        _communication = communication;
        _sendTelemetry = sendTelemetry;

        Initialize();
    }

    public override void Initialize()
    {
        var linkedNodes = LinkedNodeFactory.Create(_communication.Nodes);
        Initialize(
            [.. linkedNodes.SelectMany(n => n.Children.Where(c => c.DesignId == AzureIotHubNodeDesignId.Telemetry)).SelectMany(n => n.GetAllChildren())],
            handleTransferNode: node =>
            {
                var transferredNode = (LinkedNode)node;
                var deviceNodeId = transferredNode.Parent?.Id ?? throw new InvalidOperationException("Device node not found.");

                _transferredDeviceNodes.Add(transferredNode.Id, deviceNodeId);
            });
    }

    protected override DeviceData HandleGroupNode(INode node, JsonObject data, JsonObject meta, IReadOnlyCollection<ExternalValue> values)
        => new(
            _transferredDeviceNodes[node.Id],
#pragma warning disable CA2000 // Objekte verwerfen, bevor Bereich verloren geht. Wird nach dem Senden verworfen.
            new(JsonSerializer.SerializeToUtf8Bytes(data))
            {
                ContentType = MediaTypeNames.Application.Json,
                ContentEncoding = Encoding.UTF8.WebName,
                Properties =
                {
                    { "type", JsonSerializer.Serialize(meta) },
                    { "timestamp", values.Max(v => v.Timestamp).ToString("O") },
                    { "validity", values.Min(v => v.Validity).ToString(CultureInfo.InvariantCulture) },
                },
            });
#pragma warning restore CA2000 // Objekte verwerfen, bevor Bereich verloren geht

    protected override DeviceData HandleSingleNode(INode node, ExternalValue value)
        => throw new NotImplementedException();

    protected override async Task SendValue(DeviceData value, CancellationToken cancellationToken)
        => await _sendTelemetry(value.DeviceId, value.Data, cancellationToken).ConfigureAwait(false);

    internal async Task Process(IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
        => await Send(values.Where(v => ChannelTransferredNodes.ContainsKey(v.Channel)), cancellationToken).ConfigureAwait(false);

    internal sealed record class DeviceData(Guid DeviceId, Message Data) : IDisposable
    {
        public void Dispose()
            => Data.Dispose();
    }
}
