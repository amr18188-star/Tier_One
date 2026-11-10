using Tier1.Application.Access;
using Tier1.Domain.Access;

namespace Tier1.Tests;

public class EffectiveAccessTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid User = Guid.NewGuid();

    private static AssignedRole Role(string code, params RoleGrant[] grants) => Role(code, [], null, grants);

    private static AssignedRole Role(string code, string[] scope, DateTimeOffset? validTo, params RoleGrant[] grants) =>
        new(code, code == "SA", grants, new UserRole { UserId = User, ValidFrom = Now.AddDays(-1), ValidTo = validTo, ScopeValues = [.. scope], ApprovedAt = Now.AddDays(-1) });

    private static RoleGrant Grant(string feature, string caps, AccessScope scope, params string[] specials) =>
        new() { Feature = feature, Caps = PermissionCatalog.ParseCaps(caps), Scope = scope, Specials = [.. specials] };

    [Fact]
    public void Roles_union_takes_the_widest_scope_per_capability()
    {
        var a = EffectiveAccessCalculator.Compute(User,
        [
            Role("DEAN", ["ENG"], null, Grant("kpi.dictionary", "VPE", AccessScope.Unit)),
            Role("PM", Grant("kpi.dictionary", "VP", AccessScope.All)),
        ], [], Now);
        Assert.Equal(AccessScope.All, a.ScopeFor("kpi.dictionary", Cap.View));
        Assert.Equal(AccessScope.Unit, a.ScopeFor("kpi.dictionary", Cap.Edit));
        Assert.Contains("ENG", a.ScopeValues("kpi.dictionary", Cap.Edit));
        Assert.Empty(a.ScopeValues("kpi.dictionary", Cap.View));
        Assert.False(a.Has("kpi.dictionary", Cap.Delete));
    }

    [Fact]
    public void Unit_scope_values_stay_with_their_capability()
    {
        // دور يعطي العرض في x وآخر يعطي التعديل في y لا يعطيان التعديل في x.
        var a = EffectiveAccessCalculator.Compute(User,
        [
            Role("A", ["x"], null, Grant("kpi.dictionary", "V", AccessScope.Unit)),
            Role("B", ["y"], null, Grant("kpi.dictionary", "VE", AccessScope.Unit)),
        ], [], Now);
        Assert.Equal(["y"], a.ScopeValues("kpi.dictionary", Cap.Edit));
        Assert.Equal(["x", "y"], a.ScopeValues("kpi.dictionary", Cap.View).Order());
    }

    [Fact]
    public void An_action_is_scoped_by_its_own_capability_not_by_the_wider_view()
    {
        // لجنة كلية (تعديل في الهندسة) واطلاع (عرض الكل): يرى كلية التجارة ولا يعدل فيها.
        var a = EffectiveAccessCalculator.Compute(User,
        [
            Role("CTC", ["ENG"], null, Grant("org.structure", "VE", AccessScope.Unit)),
            Role("PRES", Grant("org.structure", "V", AccessScope.All)),
        ], [], Now);
        var view = UnitScope.For(a, "org.structure", Cap.View);
        var edit = UnitScope.For(a, "org.structure", Cap.Edit);
        Assert.True(view.Covers("/CU/COM/"));
        Assert.True(edit.Covers("/CU/ENG/"));
        Assert.True(edit.Covers("/CU/ENG/ENG-CIV/"));
        Assert.False(edit.Covers("/CU/COM/"));
        Assert.False(edit.Covers("/CU/"));
    }

    [Fact]
    public void Super_admin_assignment_waiting_for_approval_grants_nothing()
    {
        var pending = new AssignedRole("SA", true, [], new UserRole { UserId = User, ValidFrom = Now.AddDays(-1), ApprovedAt = null });
        var a = EffectiveAccessCalculator.Compute(User, [pending], [], Now);
        Assert.False(a.IsSuperAdmin);
        Assert.Empty(a.Features);
    }

    [Fact]
    public void Deny_override_wins_over_role_grants()
    {
        var a = EffectiveAccessCalculator.Compute(User,
            [Role("DEAN", Grant("kpi.dictionary", "VPEAD", AccessScope.All))],
            [new UserOverride { Feature = "kpi.dictionary", Caps = Cap.Delete, Effect = OverrideEffect.Deny, ValidFrom = Now.AddHours(-1) }],
            Now);
        Assert.True(a.Has("kpi.dictionary", Cap.Edit));
        Assert.False(a.Has("kpi.dictionary", Cap.Delete));
    }

    [Fact]
    public void Expired_assignment_and_override_are_ignored()
    {
        var a = EffectiveAccessCalculator.Compute(User,
            [Role("DEAN", [], Now.AddMinutes(-1), Grant("kpi.dictionary", "V", AccessScope.All))],
            [new UserOverride { Feature = "dash.home", Caps = Cap.View, Effect = OverrideEffect.Allow, ValidFrom = Now.AddDays(-2), ValidTo = Now.AddDays(-1) }],
            Now);
        Assert.Empty(a.Features);
        Assert.Empty(a.RoleCodes);
    }

    [Fact]
    public void Super_admin_access_comes_from_its_matrix_grants_and_ignores_overrides()
    {
        var full = RoleRules.FullGrants().Select(g => new RoleGrant { Feature = g.Feature, Caps = g.Caps, Scope = g.Scope, Specials = [.. g.Specials] }).ToArray();
        var a = EffectiveAccessCalculator.Compute(User, [Role("SA", full)], [new UserOverride { Feature = "adm.roles", Caps = Cap.Edit, Effect = OverrideEffect.Deny, ValidFrom = Now.AddDays(-1) }], Now);
        Assert.True(a.IsSuperAdmin);
        Assert.Equal(PermissionCatalog.Features.Count, a.Features.Count);
        Assert.True(a.HasSpecial("adm.roles", Specials.Approve));
        Assert.True(a.Has("adm.roles", Cap.Edit));
        Assert.False(a.Has("log.audit", Cap.Edit));

        // منح أضيق من المصفوفة يعني صلاحيات أضيق، ويبقى «مديرًا أعلى» في قواعد الفصل.
        var narrow = EffectiveAccessCalculator.Compute(User, [Role("SA", Grant("adm.roles", "VE", AccessScope.All, "approve"))], [], Now);
        Assert.True(narrow.IsSuperAdmin);
        Assert.False(narrow.Has("dash.home", Cap.View));
    }

    [Fact]
    public void Capabilities_or_specials_outside_the_catalog_are_ignored()
    {
        var a = EffectiveAccessCalculator.Compute(User,
            [Role("X", Grant("log.audit", "VPEAD", AccessScope.All, "export", "approve"), Grant("no.such", "V", AccessScope.All))],
            [], Now);
        Assert.False(a.Has("log.audit", Cap.Delete));
        Assert.True(a.HasSpecial("log.audit", "export"));
        Assert.False(a.HasSpecial("log.audit", "approve"));
        Assert.False(a.Features.ContainsKey("no.such"));
    }

    [Fact]
    public void Covers_checks_capability_scope_and_specials()
    {
        var a = EffectiveAccessCalculator.Compute(User, [Role("PRES", Grant("kpi.dictionary", "VPAD", AccessScope.Unit, "approve"))], [], Now);
        Assert.True(a.Covers("kpi.dictionary", Cap.View, AccessScope.Unit, []));
        Assert.False(a.Covers("kpi.dictionary", Cap.View, AccessScope.All, []));
        Assert.False(a.Covers("kpi.dictionary", Cap.View | Cap.Print, AccessScope.Own, ["decide"]));
        Assert.False(a.Covers("kpi.conflicts", Cap.Edit, AccessScope.Own, []));
    }
}
