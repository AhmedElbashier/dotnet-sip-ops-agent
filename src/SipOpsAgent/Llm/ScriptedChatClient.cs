using System.Text.Json;
using System.Text.RegularExpressions;

namespace SipOpsAgent.Llm;

/// <summary>
/// Deterministic stand-in for an LLM. Used by CI, unit tests and the no-key quickstart.
/// It emits tool calls from simple keyword rules, then writes a factual answer from the
/// tool results. It is not a language model.
/// </summary>
public sealed class ScriptedChatClient : IChatClient
{
    public string ProviderName => "scripted";

    public string Model => "scripted-planner";

    public Task<ChatMessage> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? string.Empty;
        var toolResults = request.Messages.Where(m => m.Role == "tool").ToList();

        if (toolResults.Count > 0)
        {
            var wantsWebhook = WantsWebhook(user);
            var alreadyPosted = request.Messages.Any(m =>
                m.Role == "assistant" &&
                m.ToolCalls is not null &&
                m.ToolCalls.Any(c => c.Name == "post_webhook"));

            if (wantsWebhook && !alreadyPosted)
            {
                return Task.FromResult(ChatMessage.Assistant(null, [BuildWebhookCall(user, toolResults)]));
            }

            return Task.FromResult(ChatMessage.Assistant(BuildAnswer(user, toolResults)));
        }

        return Task.FromResult(ChatMessage.Assistant(null, PlanTools(user)));
    }

    private static IReadOnlyList<ToolCall> PlanTools(string question)
    {
        var callId = FindCallId(question);
        if (callId is not null)
        {
            return [Call("get_call", new { call_id = callId })];
        }

        var filters = ExtractFilters(question);
        var calls = new List<ToolCall> { Call("search_call_logs", filters) };

        if (WantsSummary(question) || filters.ContainsKey("sip_code") || filters.ContainsKey("trunk") ||
            filters.ContainsKey("disposition") || WantsWebhook(question))
        {
            var summary = new Dictionary<string, object?>(filters) { ["group_by"] = GuessGroupBy(question) };
            calls.Add(Call("summarize_failures", summary));
        }

        return calls;
    }

    private static ToolCall BuildWebhookCall(string question, IReadOnlyList<ChatMessage> toolResults)
    {
        var ids = new List<string>();
        var summaryBits = new List<string>();
        foreach (var result in toolResults)
        {
            if (string.IsNullOrWhiteSpace(result.Content))
            {
                continue;
            }

            summaryBits.Add(result.Content);
            try
            {
                using var doc = JsonDocument.Parse(result.Content);
                CollectCallIds(doc.RootElement, ids);
            }
            catch (JsonException)
            {
                // Tool returned plain text; the summary still goes into the webhook body.
            }
        }

        var distinct = ids.Distinct(StringComparer.OrdinalIgnoreCase).Take(25).ToList();
        return Call("post_webhook", new
        {
            title = "SIP ops alert",
            severity = GuessSeverity(question),
            summary = "Scripted provider alert based on tool results. " + string.Join(" ", summaryBits.Select(s => Trim(s, 400))),
            call_ids = distinct
        });
    }

    private static string BuildAnswer(string question, IReadOnlyList<ChatMessage> toolResults)
    {
        var parts = new List<string>
        {
            "Scripted provider (not an LLM). Findings from tool results:"
        };

        foreach (var result in toolResults)
        {
            parts.Add(DescribeResult(result.Content));
        }

        var callId = FindCallId(question);
        if (callId is not null)
        {
            parts.Add($"Looked up call {callId}.");
        }

        return string.Join("\n\n", parts);
    }

    private static string DescribeResult(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "(empty tool result)";
        }

        try
        {
            using var doc = JsonDocument.Parse(content);
            var e = doc.RootElement;

            if (e.TryGetProperty("error", out var error))
            {
                var suffix = e.TryGetProperty("callId", out var missing) ? $" ({missing.GetString()})" : "";
                return $"Tool error: {error.GetString() ?? error.ToString()}{suffix}.";
            }

            if (e.TryGetProperty("callId", out var callId) && e.TryGetProperty("sipCode", out _))
            {
                return $"Call {callId.GetString()}: {Prop(e, "direction")} {Prop(e, "disposition")} " +
                       $"SIP {Prop(e, "sipCode")} / {Prop(e, "hangupCause")} on {Prop(e, "trunk")}, " +
                       $"caller {Prop(e, "caller")} -> {Prop(e, "callee")}, " +
                       $"duration {Prop(e, "durationSeconds")}s, MOS {Fmt(e, "mos")}, loss {Fmt(e, "packetLossPercent")}%.";
            }

            if (e.TryGetProperty("matched", out var matched) && e.TryGetProperty("groups", out var groups))
            {
                var buckets = groups.EnumerateArray()
                    .Select(g => $"{g.GetProperty("count").GetInt32()} x {g.GetProperty("key").GetString()}")
                    .ToList();
                return $"Summary: {matched.GetInt32()} matched " +
                       $"(answered {e.GetProperty("answered").GetInt32()}, " +
                       $"failed {e.GetProperty("failed").GetInt32()}, " +
                       $"busy {e.GetProperty("busy").GetInt32()}, " +
                       $"no-answer {e.GetProperty("noAnswer").GetInt32()}, " +
                       $"cancelled {e.GetProperty("cancelled").GetInt32()}), " +
                       $"grouped by {Prop(e, "groupBy")}: {string.Join(", ", buckets)}.";
            }

            if (e.TryGetProperty("matchedReturned", out var returned))
            {
                var filters = new List<string>();
                if (e.TryGetProperty("query", out var query))
                {
                    foreach (var name in new[] { "trunk", "sipCode", "disposition", "hangupCause", "caller", "callee" })
                    {
                        if (query.TryGetProperty(name, out var value) &&
                            value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
                        {
                            filters.Add($"{name}={value}");
                        }
                    }
                }

                var suffix = filters.Count == 0 ? "" : " (" + string.Join(", ", filters) + ")";
                return $"Search returned {returned.GetInt32()} call(s){suffix}.";
            }

            if (e.TryGetProperty("severity", out _) && e.TryGetProperty("id", out var hookId))
            {
                var dry = e.TryGetProperty("dryRun", out var dryEl) && dryEl.ValueKind == JsonValueKind.True;
                var mode = dry ? "dry run (no URL configured)" : $"HTTP {Fmt(e, "statusCode")}";
                return $"Webhook {hookId.GetString()} ({Prop(e, "severity")}): {mode}. {Prop(e, "title")}";
            }
        }
        catch (JsonException)
        {
            return Trim(content, 700);
        }

        return Trim(content, 700);
    }

    private static string Prop(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) ? value.ToString() : "?";

    private static string Fmt(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined
            ? value.ToString()
            : "n/a";

    private static Dictionary<string, object?> ExtractFilters(string question)
    {
        var filters = new Dictionary<string, object?>
        {
            ["from"] = "2026-11-24T04:00:00Z",
            ["to"] = "2026-11-24T08:00:00Z",
            ["limit"] = 50
        };

        var trunk = Regex.Match(question, @"trunk-[\w-]+", RegexOptions.IgnoreCase);
        if (trunk.Success)
        {
            filters["trunk"] = trunk.Value.ToLowerInvariant();
        }

        var sip = Regex.Match(question, @"\b(404|408|480|486|487|500|503)\b");
        if (sip.Success)
        {
            filters["sip_code"] = int.Parse(sip.Value);
        }

        if (Regex.IsMatch(question, @"\bbusy\b", RegexOptions.IgnoreCase))
        {
            filters["disposition"] = "busy";
            filters.TryAdd("sip_code", 486);
        }
        else if (Regex.IsMatch(question, @"\bno[- ]?answer\b", RegexOptions.IgnoreCase))
        {
            filters["disposition"] = "no_answer";
        }
        else if (Regex.IsMatch(question, @"\bcancel", RegexOptions.IgnoreCase))
        {
            filters["disposition"] = "cancelled";
        }
        else if (Regex.IsMatch(question, @"\bfail|incident|outage|503", RegexOptions.IgnoreCase))
        {
            filters["disposition"] = "failed";
        }

        var callee = Regex.Match(question, @"\+\d{8,15}");
        if (callee.Success)
        {
            filters["callee"] = callee.Value;
        }

        return filters;
    }

    private static string GuessGroupBy(string question)
    {
        if (Regex.IsMatch(question, @"hangup|cause", RegexOptions.IgnoreCase))
        {
            return "hangup_cause";
        }

        // Do not treat a trunk *name* (trunk-du-backup) as "group by trunk".
        if (Regex.IsMatch(question, @"\b(by trunk|per trunk|which trunk|across trunks|each trunk)\b", RegexOptions.IgnoreCase))
        {
            return "trunk";
        }

        return "sip_code";
    }

    private static string GuessSeverity(string question) =>
        Regex.IsMatch(question, @"critical|503|outage", RegexOptions.IgnoreCase) ? "critical" : "warning";

    private static bool WantsSummary(string question) =>
        Regex.IsMatch(question, @"why|summar|aggregat|break ?down|how many|what('s| is) wrong", RegexOptions.IgnoreCase);

    private static bool WantsWebhook(string question) =>
        Regex.IsMatch(question, @"webhook|alert|page|notify|post a ", RegexOptions.IgnoreCase);

    private static string? FindCallId(string question)
    {
        var match = Regex.Match(question, @"cdr-20261124-\d{4}", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToLowerInvariant() : null;
    }

    private static void CollectCallIds(JsonElement element, List<string> ids)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    if ((prop.NameEquals("callId") || prop.NameEquals("call_id")) &&
                        prop.Value.ValueKind == JsonValueKind.String &&
                        !string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    {
                        ids.Add(prop.Value.GetString()!);
                    }
                    else if ((prop.NameEquals("callIds") || prop.NameEquals("call_ids")) &&
                             prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in prop.Value.EnumerateArray())
                        {
                            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                            {
                                ids.Add(item.GetString()!);
                            }
                        }
                    }
                    else
                    {
                        CollectCallIds(prop.Value, ids);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectCallIds(item, ids);
                }

                break;
        }
    }

    private static ToolCall Call(string name, object args) =>
        new($"script-{name}-{Guid.NewGuid():N}"[..32], name, JsonSerializer.Serialize(args));

    private static string Trim(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
