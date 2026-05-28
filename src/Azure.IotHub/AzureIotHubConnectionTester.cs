using System;
using System.Threading.Tasks;
using Azure;
using Microsoft.Azure.Devices;
using Microsoft.Azure.Devices.Client;

namespace ViciOne.Suite.DataPort;

public sealed class AzureIotHubConnectionTester : IDataPortConnectionTester<AzureIotHubDataPortCommunication>
{
    public static async Task TestConnectionAsync(AzureIotHubDataPortCommunication communication)
    {
        try
        {
            var registrySharedAccessSignature = new SharedAccessSignatureBuilder()
            {
                Target = communication.Hostname,
                KeyName = communication.SharedAccessSignatureKeyName,
                Key = communication.SharedAccessSignatureKey,
            }.ToSignature();

            using var registryManager = RegistryManager.Create(communication.Hostname, new AzureSasCredential(registrySharedAccessSignature));

            var queryRequest = registryManager.CreateQuery("select * from devices");
            _ = await queryRequest.GetNextAsJsonAsync();
        }
        catch (Exception ex)
        {
            throw new DataPortConnectionFailedException("Failed to connect to Azure.IotHub.", ex);
        }
    }
}
