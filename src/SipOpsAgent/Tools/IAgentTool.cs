using SipOpsAgent.Llm;

namespace SipOpsAgent.Tools;

public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    object Parameters { get; }

    Task<string> InvokeAsync(string argumentsJson, CancellationToken cancellationToken = default);

    ToolDefinition Definition => new(Name, Description, Parameters);
}
