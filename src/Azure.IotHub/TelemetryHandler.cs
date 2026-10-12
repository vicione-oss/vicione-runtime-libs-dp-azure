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
    private readonly Func<Guid, Func<Message>, CancellationToken, Task> _sendTelemetry;
    private readonly Dictionary<Guid, Guid> _transferredDeviceNodes = [];

    internal TelemetryHandler(AzureIotHubDataPortCommunication communication, Func<Guid, Func<Message>, CancellationToken, Task> sendTelemetry)
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
            JsonSerializer.SerializeToUtf8Bytes(data, JsonSetup.OutgoingOptions),
            new Dictionary<string, string>
            {
                { "type", JsonSerializer.Serialize(meta) },
                { "timestamp", values.Max(v => v.Timestamp).ToString("O") },
                { "validity", values.Min(v => v.Validity).ToString(CultureInfo.InvariantCulture) },
            });

    protected override DeviceData HandleSingleNode(INode node, ExternalValue value)
        => throw new NotImplementedException();

    protected override async Task SendValue(DeviceData value, CancellationToken cancellationToken)
        => await _sendTelemetry(value.DeviceId, value.CreateMessage, cancellationToken).ConfigureAwait(false);

    internal async Task Process(IReadOnlyCollection<ExternalValue> values, CancellationToken cancellationToken)
        => await Send(values.Where(v => ChannelTransferredNodes.ContainsKey(v.Channel)), cancellationToken).ConfigureAwait(false);

    // A message body can be read only once, so every send attempt needs its own message.
    internal sealed record class DeviceData(Guid DeviceId, byte[] Body, IReadOnlyDictionary<string, string> Properties)
    {
        internal Message CreateMessage()
        {
            Message message = new(Body) { ContentType = MediaTypeNames.Application.Json, ContentEncoding = Encoding.UTF8.WebName, };

            foreach (var (key, value) in Properties)
                message.Properties.Add(key, value);

            return message;
        }
    }
}
