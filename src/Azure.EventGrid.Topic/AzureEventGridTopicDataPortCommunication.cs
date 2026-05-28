using ViciOne.ManagedEngine.Communication;

namespace ViciOne.Suite.DataPort;

[Communication("azureeventgridtopicdataport")]
public sealed record class AzureEventGridTopicDataPortCommunication : DataPortCommunication
{
    public string TopicEndpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public int MaxPendingMessages { get; set; } = 100_000;
    public int BulkSize { get; set; } = 5_000;
}
