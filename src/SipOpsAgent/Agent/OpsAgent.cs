using Microsoft.Extensions.Options;
using SipOpsAgent.Llm;
using SipOpsAgent.Options;

namespace SipOpsAgent.Agent;

public sealed record ToolInvocation(string Id, string Name, string Arguments, string Result);

public sealed record AgentTurnTrace(int Turn, string? Assistant, IReadOnlyList<ToolInvocation> ToolCalls);

public sealed record AgentResponse(
    string Answer,
    string Provider,
    string Model,
    int Turns,
    IReadOnlyList<AgentTurnTrace> Trace,
    bool ReachedTurnLimit);

public sealed class OpsAgent
{
    public const string SystemPrompt =
        """
        You are a SIP / contact-center operations assistant.
        You inspect sample CDR / SIP call logs through tools and, when asked, post a webhook alert.

        Rules:
        - Never invent call ids, counts, SIP codes, hangup causes or MOS scores. If a tool returns no rows, say so.
        - Prefer summarize_failures when the question is about why something failed or how many calls match a filter.
        - Use get_call when the user names a call id.
        - Use post_webhook only when the user asks to alert, notify, page or post a webhook, and only after you have looked up the relevant calls.
        - The sample data covers 24 Nov 2026 08:00–12:00 Asia/Dubai (04:00–08:00 UTC). Trunks: trunk-du-primary, trunk-du-backup, trunk-intl.
        - Be concise. Quote counts and codes from tool results.
        """;

    private readonly IChatClient _chat;
    private readonly ToolRegistry _tools;
    private readonly AgentOptions _options;

    public OpsAgent(IChatClient chat, ToolRegistry tools, IOptions<AgentOptions> options)
    {
        _chat = chat;
        _tools = tools;
        _options = options.Value;
    }

    public async Task<AgentResponse> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            throw new ArgumentException("A question is required.", nameof(question));
        }

        var maxTurns = Math.Clamp(_options.MaxTurns, 1, 16);
        var messages = new List<ChatMessage>
        {
            ChatMessage.System(SystemPrompt),
            ChatMessage.User(question.Trim())
        };
        var trace = new List<AgentTurnTrace>();
        var requestTools = _tools.Definitions;

        for (var turn = 1; turn <= maxTurns; turn++)
        {
            var assistant = await _chat.CompleteAsync(new ChatRequest(messages, requestTools), cancellationToken)
                .ConfigureAwait(false);
            messages.Add(assistant);

            if (assistant.ToolCalls is not { Count: > 0 })
            {
                var answer = string.IsNullOrWhiteSpace(assistant.Content)
                    ? "(The model returned an empty answer.)"
                    : assistant.Content;
                trace.Add(new AgentTurnTrace(turn, answer, []));
                return new AgentResponse(answer, _chat.ProviderName, _chat.Model, turn, trace, ReachedTurnLimit: false);
            }

            var invocations = new List<ToolInvocation>();
            foreach (var call in assistant.ToolCalls)
            {
                var result = await _tools.InvokeAsync(call.Name, call.Arguments, cancellationToken).ConfigureAwait(false);
                invocations.Add(new ToolInvocation(call.Id, call.Name, call.Arguments, result));
                messages.Add(ChatMessage.Tool(call.Id, result, call.Name));
            }

            trace.Add(new AgentTurnTrace(turn, assistant.Content, invocations));
        }

        const string limitMessage =
            "Turn limit reached before the model produced a final answer. Inspect the trace for the tool results so far.";
        return new AgentResponse(limitMessage, _chat.ProviderName, _chat.Model, maxTurns, trace, ReachedTurnLimit: true);
    }
}
