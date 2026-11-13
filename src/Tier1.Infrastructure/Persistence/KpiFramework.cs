using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Domain.Collection;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// الإطار الشامل لتقييم أداء الجامعات (Seed/kpi-framework.json، يولده SeedImporters (--import-kpi) من KPI.xlsx):
/// الركائز الست والمعايير الاثنا عشر والمؤشرات الـ92 بتعريفاتها ومستهدفات سنواتها الخمس، و«القدرات/الأنشطة» مبادرات.
/// هي بيانات الجامعة النهائية، فتحمل التعريفات والمستهدفات «معتمدة».
/// </summary>
public static class KpiFramework
{
    public const string DecisionRef = "الإطار الشامل لتقييم أداء الجامعات (KPI.xlsx)";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>يحمل الإطار على قاعدة بلا مؤشرات (أول تشغيل أو بعد إعادة الضبط).</summary>
    internal static async Task LoadAsync(Tier1DbContext db, OrgUnit university, DateTimeOffset now, CancellationToken ct)
    {
        var seed = Read();
        var pillars = seed.Pillars.ToDictionary(p => p.Code, p => new Pillar
        {
            Code = p.Code, NameAr = p.NameAr, NameEn = p.NameEn, SortOrder = p.SortOrder,
        });
        db.Pillars.AddRange(pillars.Values);

        var standards = seed.Standards.ToDictionary(s => s.Code, s => new Standard
        {
            Code = s.Code, NameAr = s.NameAr, NameEn = s.NameEn, Description = s.Description, PillarId = pillars[s.Pillar].Id, SortOrder = s.SortOrder,
        });
        db.Standards.AddRange(standards.Values);

        foreach (var s in seed.Indicators)
        {
            var indicator = new Indicator
            {
                Code = s.Code,
                StandardId = standards[s.Standard].Id,
                SortOrder = s.SortOrder,
                PlanNote = s.PlanNote,
                Perspectives = s.Perspectives,
                SdgPrimary = s.SdgPrimary,
                SdgSecondary = s.SdgSecondary,
                SdgInduced = s.SdgInduced,
                PssRationale = s.PssRationale,
                SdgRationale = s.SdgRationale,
                CreatedAt = now,
            };
            indicator.Versions.Add(new IndicatorVersion
            {
                IndicatorId = indicator.Id,
                Number = 1,
                Status = DefinitionStatus.Approved,
                NameAr = s.NameAr,
                NameEn = s.NameEn,
                Definition = s.Definition,
                Numerator = s.Numerator,
                Denominator = s.Denominator,
                Formula = s.Formula,
                Accumulation = s.Accumulation,
                Unit = s.Unit,
                Direction = s.Direction,
                Frequency = Frequency.Quarterly,
                Level = s.Level,
                Aggregation = s.Aggregation,
                DataSource = s.DataSource,
                ApprovedAt = now,
                DecisionRef = DecisionRef,
                CreatedAt = now,
            });
            db.Indicators.Add(indicator);
            foreach (var t in s.Targets)
            {
                db.Targets.Add(new IndicatorTarget
                {
                    IndicatorId = indicator.Id,
                    UnitId = university.Id,
                    Year = t.Year,
                    Kind = TargetKind.Target,
                    Value = t.Value,
                    Source = "الإطار الشامل (KPI.xlsx)",
                    Status = TargetStatus.Approved,
                    ApprovedAt = now,
                    DecisionRef = DecisionRef,
                    UpdatedAt = now,
                });
            }
        }

        // المبادرات (القدرات/الأنشطة) مرقمة داخل كل معيار بترتيب ورودها: «1-M01»…
        var first = seed.Years.Min();
        var last = seed.Years.Max();
        foreach (var group in seed.Initiatives.GroupBy(i => i.Standard))
        {
            var n = 0;
            foreach (var i in group)
            {
                db.Initiatives.Add(new Initiative
                {
                    Code = $"{group.Key}-M{++n:00}",
                    StandardId = standards[i.Standard].Id,
                    NameAr = i.NameAr,
                    StartYear = first,
                    EndYear = last,
                    Status = InitiativeStatus.Planned,
                    IndicatorCodes = i.IndicatorCodes,
                    SortOrder = n,
                    CreatedAt = now,
                    UpdatedAt = now,
                });
            }
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// يمسح بيانات المؤشرات كلها ويحمل الإطار: التعريفات والمستهدفات والتعارضات والمبادرات والدورات والنماذج والقيم وأدلتها.
    /// يبقى ما سواها: الحسابات والأدوار والصلاحيات والجهات والإعدادات وهياكل الكليات ومستنداتها والسجلات.
    /// </summary>
    public static async Task<string> ResetAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<Tier1DbContext>();
        var files = sp.GetRequiredService<IFileStore>();
        var audit = sp.GetRequiredService<IAuditLog>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger("KpiReset");
        var now = sp.GetRequiredService<IClock>().UtcNow;
        var university = await db.OrgUnits.FirstAsync(u => u.Kind == OrgUnitKind.University, ct);

        var before = new
        {
            indicators = await db.Indicators.CountAsync(ct),
            cycles = await db.Cycles.CountAsync(ct),
            forms = await db.Submissions.CountAsync(ct),
            values = await db.SubmittedValues.CountAsync(ct),
            targets = await db.Targets.CountAsync(ct),
            conflicts = await db.Conflicts.CountAsync(ct),
            initiatives = await db.Initiatives.CountAsync(ct),
        };
        var evidence = await db.Documents.Where(d => d.OwnerType == StoredDocument.ValueOwner).Select(d => d.StorageKey).ToListAsync(ct);

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await db.UniversityValues.ExecuteDeleteAsync(ct);
            await db.Documents.Where(d => d.OwnerType == StoredDocument.ValueOwner).ExecuteDeleteAsync(ct);
            await db.SubmittedValues.ExecuteDeleteAsync(ct);
            await db.SubmissionEvents.ExecuteDeleteAsync(ct);
            await db.Submissions.ExecuteDeleteAsync(ct);
            await db.CycleIndicators.ExecuteDeleteAsync(ct);
            await db.Cycles.ExecuteDeleteAsync(ct);
            await db.TargetRevisions.ExecuteDeleteAsync(ct);
            await db.Targets.ExecuteDeleteAsync(ct);
            await db.Initiatives.ExecuteDeleteAsync(ct);
            await db.Conflicts.ExecuteDeleteAsync(ct);
            await db.IndicatorVersions.ExecuteDeleteAsync(ct);
            await db.Indicators.ExecuteDeleteAsync(ct);
            await db.Standards.ExecuteDeleteAsync(ct);
            await db.Pillars.ExecuteDeleteAsync(ct);

            await LoadAsync(db, university, now, ct);
            audit.Record(AuditEvents.Audit("kpi", "kpi.dictionary", "kpi.framework_reset",
                "إعادة ضبط بيانات المؤشرات وتحميل الإطار الشامل لتقييم أداء الجامعات", "Reset indicator data and loaded the university performance framework") with
            {
                ActorName = "أمر إعادة الضبط (--reset-kpi)",
                Target = new AuditTarget("framework", null, DecisionRef),
                Details = new { removed = before, loaded = new { indicators = await db.Indicators.CountAsync(ct), initiatives = await db.Initiatives.CountAsync(ct) } },
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        // ملفات الأدلة بعد نجاح الحذف من القاعدة؛ تعذر حذف ملف لا يعيد ما حذف.
        var deleted = 0;
        foreach (var key in evidence)
        {
            try
            {
                await files.DeleteAsync(key, ct);
                deleted++;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "Could not delete evidence file {Key}.", key);
            }
        }
        var summary = $"Removed {before.indicators} indicators, {before.cycles} cycles, {before.forms} forms, {before.values} values, {before.targets} targets, "
                      + $"{before.conflicts} conflicts, {before.initiatives} initiatives and {deleted}/{evidence.Count} evidence files. Loaded {Read().Indicators.Count} indicators.";
        log.LogInformation("{Summary}", summary);
        return summary;
    }

    internal static FrameworkFile Read()
    {
        using var stream = typeof(KpiFramework).Assembly.GetManifestResourceStream("Tier1.Infrastructure.Persistence.Seed.kpi-framework.json")
            ?? throw new InvalidOperationException("kpi-framework.json is not embedded.");
        return JsonSerializer.Deserialize<FrameworkFile>(stream, Json) ?? throw new InvalidOperationException("kpi-framework.json is empty.");
    }

    internal sealed record FrameworkFile(List<int> Years, List<FPillar> Pillars, List<FStandard> Standards, List<FIndicator> Indicators, List<FInitiative> Initiatives);

    internal sealed record FPillar(string Code, string NameAr, string? NameEn, int SortOrder);

    internal sealed record FStandard(string Code, string NameAr, string? NameEn, string? Description, string Pillar, int SortOrder);

    internal sealed record FIndicator(
        string Code, string Standard, int SortOrder, string NameAr, string? NameEn, string? Definition, string Unit, string? DataSource, string? Formula,
        Direction Direction, Accumulation Accumulation, CollectionLevel Level, Aggregation Aggregation, string? Numerator, string? Denominator,
        List<int> Perspectives, int? SdgPrimary, int? SdgSecondary, int? SdgInduced, string? PssRationale, string? SdgRationale,
        List<FTarget> Targets, string? PlanNote);

    internal sealed record FTarget(int Year, decimal Value);

    internal sealed record FInitiative(string Standard, string NameAr, List<string> IndicatorCodes);
}
