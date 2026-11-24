namespace SipOpsAgent.Cdr;

public interface ICallLogStore
{
    IReadOnlyList<CallRecord> All { get; }

    CallRecord? GetById(string callId);

    IReadOnlyList<CallRecord> Search(CallLogQuery query);

    FailureSummary Summarize(CallLogQuery query, string groupBy);
}

public sealed record FailureGroup(string Key, int Count, double Share);

public sealed record FailureSummary(
    int Matched,
    string GroupBy,
    int Answered,
    int NoAnswer,
    int Busy,
    int Failed,
    int Cancelled,
    IReadOnlyList<FailureGroup> Groups);
