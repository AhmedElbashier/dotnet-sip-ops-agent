using System.Text.Json;
using SipOpsAgent.Llm;
using SipOpsAgent.Tools;

namespace SipOpsAgent.Agent;

public sealed class ToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = tools.ToDictionary(t => t.Name, t => t, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IAgentTool> Tools => _tools.Values.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();

    public IReadOnlyList<ToolDefinition> Definitions => Tools.Select(t => t.Definition).ToList();

    public async Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(name, out var tool))
        {
            return JsonSerializer.Serialize(new { error = "unknown_tool", name }, OpenAiCompatibleChatClient.JsonOptions);
        }

        try
        {
            return await tool.InvokeAsync(argumentsJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or InvalidOperationException)
        {
            return JsonSerializer.Serialize(new { error = "tool_error", name, message = ex.Message }, OpenAiCompatibleChatClient.JsonOptions);
        }
    }
}
