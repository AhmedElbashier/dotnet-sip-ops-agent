using System.Text.Json;
using SipOpsAgent.Cdr;
using SipOpsAgent.Llm;

namespace SipOpsAgent.Tools;

public sealed class SummarizeFailuresTool : IAgentTool
{
    private readonly ICallLogStore _store;

    public SummarizeFailuresTool(ICallLogStore store) => _store = store;

    public string Name => "summarize_failures";

    public string Description =>
        "Aggregate matching CDRs by sip_code, hangup_cause, trunk or disposition. Use this to explain an incident (for example a 503 spike on one trunk) instead of listing every call.";

    public object Parameters
    {
        get
        {
            var properties = CallLogQueryParser.FilterProperties();
            properties["group_by"] = new
            {
                type = "string",
                @enum = new[] { "sip_code", "hangup_cause", "trunk", "disposition" },
                description = "Bucket key. Defaults to sip_code."
            };
            return new { type = "object", properties };
        }
    }

    public Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = JsonArgs.ParseObject(argumentsJson) ?? throw new JsonException("Missing arguments.");
        // Summarize uses every matching row; Limit on the query is ignored by the store.
        var query = CallLogQueryParser.Parse(args, defaultLimit: 100) with { Limit = 100 };
        var groupBy = JsonArgs.GetString(args, "group_by", "groupBy") ?? "sip_code";
        var summary = _store.Summarize(query, groupBy);
        return Task.FromResult(JsonSerializer.Serialize(summary, OpenAiCompatibleChatClient.JsonOptions));
    }
}
