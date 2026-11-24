namespace SipOpsAgent.Cdr;

public sealed class InMemoryCallLogStore : ICallLogStore
{
    private readonly IReadOnlyList<CallRecord> _records;

    public InMemoryCallLogStore() : this(SampleData.Records)
    {
    }

    public InMemoryCallLogStore(IReadOnlyList<CallRecord> records)
    {
        _records = records;
    }

    public IReadOnlyList<CallRecord> All => _records;

    public CallRecord? GetById(string callId)
    {
        if (string.IsNullOrWhiteSpace(callId))
        {
            return null;
        }

        return _records.FirstOrDefault(r =>
            r.CallId.Equals(callId, StringComparison.OrdinalIgnoreCase) ||
            r.SipCallId.Equals(callId, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<CallRecord> Search(CallLogQuery query)
    {
        var limit = query.Limit < 1 ? 1 : Math.Min(query.Limit, 100);
        return Apply(query).Take(limit).ToList();
    }

    public FailureSummary Summarize(CallLogQuery query, string groupBy)
    {
        var matches = Apply(query).ToList();
        var matched = matches.Count;
        var keySelector = GroupKey(groupBy);
        var groups = matches
            .GroupBy(r => keySelector(r), StringComparer.OrdinalIgnoreCase)
            .Select(g => new FailureGroup(
                g.Key,
                g.Count(),
                matched == 0 ? 0 : Math.Round(g.Count() / (double)matched, 4)))
            .OrderByDescending(g => g.Count)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FailureSummary(
            Matched: matched,
            GroupBy: NormalizeGroupBy(groupBy),
            Answered: matches.Count(m => m.Disposition == CallDisposition.Answered),
            NoAnswer: matches.Count(m => m.Disposition == CallDisposition.NoAnswer),
            Busy: matches.Count(m => m.Disposition == CallDisposition.Busy),
            Failed: matches.Count(m => m.Disposition == CallDisposition.Failed),
            Cancelled: matches.Count(m => m.Disposition == CallDisposition.Cancelled),
            Groups: groups);
    }

    private IEnumerable<CallRecord> Apply(CallLogQuery query)
    {
        IEnumerable<CallRecord> q = _records;

        if (query.From is { } from)
        {
            q = q.Where(r => r.StartedAt >= from);
        }

        if (query.To is { } to)
        {
            q = q.Where(r => r.StartedAt <= to);
        }

        if (!string.IsNullOrWhiteSpace(query.Caller))
        {
            q = q.Where(r => r.Caller.Contains(query.Caller, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.Callee))
        {
            q = q.Where(r => r.Callee.Contains(query.Callee, StringComparison.OrdinalIgnoreCase));
        }

        if (query.Direction is { } direction)
        {
            q = q.Where(r => r.Direction == direction);
        }

        if (query.Disposition is { } disposition)
        {
            q = q.Where(r => r.Disposition == disposition);
        }

        if (query.SipCode is { } sip)
        {
            q = q.Where(r => r.SipCode == sip);
        }

        if (!string.IsNullOrWhiteSpace(query.Trunk))
        {
            q = q.Where(r => r.Trunk.Contains(query.Trunk, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.HangupCause))
        {
            q = q.Where(r => r.HangupCause.Contains(query.HangupCause, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderBy(r => r.StartedAt).ThenBy(r => r.CallId);
    }

    private static string NormalizeGroupBy(string? groupBy) =>
        (groupBy ?? "sip_code").Trim().ToLowerInvariant() switch
        {
            "hangup" or "hangup_cause" or "hangupcause" or "cause" => "hangup_cause",
            "trunk" => "trunk",
            "disposition" => "disposition",
            _ => "sip_code"
        };

    private static Func<CallRecord, string> GroupKey(string? groupBy) =>
        NormalizeGroupBy(groupBy) switch
        {
            "hangup_cause" => r => r.HangupCause,
            "trunk" => r => r.Trunk,
            "disposition" => r => r.Disposition.ToString(),
            _ => r => r.SipCode.ToString()
        };
}
