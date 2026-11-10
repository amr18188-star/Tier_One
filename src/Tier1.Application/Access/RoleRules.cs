using Tier1.Domain.Access;
using Tier1.Domain.Common;

namespace Tier1.Application.Access;

/// <summary>منح مقترح لخاصية في دور.</summary>
public sealed record GrantInput(string Feature, Cap Caps, AccessScope Scope, IReadOnlyList<string> Specials);

/// <summary>
/// قيود ثابتة على منح الأدوار لا تتجاوزها المصفوفة:
/// <list type="bullet">
/// <item>المدير الأعلى يعدل من المصفوفة (D6) لكن يبقى له جوهر الإدارة، فلا تقفل المنظومة على نفسها.</item>
/// <item>اعتماد المصفوفة وتعيين كلمة مؤقتة للمدير الأعلى وحده (SOD-10).</item>
/// <item>مسؤول النظام لا يرى محتوى المعاملات ولا يعتمد شيئًا (SOD-11).</item>
/// </list>
/// المراجع الرقابي دور كغيره منذ قرار D6.
/// </summary>
public static class RoleRules
{
    public const string AuditorRole = "AUD";
    public const string SystemAdminRole = "SYS";

    /// <summary>
    /// جوهر إدارة المدير الأعلى: لو سُحب لما بقي من يعتمد المصفوفة أو يسند الأدوار أو يفك قفل حساب،
    /// ولا يعاد إلا بتدخل مباشر في القاعدة.
    /// </summary>
    public static readonly IReadOnlyList<(string Feature, Cap Caps, string[] Specials)> SuperAdminCore =
    [
        ("adm.roles", Cap.View | Cap.Edit, ["approve"]),
        ("adm.grants", Cap.View | Cap.Add | Cap.Delete, []),
        ("adm.users", Cap.View | Cap.Edit, ["set_password"]),
        ("log.security", Cap.View, []),
    ];

    /// <summary>وحدات محتوى العمل (المؤشرات والمستهدفات واللوحات) التي لا تمنح لمسؤول النظام.</summary>
    private static readonly HashSet<string> BusinessModules = new(StringComparer.Ordinal) { "dash", "kpi" };

    /// <summary>
    /// خصائص محتوى خارج تلك الوحدات لا تمنح لمسؤول النظام أيضًا: أعضاء الهياكل وبياناتهم، وسجل العمليات والاطلاع.
    /// قائمة الجهات نفسها بيانات مرجعية يديرها مسؤول النظام.
    /// </summary>
    private static readonly HashSet<string> BusinessFeatures = new(StringComparer.Ordinal) { "org.structure", "log.audit", "log.access" };

    /// <summary>ينظف المنح ويتحقق منها؛ يرمي خطأ يشرح أول مخالفة.</summary>
    public static IReadOnlyList<GrantInput> Validate(string roleCode, bool roleLocked, IEnumerable<GrantInput> grants)
    {
        if (roleLocked)
            throw new DomainException("role_locked", "دور المدير الأعلى يملك كل الصلاحيات دائمًا ولا يُعدَّل من المصفوفة.", "The super administrator role always has every permission and cannot be edited.", 409);
        var result = new List<GrantInput>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in grants)
        {
            var def = PermissionCatalog.Find(g.Feature)
                ?? throw new DomainException("unknown_feature", $"الخاصية «{g.Feature}» غير موجودة في الكتالوج.", $"Feature '{g.Feature}' is not in the catalog.", 400);
            if (!seen.Add(g.Feature))
                throw new DomainException("duplicate_feature", $"الخاصية «{def.Name.Ar}» مكررة في الطلب.", $"Feature '{g.Feature}' appears twice.", 400);
            if ((g.Caps & ~def.AllowedCaps) != Cap.None)
                throw new DomainException("invalid_cap", $"إمكانية لا معنى لها في «{def.Name.Ar}».", $"A capability that does not exist in '{g.Feature}' was requested.", 400);
            if (!Enum.IsDefined(g.Scope))
                throw new DomainException("invalid_scope", "النطاق غير صالح.", "Invalid scope.", 400);
            var specials = (g.Specials ?? []).Distinct(StringComparer.Ordinal).ToList();
            var unknown = specials.FirstOrDefault(s => !def.HasSpecial(s));
            if (unknown is not null)
                throw new DomainException("invalid_special", $"الإجراء الخاص «{unknown}» غير معرّف في «{def.Name.Ar}».", $"Special action '{unknown}' is not defined in '{g.Feature}'.", 400);
            if (g.Caps == Cap.None && specials.Count == 0) continue;

            if (roleCode != PermissionCatalog.SuperAdminRole && specials.FirstOrDefault(s => PermissionCatalog.IsSuperAdminOnly(def.Key, s)) is { } reserved)
                throw DomainException.SeparationOfDuties("SOD-10", $"الإجراء «{def.Specials.First(s => s.Key == reserved).Name.Ar}» للمدير الأعلى وحده ولا يمنح لدور آخر.", $"'{reserved}' in '{def.Key}' belongs to super administrators only and cannot be granted to another role.");

            if (roleCode == SystemAdminRole && (BusinessModules.Contains(def.Module) || BusinessFeatures.Contains(def.Key)))
                throw DomainException.SeparationOfDuties("SOD-11", $"مسؤول النظام لا يرى محتوى المعاملات: «{def.Name.Ar}».", $"The system administrator cannot see transaction content: '{g.Feature}'.");
            if (roleCode == SystemAdminRole && specials.Any(PermissionCatalog.DecisionSpecials.Contains))
                throw DomainException.SeparationOfDuties("SOD-11", "مسؤول النظام لا يعتمد شيئًا ماليًا أو أكاديميًا.", "The system administrator cannot approve financial or academic decisions.");

            result.Add(g with { Specials = specials });
        }
        if (roleCode == PermissionCatalog.SuperAdminRole)
        {
            foreach (var (feature, caps, required) in SuperAdminCore)
            {
                var g = result.FirstOrDefault(x => x.Feature == feature);
                if (g is null || (g.Caps & caps) != caps || g.Scope != AccessScope.All || required.Any(s => !g.Specials.Contains(s)))
                    throw DomainException.SeparationOfDuties("SOD-10",
                        $"لا يُسحب من المدير الأعلى جوهر الإدارة في «{PermissionCatalog.Get(feature).Name.Ar}»، وإلا لم يبق من يعتمد الصلاحيات.",
                        $"The super administrator must keep the administration core in '{feature}', or nobody could approve permissions any more.");
            }
        }
        return result;
    }

    /// <summary>منح المدير الأعلى الافتراضي: كل إمكانية وكل إجراء في كل خاصية، بنطاق الكل.</summary>
    public static IReadOnlyList<GrantInput> FullGrants() =>
        PermissionCatalog.Features.Select(f => new GrantInput(f.Key, f.AllowedCaps, AccessScope.All, f.Specials.Select(s => s.Key).ToList())).ToList();

    /// <summary>الفرق بين منح حالية ومقترحة لكل خاصية تغيرت.</summary>
    public static IReadOnlyList<GrantDiff> Diff(IEnumerable<GrantInput> before, IEnumerable<GrantInput> after)
    {
        var b = before.ToDictionary(g => g.Feature, StringComparer.Ordinal);
        var a = after.ToDictionary(g => g.Feature, StringComparer.Ordinal);
        var diffs = new List<GrantDiff>();
        foreach (var key in b.Keys.Union(a.Keys).OrderBy(k => k, StringComparer.Ordinal))
        {
            b.TryGetValue(key, out var old);
            a.TryGetValue(key, out var neu);
            if (old is not null && neu is not null && old.Caps == neu.Caps && old.Scope == neu.Scope && old.Specials.Order().SequenceEqual(neu.Specials.Order()))
                continue;
            diffs.Add(new GrantDiff(key, old is null ? null : GrantView.From(old), neu is null ? null : GrantView.From(neu)));
        }
        return diffs;
    }
}

/// <summary>منح بصيغة العرض والتخزين: حروف الإمكانيات ورمز النطاق.</summary>
public sealed record GrantView(string Caps, string Scope, IReadOnlyList<string> Specials)
{
    public static GrantView From(GrantInput g) => new(PermissionCatalog.Letters(g.Caps), ScopeCode(g.Scope), [.. g.Specials.Order(StringComparer.Ordinal)]);

    public static string ScopeCode(AccessScope s) => s switch
    {
        AccessScope.All => "A",
        AccessScope.Unit => "U",
        AccessScope.Assigned => "S",
        _ => "O",
    };

    /// <summary>قيم ناقصة أو غير معروفة ترمي FormatException، فتعاد للمستخدم خطأ 400 لا 500.</summary>
    public GrantInput ToInput(string feature) =>
        new(feature ?? throw new FormatException("feature"), PermissionCatalog.ParseCaps(Caps ?? throw new FormatException("caps")),
            PermissionCatalog.ParseScope(Scope ?? throw new FormatException("scope")), Specials?.Where(s => s is not null).ToList() ?? []);
}

public sealed record GrantDiff(string Feature, GrantView? Before, GrantView? After);
