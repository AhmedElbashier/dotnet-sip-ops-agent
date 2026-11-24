namespace SipOpsAgent.Cdr;

public enum CallDirection
{
    Inbound,
    Outbound
}

public enum CallDisposition
{
    Answered,
    NoAnswer,
    Busy,
    Failed,
    Cancelled
}

public sealed record CallRecord(
    string CallId,
    string SipCallId,
    DateTimeOffset StartedAt,
    DateTimeOffset? AnsweredAt,
    DateTimeOffset EndedAt,
    CallDirection Direction,
    string Caller,
    string Callee,
    string Trunk,
    CallDisposition Disposition,
    int SipCode,
    string HangupCause,
    int DurationSeconds,
    double? Mos,
    double? PacketLossPercent)
{
    public object ToSummary() => new
    {
        callId = CallId,
        sipCallId = SipCallId,
        startedAt = StartedAt,
        direction = Direction.ToString(),
        caller = Caller,
        callee = Callee,
        trunk = Trunk,
        disposition = Disposition.ToString(),
        sipCode = SipCode,
        hangupCause = HangupCause,
        durationSeconds = DurationSeconds,
        mos = Mos
    };
}
