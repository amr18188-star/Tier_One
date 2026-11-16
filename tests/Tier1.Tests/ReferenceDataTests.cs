using System.Text.Json;
using Tier1.Application.Access;
using Tier1.Domain.Access;

namespace Tier1.Tests;

/// <summary>سلامة البيانات المرجعية المضمنة وكتالوج الصلاحيات قبل أي تشغيل.</summary>
public class ReferenceDataTests
{
    private static JsonElement Seed()
    {
        using var stream = typeof(Tier1.Infrastructure.DependencyInjection).Assembly
            .GetManifestResourceStream("Tier1.Infrastructure.Persistence.Seed.reference-seed.json");
        Assert.NotNull(stream);
        return JsonDocument.Parse(stream!).RootElement.Clone();
    }

    [Fact]
    public void The_seed_has_the_twelve_standards_and_every_extracted_indicator()
    {
        var seed = Seed();
        Assert.Equal(12, seed.GetProperty("standards").GetArrayLength());
        Assert.Equal(108, seed.GetProperty("indicators").GetArrayLength());
        Assert.Equal(49, seed.GetProperty("conflicts").GetArrayLength());
        Assert.Equal(7, seed.GetProperty("structure").GetArrayLength());
    }

    [Fact]
    public void Indicator_codes_are_unique_and_their_conflicts_exist()
    {
        var seed = Seed();
        var codes = seed.GetProperty("indicators").EnumerateArray().Select(i => i.GetProperty("code").GetString()!).ToList();
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        var conflicts = seed.GetProperty("conflicts").EnumerateArray().Select(c => c.GetProperty("code").GetString()).ToHashSet();
        foreach (var i in seed.GetProperty("indicators").EnumerateArray())
            foreach (var c in i.GetProperty("conflicts").EnumerateArray())
                Assert.Contains(c.GetString(), conflicts);
    }

    [Fact]
    public void Every_role_in_the_catalog_exists_and_committee_actions_go_to_the_committee()
    {
        var roles = PermissionCatalog.Roles.Select(r => r.Code).ToHashSet();
        Assert.Contains("HC", roles);
        foreach (var f in PermissionCatalog.Features)
            Assert.All(f.Defaults.Keys, r => Assert.Contains(r, roles));
        Assert.Equal(["HC"], PermissionCatalog.Get(Features.Dictionary).Specials.Single(s => s.Key == Specials.Approve).DefaultRoles);
        Assert.Equal(["HC"], PermissionCatalog.Get(Features.Conflicts).Specials.Single(s => s.Key == Specials.Decide).DefaultRoles);
        // مسؤول النظام لا يعتمد ولا يحسم.
        Assert.DoesNotContain(PermissionCatalog.Features.SelectMany(f => f.Specials), s => s.DefaultRoles.Contains("SYS"));
    }

    [Fact]
    public void Every_role_can_open_the_home_page_because_it_is_everyones_entry_point()
    {
        // الصفحة الرئيسية تنادي /api/home لكل مستخدم؛ دور بلا هذه الخاصية يرى «لا تملك صلاحية» في أول صفحة.
        // المدير الأعلى يأخذ كل الخصائص من RoleRules.FullGrants لا من الافتراضيات.
        var home = PermissionCatalog.Get(Features.Dashboard);
        foreach (var role in PermissionCatalog.Roles.Select(r => r.Code).Where(c => c != PermissionCatalog.SuperAdminRole))
        {
            Assert.True(home.Defaults.TryGetValue(role, out var grant), $"{role} has no home page grant");
            Assert.True(grant!.Caps.HasFlag(Cap.View), $"{role} cannot view the home page");
        }
    }
}
