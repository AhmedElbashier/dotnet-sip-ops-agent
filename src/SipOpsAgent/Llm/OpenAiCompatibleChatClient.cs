using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SipOpsAgent.Options;

namespace SipOpsAgent.Llm;

public sealed class OpenAiCompatibleChatClient : IChatClient
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _http;
    private readonly LlmOptions _options;

    public OpenAiCompatibleChatClient(HttpClient http, IOptions<LlmOptions> options)
    {
        _http = http;
        _options = options.Value;
        _http.Timeout = TimeSpan.FromSeconds(Math.Max(5, _options.TimeoutSeconds));
        _http.BaseAddress ??= new Uri(EnsureTrailingSlash(_options.BaseUrl));
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
        }
    }

    public string ProviderName => "openai";

    public string Model => _options.Model;

    public async Task<ChatMessage> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var payload = new OpenAiChatRequest
        {
            Model = _options.Model,
            Temperature = _options.Temperature,
            ToolChoice = request.Tools.Count > 0 ? "auto" : null,
            Tools = request.Tools.Count == 0
                ? null
                : request.Tools.Select(t => new OpenAiTool
                {
                    Function = new OpenAiFunction
                    {
                        Name = t.Name,
                        Description = t.Description,
                        Parameters = t.Parameters
                    }
                }).ToList(),
            Messages = request.Messages.Select(ToWire).ToList()
        };

        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync("chat/completions", content, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"LLM request failed with {(int)response.StatusCode} {response.ReasonPhrase}: {Trim(body, 800)}");
        }

        var parsed = JsonSerializer.Deserialize<OpenAiChatResponse>(body, JsonOptions)
                     ?? throw new InvalidOperationException("LLM returned an empty response.");

        if (!string.IsNullOrWhiteSpace(parsed.Error?.Message))
        {
            throw new InvalidOperationException($"LLM error: {parsed.Error.Message}");
        }

        var message = parsed.Choices?.FirstOrDefault()?.Message
                      ?? throw new InvalidOperationException("LLM response contained no choices.");

        var toolCalls = message.ToolCalls?
            .Where(c => !string.IsNullOrWhiteSpace(c.Function?.Name))
            .Select(c => new ToolCall(
                string.IsNullOrWhiteSpace(c.Id) ? Guid.NewGuid().ToString("N") : c.Id!,
                c.Function!.Name!,
                string.IsNullOrWhiteSpace(c.Function.Arguments) ? "{}" : c.Function.Arguments))
            .ToList();

        return ChatMessage.Assistant(message.Content, toolCalls is { Count: > 0 } ? toolCalls : null);
    }

    internal static OpenAiChatMessage ToWire(ChatMessage message) => new()
    {
        Role = message.Role,
        Content = message.Content,
        ToolCallId = message.ToolCallId,
        Name = message.Name,
        ToolCalls = message.ToolCalls is { Count: > 0 }
            ? message.ToolCalls.Select(c => new OpenAiToolCall
            {
                Id = c.Id,
                Type = "function",
                Function = new OpenAiFunctionCall { Name = c.Name, Arguments = c.Arguments }
            }).ToList()
            : null
    };

    internal static string EnsureTrailingSlash(string url) =>
        url.EndsWith('/') ? url : url + "/";

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
