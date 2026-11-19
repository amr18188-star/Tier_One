using Tier1.Application.Collection;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;

namespace Tier1.Tests;

/// <summary>D4 (ADR-0004): reopening an approved form is a reasoned request decided by the higher committee, never by its requester.</summary>
public class ReopenRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Planner = Guid.NewGuid();
    private static readonly Guid Committee = Guid.NewGuid();

    private static Submission Form(SubmissionStatus status) => new() { Status = status, Step = status == SubmissionStatus.Approved ? ApprovalStep.Done : ApprovalStep.Entry, ApprovedAt = Now.AddDays(-3) };
    private static CollectionCycle Cycle(CycleStatus status) => new() { Status = status };
    private static ReopenRequest Request(Submission form, ReopenRequestStatus status = ReopenRequestStatus.Pending, int daysAgo = 1) =>
        new() { SubmissionId = form.Id, RequestedBy = Planner, Status = status, Reason = "تصحيح مؤشر", DecidedAt = status == ReopenRequestStatus.Pending ? null : Now.AddDays(-daysAgo) };

    [Fact]
    public void Only_an_approved_form_in_an_open_cycle_without_a_pending_request_can_be_requested()
    {
        ReopenRules.EnsureCanRequest(Form(SubmissionStatus.Approved), Cycle(CycleStatus.Open), hasPending: false);
        Assert.Equal("not_approved", Assert.Throws<DomainException>(() => ReopenRules.EnsureCanRequest(Form(SubmissionStatus.InReview), Cycle(CycleStatus.Open), false)).Code);
        Assert.Equal("cycle_not_open", Assert.Throws<DomainException>(() => ReopenRules.EnsureCanRequest(Form(SubmissionStatus.Approved), Cycle(CycleStatus.Closed), false)).Code);
        Assert.Equal("reopen_pending", Assert.Throws<DomainException>(() => ReopenRules.EnsureCanRequest(Form(SubmissionStatus.Approved), Cycle(CycleStatus.Open), true)).Code);
    }

    [Fact]
    public void The_requester_never_decides_and_a_decided_request_is_final()
    {
        var form = Form(SubmissionStatus.Approved);
        ReopenRules.EnsureCanDecide(Request(form), Committee);
        Assert.Equal("sod_sod_r1", Assert.Throws<DomainException>(() => ReopenRules.EnsureCanDecide(Request(form), Planner)).Code);
        Assert.Equal("reopen_decided", Assert.Throws<DomainException>(() => ReopenRules.EnsureCanDecide(Request(form, ReopenRequestStatus.Rejected), Committee)).Code);
    }

    [Fact]
    public void A_form_under_correction_counts_with_the_snapshot_of_its_latest_approved_request()
    {
        var form = Form(SubmissionStatus.InProgress);
        var older = Request(form, ReopenRequestStatus.Approved, daysAgo: 30);
        var latest = Request(form, ReopenRequestStatus.Approved, daysAgo: 1);
        var rejected = Request(form, ReopenRequestStatus.Rejected, daysAgo: 0);
        Assert.Equal(latest.Id, ReopenRules.OfficialSnapshot(form, [older, rejected, latest]));
    }

    [Fact]
    public void An_approved_form_counts_with_its_live_values()
    {
        var form = Form(SubmissionStatus.Approved);
        Assert.Null(ReopenRules.OfficialSnapshot(form, [Request(form, ReopenRequestStatus.Approved)]));
        // A form that was never reopened has no snapshot either.
        Assert.Null(ReopenRules.OfficialSnapshot(Form(SubmissionStatus.InReview), []));
    }

    [Fact]
    public void The_snapshot_keeps_the_value_exactly_as_approved()
    {
        var form = Form(SubmissionStatus.Approved);
        var request = Request(form);
        var value = new SubmittedValue { SubmissionId = form.Id, IndicatorId = Guid.NewGuid(), Kind = ValueKind.Number, Value = 82.5m, Numerator = 165, Denominator = 200, Explanation = "م", EditedBy = Planner, EditedAt = Now };
        var snap = ReopenRules.Snapshot(request, form, value);
        Assert.Equal((request.Id, form.Id, value.IndicatorId, 82.5m, 165m, 200m, form.ApprovedAt), (snap.RequestId, snap.SubmissionId, snap.IndicatorId, snap.Value!.Value, snap.Numerator!.Value, snap.Denominator!.Value, snap.ApprovedAt));
        var back = ReopenRules.AsValue(snap);
        Assert.Equal((value.Kind, value.Value, value.Numerator, value.Denominator), (back.Kind, back.Value, back.Numerator, back.Denominator));
    }
}
