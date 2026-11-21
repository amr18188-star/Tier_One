using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;
using Tier1.Domain.Structure;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// بذرة القاعدة الفارغة: الجامعة، والهيكل المرجعي لـ Tier 1 (من Seed/reference-seed.json)، ثم الإطار الشامل لتقييم أداء الجامعات
/// (Seed/kpi-framework.json: الركائز والمعايير والمؤشرات ومستهدفاتها معتمدة). المؤشرات القديمة وسجل التعارضات في reference-seed.json لم تعد تبذر.
/// </summary>
internal static class ReferenceSeed
{
    public const string UniversityCode = "CU";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>المناصب التي تلزم لاكتمال الهيكل، والدور المقترح لصاحب كل منها.</summary>
    private static readonly Dictionary<string, (bool Required, string? Role)> PositionRules = new(StringComparer.Ordinal)
    {
        ["عميد الكلية"] = (true, "DEAN"),
        // إدارة الكلية: الوكلاء الثلاثة، ومسئول الجودة في مركز التوظيف والجودة؛ أدوارهم ينشئها المدير الأعلى (ADR-0017).
        ["وكيل الكلية للدراسات العليا والبحوث"] = (true, null),
        ["وكيل الكلية لشئون التعليم والطلاب"] = (true, null),
        ["وكيل الكلية لشئون خدمة المجتمع وتنمية البيئة"] = (true, null),
        ["مسئول الجودة بالكلية"] = (true, null),
        ["رئيس اللجنة"] = (true, "CTC"),
        ["مقرر اللجنة"] = (true, "CTC"),
        ["رئيس المجلس"] = (true, null),
        ["مدير الوحدة"] = (true, null),
        ["مدير المركز"] = (true, null),
        ["ممثل Tier 1 بالقسم"] = (true, "DREP"),
        ["رئيس القسم"] = (false, null),
    };

    public static async Task ApplyAsync(Tier1DbContext db, DateTimeOffset now, ILogger log, CancellationToken ct)
    {
        if (await db.OrgUnits.AnyAsync(u => u.Kind == OrgUnitKind.University, ct)) return;

        var seed = Load();
        var university = new OrgUnit
        {
            Code = UniversityCode,
            NameAr = "جامعة العاصمة",
            NameEn = "Capital University",
            Kind = OrgUnitKind.University,
            Path = OrgUnit.PathOf(null, UniversityCode),
            CreatedAt = now,
        };
        db.OrgUnits.Add(university);

        foreach (var u in seed.Structure)
        {
            var unit = new StructureUnit
            {
                Code = u.Code,
                NameAr = u.NameAr,
                RoleAr = u.RoleAr,
                Duties = u.Duties,
                IsHead = u.Code == "DEAN",
                PerDepartment = u.Code == "DEPARTMENTS",
                SortOrder = u.SortOrder,
            };
            var order = 0;
            foreach (var p in seed.Positions.Where(p => p.Unit == u.NameAr))
            {
                var (required, role) = PositionRules.GetValueOrDefault(p.TitleAr);
                unit.Positions.Add(new StructurePosition
                {
                    UnitId = unit.Id,
                    TitleAr = p.TitleAr,
                    Single = p.CountHint.StartsWith('1'),
                    Required = required,
                    SuggestedRoleCode = role ?? (u.Code == "TIER1-COMMITTEE" ? "CTC" : null),
                    SortOrder = ++order,
                });
            }
            db.StructureUnits.Add(unit);
        }

        await db.SaveChangesAsync(ct);
        await KpiFramework.LoadAsync(db, university, now, ct);
        log.LogInformation("Seeded the university, the Tier 1 structure template and the performance framework ({Indicators} indicators).", KpiFramework.Read().Indicators.Count);
    }

    private static SeedFile Load()
    {
        using var stream = typeof(ReferenceSeed).Assembly.GetManifestResourceStream("Tier1.Infrastructure.Persistence.Seed.reference-seed.json")
            ?? throw new InvalidOperationException("reference-seed.json is not embedded.");
        return JsonSerializer.Deserialize<SeedFile>(stream, Json) ?? throw new InvalidOperationException("reference-seed.json is empty.");
    }

    private sealed record SeedFile(List<SeedPillar> Pillars, List<SeedStandard> Standards, List<SeedIndicator> Indicators, List<SeedConflict> Conflicts, List<SeedUnit> Structure, List<SeedPosition> Positions);

    private sealed record SeedPillar(string Code, string NameAr, int SortOrder);

    private sealed record SeedStandard(string Code, string NameAr, string? Pillar, string? SourcePages, string? ClassificationNote, int SortOrder);

    private sealed record SeedIndicator(
        string Code, string Standard, int SortOrder, string NameAr, string? AlternateName, string? Definition, string? Numerator, string? Denominator,
        string Unit, Direction Direction, CollectionLevel Level, Aggregation Aggregation, string? AggregationNote, string? DataSource, string? SourcePages,
        List<string> Conflicts, List<SeedValue> University, string? UniversityNote, SeedEach? EachCollege);

    private sealed record SeedValue(int Year, decimal Value);

    private sealed record SeedEach(decimal? Value, string? Text, string? UnitAsWritten);

    private sealed record SeedConflict(string Code, List<string> Scope, string Type, ConflictSeverity Severity, string Description, string? ValueA, string? ValueB, string? Proposal);

    private sealed record SeedUnit(string Code, string NameAr, string RoleAr, List<string> Duties, int SortOrder);

    private sealed record SeedPosition(string Unit, string TitleAr, string CountHint);
}
