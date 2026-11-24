namespace SipOpsAgent.Cdr;

public sealed record CallLogQuery(
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Caller = null,
    string? Callee = null,
    CallDirection? Direction = null,
    CallDisposition? Disposition = null,
    int? SipCode = null,
    string? Trunk = null,
    string? HangupCause = null,
    int Limit = 25);
