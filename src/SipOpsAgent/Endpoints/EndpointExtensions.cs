using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SipOpsAgent.Agent;
using SipOpsAgent.Cdr;
using SipOpsAgent.Llm;
using SipOpsAgent.Options;
using SipOpsAgent.Webhooks;

namespace SipOpsAgent.Endpoints;

public static class EndpointExtensions
{
    public static WebApplication MapSipOpsEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }))
            .WithName("HealthLive")
            .WithTags("Health");

        app.MapGet("/health/ready", (ICallLogStore store, IChatClient chat) =>
            {
                if (store.All.Count == 0)
                {
                    return Results.Json(new { status = "unready", reason = "call log store is empty" }, statusCode: 503);
                }

                return Results.Ok(new
                {
                    status = "ready",
                    records = store.All.Count,
                    provider = chat.ProviderName,
                    model = chat.Model
                });
            })
            .WithName("HealthReady")
            .WithTags("Health");

        app.MapGet("/api/tools", (ToolRegistry registry) =>
            registry.Definitions.Select(t => new { t.Name, t.Description, t.Parameters }))
            .WithName("ListTools")
            .WithTags("Agent");

        app.MapPost("/api/agent/ask", async (AskRequest body, OpsAgent agent, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(body.Question))
                {
                    return Results.BadRequest(new ProblemDetails
                    {
                        Title = "Question is required",
                        Status = StatusCodes.Status400BadRequest
                    });
                }

                var response = await agent.AskAsync(body.Question, ct).ConfigureAwait(false);
                return Results.Ok(response);
            })
            .WithName("AskAgent")
            .WithTags("Agent");

        app.MapGet("/api/calls", (
                ICallLogStore store,
                DateTimeOffset? from,
                DateTimeOffset? to,
                string? caller,
                string? callee,
                string? direction,
                string? disposition,
                int? sipCode,
                string? trunk,
                string? hangupCause,
                int limit = 25) =>
            {
                CallDirection? dir = null;
                CallDisposition? disp = null;
                if (!string.IsNullOrWhiteSpace(direction))
                {
                    if (!Enum.TryParse(direction, ignoreCase: true, out CallDirection parsedDir))
                    {
                        return Results.BadRequest(new { error = $"Unknown direction '{direction}'." });
                    }

                    dir = parsedDir;
                }

                if (!string.IsNullOrWhiteSpace(disposition))
                {
                    if (!TryParseDisposition(disposition, out var parsedDisp))
                    {
                        return Results.BadRequest(new { error = $"Unknown disposition '{disposition}'." });
                    }

                    disp = parsedDisp;
                }

                var query = new CallLogQuery(from, to, caller, callee, dir, disp, sipCode, trunk, hangupCause, limit);
                var rows = store.Search(query);
                return Results.Ok(new { count = rows.Count, calls = rows });
            })
            .WithName("SearchCalls")
            .WithTags("Calls");

        app.MapGet("/api/calls/{callId}", (string callId, ICallLogStore store) =>
            {
                var record = store.GetById(callId);
                return record is null ? Results.NotFound(new { error = "not_found", callId }) : Results.Ok(record);
            })
            .WithName("GetCall")
            .WithTags("Calls");

        app.MapPost("/api/dev/webhook-sink", async (HttpRequest request, WebhookSink sink, CancellationToken ct) =>
            {
                if (!app.Configuration.GetValue("Agent:EnableDevSink", false))
                {
                    return Results.NotFound();
                }

                using var reader = new StreamReader(request.Body);
                var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                JsonElement payload;
                try
                {
                    payload = JsonSerializer.Deserialize<JsonElement>(body);
                }
                catch (JsonException)
                {
                    payload = JsonSerializer.SerializeToElement(new { raw = body });
                }

                var captured = new WebhookDelivery
                {
                    Id = payload.TryGetProperty("id", out var id) ? id.GetString() ?? "sink" : "sink",
                    GeneratedAt = DateTimeOffset.UtcNow,
                    Title = payload.TryGetProperty("title", out var title) ? title.GetString() ?? "inbound" : "inbound",
                    Severity = payload.TryGetProperty("severity", out var sev) ? sev.GetString() ?? "info" : "info",
                    Summary = payload.TryGetProperty("summary", out var sum) ? sum.GetString() ?? body : body,
                    CallIds = payload.TryGetProperty("callIds", out var ids) && ids.ValueKind == JsonValueKind.Array
                        ? ids.EnumerateArray().Select(e => e.GetString() ?? "").Where(s => s.Length > 0).ToList()
                        : [],
                    Destination = "/api/dev/webhook-sink",
                    StatusCode = 200,
                    DryRun = false
                };
                sink.Add(captured);
                return Results.Ok(new { accepted = true, id = captured.Id });
            })
            .WithName("DevWebhookSink")
            .WithTags("Dev")
            .ExcludeFromDescription();

        app.MapGet("/api/dev/webhook-sink", (WebhookSink sink) =>
            {
                if (!app.Configuration.GetValue("Agent:EnableDevSink", false))
                {
                    return Results.NotFound();
                }

                return Results.Ok(sink.List());
            })
            .WithName("ListDevWebhookSink")
            .WithTags("Dev");

        return app;
    }

    private static bool TryParseDisposition(string raw, out CallDisposition disposition)
    {
        var key = raw.Trim().ToLowerInvariant().Replace("-", "").Replace("_", "");
        switch (key)
        {
            case "answered":
                disposition = CallDisposition.Answered;
                return true;
            case "noanswer" or "unanswered":
                disposition = CallDisposition.NoAnswer;
                return true;
            case "busy":
                disposition = CallDisposition.Busy;
                return true;
            case "failed" or "fail":
                disposition = CallDisposition.Failed;
                return true;
            case "cancelled" or "canceled" or "cancel":
                disposition = CallDisposition.Cancelled;
                return true;
            default:
                return Enum.TryParse(raw, ignoreCase: true, out disposition);
        }
    }
}

public sealed record AskRequest(string Question);
