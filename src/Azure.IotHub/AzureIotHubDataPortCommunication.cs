using ViciOne.ManagedEngine.Communication;

namespace ViciOne.Suite.DataPort;

[Communication("azureiothubdataport")]
public sealed record class AzureIotHubDataPortCommunication : DataPortCommunication
{
    public string Hostname { get; set; } = string.Empty;
    public string SharedAccessSignatureKey { get; set; } = string.Empty;
    public string SharedAccessSignatureKeyName { get; set; } = string.Empty;
    public int MaxPendingMessages { get; set; } = 100_000;
    public int BulkSize { get; set; } = 5_000;
}
