using System.Text.Json;
using System.Text.Json.Serialization;
using Tier1.Domain.Access;
using Tier1.Domain.Common;

namespace Tier1.Application.Access;

public sealed record ModuleDefinition(string Key, BiText Name);

public sealed record RoleDefinition(string Code, BiText Name, string DescriptionAr, bool IsNew);

public sealed record SpecialDefinition(string Key, BiText Name, IReadOnlyList<string> DefaultRoles);

/// <summary>منح افتراضي لدور: الإمكانيات والنطاق، كما في المصفوفة المعتمدة في المراجعة.</summary>
public sealed record DefaultGrant(Cap Caps, AccessScope Scope);

/// <summary>خاصية في المصفوفة: معنى كل إمكانية فيها، وإجراءاتها الخاصة، ومنحها الافتراضية.</summary>
public sealed record FeatureDefinition(
    string Key,
    string Module,
    BiText Name,
    IReadOnlyDictionary<Cap, string> CapMeanings,
    IReadOnlyList<SpecialDefinition> Specials,
    IReadOnlyDictionary<string, DefaultGrant> Defaults)
{
    /// <summary>الإمكانيات التي لها معنى في هذه الخاصية؛ غيرها لا يمنح أبدًا.</summary>
    public Cap AllowedCaps { get; } = CapMeanings.Keys.Aggregate(Cap.None, (a, c) => a | c);

    public bool HasSpecial(string key) => Specials.Any(s => s.Key == key);
}

/// <summary>
/// كتالوج الصلاحيات: 64 خاصية في 12 وحدة و14 دورًا افتراضيًا. مصدره ملف permission-catalog.json المضمّن،
/// المولّد من المصفوفة المنشورة في مراجعة المتطلبات. الكود يحدد <em>ماذا</em> يعني كل خيار، والمصفوفة في القاعدة
/// تحدد <em>من</em> يملكه.
/// </summary>
public static class PermissionCatalog
{
    public const string SuperAdminRole = "SA";

    /// <summary>أدوار لا يسندها إلا المدير الأعلى (SOD-9).</summary>
    public static readonly IReadOnlySet<string> ReservedRoles = new HashSet<string>(StringComparer.Ordinal) { "SA", "SYS", "AUD", "PRES", "HC" };

    /// <summary>الإجراءات الخاصة التي تعني اعتمادًا أو قرارًا؛ لا تمنح لمسؤول النظام (SOD-11).</summary>
    public static readonly IReadOnlySet<string> DecisionSpecials = new HashSet<string>(StringComparer.Ordinal) { "approve", "approve_void", "execute", "publish", "decide", "review", "confirm", "reissue", "review_department", "review_college", "approve_department_directly", "approve_as_dean_directly", "approve_college", "approve_final", "reopen", "approve_reopen" };

    /// <summary>
    /// إجراءات للمدير الأعلى وحده: لا تمنح لدور ولا بمنح فردي. اعتماد المصفوفة لو مُنح لغيره لسقط شرط «مدير أعلى ثانٍ».
    /// </summary>
    private static readonly HashSet<(string Feature, string Special)> SuperAdminOnly = [("adm.roles", "approve"), ("adm.users", "set_password")];

    public static bool IsSuperAdminOnly(string feature, string special) => SuperAdminOnly.Contains((feature, special));

    private static readonly Lazy<Loaded> Data = new(Load);

    public static IReadOnlyList<ModuleDefinition> Modules => Data.Value.Modules;

    public static IReadOnlyList<RoleDefinition> Roles => Data.Value.Roles;

    public static IReadOnlyList<FeatureDefinition> Features => Data.Value.Features;

    public static FeatureDefinition? Find(string key) => Data.Value.ByKey.GetValueOrDefault(key);

    public static FeatureDefinition Get(string key) => Find(key) ?? throw new InvalidOperationException($"Unknown feature '{key}'.");

    public static Cap ParseCaps(string letters)
    {
        var caps = Cap.None;
        foreach (var ch in letters)
            caps |= ch switch
            {
                'V' => Cap.View,
                'P' => Cap.Print,
                'E' => Cap.Edit,
                'A' => Cap.Add,
                'D' => Cap.Delete,
                _ => throw new FormatException($"Unknown capability letter '{ch}'."),
            };
        return caps;
    }

    public static string Letters(Cap caps) =>
        string.Concat(new[] { (Cap.View, 'V'), (Cap.Print, 'P'), (Cap.Edit, 'E'), (Cap.Add, 'A'), (Cap.Delete, 'D') }
            .Where(p => caps.HasFlag(p.Item1)).Select(p => p.Item2));

    public static AccessScope ParseScope(string code) => code switch
    {
        "A" => AccessScope.All,
        "U" => AccessScope.Unit,
        "S" => AccessScope.Assigned,
        "O" => AccessScope.Own,
        _ => throw new FormatException($"Unknown scope '{code}'."),
    };

    public static IEnumerable<Cap> Each(Cap caps)
    {
        foreach (var c in new[] { Cap.View, Cap.Print, Cap.Edit, Cap.Add, Cap.Delete })
            if (caps.HasFlag(c)) yield return c;
    }

    private static Loaded Load()
    {
        using var stream = typeof(PermissionCatalog).Assembly.GetManifestResourceStream("Tier1.Application.Access.permission-catalog.json")
            ?? throw new InvalidOperationException("permission-catalog.json is not embedded.");
        var raw = JsonSerializer.Deserialize<RawCatalog>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("permission-catalog.json is empty.");
        var modules = raw.Modules.Select(m => new ModuleDefinition(m.Key, new BiText(m.Ar, m.En))).ToList();
        var roles = raw.Roles.Select(r => new RoleDefinition(r.Code, new BiText(r.Ar, r.En), r.DescriptionAr, r.IsNew)).ToList();
        var features = raw.Features.Select(f => new FeatureDefinition(
            f.Key,
            f.Module,
            new BiText(f.Ar, f.En),
            f.Caps.ToDictionary(kv => ParseCaps(kv.Key), kv => kv.Value),
            f.Specials.Select(s => new SpecialDefinition(s.Key, new BiText(s.Ar, s.En), s.Roles)).ToList(),
            f.Defaults.ToDictionary(kv => kv.Key, kv =>
            {
                var parts = kv.Value.Split(':');
                return new DefaultGrant(ParseCaps(parts[0]), ParseScope(parts[1]));
            }, StringComparer.Ordinal))).ToList();
        return new Loaded(modules, roles, features, features.ToDictionary(f => f.Key, StringComparer.Ordinal));
    }

    private sealed record Loaded(List<ModuleDefinition> Modules, List<RoleDefinition> Roles, List<FeatureDefinition> Features, Dictionary<string, FeatureDefinition> ByKey);

    private sealed class RawCatalog
    {
        public List<RawModule> Modules { get; set; } = [];

        public List<RawRole> Roles { get; set; } = [];

        public List<RawFeature> Features { get; set; } = [];
    }

    private sealed class RawModule
    {
        public string Key { get; set; } = "";

        public string Ar { get; set; } = "";

        public string? En { get; set; }
    }

    private sealed class RawRole
    {
        public string Code { get; set; } = "";

        public string Ar { get; set; } = "";

        public string? En { get; set; }

        public string DescriptionAr { get; set; } = "";

        public bool IsNew { get; set; }
    }

    private sealed class RawFeature
    {
        public string Key { get; set; } = "";

        public string Module { get; set; } = "";

        public string Ar { get; set; } = "";

        public string? En { get; set; }

        public Dictionary<string, string> Caps { get; set; } = [];

        public List<RawSpecial> Specials { get; set; } = [];

        public Dictionary<string, string> Defaults { get; set; } = [];
    }

    private sealed class RawSpecial
    {
        public string Key { get; set; } = "";

        public string Ar { get; set; } = "";

        public string? En { get; set; }

        [JsonPropertyName("roles")]
        public List<string> Roles { get; set; } = [];
    }
}

/// <summary>رموز الخصائص التي يشير إليها الكود مباشرة.</summary>
public static class Features
{
    public const string Users = "adm.users";
    public const string Roles = "adm.roles";
    public const string Grants = "adm.grants";
    public const string AuditLog = "log.audit";
    public const string SecurityLog = "log.security";
    public const string AccessLog = "log.access";
    public const string TechLog = "log.tech";

    /// <summary>BE-11: the outgoing mail, its failures, and resending a failed notification.</summary>
    public const string Mail = "adm.mail";
    public const string Dashboard = "dash.home";
    public const string Units = "org.units";
    public const string Structure = "org.structure";
    public const string Strategy = "kpi.strategy";
    public const string Dictionary = "kpi.dictionary";
    public const string Targets = "kpi.targets";
    public const string Conflicts = "kpi.conflicts";
    public const string Cycles = "dc.cycles";
    public const string Forms = "dc.forms";
    public const string Results = "dc.results";

    /// <summary>القيم الرسمية للجامعة: مراجعة مؤشرات الكليات ونقل القيمة إلى مؤشر الجامعة (مسئول ملف Tier 1).</summary>
    public const string University = "dc.university";
}

public static class Specials
{
    public const string Approve = "approve";
    public const string Export = "export";
    public const string SetPassword = "set_password";

    /// <summary>عرض الهاتف والبريد لأعضاء الهيكل.</summary>
    public const string ViewContacts = "view_contacts";

    /// <summary>تسجيل قرار اللجنة العليا في تعارض.</summary>
    public const string Decide = "decide";
}
