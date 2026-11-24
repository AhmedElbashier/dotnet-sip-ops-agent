using System.Text.Json;
using SipOpsAgent.Cdr;
using SipOpsAgent.Llm;

namespace SipOpsAgent.Tools;

public sealed class SearchCallLogsTool : IAgentTool
{
    private readonly ICallLogStore _store;

    public SearchCallLogsTool(ICallLogStore store) => _store = store;

    public string Name => "search_call_logs";

    public string Description =>
        "Search sample SIP/CDR call logs. Filter by time window, caller, callee, direction, disposition, SIP code, trunk or hangup cause. Returns a count and matching records.";

    public object Parameters => CallLogQueryParser.FilterSchema();

    public Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = JsonArgs.ParseObject(argumentsJson) ?? throw new JsonException("Missing arguments.");
        var query = CallLogQueryParser.Parse(args);
        var rows = _store.Search(query);
        var payload = new
        {
            matchedReturned = rows.Count,
            query,
            calls = rows.Select(r => r.ToSummary())
        };
        return Task.FromResult(JsonSerializer.Serialize(payload, OpenAiCompatibleChatClient.JsonOptions));
    }
}
