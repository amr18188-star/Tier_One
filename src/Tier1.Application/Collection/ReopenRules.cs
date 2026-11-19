using Tier1.Domain.Collection;
using Tier1.Domain.Common;

namespace Tier1.Application.Collection;

/// <summary>
/// إعادة فتح النموذج المعتمد (D4، ADR-0004)، دوال نقية:
/// طلب مسبب ممن يملك «إعادة الفتح» على نموذج معتمد في دورة مفتوحة، ثم قرار اللجنة العليا برقم المحضر أو رفض بسبب، ولا يقرر الطلب مقدمه.
/// وما دام النموذج قيد التصحيح تبقى قيمه المعتمدة السابقة هي الرسمية في النتائج.
/// </summary>
public static class ReopenRules
{
    public static void EnsureCanRequest(Submission form, CollectionCycle cycle, bool hasPending)
    {
        if (form.Status != SubmissionStatus.Approved)
            throw new DomainException("not_approved", "يطلب إعادة فتح النموذج المعتمد فقط.", "Only an approved form can be reopened.");
        if (cycle.Status != CycleStatus.Open)
            throw new DomainException("cycle_not_open", "الدورة مغلقة؛ إعادة فتح نموذج في دورة مغلقة قرار مستقل لم يعتمد بعد.", "The cycle is closed.");
        if (hasPending)
            throw new DomainException("reopen_pending", "لهذا النموذج طلب إعادة فتح ينتظر قرار اللجنة العليا.", "This form already has a pending reopen request.", 409);
    }

    public static void EnsureCanDecide(ReopenRequest request, Guid decider)
    {
        if (request.Status != ReopenRequestStatus.Pending)
            throw new DomainException("reopen_decided", "قُرّر هذا الطلب من قبل.", "This request has already been decided.", 409);
        if (request.RequestedBy == decider)
            throw DomainException.SeparationOfDuties("SOD-R1", "من طلب إعادة الفتح لا يقرر طلبه.", "Whoever requested reopening cannot decide the request.");
    }

    /// <summary>
    /// اللقطة الرسمية لنموذج قيد التصحيح: لقطة آخر طلب معتمد إن لم يكن النموذج معتمدًا الآن؛ والنموذج المعتمد قيمه الحية هي الرسمية.
    /// </summary>
    public static Guid? OfficialSnapshot(Submission form, IEnumerable<ReopenRequest> requests) =>
        form.Status == SubmissionStatus.Approved
            ? null
            : requests.Where(r => r.SubmissionId == form.Id && r.Status == ReopenRequestStatus.Approved)
                .OrderByDescending(r => r.DecidedAt).Select(r => (Guid?)r.Id).FirstOrDefault();

    public static ValueSnapshot Snapshot(ReopenRequest request, Submission form, SubmittedValue v) => new()
    {
        RequestId = request.Id, SubmissionId = form.Id, IndicatorId = v.IndicatorId, Kind = v.Kind, Value = v.Value, Numerator = v.Numerator,
        Denominator = v.Denominator, Reason = v.Reason, Explanation = v.Explanation, EditedBy = v.EditedBy, EditedAt = v.EditedAt, ApprovedAt = form.ApprovedAt,
    };

    /// <summary>القيمة الرسمية من اللقطة بشكل القيمة المعتمدة، للنتائج.</summary>
    public static SubmittedValue AsValue(ValueSnapshot s) => new()
    {
        SubmissionId = s.SubmissionId, IndicatorId = s.IndicatorId, Kind = s.Kind, Value = s.Value, Numerator = s.Numerator, Denominator = s.Denominator,
        Reason = s.Reason, Explanation = s.Explanation, EditedBy = s.EditedBy, EditedAt = s.EditedAt,
    };
}
