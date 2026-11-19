using Tier1.Application.Access;
using Tier1.Application.Collection;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Organization;

namespace Tier1.Tests;

/// <summary>
/// D1: صلاحية الاعتماد تقاس بنطاق الإجراء الخاص (نطاق الدور الذي منحه)، لا بنطاق العرض.
/// قبلها كان الإجراء الخاص بلا نطاق، والاعتماد يتحقق من نطاق العرض الذي يتسع للجامعة كلها لمن نطاقه SPU أو ICT،
/// فيعتمد عضو لجنة كلية نماذج كل الكليات.
/// </summary>
public class ScopedApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();

    private static readonly OrgUnit Eng = Unit("ENG", OrgUnitKind.College, "/U/ENG/");
    private static readonly OrgUnit EngCiv = Unit("ENG-CIV", OrgUnitKind.Department, "/U/ENG/ENG-CIV/");
    private static readonly OrgUnit Cs = Unit("CS", OrgUnitKind.College, "/U/CS/");
    private static readonly OrgUnit CsSe = Unit("CS-SE", OrgUnitKind.Department, "/U/CS/CS-SE/");

    private static OrgUnit Unit(string code, OrgUnitKind kind, string path) => new() { Code = code, Kind = kind, Path = path };

    private static AssignedRole Role(string code, string[] scope, params RoleGrant[] grants) =>
        new(code, false, grants, new UserRole { UserId = User, ValidFrom = Now.AddDays(-1), ScopeValues = [.. scope], ApprovedAt = Now.AddDays(-1) });

    private static RoleGrant Forms(string caps, AccessScope scope, params string[] specials) =>
        new() { Feature = Features.Forms, Caps = PermissionCatalog.ParseCaps(caps), Scope = scope, Specials = [.. specials] };

    private static AssignedRole DataEntry(string unit) => Role("DE", [unit], Forms("VPE", AccessScope.Unit));
    private static AssignedRole Committee(string unit) =>
        Role("CTC", [unit], Forms("VPE", AccessScope.Unit, ApprovalRules.ReviewCollege, ApprovalRules.ApproveDepartmentDirectly));
    private static AssignedRole Dean(string unit) =>
        Role("DEAN", [unit], Forms("VP", AccessScope.Unit, ApprovalRules.ApproveCollege, ApprovalRules.ApproveAsDeanDirectly));

    private static EffectiveAccess Compute(params AssignedRole[] roles) =>
        EffectiveAccessCalculator.Compute(User, roles, [], Now, ["SPU", "ICT"]);

    [Fact]
    public void A_committee_member_who_sees_the_whole_university_reviews_only_their_own_college()
    {
        // إدخال في وحدة التخطيط (عرضه الجامعة كلها) + لجنة كلية الهندسة.
        var a = Compute(DataEntry("SPU"), Committee("ENG"));
        Assert.Equal(AccessScope.All, a.ScopeFor(Features.Forms, Cap.View));

        Assert.True(ApprovalRules.MayReview(a, Eng, ApprovalStep.CollegeCommittee));
        Assert.True(ApprovalRules.MayReview(a, EngCiv, ApprovalStep.CollegeCommittee));
        Assert.False(ApprovalRules.MayReview(a, Cs, ApprovalStep.CollegeCommittee));
        Assert.False(ApprovalRules.MayReview(a, CsSe, ApprovalStep.CollegeCommittee));
    }

    [Fact]
    public void A_dean_who_enters_data_elsewhere_cannot_approve_that_college()
    {
        var a = Compute(Dean("ENG"), DataEntry("CS"));
        Assert.True(ApprovalRules.MayReview(a, Eng, ApprovalStep.Dean));
        Assert.False(ApprovalRules.MayReview(a, Cs, ApprovalStep.Dean));
        Assert.False(ApprovalRules.MayReview(a, CsSe, ApprovalStep.Dean));
    }

    [Fact]
    public void Direct_approval_paths_respect_the_scope_too()
    {
        var dean = Compute(Dean("ENG"), DataEntry("SPU"));
        Assert.Equal(ApprovalStep.Quality, ApprovalRules.DirectApprovalTo(dean, EngCiv, ApprovalStep.CollegeCommittee));
        Assert.Null(ApprovalRules.DirectApprovalTo(dean, CsSe, ApprovalStep.CollegeCommittee));
        Assert.False(ApprovalRules.MayReview(dean, CsSe, ApprovalStep.DepartmentReview));

        var committee = Compute(Committee("ENG"));
        Assert.Equal(ApprovalStep.Dean, ApprovalRules.DirectApprovalTo(committee, EngCiv, ApprovalStep.DepartmentReview));
        Assert.Null(ApprovalRules.DirectApprovalTo(committee, CsSe, ApprovalStep.DepartmentReview));
    }

    [Fact]
    public void Whole_university_view_never_widens_a_special_action()
    {
        var a = Compute(Committee("SPU"));
        Assert.Equal(AccessScope.All, a.ScopeFor(Features.Forms, Cap.View));
        Assert.Equal(AccessScope.Unit, a.SpecialScopeFor(Features.Forms, ApprovalRules.ReviewCollege));
        Assert.Equal(["SPU"], a.SpecialScopeValues(Features.Forms, ApprovalRules.ReviewCollege));
        Assert.False(ApprovalRules.MayReview(a, Eng, ApprovalStep.CollegeCommittee));
    }

    [Fact]
    public void A_university_wide_grant_approves_everywhere_and_the_widest_scope_wins()
    {
        var quality = Compute(Role("QA", [], Forms("VP", AccessScope.All, ApprovalRules.ApproveFinal)));
        Assert.True(ApprovalRules.MayReview(quality, Cs, ApprovalStep.Quality));
        Assert.True(ApprovalRules.MayReview(quality, EngCiv, ApprovalStep.Quality));

        var two = Compute(Committee("ENG"), Committee("CS"));
        Assert.True(ApprovalRules.MayReview(two, Eng, ApprovalStep.CollegeCommittee));
        Assert.True(ApprovalRules.MayReview(two, CsSe, ApprovalStep.CollegeCommittee));
    }

    [Fact]
    public void A_deny_override_removes_the_special_and_its_scope()
    {
        var a = EffectiveAccessCalculator.Compute(User, [Committee("ENG")],
            [new UserOverride { Feature = Features.Forms, Caps = Cap.None, Specials = [ApprovalRules.ReviewCollege], Effect = OverrideEffect.Deny, ValidFrom = Now.AddDays(-1) }], Now);
        Assert.Null(a.SpecialScopeFor(Features.Forms, ApprovalRules.ReviewCollege));
        Assert.False(ApprovalRules.MayReview(a, Eng, ApprovalStep.CollegeCommittee, allowDirect: false));
    }

    [Fact]
    public void A_special_held_in_one_unit_cannot_be_granted_university_wide()
    {
        // «لا يمنح غيره ما لا يملك» يشمل نطاق الإجراء الخاص كما يشمل نطاق الإمكانية.
        var a = Compute(Committee("ENG"));
        Assert.True(a.Covers(Features.Forms, Cap.None, AccessScope.Unit, [ApprovalRules.ReviewCollege]));
        Assert.False(a.Covers(Features.Forms, Cap.None, AccessScope.All, [ApprovalRules.ReviewCollege]));
    }
}
