using Tier1.Application.Access;
using Tier1.Application.Accounts;
using Tier1.Application.Collection;
using Tier1.Application.Settings;
using Tier1.Domain.Access;
using Tier1.Domain.Common;

namespace Tier1.Tests;

/// <summary>
/// D11 (ADR-0007): two-step verification follows what a user can actually decide, not a fixed list of role names, once the
/// super administrator turns the switch on; the core security roles always need it; only a super administrator flips the switch.
/// </summary>
public class MfaPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();
    private static readonly AuthOptions On = new() { RequireMfa = true };

    private static AssignedRole Role(string code, string feature, string caps, AccessScope scope, params string[] specials) =>
        new(code, code == "SA", [new RoleGrant { Feature = feature, Caps = PermissionCatalog.ParseCaps(caps), Scope = scope, Specials = [.. specials] }],
            new UserRole { UserId = User, ValidFrom = Now.AddDays(-1), ScopeValues = ["ENG"], ApprovedAt = Now.AddDays(-1) });

    private static EffectiveAccess Of(params AssignedRole[] roles) => EffectiveAccessCalculator.Compute(User, roles, [], Now);

    public static TheoryData<string, EffectiveAccess> DecisionHoldersOutsideTheOldList => new()
    {
        { "QA final approval", Of(Role("QA", Features.Forms, "VP", AccessScope.All, ApprovalRules.ApproveFinal)) },
        { "Dean", Of(Role("DEAN", Features.Forms, "VP", AccessScope.Unit, ApprovalRules.ApproveCollege, ApprovalRules.ApproveAsDeanDirectly)) },
        { "College committee", Of(Role("CTC", Features.Forms, "VPE", AccessScope.Unit, ApprovalRules.ReviewCollege)) },
        { "Department rep", Of(Role("DREP", Features.Forms, "VPE", AccessScope.Unit, ApprovalRules.ReviewDepartment)) },
        { "Tier 1 officer (official university values)", Of(Role("T1O", Features.University, "VE", AccessScope.All)) },
        { "A custom role with a decision", Of(Role("CUSTOM_REVIEWERS", Features.Forms, "V", AccessScope.Unit, ApprovalRules.ReviewCollege)) },
    };

    [Theory]
    [MemberData(nameof(DecisionHoldersOutsideTheOldList))]
    public void Decision_holders_need_it_only_when_the_switch_is_on(string who, EffectiveAccess access)
    {
        Assert.False(MfaPolicy.Requires(access, On, forDecisions: false), $"{who} with the switch off");
        Assert.True(MfaPolicy.Requires(access, On, forDecisions: true), $"{who} with the switch on");
    }

    [Fact]
    public void An_individual_override_granting_a_decision_counts_too()
    {
        var a = EffectiveAccessCalculator.Compute(User, [Role("DE", Features.Forms, "VPE", AccessScope.Unit)],
            [new UserOverride { Feature = Features.Forms, Caps = Cap.None, Scope = AccessScope.All, Specials = [ApprovalRules.ApproveFinal], Effect = OverrideEffect.Allow, ValidFrom = Now.AddDays(-1) }], Now);
        Assert.True(MfaPolicy.Requires(a, On, forDecisions: true));
    }

    [Fact]
    public void Data_entry_and_viewers_never_need_it()
    {
        Assert.False(MfaPolicy.Requires(Of(Role("DE", Features.Forms, "VPE", AccessScope.Unit)), On, forDecisions: true));
        Assert.False(MfaPolicy.Requires(Of(Role("VIEW", Features.Forms, "V", AccessScope.Unit)), On, forDecisions: true));
    }

    [Fact]
    public void Core_security_roles_always_need_it_and_the_switch_cannot_relax_them()
    {
        foreach (var code in new[] { "SA", "SYS", "AUD", "PRES", "HC", "PM" })
            Assert.True(MfaPolicy.Requires(Of(Role(code, "dash.home", "V", AccessScope.All)), On, forDecisions: false), code);
    }

    [Fact]
    public void Development_without_mfa_needs_nothing()
    {
        var off = new AuthOptions { RequireMfa = false };
        Assert.False(MfaPolicy.Requires(Of(Role("SA", "dash.home", "V", AccessScope.All)), off, forDecisions: true));
        Assert.False(MfaPolicy.Requires(Of(Role("QA", Features.Forms, "VP", AccessScope.All, ApprovalRules.ApproveFinal)), off, forDecisions: true));
    }

    [Fact]
    public void Managing_access_is_sensitive_viewing_it_is_not()
    {
        Assert.True(MfaPolicy.Requires(Of(Role("ACCESS_ADMIN", "adm.users", "VE", AccessScope.All)), On, forDecisions: true));
        Assert.False(MfaPolicy.Requires(Of(Role("ACCESS_VIEW", "adm.users", "V", AccessScope.All)), On, forDecisions: true));
    }

    [Fact]
    public void Only_a_super_administrator_flips_the_switch()
    {
        var mfaSwitch = SettingKeys.Find(SettingKeys.MfaForDecisions)!;
        Assert.True(mfaSwitch.SuperAdminOnly && mfaSwitch.IsSwitch && mfaSwitch.Default == 0);
        SettingsService.EnsureCanEdit(mfaSwitch, Of(Role("SA", "adm.settings", "VE", AccessScope.All)));
        var president = Of(Role("PRES", "adm.settings", "VE", AccessScope.All));
        Assert.Equal("sod_sod_10", Assert.Throws<DomainException>(() => SettingsService.EnsureCanEdit(mfaSwitch, president)).Code);
        // Other settings stay editable by whoever may edit settings.
        SettingsService.EnsureCanEdit(SettingKeys.Find(SettingKeys.NearTargetPercent)!, president);
    }
}
