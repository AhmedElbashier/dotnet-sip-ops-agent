using System.Text.Json;
using SipOpsAgent.Cdr;
using SipOpsAgent.Tools;

namespace SipOpsAgent.Tests;

public class ToolTests
{
    private readonly InMemoryCallLogStore _store = new();

    [Fact]
    public async Task Search_returns_the_twelve_backup_503s()
    {
        var tool = new SearchCallLogsTool(_store);
        var json = await tool.InvokeAsync("""
            {"trunk":"trunk-du-backup","sip_code":503,"from":"2026-11-24T04:00:00Z","to":"2026-11-24T08:00:00Z","limit":50}
            """);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(12, doc.RootElement.GetProperty("matchedReturned").GetInt32());
    }

    [Fact]
    public async Task Get_call_returns_not_found_for_unknown_ids()
    {
        var tool = new GetCallTool(_store);
        var json = await tool.InvokeAsync("""{"call_id":"missing"}""");
        Assert.Contains("not_found", json, StringComparison.Ordinal);
        var featured = await tool.InvokeAsync("""{"callId":"cdr-20261124-0042"}""");
        Assert.Contains("4.2", featured, StringComparison.Ordinal);
        var missingArg = await tool.InvokeAsync("{}");
        Assert.Contains("required", missingArg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Summarize_accepts_camelCase_and_snake_case()
    {
        var tool = new SummarizeFailuresTool(_store);
        var snake = await tool.InvokeAsync("""{"sip_code":486,"group_by":"hangup_cause"}""");
        var camel = await tool.InvokeAsync("""{"sipCode":486,"groupBy":"hangup_cause"}""");
        using var a = JsonDocument.Parse(snake);
        using var b = JsonDocument.Parse(camel);
        Assert.Equal(5, a.RootElement.GetProperty("busy").GetInt32());
        Assert.Equal(5, b.RootElement.GetProperty("busy").GetInt32());
    }

    [Fact]
    public async Task Unknown_disposition_is_returned_as_a_structured_tool_error_by_the_registry()
    {
        var registry = new Agent.ToolRegistry([new SearchCallLogsTool(_store)]);
        var json = await registry.InvokeAsync("search_call_logs", """{"disposition":"exploded"}""");
        Assert.Contains("tool_error", json, StringComparison.Ordinal);
        Assert.Contains("Unknown disposition", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unknown_tool_name_returns_an_error_payload()
    {
        var registry = new Agent.ToolRegistry([new GetCallTool(_store)]);
        var json = await registry.InvokeAsync("drop_table", "{}");
        Assert.Contains("unknown_tool", json, StringComparison.Ordinal);
    }
}
