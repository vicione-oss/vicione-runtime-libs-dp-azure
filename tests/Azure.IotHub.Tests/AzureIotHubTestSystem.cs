using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Xunit;

namespace ViciOne.Suite.DataPort;

[SuppressMessage("Maintainability", "CA1515:Erwägen Sie, öffentliche Typen intern zu machen.")]
public sealed class AzureIotHubTestSystem : IAsyncLifetime
{
    private RegistryManager? _registryManager;

    internal AzureIotHubDataPortCommunication Communication { get; } = new();
    internal string EventHubConnectionString { get; } = string.Empty;

    public ValueTask InitializeAsync()
    {
        Communication.Hostname = "my.azure-devices.net";
        Communication.SharedAccessSignatureKeyName = "iothubowner";
        Communication.SharedAccessSignatureKey = "";

        _registryManager = CreateRegistryManager(Communication);

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _registryManager?.Dispose();
        return ValueTask.CompletedTask;
    }

    private static RegistryManager CreateRegistryManager(AzureIotHubDataPortCommunication communication)
    {
        var registrySharedAccessSignature = new SharedAccessSignatureBuilder()
        {
            Target = communication.Hostname,
            KeyName = communication.SharedAccessSignatureKeyName,
            Key = communication.SharedAccessSignatureKey,
        }.ToSignature();

        return RegistryManager.Create(communication.Hostname, new AzureSasCredential(registrySharedAccessSignature));
    }

    internal async Task<JsonObject> GetDeviceTwin(string device)
    {
        if (_registryManager is null)
            throw new InvalidOperationException("Registry manager is not initialized.");

        var getTwinResult = await _registryManager.GetTwinAsync(device);

        return JsonNode.Parse(getTwinResult.ToJson())?.AsObject() ?? throw new InvalidOperationException("Failed to parse device twin.");
    }

    internal async Task UpdateDesiredProperties(string device, JsonObject value)
    {
        if (_registryManager is null)
            throw new InvalidOperationException("Registry manager is not initialized.");

        var getTwinResult = await _registryManager.GetTwinAsync(device);

        Twin twin = new(device);
        twin.Properties.Desired = new TwinCollection(value.ToJsonString());

        await _registryManager.UpdateTwinAsync(device, twin, getTwinResult.ETag);
    }

    internal async Task DeleteDevice(string device)
    {
        if (_registryManager is null)
            throw new InvalidOperationException("Registry manager is not initialized.");

        await _registryManager.RemoveDeviceAsync(device);
    }
}
