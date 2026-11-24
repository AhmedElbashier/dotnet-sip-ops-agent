namespace SipOpsAgent.Options;

public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// "openai" talks to any OpenAI-compatible Chat Completions API (OpenAI, Azure OpenAI,
    /// Ollama at {BaseUrl}/v1, LM Studio, vLLM, etc.). "scripted" is a deterministic
    /// stand-in used by CI, tests and the no-key quickstart — it is not an LLM.
    /// </summary>
    public string Provider { get; set; } = "scripted";

    /// <summary>Base URL including the /v1 suffix, e.g. http://localhost:11434/v1 or https://api.openai.com/v1.</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434/v1";

    public string Model { get; set; } = "llama3.1";

    /// <summary>Optional. Ollama ignores this; OpenAI and Azure require it.</summary>
    public string? ApiKey { get; set; }

    public float Temperature { get; set; }

    public int TimeoutSeconds { get; set; } = 60;
}
