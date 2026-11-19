using Tier1.Application.Access;
using Tier1.Application.Collection;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Organization;

namespace Tier1.Tests;

/// <summary>
/// D2: الاعتماد المباشر اختيار صريح بجانب الاعتماد العادي، لا يفرض نفسه؛ ومن يملك أي قرار على الخطوة يملك الإعادة؛
/// وبريد «ينتظر مراجعتك» لمراجعي الخطوة العاديين وحدهم.
/// </summary>
public class DirectApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();
    private static readonly OrgUnit Eng = new() { Code = "ENG", Kind = OrgUnitKind.College, Path = "/U/ENG/" };
    private static readonly OrgUnit EngCiv = new() { Code = "ENG-CIV", Kind = OrgUnitKind.Department, Path = "/U/ENG/ENG-CIV/" };
    private static readonly OrgUnit Central = new() { Code = "QAC", Kind = OrgUnitKind.Administration, Path = "/U/QAC/" };

    private static AssignedRole Role(string code, string unit, params string[] specials) =>
        new(code, false, [new RoleGrant { Feature = Features.Forms, Caps = Cap.View, Scope = AccessScope.Unit, Specials = [.. specials] }],
            new UserRole { UserId = User, ValidFrom = Now.AddDays(-1), ScopeValues = [unit], ApprovedAt = Now.AddDays(-1) });

    private static AssignedRole Dean => Role("DEAN", "ENG", ApprovalRules.ApproveCollege, ApprovalRules.ApproveAsDeanDirectly);
    private static AssignedRole Committee => Role("CTC", "ENG", ApprovalRules.ReviewCollege, ApprovalRules.ApproveDepartmentDirectly);
    private static AssignedRole DeptRep => Role("DREP", "ENG-CIV", ApprovalRules.ReviewDepartment);

    private static EffectiveAccess Of(params AssignedRole[] roles) => EffectiveAccessCalculator.Compute(User, roles, [], Now);

    [Fact]
    public void Holding_both_paths_offers_both_and_forces_neither()
    {
        // كان العميد عضو اللجنة يحال نموذجه إلى الجودة دائمًا ولا يستطيع اعتماد خطوة اللجنة عاديًا (CR-3).
        var a = Of(Dean, Committee);
        Assert.True(ApprovalRules.MayApprove(a, Eng, ApprovalStep.CollegeCommittee));
        Assert.Equal(ApprovalStep.Quality, ApprovalRules.DirectApprovalTo(a, Eng, ApprovalStep.CollegeCommittee));
        Assert.Equal(ApprovalStep.Dean, ApprovalRules.Next(Eng.Kind, ApprovalStep.CollegeCommittee));

        // لجنة كلية هي ممثل القسم أيضًا: تعتمد خطوة القسم عاديًا، أو تحيل مباشرة إلى العميد.
        var b = Of(Committee, DeptRep);
        Assert.True(ApprovalRules.MayApprove(b, EngCiv, ApprovalStep.DepartmentReview));
        Assert.Equal(ApprovalStep.Dean, ApprovalRules.DirectApprovalTo(b, EngCiv, ApprovalStep.DepartmentReview));
    }

    [Fact]
    public void A_direct_only_approver_may_decide_and_therefore_return()
    {
        var dean = Of(Dean);
        Assert.False(ApprovalRules.MayApprove(dean, EngCiv, ApprovalStep.DepartmentReview));
        Assert.True(ApprovalRules.MayReview(dean, EngCiv, ApprovalStep.DepartmentReview));
        Assert.Equal(ApprovalStep.Quality, ApprovalRules.DirectApprovalTo(dean, EngCiv, ApprovalStep.DepartmentReview));
    }

    [Fact]
    public void Review_mail_goes_to_the_step_reviewers_only()
    {
        // MayReview بلا المسار المباشر هو شرط المستلمين في FormNotifier.
        Assert.False(ApprovalRules.MayReview(Of(Dean), EngCiv, ApprovalStep.DepartmentReview, allowDirect: false));
        Assert.False(ApprovalRules.MayReview(Of(Committee), EngCiv, ApprovalStep.DepartmentReview, allowDirect: false));
        Assert.True(ApprovalRules.MayReview(Of(DeptRep), EngCiv, ApprovalStep.DepartmentReview, allowDirect: false));
    }

    [Fact]
    public void Direct_paths_exist_only_where_the_policy_defines_them()
    {
        var a = Of(Dean, Committee);
        // لا مسار مباشر من خطوة العميد نفسها، ولا للجهات المركزية، ولا للجنة الكلية من خطوة اللجنة.
        Assert.Null(ApprovalRules.DirectApprovalTo(a, Eng, ApprovalStep.Dean));
        Assert.Null(ApprovalRules.DirectApprovalTo(a, Central, ApprovalStep.Quality));
        Assert.Null(ApprovalRules.DirectApprovalTo(Of(Committee), Eng, ApprovalStep.CollegeCommittee));
        Assert.False(ApprovalRules.MayApprove(a, Eng, ApprovalStep.Entry));
        Assert.False(ApprovalRules.MayApprove(a, Eng, ApprovalStep.Done));
    }

    [Fact]
    public void History_marks_exactly_the_skipped_steps()
    {
        SubmissionEvent Approved(ApprovalStep from, ApprovalStep to) => new() { Action = SubmissionAction.Approved, FromStep = from, ToStep = to };
        var deanFromDept = Approved(ApprovalStep.DepartmentReview, ApprovalStep.Quality);
        Assert.True(ApprovalRules.SkippedBy(deanFromDept, ApprovalStep.DepartmentReview));
        Assert.True(ApprovalRules.SkippedBy(deanFromDept, ApprovalStep.CollegeCommittee));
        Assert.False(ApprovalRules.SkippedBy(deanFromDept, ApprovalStep.Dean));
        var committeeFromDept = Approved(ApprovalStep.DepartmentReview, ApprovalStep.Dean);
        Assert.True(ApprovalRules.SkippedBy(committeeFromDept, ApprovalStep.DepartmentReview));
        Assert.False(ApprovalRules.SkippedBy(committeeFromDept, ApprovalStep.CollegeCommittee));
        Assert.False(ApprovalRules.SkippedBy(Approved(ApprovalStep.DepartmentReview, ApprovalStep.CollegeCommittee), ApprovalStep.DepartmentReview));
    }
}
