using System.Text.Json.Serialization;

namespace SipOpsAgent.Llm;

public sealed record ChatMessage(string Role, string? Content, IReadOnlyList<ToolCall>? ToolCalls = null, string? ToolCallId = null, string? Name = null)
{
    public static ChatMessage System(string content) => new("system", content);

    public static ChatMessage User(string content) => new("user", content);

    public static ChatMessage Assistant(string? content, IReadOnlyList<ToolCall>? toolCalls = null) =>
        new("assistant", content, toolCalls);

    public static ChatMessage Tool(string toolCallId, string content, string? name = null) =>
        new("tool", content, ToolCallId: toolCallId, Name: name);
}

public sealed record ToolCall(string Id, string Name, string Arguments);

public sealed record ChatRequest(IReadOnlyList<ChatMessage> Messages, IReadOnlyList<ToolDefinition> Tools);

public sealed record ToolDefinition(string Name, string Description, object Parameters);

public interface IChatClient
{
    string ProviderName { get; }

    string Model { get; }

    Task<ChatMessage> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

internal sealed class OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("messages")]
    public required IReadOnlyList<OpenAiChatMessage> Messages { get; init; }

    [JsonPropertyName("tools")]
    public IReadOnlyList<OpenAiTool>? Tools { get; init; }

    [JsonPropertyName("tool_choice")]
    public string? ToolChoice { get; init; }

    [JsonPropertyName("temperature")]
    public float Temperature { get; init; }
}

internal sealed class OpenAiChatMessage
{
    [JsonPropertyName("role")]
    public required string Role { get; init; }

    [JsonPropertyName("content")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Content { get; init; }

    [JsonPropertyName("tool_calls")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<OpenAiToolCall>? ToolCalls { get; init; }

    [JsonPropertyName("tool_call_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ToolCallId { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
}

internal sealed class OpenAiTool
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public required OpenAiFunction Function { get; init; }
}

internal sealed class OpenAiFunction
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("parameters")]
    public required object Parameters { get; init; }
}

internal sealed class OpenAiToolCall
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("function")]
    public OpenAiFunctionCall? Function { get; init; }
}

internal sealed class OpenAiFunctionCall
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("arguments")]
    public string? Arguments { get; init; }
}

internal sealed class OpenAiChatResponse
{
    [JsonPropertyName("choices")]
    public IReadOnlyList<OpenAiChoice>? Choices { get; init; }

    [JsonPropertyName("error")]
    public OpenAiError? Error { get; init; }
}

internal sealed class OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiChatMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

internal sealed class OpenAiError
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
