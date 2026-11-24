using System.Text.Json;
using SipOpsAgent.Cdr;

namespace SipOpsAgent.Tools;

internal static class CallLogQueryParser
{
    public static CallLogQuery Parse(JsonElement args, int defaultLimit = 25)
    {
        return new CallLogQuery(
            From: JsonArgs.GetTime(args, "from", "start", "started_after"),
            To: JsonArgs.GetTime(args, "to", "end", "started_before"),
            Caller: JsonArgs.GetString(args, "caller"),
            Callee: JsonArgs.GetString(args, "callee"),
            Direction: ParseDirection(JsonArgs.GetString(args, "direction")),
            Disposition: ParseDisposition(JsonArgs.GetString(args, "disposition")),
            SipCode: JsonArgs.GetInt(args, "sip_code", "sipCode", "status_code"),
            Trunk: JsonArgs.GetString(args, "trunk"),
            HangupCause: JsonArgs.GetString(args, "hangup_cause", "hangupCause", "cause"),
            Limit: JsonArgs.GetInt(args, "limit") ?? defaultLimit);
    }

    private static CallDirection? ParseDirection(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "in" or "inbound" => CallDirection.Inbound,
            "out" or "outbound" => CallDirection.Outbound,
            null or "" => null,
            _ => throw new ArgumentException($"Unknown direction '{raw}'. Use inbound or outbound.")
        };

    private static CallDisposition? ParseDisposition(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "answered" or "ok" => CallDisposition.Answered,
            "no-answer" or "no_answer" or "noanswer" or "unanswered" => CallDisposition.NoAnswer,
            "busy" => CallDisposition.Busy,
            "failed" or "fail" or "error" => CallDisposition.Failed,
            "cancelled" or "canceled" or "cancel" => CallDisposition.Cancelled,
            null or "" => null,
            _ => throw new ArgumentException($"Unknown disposition '{raw}'.")
        };

    public static Dictionary<string, object> FilterProperties() => new()
    {
        ["from"] = new { type = "string", description = "ISO-8601 start of the window (inclusive). Sample data is 2026-11-24T04:00:00Z to 2026-11-24T08:00:00Z." },
        ["to"] = new { type = "string", description = "ISO-8601 end of the window (inclusive)." },
        ["caller"] = new { type = "string", description = "Substring match on the caller number." },
        ["callee"] = new { type = "string", description = "Substring match on the callee number." },
        ["direction"] = new { type = "string", @enum = new[] { "inbound", "outbound" } },
        ["disposition"] = new { type = "string", @enum = new[] { "answered", "no_answer", "busy", "failed", "cancelled" } },
        ["sip_code"] = new { type = "integer", description = "SIP response code, for example 486 or 503." },
        ["trunk"] = new { type = "string", description = "Substring match on the trunk name (trunk-du-primary, trunk-du-backup, trunk-intl)." },
        ["hangup_cause"] = new { type = "string", description = "FreeSWITCH-style hangup cause, for example USER_BUSY or NORMAL_TEMPORARY_FAILURE." },
        ["limit"] = new { type = "integer", description = "Max rows to return (1-100). Default 25." }
    };

    public static object FilterSchema() => new { type = "object", properties = FilterProperties() };
}
