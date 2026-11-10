using Tier1.Application.Access;
using Tier1.Domain.Access;
using Tier1.Domain.Common;

namespace Tier1.Tests;

public class RoleRulesTests
{
    private static GrantInput G(string feature, string caps, AccessScope scope = AccessScope.All, params string[] specials) =>
        new(feature, PermissionCatalog.ParseCaps(caps), scope, specials);

    [Fact]
    public void Super_admin_is_editable_but_keeps_the_administration_core()
    {
        // D6: المدير الأعلى يعدل من المصفوفة؛ سحب أي إمكانية خارج الجوهر مقبول.
        var full = RoleRules.FullGrants().ToList();
        Assert.Equal(PermissionCatalog.Features.Count, RoleRules.Validate("SA", false, full).Count);
        var withoutDashboards = full.Where(g => !g.Feature.StartsWith("dash.", StringComparison.Ordinal)).ToList();
        Assert.Equal(PermissionCatalog.Features.Count - 1, RoleRules.Validate("SA", false, withoutDashboards).Count);
        // سحب اعتماد المصفوفة أو إسناد الأدوار يقفل المنظومة على نفسها، فيرفض.
        var noApprove = full.Select(g => g.Feature == "adm.roles" ? g with { Specials = [] } : g).ToList();
        Assert.Equal("sod_sod_10", Assert.Throws<DomainException>(() => RoleRules.Validate("SA", false, noApprove)).Code);
        var noGrants = full.Where(g => g.Feature != "adm.grants").ToList();
        Assert.Equal("sod_sod_10", Assert.Throws<DomainException>(() => RoleRules.Validate("SA", false, noGrants)).Code);
    }

    [Fact]
    public void Auditor_is_editable_like_any_role()
    {
        Assert.Single(RoleRules.Validate("AUD", false, [G("kpi.dictionary", "VPEAD")]));
        Assert.Single(RoleRules.Validate("AUD", false, [G("kpi.conflicts", "V", AccessScope.All, "decide")]));
    }

    [Fact]
    public void Every_feature_can_be_viewed_and_logs_only_viewed_or_printed()
    {
        foreach (var f in PermissionCatalog.Features)
        {
            Assert.True(f.AllowedCaps.HasFlag(Cap.View), f.Key);
            if (f.Module == "log") Assert.Equal(Cap.View | Cap.Print, f.AllowedCaps);
        }
    }

    [Fact]
    public void System_admin_manages_units_but_cannot_see_indicators_or_members_or_approve()
    {
        Assert.Equal("sod_sod_11", Assert.Throws<DomainException>(() => RoleRules.Validate("SYS", false, [G("kpi.dictionary", "V")])).Code);
        Assert.Equal("sod_sod_11", Assert.Throws<DomainException>(() => RoleRules.Validate("SYS", false, [G("org.structure", "V")])).Code);
        Assert.Equal("sod_sod_11", Assert.Throws<DomainException>(() => RoleRules.Validate("SYS", false, [G("log.audit", "V")])).Code);
        Assert.Single(RoleRules.Validate("SYS", false, [G("org.units", "VPEAD")]));
        Assert.Single(RoleRules.Validate("SYS", false, [G("adm.users", "VPEAD")]));
    }

    [Theory]
    [InlineData("PRES", "adm.roles", "approve")]
    [InlineData("PM", "adm.users", "set_password")]
    public void Super_admin_only_actions_cannot_be_granted_to_any_role(string role, string feature, string special) =>
        Assert.Equal("sod_sod_10", Assert.Throws<DomainException>(() => RoleRules.Validate(role, false, [G(feature, "V", AccessScope.All, special)])).Code);

    [Fact]
    public void Missing_grant_values_are_a_format_error_not_a_crash()
    {
        Assert.Throws<FormatException>(() => new GrantView(null!, "A", []).ToInput("kpi.dictionary"));
        Assert.Throws<FormatException>(() => new GrantView("V", null!, []).ToInput("kpi.dictionary"));
        Assert.Empty(new GrantView("V", "A", null!).ToInput("kpi.dictionary").Specials);
    }

    [Fact]
    public void Edit_or_delete_on_logs_is_rejected_for_every_role() =>
        Assert.Equal("invalid_cap", Assert.Throws<DomainException>(() => RoleRules.Validate("SA", false, [G("log.audit", "VE")])).Code);

    [Fact]
    public void Unknown_feature_and_special_are_rejected()
    {
        Assert.Equal("unknown_feature", Assert.Throws<DomainException>(() => RoleRules.Validate("PM", false, [G("no.such", "V")])).Code);
        Assert.Equal("invalid_special", Assert.Throws<DomainException>(() => RoleRules.Validate("PM", false, [G("kpi.dictionary", "V", AccessScope.All, "decide")])).Code);
    }

    [Fact]
    public void Empty_grants_are_dropped_and_duplicates_rejected()
    {
        Assert.Empty(RoleRules.Validate("PM", false, [G("kpi.dictionary", "")]));
        Assert.Equal("duplicate_feature", Assert.Throws<DomainException>(() => RoleRules.Validate("PM", false, [G("kpi.dictionary", "V"), G("kpi.dictionary", "VP")])).Code);
    }

    [Fact]
    public void Diff_lists_only_changed_features()
    {
        var before = new[] { G("kpi.targets", "VP", AccessScope.Unit), G("org.units", "V"), G("kpi.conflicts", "V") };
        var after = new[] { G("kpi.targets", "VPE", AccessScope.Unit), G("org.units", "V"), G("dash.home", "V") };
        var diff = RoleRules.Diff(before, after);
        Assert.Equal(["dash.home", "kpi.conflicts", "kpi.targets"], diff.Select(d => d.Feature));
        Assert.Null(diff.Single(d => d.Feature == "kpi.conflicts").After);
        Assert.Null(diff.Single(d => d.Feature == "dash.home").Before);
        Assert.Equal("VPE", diff.Single(d => d.Feature == "kpi.targets").After!.Caps);
    }
}
