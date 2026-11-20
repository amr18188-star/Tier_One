using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

/// <summary>
/// ARCH-1: the form's path. Submit, approve (normal or direct), return with notes, extend a deadline and reopen by decision;
/// each move is recorded, audited and raised as form.moved (BE-3) in the same save.
/// </summary>
public sealed partial class FormWorkflowService(ICollectionStore db,  IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, Integration.IEventOutbox events, FormReader reader)
{
    private const string Feature = Features.Forms;

    public async Task<FormDto> SubmitAsync(Guid id, uint? version, CancellationToken ct)
    {
        var ctx = await reader.LoadAsync(id, Cap.Edit, tracking: true, ct);
        FormReader.EnsureEditable(ctx);
        Concurrency.Transition(ctx.Submission.Version, version);
        var blocking = await reader.BlockingAsync(ctx, ct);
        if (blocking.Count > 0)
            throw new DomainException("not_ready", "لا يرسل النموذج قبل استكمال: " + string.Join(" — ", blocking.Take(3)) + (blocking.Count > 3 ? $" (و{blocking.Count - 3} غيرها)" : ""), "The form is not ready.");
        var next = ApprovalRules.Next(ctx.Unit.Kind, ApprovalStep.Entry);
        Move(ctx, SubmissionAction.Submitted, next, null);
        foreach (var v in ctx.Values.Values) v.ReviewNote = null;
        ctx.Submission.SubmittedAt = clock.UtcNow;
        ctx.Submission.ReturnNote = null;
        RaiseMoved(ctx);
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(ctx, ct);
    }

    public async Task<FormDto> ApproveAsync(Guid id, ApproveFormRequest req, CancellationToken ct)
    {
        var ctx = await reader.LoadAsync(id, Cap.View, tracking: true, ct);
        // The version first: a stale screen is told so (409), whoever's turn it is now.
        Concurrency.Transition(ctx.Submission.Version, req.Version);
        await EnsureReviewerAsync(ctx, ct, allowDirect: true);
        // D2: التخطي اختيار صريح بسبب مكتوب يبقى في سجل النموذج؛ والاعتماد العادي لا يتحول مباشرًا من تلقاء نفسه.
        ApprovalStep next;
        string? note;
        if (req.Direct)
        {
            next = ApprovalRules.DirectApprovalTo(ctx.Me, ctx.Unit, ctx.Submission.Step) ?? throw DomainException.Forbidden();
            note = Require.Reason(req.Note, 5, "direct_reason_required", 2000);
        }
        else
        {
            if (!ApprovalRules.MayApprove(ctx.Me, ctx.Unit, ctx.Submission.Step))
                throw new DomainException("direct_only", "لا تملك اعتماد هذه الخطوة؛ اختر «اعتماد مباشر» واكتب سبب التخطي.",
                    "You cannot approve this step; choose direct approval and give the reason for skipping.", 409);
            next = ApprovalRules.Next(ctx.Unit.Kind, ctx.Submission.Step);
            note = Require.Optional(req.Note, 2000);
        }
        Move(ctx, SubmissionAction.Approved, next, note);
        RaiseMoved(ctx);
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(ctx, ct);
    }

    /// <summary>الإعادة للتصحيح بسبب، مع ملاحظة على كل مؤشر يحتاج تعديلًا فيبرز لمدخل البيانات.</summary>
    public async Task<FormDto> ReturnAsync(Guid id, ReturnRequest req, CancellationToken ct)
    {
        var ctx = await reader.LoadAsync(id, Cap.View, tracking: true, ct);
        Concurrency.Transition(ctx.Submission.Version, req.Version);
        // من يملك تمرير النموذج (عاديًا أو مباشرًا) يملك إعادته للتصحيح (D2).
        await EnsureReviewerAsync(ctx, ct, allowDirect: true);
        var note = Require.Reason(req.Note, 5, "note_required", 2000);
        foreach (var item in req.Items ?? [])
            if (ctx.Values.TryGetValue(item.IndicatorId, out var v)) v.ReviewNote = Require.Optional(item.Note, 1000);
        Move(ctx, SubmissionAction.Returned, ApprovalStep.Entry, note);
        ctx.Submission.ReturnNote = note;
        RaiseMoved(ctx);
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(ctx, ct);
    }

    /// <summary>تمديد موعد جهة بعينها بقرار مسجل دون تمديد الدورة للجميع (ضوابط التجربة 8-1 بند 7).</summary>
    public async Task<FormDto> ExtendAsync(Guid id, ExtendRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Features.Cycles, Cap.Edit, ct);
        var ctx = await reader.LoadAsync(id, Cap.View, tracking: true, ct);
        var reason = Require.Reason(req.Reason);
        if (ctx.Cycle.Status != CycleStatus.Open) throw new DomainException("cycle_not_open", "الدورة غير مفتوحة.", "The cycle is not open.");
        if (ctx.Submission.Status == SubmissionStatus.Approved) throw new DomainException("form_approved", "النموذج معتمد؛ لا يحتاج تمديدًا.", "The form is approved.");
        if (req.DueOn <= ctx.Submission.DueOn)
            throw new DomainException("due_not_later", "الموعد الجديد بعد الموعد الحالي.", "The new deadline must be later.", 400);
        var old = ctx.Submission.DueOn;
        ctx.Submission.DueOn = req.DueOn;
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.form_extended", "تمديد موعد نموذج جهة", "Extended a unit form deadline") with
        {
            Target = new AuditTarget("submission", ctx.Submission.Id.ToString(), ctx.Unit.Code, ctx.Unit.Code),
            Reason = reason,
            Changes = new ChangeSet().Add("dueOn", old.ToString("yyyy-MM-dd"), req.DueOn.ToString("yyyy-MM-dd")).ToList(),
        });
        await db.SaveChangesAsync(ct);
        return await reader.BuildAsync(ctx, ct);
    }

    private async Task EnsureReviewerAsync(FormContext ctx, CancellationToken ct, bool allowDirect = false)
    {
        if (ctx.Cycle.Status != CycleStatus.Open) throw new DomainException("cycle_not_open", "الدورة مغلقة.", "The cycle is closed.");
        if (ctx.Submission.Status != SubmissionStatus.InReview)
            throw new DomainException("not_in_review", "النموذج لا ينتظر مراجعة الآن.", "The form is not waiting for review.", 409);
        if (!ApprovalRules.MayReview(ctx.Me, ctx.Unit, ctx.Submission.Step, allowDirect)) throw DomainException.Forbidden();
        var history = await db.SubmissionEvents.AsNoTracking().Where(e => e.SubmissionId == ctx.Submission.Id && e.Action == SubmissionAction.Approved).ToListAsync(ct);
        ApprovalRules.EnsureSeparation(current.UserId!.Value, ctx.Values.Values.Select(v => v.EditedBy), history, ctx.Submission.SubmittedAt);
    }

    /// <summary>BE-3: whoever acts next is told after the commit, by the form.moved handler, not inside this request.</summary>
    private void RaiseMoved(FormContext ctx)
    {
        var s = ctx.Submission;
        events.Add(new FormMoved(s.Id, s.UnitId, s.CycleId, s.Status, s.Step, s.DueOn, s.ReturnNote), "submission", s.Id);
    }

    private void Move(FormContext ctx, SubmissionAction action, ApprovalStep to, string? note)
    {
        var s = ctx.Submission;
        var from = s.Step;
        db.SubmissionEvents.Add(new SubmissionEvent
        {
            SubmissionId = s.Id, Action = action, FromStep = from, ToStep = to, By = current.UserId!.Value, ByName = current.DisplayName, At = clock.UtcNow, Note = note,
        });
        s.Step = to;
        s.Status = to switch
        {
            ApprovalStep.Done => SubmissionStatus.Approved,
            ApprovalStep.Entry => action == SubmissionAction.Returned ? SubmissionStatus.Returned : SubmissionStatus.InProgress,
            _ => SubmissionStatus.InReview,
        };
        if (s.Status == SubmissionStatus.Approved) s.ApprovedAt = clock.UtcNow;
        var (verb, ar, en) = ApprovalRules.AuditVerb(action, from, to);
        audit.Record(AuditEvents.Audit("dc", Feature, verb, ar, en) with
        {
            Target = new AuditTarget("submission", s.Id.ToString(), ctx.Unit.Code, ctx.Unit.Code),
            Reason = note,
            Details = new { from = ApprovalRules.StepName(from), to = ApprovalRules.StepName(to), cycle = ctx.Cycle.Name },
        });
    }
}
