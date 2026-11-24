using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SipOpsAgent.Agent;
using SipOpsAgent.Cdr;
using SipOpsAgent.Llm;
using SipOpsAgent.Options;
using SipOpsAgent.Tools;
using SipOpsAgent.Webhooks;

namespace SipOpsAgent.Tests.TestSupport;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services =>
        {
            services.PostConfigure<LlmOptions>(o => o.Provider = "scripted");
            services.PostConfigure<AgentOptions>(o => o.EnableDevSink = true);
            services.RemoveAll<IChatClient>();
            services.AddSingleton<IChatClient, ScriptedChatClient>();
        });
    }
}

public static class TestAgent
{
    public static (OpsAgent Agent, WebhookSink Sink, RecordingHandler Handler) Create(
        IChatClient chat,
        int maxTurns = 6,
        string? webhookUrl = "http://webhook.test/hooks/ops",
        string hmac = "test-secret",
        System.Net.HttpStatusCode webhookStatus = System.Net.HttpStatusCode.OK)
    {
        var store = new InMemoryCallLogStore();
        var sink = new WebhookSink();
        var handler = new RecordingHandler((_, _) =>
            new HttpResponseMessage(webhookStatus) { Content = new StringContent("""{"accepted":true}""") });
        var poster = new WebhookPoster(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(new WebhookOptions { Url = webhookUrl, HmacSecret = hmac, TimeoutSeconds = 5 }),
            sink);
        var tools = new ToolRegistry(
        [
            new SearchCallLogsTool(store),
            new GetCallTool(store),
            new SummarizeFailuresTool(store),
            new PostWebhookTool(poster)
        ]);
        var agent = new OpsAgent(chat, tools, Microsoft.Extensions.Options.Options.Create(new AgentOptions { MaxTurns = maxTurns }));
        return (agent, sink, handler);
    }
}
