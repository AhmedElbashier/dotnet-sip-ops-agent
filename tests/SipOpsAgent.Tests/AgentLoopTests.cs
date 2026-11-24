using SipOpsAgent.Cdr;
using SipOpsAgent.Llm;
using SipOpsAgent.Tests.TestSupport;

namespace SipOpsAgent.Tests;

public class AgentLoopTests
{
    [Fact]
    public async Task Agent_executes_tool_calls_and_returns_the_final_answer()
    {
        var chat = new QueueChatClient(
        [
            ChatMessage.Assistant(null,
            [
                new ToolCall("t1", "get_call", """{"call_id":"cdr-20261124-0042"}""")
            ]),
            ChatMessage.Assistant("Call 0042 answered on trunk-du-primary with MOS 4.2.")
        ]);
        var (agent, _, _) = TestAgent.Create(chat);
        var response = await agent.AskAsync("Get details for cdr-20261124-0042");
        Assert.False(response.ReachedTurnLimit);
        Assert.Equal(2, response.Turns);
        Assert.Contains("MOS 4.2", response.Answer, StringComparison.Ordinal);
        var invocation = Assert.Single(response.Trace[0].ToolCalls);
        Assert.Equal("get_call", invocation.Name);
        Assert.Contains("4.2", invocation.Result, StringComparison.Ordinal);
        Assert.Equal(0, chat.Remaining);
    }

    [Fact]
    public async Task Agent_stops_at_the_turn_limit()
    {
        var chat = new QueueChatClient(
        [
            ChatMessage.Assistant(null, [new ToolCall("a", "search_call_logs", """{"limit":1}""")]),
            ChatMessage.Assistant(null, [new ToolCall("b", "search_call_logs", """{"limit":1}""")])
        ]);
        var (agent, _, _) = TestAgent.Create(chat, maxTurns: 2);
        var response = await agent.AskAsync("keep going");
        Assert.True(response.ReachedTurnLimit);
        Assert.Equal(2, response.Turns);
        Assert.Contains("Turn limit", response.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Empty_question_is_rejected()
    {
        var (agent, _, _) = TestAgent.Create(new QueueChatClient([]));
        await Assert.ThrowsAsync<ArgumentException>(() => agent.AskAsync("  "));
    }

    [Fact]
    public async Task Invalid_tool_json_is_fed_back_and_the_loop_can_finish()
    {
        var chat = new QueueChatClient(
        [
            ChatMessage.Assistant(null, [new ToolCall("t1", "search_call_logs", "not-json")]),
            ChatMessage.Assistant("The search arguments were invalid, so I stopped.")
        ]);
        var (agent, _, _) = TestAgent.Create(chat);
        var response = await agent.AskAsync("search please");
        Assert.Contains("tool_error", response.Trace[0].ToolCalls[0].Result, StringComparison.Ordinal);
        Assert.Contains("invalid", response.Answer, StringComparison.OrdinalIgnoreCase);
    }
}
