using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging;
using ViciOne.ManagedEngine.ExternalCommunication;
using ViciOne.Suite.DataPort;

namespace Benchmarks;

[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.", Justification = "Must be public for BenchmarkDotNet")]
public class AzureIotHubDataPortOutgoingBenchmark : IDisposable
{
    private AzureIotHubDataPortOutgoing? _communication;
    private bool _disposedValue;
    private readonly LoggerFactory _loggerFactory = new();
    private readonly List<List<ExternalValue>> _data = [];

    [GlobalSetup]
    public void Setup()
    {
        List<Node> nodes = [];
        List<Node> valueNodes = [];
        Random random = new(100);

        for (var i = 0; i < CountPerBatch; i++)
        {
            var deviceNodeId = Guid.NewGuid();
            Node deviceNode = new()
            {
                Id = deviceNodeId,
                Name = $"test{i}",
                DesignId = AzureIotHubNodeDesignId.Device,
            };
            var telemetryNodeId = Guid.NewGuid();
            var nodeId = Guid.NewGuid();
            Node telemetryNode = new()
            {
                Id = telemetryNodeId,
                ParentId = deviceNodeId,
                DesignId = AzureIotHubNodeDesignId.Telemetry,
                Name = "Telemetry",
                TransferredChannels = { nodeId.ToString(), },
            };
            Node node = new()
            {
                Id = nodeId,
                ParentId = telemetryNodeId,
                DesignId = AzureIotHubNodeDesignId.JsonStructure,
                Name = "value",
                AffectedChannels = { nodeId.ToString(), },
                ValueType = typeof(int),
            };
            nodes.Add(deviceNode);
            nodes.Add(telemetryNode);
            nodes.Add(node);
            valueNodes.Add(node);

            var propertiesNodeId = Guid.NewGuid();
            Node propertiesNode = new()
            {
                Id = propertiesNodeId,
                ParentId = deviceNodeId,
                DesignId = AzureIotHubNodeDesignId.Properties,
                Name = "Properties",
            };
            var reportedNodeId = Guid.NewGuid();
            var reportedValueNodeId = Guid.NewGuid();
            Node reportedNode = new()
            {
                Id = reportedNodeId,
                ParentId = propertiesNodeId,
                DesignId = AzureIotHubNodeDesignId.Reported,
                Name = "Reported",
                TransferredChannels = { reportedValueNodeId.ToString(), },
            };
            Node reportedValueNode = new()
            {
                Id = reportedValueNodeId,
                ParentId = reportedNodeId,
                DesignId = AzureIotHubNodeDesignId.JsonStructure,
                Name = "value",
                AffectedChannels = { reportedValueNodeId.ToString(), },
                ValueType = typeof(int),
            };
            nodes.Add(propertiesNode);
            nodes.Add(reportedNode);
            nodes.Add(reportedValueNode);
            valueNodes.Add(reportedValueNode);
        }

        for (var i = 0; i < Batches; i++)
        {
#pragma warning disable CA5394 // Keine unsichere Zufälligkeitsstufe verwenden
            _data.Add([.. valueNodes.Select(n => new ExternalValue
            {
                Channel = n.Id.ToString(),
                Value = random.Next(),
            })]);
#pragma warning restore CA5394 // Keine unsichere Zufälligkeitsstufe verwenden
        }

        AzureIotHubDataPortCommunication communication = new()
        {
            Hostname = "",
            Nodes = nodes,
            SharedAccessSignatureKeyName = "",
            SharedAccessSignatureKey = "",
        };
        _communication = new(communication, _loggerFactory.CreateLogger<AzureIotHubDataPortOutgoing>());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _communication?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _data.Clear();
    }

    [Params(3)]
    public int CountPerBatch { get; set; }
    [Params(2)]
    public int Batches { get; set; }

    [Benchmark]
    [SuppressMessage("Naming", "CA1707:Bezeichner dürfen keine Unterstriche enthalten")]
    public async Task AzureIotHub_External_Outgoing_Async()
    {
        await _communication!.ConnectAsync(CancellationToken.None);
        foreach (var partition in _data)
            await _communication.SendAsync(0, partition, CancellationToken.None);
        await _communication.DisconnectAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposedValue)
        {
            if (disposing)
            {
                _communication?.DisposeAsync().AsTask().GetAwaiter().GetResult();
                _loggerFactory.Dispose();
            }
            _disposedValue = true;
        }
    }
}
