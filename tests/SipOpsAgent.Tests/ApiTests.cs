using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SipOpsAgent.Cdr;
using SipOpsAgent.Tests.TestSupport;

namespace SipOpsAgent.Tests;

public class ApiTests : IClassFixture<TestAppFactory>
{
    private readonly HttpClient _client;

    public ApiTests(TestAppFactory factory) => _client = factory.CreateClient();

    [Fact]
    public async Task Health_and_tools_are_published()
    {
        var live = await _client.GetAsync("/health/live");
        live.EnsureSuccessStatusCode();
        var ready = await _client.GetFromJsonAsync<JsonElement>("/health/ready");
        Assert.Equal("ready", ready.GetProperty("status").GetString());
        Assert.Equal(48, ready.GetProperty("records").GetInt32());
        Assert.Equal("scripted", ready.GetProperty("provider").GetString());

        var tools = await _client.GetFromJsonAsync<JsonElement>("/api/tools");
        var names = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()).ToList();
        Assert.Contains("search_call_logs", names);
        Assert.Contains("get_call", names);
        Assert.Contains("summarize_failures", names);
        Assert.Contains("post_webhook", names);
    }

    [Fact]
    public async Task Calls_api_returns_the_featured_record_and_filters_503s()
    {
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            Converters = { new JsonStringEnumConverter() }
        };
        var featured = await _client.GetFromJsonAsync<CallRecord>($"/api/calls/{SampleData.FeaturedCallId}", jsonOptions);
        Assert.Equal(4.2, featured!.Mos);
        Assert.Equal(CallDirection.Inbound, featured.Direction);
        Assert.Equal(CallDisposition.Answered, featured.Disposition);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/api/calls/missing")).StatusCode);

        var filtered = await _client.GetFromJsonAsync<JsonElement>("/api/calls?sipCode=503&trunk=trunk-du-backup&limit=50");
        Assert.Equal(12, filtered.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Ask_rejects_an_empty_question_and_answers_a_real_one()
    {
        var bad = await _client.PostAsJsonAsync("/api/agent/ask", new { question = "" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var ok = await _client.PostAsJsonAsync("/api/agent/ask", new { question = "Get details for cdr-20261124-0042" });
        ok.EnsureSuccessStatusCode();
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("scripted", body.GetProperty("provider").GetString());
        Assert.Contains("cdr-20261124-0042", body.GetProperty("answer").GetString(), StringComparison.Ordinal);
        Assert.Contains("4.2", body.GetProperty("answer").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ask_can_post_a_webhook_that_lands_in_the_dev_sink()
    {
        var ok = await _client.PostAsJsonAsync("/api/agent/ask", new
        {
            question = "Find all 486 busy calls and post a warning webhook."
        });
        ok.EnsureSuccessStatusCode();
        var body = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("post_webhook", body.GetRawText(), StringComparison.Ordinal);

        var sink = await _client.GetFromJsonAsync<JsonElement>("/api/dev/webhook-sink");
        Assert.True(sink.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Home_page_is_served()
    {
        var page = await _client.GetStringAsync("/");
        Assert.Contains("sip-ops-agent", page, StringComparison.Ordinal);
        Assert.Contains("/api/agent/ask", page, StringComparison.Ordinal);
    }
}
