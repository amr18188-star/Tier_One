using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;
using Tier1.Application.Access;

namespace Tier1.Application.Collection;

/// <summary>
/// مسار اعتماد النموذج بحسب نوع الجهة (دالة نقية):
/// القسم: الإدخال ← ممثل القسم ← لجنة الكلية ← العميد ← لجنة Tier 1 بالجامعة.
/// الكلية: الإدخال ← لجنة الكلية ← العميد ← الجودة. الجهات المركزية: الإدخال ← الجودة.
/// </summary>
public static class ApprovalRules
{
    public const string ReviewDepartment = "review_department";
    public const string ReviewCollege = "review_college";
    public const string ApproveDepartmentDirectly = "approve_department_directly";
    public const string ApproveAsDeanDirectly = "approve_as_dean_directly";
    public const string ApproveCollege = "approve_college";
    public const string ApproveFinal = "approve_final";
    /// <summary>طلب إعادة فتح نموذج معتمد (dc.cycles).</summary>
    public const string Reopen = "reopen";

    /// <summary>اعتماد طلب إعادة الفتح أو رفضه، للجنة العليا (dc.cycles، D4).</summary>
    public const string ApproveReopen = "approve_reopen";

    public static IReadOnlyList<ApprovalStep> PathFor(OrgUnitKind kind) => kind switch
    {
        OrgUnitKind.Department => [ApprovalStep.Entry, ApprovalStep.DepartmentReview, ApprovalStep.CollegeCommittee, ApprovalStep.Dean, ApprovalStep.Quality],
        OrgUnitKind.College or OrgUnitKind.Institute => [ApprovalStep.Entry, ApprovalStep.CollegeCommittee, ApprovalStep.Dean, ApprovalStep.Quality],
        _ => [ApprovalStep.Entry, ApprovalStep.Quality],
    };

    public static ApprovalStep Next(OrgUnitKind kind, ApprovalStep current)
    {
        var path = PathFor(kind);
        var i = path.ToList().IndexOf(current);
        if (i < 0) throw new InvalidOperationException($"{current} is not on the path of {kind}.");
        return i + 1 < path.Count ? path[i + 1] : ApprovalStep.Done;
    }

    /// <summary>الإجراء الخاص الذي يملكه من يراجع هذه الخطوة.</summary>
    public static string SpecialFor(ApprovalStep step) => step switch
    {
        ApprovalStep.DepartmentReview => ReviewDepartment,
        ApprovalStep.CollegeCommittee => ReviewCollege,
        ApprovalStep.Dean => ApproveCollege,
        ApprovalStep.Quality => ApproveFinal,
        _ => throw new InvalidOperationException($"{step} has no reviewer."),
    };

    /// <summary>
    /// هل يملك المستخدم هذا الإجراء على هذه الجهة؟ يقاس بنطاق الإجراء نفسه (نطاق الدور الذي منحه)، لا بنطاق العرض:
    /// من يحمل لجنة كلية في الهندسة ودورًا آخر يوسع عرضه لا يعتمد نماذج كلية أخرى (D1).
    /// </summary>
    public static bool Holds(EffectiveAccess access, string special, OrgUnit unit) =>
        UnitScope.ForSpecial(access, Features.Forms, special).Covers(unit);

    /// <summary>
    /// إلى أين ينقل الاعتماد المباشر النموذج من هذه الخطوة إن كان المستخدم يملكه على هذه الجهة، وإلا null.
    /// الاعتماد المباشر اختيار صريح من المعتمد بسبب مكتوب (D2)، ولا يفرض نفسه على الاعتماد العادي لمن يملك الاثنين.
    /// العميد يحيل نموذج القسم أو الكلية من خطوتي القسم واللجنة إلى الجودة؛ ولجنة الكلية تحيل نموذج القسم من خطوة القسم إلى العميد.
    /// من يملك المسارين معًا يأخذ الأبعد (العميد).
    /// </summary>
    public static ApprovalStep? DirectApprovalTo(EffectiveAccess access, OrgUnit unit, ApprovalStep step)
    {
        var kind = unit.Kind;
        var departmentReview = kind == OrgUnitKind.Department && step == ApprovalStep.DepartmentReview;
        var collegeReview = kind is OrgUnitKind.Department or OrgUnitKind.College or OrgUnitKind.Institute
            && step == ApprovalStep.CollegeCommittee;
        if (!departmentReview && !collegeReview) return null;
        if (Holds(access, ApproveAsDeanDirectly, unit)) return ApprovalStep.Quality;
        return departmentReview && Holds(access, ApproveDepartmentDirectly, unit) ? ApprovalStep.Dean : null;
    }

    /// <summary>الاعتماد العادي: إجراء مراجعة الخطوة نفسها على الجهة، فينتقل النموذج إلى الخطوة التالية في مساره.</summary>
    public static bool MayApprove(EffectiveAccess access, OrgUnit unit, ApprovalStep step) =>
        step is not (ApprovalStep.Entry or ApprovalStep.Done) && Holds(access, SpecialFor(step), unit);

    /// <summary>
    /// من يملك قرارًا على النموذج في خطوته: اعتمادها عاديًا، أو مباشرًا إن سمح allowDirect. ومن يملك القرار يملك الإعادة للتصحيح.
    /// </summary>
    public static bool MayReview(EffectiveAccess access, OrgUnit unit, ApprovalStep step, bool allowDirect = true) =>
        MayApprove(access, unit, step) || (allowDirect && DirectApprovalTo(access, unit, step) is not null);

    public static bool SkippedBy(SubmissionEvent e, ApprovalStep step) => (e.Action, e.FromStep, e.ToStep, step) switch
    {
        (SubmissionAction.Approved, ApprovalStep.DepartmentReview, ApprovalStep.Dean, ApprovalStep.DepartmentReview) => true,
        (SubmissionAction.Approved, ApprovalStep.DepartmentReview, ApprovalStep.Quality, ApprovalStep.DepartmentReview or ApprovalStep.CollegeCommittee) => true,
        (SubmissionAction.Approved, ApprovalStep.CollegeCommittee, ApprovalStep.Quality, ApprovalStep.CollegeCommittee) => true,
        _ => false,
    };

    public static string StepName(ApprovalStep step) => step switch
    {
        ApprovalStep.Entry => "إدخال البيانات",
        ApprovalStep.DepartmentReview => "ممثل Tier 1 بالقسم",
        ApprovalStep.CollegeCommittee => "لجنة Tier 1 بالكلية",
        ApprovalStep.Dean => "عميد الكلية",
        ApprovalStep.Quality => "لجنة Tier 1 بالجامعة",
        _ => "معتمد",
    };

    /// <summary>
    /// فصل المهام: من أدخل قيمة في النموذج لا يراجعه ولا يعتمده، ومن اعتمد خطوة لا يعتمد خطوة أخرى للنموذج نفسه.
    /// </summary>
    public static void EnsureSeparation(Guid reviewer, IEnumerable<Guid> editors, IEnumerable<SubmissionEvent> history, DateTimeOffset? submittedAt = null)
    {
        switch (SeparationBreach(reviewer, editors, history, submittedAt))
        {
            case "SOD-C1":
                throw DomainException.SeparationOfDuties("SOD-C1", "من أدخل بيانات النموذج لا يراجعه ولا يعتمده.", "Whoever entered the form's data cannot review or approve it.");
            case "SOD-C2":
                throw DomainException.SeparationOfDuties("SOD-C2", "اعتمدت خطوة سابقة لهذا النموذج؛ يعتمد الخطوة التالية شخص آخر.", "You approved an earlier step of this form; another person approves this one.");
        }
    }

    /// <summary>
    /// ARCH-6 and CR-4: the one rule for who may decide on a form, used by the forms list ("awaiting me"), the form page and the
    /// server check. The round under review starts at the last submission: an approval in an earlier round (before the form was
    /// returned and sent again) does not bar the same person now. Without a submission time every approval counts.
    /// An approval always follows the submission it reviews, so the boundary is exact (a one-second slack let a quick earlier round count).
    /// </summary>
    public static bool InCurrentRound(SubmissionEvent e, DateTimeOffset? submittedAt) => submittedAt is not { } at || e.At >= at;

    /// <summary>The separation of duties the reviewer would break: SOD-C1 (entered the data), SOD-C2 (approved a step this round), or null.</summary>
    public static string? SeparationBreach(Guid reviewer, IEnumerable<Guid> editors, IEnumerable<SubmissionEvent> events, DateTimeOffset? submittedAt) =>
        editors.Contains(reviewer) ? "SOD-C1"
        : events.Any(e => e.Action == SubmissionAction.Approved && e.By == reviewer && InCurrentRound(e, submittedAt)) ? "SOD-C2"
        : null;

    /// <summary>Whether the user has a decision on the form now: it is in review, the user reviews its step (or approves past it), and no separation is broken.</summary>
    public static bool CanDecide(EffectiveAccess access, OrgUnit unit, Submission form, Guid reviewer, IEnumerable<Guid> editors, IEnumerable<SubmissionEvent> events) =>
        form.Status == SubmissionStatus.InReview && MayReview(access, unit, form.Step) && SeparationBreach(reviewer, editors, events, form.SubmittedAt) is null;

    /// <summary>ARCH-6: the audit verb of a transition, next to the path it describes.</summary>
    public static (string Verb, string Ar, string En) AuditVerb(SubmissionAction action, ApprovalStep from, ApprovalStep to) => action switch
    {
        SubmissionAction.Submitted => ("dc.form_submitted", "إرسال نموذج جهة للمراجعة", "Submitted a unit form"),
        SubmissionAction.Approved when from == ApprovalStep.DepartmentReview && to == ApprovalStep.Dean
            => ("dc.form_approved_department_directly", "اعتماد مباشر لنموذج القسم من لجنة الكلية", "College committee approved a department form directly"),
        SubmissionAction.Approved when to == ApprovalStep.Quality && from is ApprovalStep.DepartmentReview or ApprovalStep.CollegeCommittee
            => ("dc.form_approved_as_dean_directly", "اعتماد مباشر من العميد وإحالة للجنة Tier 1 بالجامعة", "Dean approved the form directly and forwarded it to central quality"),
        SubmissionAction.Approved => (to == ApprovalStep.Done ? "dc.form_approved_final" : "dc.form_approved", to == ApprovalStep.Done ? "الاعتماد النهائي لنموذج جهة" : "اعتماد خطوة في نموذج جهة", "Approved a unit form step"),
        SubmissionAction.Returned => ("dc.form_returned", "إعادة نموذج جهة للتصحيح", "Returned a unit form"),
        _ => ("dc.form_reopened", "إعادة فتح نموذج معتمد", "Reopened an approved form"),
    };
}
