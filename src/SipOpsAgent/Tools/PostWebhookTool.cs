using System.Text.Json;
using SipOpsAgent.Llm;
using SipOpsAgent.Webhooks;

namespace SipOpsAgent.Tools;

public sealed class PostWebhookTool : IAgentTool
{
    private readonly WebhookPoster _poster;

    public PostWebhookTool(WebhookPoster poster) => _poster = poster;

    public string Name => "post_webhook";

    public string Description =>
        "Post a structured ops alert to the configured webhook URL (HMAC-SHA256 signed when a secret is set). If no URL is configured the delivery is recorded as a dry run. Use this after you have looked up the relevant calls — do not invent call ids.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            title = new { type = "string", description = "Short alert title." },
            severity = new { type = "string", @enum = new[] { "info", "warning", "critical" } },
            summary = new { type = "string", description = "Factual summary from tool results. Do not invent counts." },
            call_ids = new { type = "array", items = new { type = "string" }, description = "Related call ids, if any." }
        },
        required = new[] { "title", "summary" }
    };

    public async Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonArgs.ParseObject(argumentsJson) ?? throw new JsonException("Missing arguments.");
        var title = JsonArgs.GetString(args, "title") ?? "SIP ops alert";
        var severity = JsonArgs.GetString(args, "severity") ?? "info";
        var summary = JsonArgs.GetString(args, "summary") ?? "";
        var callIds = JsonArgs.GetStringArray(args, "call_ids", "callIds");
        var delivery = await _poster.PostAsync(title, severity, summary, callIds, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(delivery, OpenAiCompatibleChatClient.JsonOptions);
    }
}
