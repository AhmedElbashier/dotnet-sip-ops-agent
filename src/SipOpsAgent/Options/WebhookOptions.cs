namespace SipOpsAgent.Options;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhook";

    /// <summary>Destination for the post_webhook tool. Leave empty to run the tool in dry-run mode.</summary>
    public string? Url { get; set; }

    /// <summary>
    /// When set, the agent signs each delivery as hex HMAC-SHA256 of "{timestamp}.{body}"
    /// and sends it in X-Agent-Signature, with X-Agent-Timestamp as the Unix seconds.
    /// </summary>
    public string? HmacSecret { get; set; }

    public int TimeoutSeconds { get; set; } = 10;
}
