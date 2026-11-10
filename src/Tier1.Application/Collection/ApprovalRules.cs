using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;

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
