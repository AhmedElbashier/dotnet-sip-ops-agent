using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SipOpsAgent.Llm;
using SipOpsAgent.Options;
using SipOpsAgent.Tests.TestSupport;

namespace SipOpsAgent.Tests;

public class ChatClientTests
{
    [Fact]
    public async Task Client_serializes_tools_and_parses_tool_calls()
    {
        var handler = new RecordingHandler((_, body) =>
        {
            using var doc = JsonDocument.Parse(body);
            Assert.Equal("llama3.1", doc.RootElement.GetProperty("model").GetString());
            Assert.Equal("auto", doc.RootElement.GetProperty("tool_choice").GetString());
            var tools = doc.RootElement.GetProperty("tools");
            Assert.Equal("search_call_logs", tools[0].GetProperty("function").GetProperty("name").GetString());
            Assert.Equal("system", doc.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());

            var response = """
                {
                  "choices": [
                    {
                      "finish_reason": "tool_calls",
                      "message": {
                        "role": "assistant",
                        "content": null,
                        "tool_calls": [
                          {
                            "id": "call_1",
                            "type": "function",
                            "function": {
                              "name": "search_call_logs",
                              "arguments": "{\"sip_code\":503,\"trunk\":\"trunk-du-backup\"}"
                            }
                          }
                        ]
                      }
                    }
                  ]
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json")
            };
        });

        var client = NewClient(handler, apiKey: null);
        var message = await client.CompleteAsync(new ChatRequest(
            [ChatMessage.System("sys"), ChatMessage.User("why 503?")],
            [new ToolDefinition("search_call_logs", "search", new { type = "object" })]));

        Assert.NotNull(message.ToolCalls);
        var call = Assert.Single(message.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("search_call_logs", call.Name);
        Assert.Contains("503", call.Arguments, StringComparison.Ordinal);
        Assert.Null(handler.LastRequest!.Headers.Authorization);
        Assert.EndsWith("/chat/completions", handler.LastRequest.RequestUri!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Client_sends_a_bearer_token_only_when_a_key_is_configured()
    {
        var handler = new RecordingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"choices":[{"message":{"role":"assistant","content":"ok"}}]}""")
            });
        var client = NewClient(handler, apiKey: "sk-test");
        var message = await client.CompleteAsync(new ChatRequest([ChatMessage.User("hi")], []));
        Assert.Equal("ok", message.Content);
        Assert.Equal("sk-test", handler.LastRequest!.Headers.Authorization?.Parameter);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "sk-test"), handler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task Client_surfaces_http_errors()
    {
        var handler = new RecordingHandler((_, _) =>
            new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                ReasonPhrase = "Bad Request",
                Content = new StringContent("""{"error":{"message":"unknown model"}}""")
            });
        var client = NewClient(handler, apiKey: "sk");
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.CompleteAsync(new ChatRequest([ChatMessage.User("hi")], [])));
        Assert.Contains("400", ex.Message, StringComparison.Ordinal);
        Assert.Contains("unknown model", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wire_format_includes_tool_results()
    {
        var wired = OpenAiCompatibleChatClient.ToWire(ChatMessage.Tool("call_1", "{\"ok\":true}", "search_call_logs"));
        Assert.Equal("tool", wired.Role);
        Assert.Equal("call_1", wired.ToolCallId);
        Assert.Equal("search_call_logs", wired.Name);
        Assert.Equal("{\"ok\":true}", wired.Content);
    }

    private static OpenAiCompatibleChatClient NewClient(RecordingHandler handler, string? apiKey)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://llm.test/v1/") };
        return new OpenAiCompatibleChatClient(http, Microsoft.Extensions.Options.Options.Create(new LlmOptions
        {
            Provider = "openai",
            BaseUrl = "http://llm.test/v1",
            Model = "llama3.1",
            ApiKey = apiKey
        }));
    }
}
