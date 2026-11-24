namespace SipOpsAgent.Options;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public int MaxTurns { get; set; } = 6;

    /// <summary>Expose /api/dev/webhook-sink so demos and CI can inspect delivered alerts.</summary>
    public bool EnableDevSink { get; set; }
}
