using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;
using Tier1.Application.Access;

namespace Tier1.Application.Collection;

/// <summary>
/// مسار اعتماد النموذج بحسب نوع الجهة (دالة نقية):
/// القسم: الإدخال ← ممثل القسم ← لجنة الكلية ← العميد ← الجودة المركزية.
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
    public const string Reopen = "reopen";

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

    public static ApprovalStep? DirectApprovalTo(EffectiveAccess access, OrgUnit unit, ApprovalStep step)
    {
        var kind = unit.Kind;
        var departmentReview = kind == OrgUnitKind.Department && step == ApprovalStep.DepartmentReview;
        var collegeReview = kind is OrgUnitKind.Department or OrgUnitKind.College or OrgUnitKind.Institute
            && step == ApprovalStep.CollegeCommittee;
        if (!departmentReview && !collegeReview) return null;

        var canReviewNormally = Holds(access, SpecialFor(step), unit);
        // Preserve ordinary approvals for the super admin, who holds every review permission.
        if (access.IsSuperAdmin && canReviewNormally) return null;
        if (Holds(access, ApproveAsDeanDirectly, unit)) return ApprovalStep.Quality;
        return departmentReview && !canReviewNormally && Holds(access, ApproveDepartmentDirectly, unit)
            ? ApprovalStep.Dean : null;
    }

    public static bool MayReview(EffectiveAccess access, OrgUnit unit, ApprovalStep step, bool allowDirect = true) =>
        Holds(access, SpecialFor(step), unit) || (allowDirect && DirectApprovalTo(access, unit, step) is not null);

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
        ApprovalStep.Quality => "الجودة المركزية",
        _ => "معتمد",
    };

    /// <summary>
    /// فصل المهام: من أدخل قيمة في النموذج لا يراجعه ولا يعتمده، ومن اعتمد خطوة لا يعتمد خطوة أخرى للنموذج نفسه.
    /// </summary>
    public static void EnsureSeparation(Guid reviewer, IEnumerable<Guid> editors, IEnumerable<SubmissionEvent> history)
    {
        if (editors.Contains(reviewer))
            throw DomainException.SeparationOfDuties("SOD-C1", "من أدخل بيانات النموذج لا يراجعه ولا يعتمده.", "Whoever entered the form's data cannot review or approve it.");
        if (history.Any(e => e.Action == SubmissionAction.Approved && e.By == reviewer))
            throw DomainException.SeparationOfDuties("SOD-C2", "اعتمدت خطوة سابقة لهذا النموذج؛ يعتمد الخطوة التالية شخص آخر.", "You approved an earlier step of this form; another person approves this one.");
    }
}
