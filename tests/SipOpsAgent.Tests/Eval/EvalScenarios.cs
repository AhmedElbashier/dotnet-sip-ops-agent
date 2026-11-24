using System.Text.Json;
using SipOpsAgent.Cdr;
using SipOpsAgent.Llm;
using SipOpsAgent.Tests.TestSupport;

namespace SipOpsAgent.Tests.Eval;

/// <summary>
/// Tiny eval suite: a queued (mocked) model follows a fixed tool plan and the
/// assertions check that the agent actually ran the right tools against the sample CDRs.
/// No API key is required.
/// </summary>
public class EvalScenarios
{
    [Fact]
    public async Task Incident_on_backup_trunk_is_explained_with_search_and_summary()
    {
        var chat = new QueueChatClient(
        [
            ChatMessage.Assistant(null,
            [
                new ToolCall("e1", "search_call_logs", """
                    {"trunk":"trunk-du-backup","sip_code":503,"from":"2026-11-24T04:00:00Z","to":"2026-11-24T08:00:00Z","limit":50}
                    """),
                new ToolCall("e2", "summarize_failures", """
                    {"trunk":"trunk-du-backup","disposition":"failed","group_by":"sip_code"}
                    """)
            ]),
            ChatMessage.Assistant("trunk-du-backup had 12 failed calls, all SIP 503 / NORMAL_TEMPORARY_FAILURE, between 05:15Z and 05:38Z.")
        ]);

        var (agent, _, _) = TestAgent.Create(chat);
        var response = await agent.AskAsync("Why did calls fail on trunk-du-backup this morning?");

        Assert.False(response.ReachedTurnLimit);
        Assert.Contains("12 failed", response.Answer, StringComparison.Ordinal);
        Assert.Contains("503", response.Answer, StringComparison.Ordinal);

        var search = response.Trace[0].ToolCalls.Single(c => c.Name == "search_call_logs");
        using var searchJson = JsonDocument.Parse(search.Result);
        Assert.Equal(12, searchJson.RootElement.GetProperty("matchedReturned").GetInt32());

        var summary = response.Trace[0].ToolCalls.Single(c => c.Name == "summarize_failures");
        using var summaryJson = JsonDocument.Parse(summary.Result);
        Assert.Equal(12, summaryJson.RootElement.GetProperty("groups")[0].GetProperty("count").GetInt32());
        Assert.Equal("503", summaryJson.RootElement.GetProperty("groups")[0].GetProperty("key").GetString());
    }

    [Fact]
    public async Task Featured_call_lookup_returns_the_expected_cdr()
    {
        var chat = new QueueChatClient(
        [
            ChatMessage.Assistant(null, [new ToolCall("g1", "get_call", """{"call_id":"cdr-20261124-0042"}""")]),
            ChatMessage.Assistant("cdr-20261124-0042 is an inbound answered call on trunk-du-primary, MOS 4.2, 187 seconds.")
        ]);
        var (agent, _, _) = TestAgent.Create(chat);
        var response = await agent.AskAsync("Get details for cdr-20261124-0042");
        using var doc = JsonDocument.Parse(response.Trace[0].ToolCalls[0].Result);
        Assert.Equal(SampleData.FeaturedCallId, doc.RootElement.GetProperty("callId").GetString());
        Assert.Equal(4.2, doc.RootElement.GetProperty("mos").GetDouble());
        Assert.Contains("187", response.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Busy_calls_can_be_alerted_over_the_webhook()
    {
        var chat = new QueueChatClient(
        [
            ChatMessage.Assistant(null,
            [
                new ToolCall("s1", "search_call_logs", """{"sip_code":486,"limit":50}"""),
                new ToolCall("s2", "summarize_failures", """{"sip_code":486,"group_by":"trunk"}""")
            ]),
            ChatMessage.Assistant(null,
            [
                new ToolCall("w1", "post_webhook", """
                    {"title":"Busy-hour 486s","severity":"warning","summary":"5 calls returned 486 USER_BUSY.","call_ids":["cdr-20261124-0013"]}
                    """)
            ]),
            ChatMessage.Assistant("Posted a warning webhook for the five 486 busy calls.")
        ]);

        var (agent, sink, handler) = TestAgent.Create(chat);
        var response = await agent.AskAsync("Find all 486 responses and post a warning webhook.");

        Assert.Contains("five 486", response.Answer, StringComparison.OrdinalIgnoreCase);
        var webhook = response.Trace[1].ToolCalls.Single(c => c.Name == "post_webhook");
        using var delivered = JsonDocument.Parse(webhook.Result);
        Assert.Equal(200, delivered.RootElement.GetProperty("statusCode").GetInt32());
        Assert.False(delivered.RootElement.GetProperty("dryRun").GetBoolean());
        Assert.Equal(1, handler.Calls);
        Assert.Contains("Busy-hour 486s", handler.LastBody, StringComparison.Ordinal);
        Assert.Single(sink.List());
    }

    [Fact]
    public async Task Missing_call_is_reported_without_inventing_a_record()
    {
        var chat = new QueueChatClient(
        [
            ChatMessage.Assistant(null, [new ToolCall("g1", "get_call", """{"call_id":"cdr-does-not-exist"}""")]),
            ChatMessage.Assistant("No CDR named cdr-does-not-exist exists in the sample data.")
        ]);
        var (agent, _, _) = TestAgent.Create(chat);
        var response = await agent.AskAsync("Get details for cdr-does-not-exist");
        Assert.Contains("not_found", response.Trace[0].ToolCalls[0].Result, StringComparison.Ordinal);
        Assert.Contains("No CDR named cdr-does-not-exist", response.Answer, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MOS", response.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Scripted_provider_resolves_the_backup_incident_without_a_key()
    {
        var (agent, _, _) = TestAgent.Create(new ScriptedChatClient(), webhookUrl: null);
        var response = await agent.AskAsync("Why did calls fail on trunk-du-backup this morning?");
        Assert.Equal("scripted", response.Provider);
        Assert.False(response.ReachedTurnLimit);
        Assert.Contains(response.Trace.SelectMany(t => t.ToolCalls), c => c.Name == "search_call_logs");
        Assert.Contains(response.Trace.SelectMany(t => t.ToolCalls), c => c.Name == "summarize_failures");
        Assert.Contains("503", response.Answer, StringComparison.Ordinal);
        Assert.Contains("trunk-du-backup", response.Answer, StringComparison.Ordinal);
    }
}
