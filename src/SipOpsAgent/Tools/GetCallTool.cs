using System.Text.Json;
using SipOpsAgent.Cdr;
using SipOpsAgent.Llm;

namespace SipOpsAgent.Tools;

public sealed class GetCallTool : IAgentTool
{
    private readonly ICallLogStore _store;

    public GetCallTool(ICallLogStore store) => _store = store;

    public string Name => "get_call";

    public string Description =>
        "Fetch one CDR by internal call id (cdr-20261124-00xx) or SIP Call-ID. Returns the full record, or an error if it does not exist.";

    public object Parameters => new
    {
        type = "object",
        properties = new
        {
            call_id = new { type = "string", description = "Internal call id or SIP Call-ID." }
        },
        required = new[] { "call_id" }
    };

    public Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = JsonArgs.ParseObject(argumentsJson) ?? throw new JsonException("Missing arguments.");
        var callId = JsonArgs.GetString(args, "call_id", "callId", "sip_call_id");
        if (string.IsNullOrWhiteSpace(callId))
        {
            return Task.FromResult("""{"error":"call_id is required"}""");
        }

        var record = _store.GetById(callId);
        if (record is null)
        {
            return Task.FromResult(JsonSerializer.Serialize(new { error = "not_found", callId }, OpenAiCompatibleChatClient.JsonOptions));
        }

        return Task.FromResult(JsonSerializer.Serialize(record, OpenAiCompatibleChatClient.JsonOptions));
    }
}
