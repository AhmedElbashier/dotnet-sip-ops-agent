using SipOpsAgent.Cdr;

namespace SipOpsAgent.Tests;

public class CallLogStoreTests
{
    private readonly InMemoryCallLogStore _store = new();

    [Fact]
    public void Sample_data_has_the_documented_invariants()
    {
        Assert.Equal(48, _store.All.Count);
        Assert.All(_store.All, r =>
        {
            Assert.InRange(r.StartedAt, SampleData.WindowStart, SampleData.WindowEnd);
        });

        var incident = _store.Search(new CallLogQuery(
            From: DateTimeOffset.Parse("2026-11-24T05:15:00Z"),
            To: DateTimeOffset.Parse("2026-11-24T05:40:00Z"),
            Trunk: SampleData.IncidentTrunk,
            SipCode: 503,
            Limit: 100));
        Assert.Equal(12, incident.Count);
        Assert.All(incident, r =>
        {
            Assert.Equal(CallDisposition.Failed, r.Disposition);
            Assert.Equal("NORMAL_TEMPORARY_FAILURE", r.HangupCause);
            Assert.Equal(CallDirection.Outbound, r.Direction);
        });

        Assert.Equal(5, _store.Search(new CallLogQuery(SipCode: 486, Limit: 100)).Count);
        Assert.Equal(4, _store.Search(new CallLogQuery(Disposition: CallDisposition.NoAnswer, Limit: 100)).Count);
        Assert.Equal(3, _store.Search(new CallLogQuery(Disposition: CallDisposition.Cancelled, Limit: 100)).Count);
        Assert.Equal(2, _store.Search(new CallLogQuery(SipCode: 404, Trunk: "trunk-intl", Limit: 100)).Count);
        Assert.Equal(2, _store.Search(new CallLogQuery(SipCode: 408, Limit: 100)).Count);
    }

    [Fact]
    public void GetById_accepts_internal_id_and_sip_call_id()
    {
        var byInternal = _store.GetById(SampleData.FeaturedCallId);
        Assert.NotNull(byInternal);
        Assert.Equal(4.2, byInternal!.Mos);
        Assert.Equal(187, byInternal.DurationSeconds);
        Assert.Equal("+971500100042", byInternal.Caller);
        Assert.Equal("trunk-du-primary", byInternal.Trunk);

        var bySip = _store.GetById("8f3c1a2b-42@fs-01.sip-ops.example");
        Assert.Equal(byInternal.CallId, bySip?.CallId);
        Assert.Null(_store.GetById("does-not-exist"));
    }

    [Fact]
    public void Search_filters_by_callee_substring_and_caps_limit()
    {
        var rows = _store.Search(new CallLogQuery(Callee: "+971400100100", Limit: 3));
        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Contains("+971400100100", r.Callee, StringComparison.Ordinal));
    }

    [Fact]
    public void Poor_quality_call_is_present()
    {
        var call = _store.GetById(SampleData.PoorQualityCallId);
        Assert.NotNull(call);
        Assert.Equal(2.1, call!.Mos);
        Assert.Equal(8.5, call.PacketLossPercent);
        Assert.Equal(CallDisposition.Answered, call.Disposition);
    }

    [Fact]
    public void Summarize_groups_the_backup_trunk_incident_by_sip_code()
    {
        var summary = _store.Summarize(
            new CallLogQuery(Trunk: "trunk-du-backup", Disposition: CallDisposition.Failed, Limit: 100),
            "sip_code");
        Assert.True(summary.Failed >= 12);
        var sip503 = summary.Groups.Single(g => g.Key == "503");
        Assert.Equal(12, sip503.Count);
        Assert.True(sip503.Share >= 0.8);
    }

    [Fact]
    public void Summarize_can_group_by_hangup_cause_and_trunk()
    {
        var byCause = _store.Summarize(new CallLogQuery(SipCode: 486, Limit: 100), "hangup_cause");
        Assert.Equal(5, byCause.Busy);
        Assert.Equal("USER_BUSY", byCause.Groups.Single().Key);

        var byTrunk = _store.Summarize(new CallLogQuery(SipCode: 503, Limit: 100), "trunk");
        Assert.Contains(byTrunk.Groups, g => g.Key == "trunk-du-backup" && g.Count == 12);
    }
}
