using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;
using Tier1.Domain.Structure;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// البيانات المرجعية المستخرجة من ملفي الخطة (Seed/reference-seed.json، يولده tools/export_seed.py):
/// الجامعة، والركائز والمعايير، والمؤشرات بإصدارها الأول «مسودة»، ومستهدفات الجامعة والمستهدف الموحد لكل كلية «مقترحة»،
/// وسجل التعارضات «مفتوحة»، والهيكل المرجعي لـ Tier 1. تبذر مرة واحدة على قاعدة فارغة؛ لا شيء معتمد قبل قرار اللجنة.
/// </summary>
internal static class ReferenceSeed
{
    public const string UniversityCode = "CU";
    private const int BaselineYear = 2026;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>المناصب التي تلزم لاكتمال الهيكل، والدور المقترح لصاحب كل منها.</summary>
    private static readonly Dictionary<string, (bool Required, string? Role)> PositionRules = new(StringComparer.Ordinal)
    {
        ["عميد الكلية"] = (true, "DEAN"),
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

        var pillars = seed.Pillars.ToDictionary(p => p.Code, p => new Pillar { Code = p.Code, NameAr = p.NameAr, SortOrder = p.SortOrder });
        db.Pillars.AddRange(pillars.Values);

        var standards = seed.Standards.ToDictionary(s => s.Code, s => new Standard
        {
            Code = s.Code,
            NameAr = s.NameAr,
            PillarId = s.Pillar is { } p ? pillars[p].Id : null,
            SortOrder = s.SortOrder,
            SourcePages = s.SourcePages,
            ClassificationNote = s.ClassificationNote,
        });
        db.Standards.AddRange(standards.Values);

        foreach (var s in seed.Indicators)
        {
            var indicator = new Indicator { Code = s.Code, StandardId = standards[s.Standard].Id, SortOrder = s.SortOrder, ConflictCodes = s.Conflicts, CreatedAt = now };
            indicator.Versions.Add(new IndicatorVersion
            {
                IndicatorId = indicator.Id,
                Number = 1,
                Status = DefinitionStatus.Draft,
                NameAr = s.NameAr,
                AlternateName = s.AlternateName,
                Definition = s.Definition,
                Numerator = s.Numerator,
                Denominator = s.Denominator,
                Unit = s.Unit,
                Direction = s.Direction,
                Level = s.Level,
                Aggregation = s.Aggregation,
                AggregationNote = s.AggregationNote,
                DataSource = s.DataSource,
                SourcePages = s.SourcePages,
                CreatedAt = now,
            });
            db.Indicators.Add(indicator);

            foreach (var t in s.University)
            {
                db.Targets.Add(new IndicatorTarget
                {
                    IndicatorId = indicator.Id,
                    UnitId = university.Id,
                    Year = t.Year,
                    Kind = t.Year == BaselineYear ? TargetKind.Baseline : TargetKind.Target,
                    Value = t.Value,
                    Source = "ملف Tier 1",
                    Note = s.UniversityNote,
                    UpdatedAt = now,
                });
            }
            if (s.EachCollege is { } each)
            {
                db.Targets.Add(new IndicatorTarget
                {
                    IndicatorId = indicator.Id,
                    UnitId = university.Id,
                    Year = 2027,
                    Kind = TargetKind.EachCollege,
                    Value = each.Value,
                    Text = each.Text,
                    Source = "ملف مؤشرات الكليات" + (each.UnitAsWritten is { } u ? $" (الوحدة كما وردت: {u})" : ""),
                    UpdatedAt = now,
                });
            }
        }

        db.Conflicts.AddRange(seed.Conflicts.Select(c => new DataConflict
        {
            Code = c.Code,
            Scope = c.Scope,
            Type = c.Type,
            Severity = c.Severity,
            Description = c.Description,
            ValueA = c.ValueA,
            ValueB = c.ValueB,
            Proposal = c.Proposal,
            CreatedAt = now,
        }));

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
        log.LogInformation("Seeded the Tier 1 reference data: {Indicators} indicators, {Conflicts} conflicts.", seed.Indicators.Count, seed.Conflicts.Count);
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
