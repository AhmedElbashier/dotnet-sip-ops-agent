using SipOpsAgent.Agent;
using SipOpsAgent.Cdr;
using SipOpsAgent.Endpoints;
using SipOpsAgent.Llm;
using SipOpsAgent.Options;
using SipOpsAgent.Tools;
using System.Text.Json;
using System.Text.Json.Serialization;
using SipOpsAgent.Webhooks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection(LlmOptions.SectionName));
builder.Services.Configure<WebhookOptions>(builder.Configuration.GetSection(WebhookOptions.SectionName));
builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

builder.Services.AddSingleton<ICallLogStore, InMemoryCallLogStore>();
builder.Services.AddSingleton<WebhookSink>();
builder.Services.AddHttpClient<WebhookPoster>();
builder.Services.AddHttpClient<OpenAiCompatibleChatClient>((sp, http) =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value;
    http.BaseAddress = new Uri(OpenAiCompatibleChatClient.EnsureTrailingSlash(options.BaseUrl));
});
builder.Services.AddSingleton<IChatClient>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmOptions>>().Value;
    return options.Provider.Equals("scripted", StringComparison.OrdinalIgnoreCase)
        ? new ScriptedChatClient()
        : sp.GetRequiredService<OpenAiCompatibleChatClient>();
});
builder.Services.AddSingleton<IAgentTool, SearchCallLogsTool>();
builder.Services.AddSingleton<IAgentTool, GetCallTool>();
builder.Services.AddSingleton<IAgentTool, SummarizeFailuresTool>();
builder.Services.AddSingleton<IAgentTool, PostWebhookTool>();
builder.Services.AddSingleton<ToolRegistry>();
builder.Services.AddSingleton<OpsAgent>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "sip-ops-agent",
        Version = "v1",
        Description = "A .NET 8 AI ops agent that inspects sample SIP/CDR call logs through LLM tool calling and can post HMAC-signed webhook alerts."
    });
});

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapSipOpsEndpoints();

app.Run();

public partial class Program;
