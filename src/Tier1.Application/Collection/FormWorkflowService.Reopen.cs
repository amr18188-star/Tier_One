using Microsoft.EntityFrameworkCore;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Kpi;
using Tier1.Application.Notifications;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;

namespace Tier1.Application.Collection;

/// <summary>طلب إعادة الفتح على النموذج: آخر طلب معلق أو مقرر، لعرضه في صفحة النموذج.</summary>
public sealed record FormReopenDto(
    Guid Id, ReopenRequestStatus Status, string Reason, string? RequestedByName, DateTimeOffset RequestedAt,
    string? DecidedByName, DateTimeOffset? DecidedAt, string? DecisionRef, string? DecisionNote);

/// <summary>القيمة كما اعتمدت قبل إعادة فتح النموذج (D4): تبقى الرسمية في النتائج حتى يعاد اعتماده.</summary>
public sealed record ApprovedValueDto(ValueKind Kind, decimal? Value, decimal? Numerator, decimal? Denominator, string? Reason, DateTimeOffset? ApprovedAt);

/// <summary>
/// إعادة فتح النموذج المعتمد بطلب وقرار (D4، ADR-0004): من يملك «إعادة الفتح» يطلب بسبب والنموذج باق معتمدًا؛
/// واللجنة العليا تعتمد برقم المحضر فيفتح وتحفظ قيمه المعتمدة في لقطة، أو ترفض بسبب. لا يقرر الطلب مقدمه.
/// </summary>
public sealed partial class FormWorkflowService
{
    public async Task<FormDto> RequestReopenAsync(Guid id, FormTransitionRequest req, CancellationToken ct)
    {
        var me = await access.RequireSpecialAsync(Features.Cycles, ApprovalRules.Reopen, ct);
        var ctx = await reader.LoadAsync(id, Cap.View, tracking: true, ct);
        // G-16 (system integration review 2026-11-24): the special's own unit scope, not only the view scope of the form.
        UnitScope.ForSpecial(me, Features.Cycles, ApprovalRules.Reopen).Require(ctx.Unit);
        Concurrency.Transition(ctx.Submission.Version, req.Version);
        var reason = Require.Reason(req.Note, 5, "reason_required", 2000);
        var pending = await db.ReopenRequests.AnyAsync(r => r.SubmissionId == id && r.Status == ReopenRequestStatus.Pending, ct);
        ReopenRules.EnsureCanRequest(ctx.Submission, ctx.Cycle, pending);
        var request = new ReopenRequest
        {
            SubmissionId = id, RequestedBy = current.UserId!.Value, RequestedByName = current.DisplayName, RequestedAt = clock.UtcNow, Reason = reason,
        };
        db.ReopenRequests.Add(request);
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.reopen_requested", "طلب إعادة فتح نموذج معتمد", "Requested reopening an approved form") with
        {
            Target = new AuditTarget("reopen_request", request.Id.ToString(), ctx.Unit.Code),
            Related = [new AuditTarget("submission", id.ToString(), ctx.Unit.Code)],
            Reason = reason,
        });
        // G-04: the higher committee decides; it was not told (the requester never decides their own request, SOD-R1).
        events.Notify(new WorkNotice("dc.reopen", $"طلب إعادة فتح نموذج {ctx.Unit.NameAr} — {ctx.Cycle.Name}",
            $"طلب {current.DisplayName} إعادة فتح نموذج {ctx.Unit.NameAr} المعتمد في دورة «{ctx.Cycle.Name}» بالسبب:\n{reason}\nاعتمد الطلب برقم المحضر أو ارفضه بسبب.",
            $"/forms/{id}", new WorkAudience(Feature: Features.Cycles, Special: ApprovalRules.ApproveReopen, UnitId: ctx.Unit.Id, Except: [current.UserId!.Value])),
            "reopen_request", request.Id);
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(ctx, ct);
    }

    public async Task<FormDto> ApproveReopenAsync(Guid id, Kpi.CommitteeDecisionRequest req, CancellationToken ct)
    {
        var me = await access.RequireSpecialAsync(Features.Cycles, ApprovalRules.ApproveReopen, ct);
        var ctx = await reader.LoadAsync(id, Cap.View, tracking: true, ct);
        // G-16 (system integration review 2026-11-24): the special's own unit scope, not only the view scope of the form.
        UnitScope.ForSpecial(me, Features.Cycles, ApprovalRules.ApproveReopen).Require(ctx.Unit);
        var request = await PendingRequestAsync(id, ct);
        ReopenRules.EnsureCanDecide(request, current.UserId!.Value);
        var decision = Require.Optional(req.DecisionRef, 200)
            ?? throw new DomainException("decision_ref_required", "اعتماد إعادة الفتح يحتاج رقم محضر اللجنة العليا.", "Approving a reopen needs the committee's decision reference.", 400) { Field = "decisionRef" };
        // الطلب قدم والنموذج معتمد في دورة مفتوحة؛ يعاد الفحص لأن الحال قد تغير منذ الطلب.
        ReopenRules.EnsureCanRequest(ctx.Submission, ctx.Cycle, hasPending: false);

        var now = clock.UtcNow;
        foreach (var v in ctx.Values.Values) db.ValueSnapshots.Add(ReopenRules.Snapshot(request, ctx.Submission, v));
        request.Status = ReopenRequestStatus.Approved;
        request.DecidedBy = current.UserId;
        request.DecidedByName = current.DisplayName;
        request.DecidedAt = now;
        request.DecisionRef = decision;
        request.DecisionNote = Require.Optional(req.Note, 2000);

        Move(ctx, SubmissionAction.Reopened, ApprovalStep.Entry, $"{request.Reason} — قرار اللجنة العليا: {decision}");
        ctx.Submission.ApprovedAt = null;
        ctx.Submission.ReturnNote = request.Reason;
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.reopen_approved", "اعتماد طلب إعادة فتح نموذج", "Approved a request to reopen a form") with
        {
            Target = new AuditTarget("reopen_request", request.Id.ToString(), ctx.Unit.Code),
            Related = [new AuditTarget("submission", id.ToString(), ctx.Unit.Code)],
            Reason = request.Reason,
            Details = new { decision, snapshot = ctx.Values.Count },
        });
        RaiseMoved(ctx);
        events.Notify(new WorkNotice("dc.reopen", $"اعتُمد طلبك إعادة فتح نموذج {ctx.Unit.NameAr} — {ctx.Cycle.Name}",
            $"اعتمدت اللجنة العليا إعادة فتح نموذج {ctx.Unit.NameAr} بالقرار {decision}. عاد النموذج إلى الجهة للتصحيح.",
            $"/forms/{id}", new WorkAudience(Users: [request.RequestedBy])), "reopen_request", request.Id);
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(ctx, ct);
    }

    public async Task<FormDto> RejectReopenAsync(Guid id, NoteRequest req, CancellationToken ct)
    {
        var me = await access.RequireSpecialAsync(Features.Cycles, ApprovalRules.ApproveReopen, ct);
        var ctx = await reader.LoadAsync(id, Cap.View, tracking: true, ct);
        // G-16 (system integration review 2026-11-24): the special's own unit scope, not only the view scope of the form.
        UnitScope.ForSpecial(me, Features.Cycles, ApprovalRules.ApproveReopen).Require(ctx.Unit);
        var request = await PendingRequestAsync(id, ct);
        ReopenRules.EnsureCanDecide(request, current.UserId!.Value);
        request.Status = ReopenRequestStatus.Rejected;
        request.DecidedBy = current.UserId;
        request.DecidedByName = current.DisplayName;
        request.DecidedAt = clock.UtcNow;
        request.DecisionNote = Require.Reason(req.Note, 5, "reason_required", 2000);
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.reopen_rejected", "رفض طلب إعادة فتح نموذج", "Rejected a request to reopen a form") with
        {
            Target = new AuditTarget("reopen_request", request.Id.ToString(), ctx.Unit.Code),
            Related = [new AuditTarget("submission", id.ToString(), ctx.Unit.Code)],
            Reason = request.DecisionNote,
        });
        events.Notify(new WorkNotice("dc.reopen", $"رُفض طلبك إعادة فتح نموذج {ctx.Unit.NameAr} — {ctx.Cycle.Name}",
            $"رفضت اللجنة العليا طلب إعادة فتح نموذج {ctx.Unit.NameAr} بالسبب:\n{request.DecisionNote}",
            $"/forms/{id}", new WorkAudience(Users: [request.RequestedBy])), "reopen_request", request.Id);
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(ctx, ct);
    }

    private async Task<ReopenRequest> PendingRequestAsync(Guid formId, CancellationToken ct) =>
        await db.ReopenRequests.FirstOrDefaultAsync(r => r.SubmissionId == formId && r.Status == ReopenRequestStatus.Pending, ct)
        ?? throw new DomainException("no_pending_reopen", "لا طلب إعادة فتح معلقًا لهذا النموذج.", "This form has no pending reopen request.", 409);
}
