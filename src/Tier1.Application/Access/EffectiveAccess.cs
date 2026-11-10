using Tier1.Domain.Access;

namespace Tier1.Application.Access;

/// <summary>ما يملكه المستخدم في خاصية واحدة بعد جمع أدواره واستثناءاته.</summary>
public sealed class FeatureAccess
{
    public Dictionary<Cap, AccessScope> Caps { get; } = [];

    public HashSet<string> Specials { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// قيم نطاق «نطاقه» لكل إمكانية على حدة: دور يعطي العرض في x وآخر يعطي التعديل في y لا يعطيان التعديل في x.
    /// </summary>
    public Dictionary<Cap, HashSet<string>> CapScopeValues { get; } = [];

    /// <summary>اتحاد قيم النطاق للعرض فقط.</summary>
    public IEnumerable<string> ScopeValues => CapScopeValues.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal);

    public bool IsEmpty => Caps.Count == 0 && Specials.Count == 0;
}

/// <summary>الصلاحيات الفعلية لمستخدم في لحظة معينة.</summary>
public sealed class EffectiveAccess
{
    public EffectiveAccess(Guid userId, bool isSuperAdmin, IReadOnlyList<string> roleCodes, IReadOnlyDictionary<string, FeatureAccess> features)
    {
        UserId = userId;
        IsSuperAdmin = isSuperAdmin;
        RoleCodes = roleCodes;
        Features = features;
    }

    public static EffectiveAccess None(Guid userId) => new(userId, false, [], new Dictionary<string, FeatureAccess>());

    public Guid UserId { get; }

    public bool IsSuperAdmin { get; }

    public IReadOnlyList<string> RoleCodes { get; }

    public IReadOnlyDictionary<string, FeatureAccess> Features { get; }

    public bool Has(string feature, Cap cap) => Features.TryGetValue(feature, out var f) && f.Caps.ContainsKey(cap);

    public bool HasSpecial(string feature, string special) => Features.TryGetValue(feature, out var f) && f.Specials.Contains(special);

    public AccessScope? ScopeFor(string feature, Cap cap) => Features.TryGetValue(feature, out var f) && f.Caps.TryGetValue(cap, out var s) ? s : null;

    /// <summary>قيم «نطاقه» لإمكانية محددة؛ فارغة إن لم يكن نطاقها «نطاقه».</summary>
    public IReadOnlyCollection<string> ScopeValues(string feature, Cap cap) =>
        Features.TryGetValue(feature, out var f) && f.Caps.TryGetValue(cap, out var s) && s == AccessScope.Unit && f.CapScopeValues.TryGetValue(cap, out var v) ? v : [];

    /// <summary>هل يملك المستخدم كل ما في المنح المعطى (لقاعدة «لا يمنح غيره ما لا يملك»)؟</summary>
    public bool Covers(string feature, Cap caps, AccessScope scope, IEnumerable<string> specials)
    {
        if (IsSuperAdmin) return true;
        if (!Features.TryGetValue(feature, out var f)) return caps == Cap.None && !specials.Any();
        foreach (var c in PermissionCatalog.Each(caps))
            if (!f.Caps.TryGetValue(c, out var mine) || mine < scope) return false;
        return specials.All(f.Specials.Contains);
    }
}

/// <summary>إسناد ساري مع منح دوره، كما يقرأ من القاعدة.</summary>
public sealed record AssignedRole(string RoleCode, bool RoleLocked, IReadOnlyList<RoleGrant> Grants, UserRole Assignment);

/// <summary>
/// حساب الصلاحيات الفعلية (دالة نقية قابلة للاختبار):
/// اتحاد منح الأدوار السارية مع أوسع نطاق لكل إمكانية، ثم المنح الفردية، ثم يُطرح المنع الفردي (المنع يغلب دائمًا).
/// المدير الأعلى دور كغيره تحدد منحَه المصفوفة (D6)، ويبقى «مديرًا أعلى» في قواعد الفصل، ولا تنطبق عليه الاستثناءات الفردية.
/// </summary>
public static class EffectiveAccessCalculator
{
    public static EffectiveAccess Compute(Guid userId, IEnumerable<AssignedRole> roles, IEnumerable<UserOverride> overrides, DateTimeOffset now)
    {
        var active = roles.Where(r => r.Assignment.IsActiveAt(now)).ToList();
        var codes = active.Select(r => r.RoleCode).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        var features = new Dictionary<string, FeatureAccess>(StringComparer.Ordinal);
        var superAdmin = active.Any(r => r.RoleCode == PermissionCatalog.SuperAdminRole);

        foreach (var role in active)
            foreach (var grant in role.Grants)
                Apply(features, grant.Feature, grant.Caps, grant.Scope, grant.Specials, role.Assignment.ScopeValues);

        // استثناء فردي لا يمس المدير الأعلى: يديره المدير الأعلى الآخر من المصفوفة باعتماد ثنائي، لا بمنع فردي.
        var activeOverrides = superAdmin ? [] : overrides.Where(o => o.IsActiveAt(now)).ToList();
        foreach (var o in activeOverrides.Where(o => o.Effect == OverrideEffect.Allow))
            Apply(features, o.Feature, o.Caps, o.Scope, o.Specials, []);

        foreach (var o in activeOverrides.Where(o => o.Effect == OverrideEffect.Deny))
        {
            if (!features.TryGetValue(o.Feature, out var fa)) continue;
            foreach (var c in PermissionCatalog.Each(o.Caps))
            {
                fa.Caps.Remove(c);
                fa.CapScopeValues.Remove(c);
            }
            foreach (var s in o.Specials) fa.Specials.Remove(s);
        }

        foreach (var key in features.Where(kv => kv.Value.IsEmpty).Select(kv => kv.Key).ToList()) features.Remove(key);
        return new EffectiveAccess(userId, superAdmin, codes, features);
    }

    private static void Apply(Dictionary<string, FeatureAccess> features, string feature, Cap caps, AccessScope scope, IEnumerable<string> specials, IEnumerable<string> scopeValues)
    {
        // منح لخاصية أو إمكانية لم تعد في الكتالوج يتجاهل، فلا يفتح بابًا لا معنى له.
        var def = PermissionCatalog.Find(feature);
        if (def is null) return;
        if (!features.TryGetValue(feature, out var fa)) features[feature] = fa = new FeatureAccess();
        foreach (var c in PermissionCatalog.Each(caps & def.AllowedCaps))
        {
            fa.Caps[c] = fa.Caps.TryGetValue(c, out var existing) && existing > scope ? existing : scope;
            if (scope == AccessScope.Unit)
            {
                if (!fa.CapScopeValues.TryGetValue(c, out var values)) fa.CapScopeValues[c] = values = new HashSet<string>(StringComparer.Ordinal);
                values.UnionWith(scopeValues);
            }
        }
        foreach (var s in specials.Where(def.HasSpecial)) fa.Specials.Add(s);
    }
}
