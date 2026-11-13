using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// هيكل الجامعة الرسمي (Seed/university-structure.json، يولده tools/import_structure.py من ملف الهيكل):
/// القطاعات والإدارات العامة والكليات والمعاهد وأقسامها، ووحدة التخطيط الاستراتيجي ومركز الاتصالات وتكنولوجيا المعلومات.
/// التحميل يضيف الجهات أو يحدث اسمها وتبعيتها بالرمز، ويحل محل الجهات التجريبية: تنقل نطاقات حساباتها إلى الجهات المقابلة ثم تحذف.
/// الجهات غير الموجودة في الملف تبقى كما هي (لا تغلق تلقائيًا).
/// </summary>
public static class UniversityStructure
{
    public const string SettingKey = "org.structure";

    /// <summary>الجهات التجريبية ← الجهات الحقيقية المقابلة لها، لنقل نطاق الحسابات التجريبية.</summary>
    private static readonly Dictionary<string, string> DemoMap = new(StringComparer.Ordinal)
    {
        ["ENG"] = "F06", ["ENG-CIV"] = "F06-D01", ["ENG-ELE"] = "F06-D02", ["ENG-MEC"] = "F06-D03",
        ["CS"] = "F08", ["CS-SE"] = "F08-D01", ["CS-AI"] = "F08-D04",
        ["COM"] = "F17", ["COM-ACC"] = "F17-D01", ["COM-BUS"] = "F17-D02",
        ["QAC"] = "SPU", ["STU"] = "A06", ["DIG"] = "ICT", ["INTL"] = "A11",
    };

    /// <summary>أسماء الحسابات التجريبية تتبع جهاتها الجديدة.</summary>
    private static readonly (string Old, string New)[] DemoNames =
    [
        ("كلية الهندسة", "كلية الهندسة بحلوان"),
        ("كلية التجارة", "كلية التجارة وإدارة الأعمال"),
        ("بقسم المدنية", "بقسم هندسة الإلكترونيات والاتصالات"),
        ("بقسم الكهربية", "بقسم هندسة القوى والآلات الكهربائية"),
        ("بقسم الميكانيكية", "بقسم الهندسة الميكانيكية"),
        ("بقسم هندسة البرمجيات", "بقسم علوم الحاسب"),
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public static string MapDemoCode(string code) => DemoMap.GetValueOrDefault(code, code);

    public static string MapDemoName(string name) => DemoNames.Aggregate(name, (n, r) => n.Contains(r.New, StringComparison.Ordinal) ? n : n.Replace(r.Old, r.New, StringComparison.Ordinal));

    public static async Task<bool> LoadedAsync(Tier1DbContext db, CancellationToken ct) => await db.Settings.AnyAsync(s => s.Key == SettingKey, ct);

    /// <summary>أمر --load-structure: يحمل الهيكل في معاملة واحدة ويسجله في السجل المختوم.</summary>
    public static async Task<string> LoadAsync(IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<Tier1DbContext>();
        var audit = sp.GetRequiredService<IAuditLog>();
        var now = sp.GetRequiredService<IClock>().UtcNow;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var summary = await ApplyAsync(db, now, ct);
        audit.Record(AuditEvents.Audit("org", "org.units", "org.structure_loaded", "تحميل هيكل الجامعة الرسمي", "Loaded the official university structure") with
        {
            ActorName = "أمر تحميل الهيكل (--load-structure)",
            Target = new AuditTarget("structure", null, Read().Source),
            Details = summary,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        sp.GetRequiredService<ILoggerFactory>().CreateLogger("Structure").LogInformation("{Summary}", summary.Text);
        return summary.Text;
    }

    public sealed record Summary(int Added, int Updated, int DemoRemoved, int ScopesMoved, int Untouched, string Text);

    /// <summary>يطبق الهيكل على القاعدة (بلا معاملة؛ يستدعيه الأمر أو بذر النسخة التجريبية).</summary>
    internal static async Task<Summary> ApplyAsync(Tier1DbContext db, DateTimeOffset now, CancellationToken ct)
    {
        var file = Read();
        var root = await db.OrgUnits.FirstAsync(u => u.Kind == OrgUnitKind.University, ct);
        root.NameAr = file.University;
        var all = await db.OrgUnits.ToDictionaryAsync(u => u.Code, StringComparer.Ordinal, ct);
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        int added = 0, updated = 0;

        foreach (var s in file.Units)
        {
            var parent = s.Parent is null ? root : all.TryGetValue(s.Parent, out var p) ? p : throw new InvalidOperationException($"{s.Code}: unknown parent {s.Parent}.");
            var path = OrgUnit.PathOf(parent.Path, s.Code);
            if (!all.TryGetValue(s.Code, out var unit))
            {
                unit = new OrgUnit
                {
                    Code = s.Code, NameAr = s.NameAr, Kind = s.Kind, ParentId = parent.Id, Path = path, SortOrder = s.SortOrder,
                    ViewsWholeUniversity = s.ViewsWholeUniversity, CreatedAt = now,
                };
                db.OrgUnits.Add(unit);
                db.OrgUnitChanges.Add(new OrgUnitChange { UnitId = unit.Id, Kind = OrgUnitChangeKind.Created, EffectiveOn = today, NewValue = s.NameAr, Reason = $"هيكل الجامعة الرسمي ({file.Source})", At = now });
                all[s.Code] = unit;
                added++;
                continue;
            }
            var changed = false;
            if (unit.NameAr != s.NameAr)
            {
                db.OrgUnitChanges.Add(new OrgUnitChange { UnitId = unit.Id, Kind = OrgUnitChangeKind.Renamed, EffectiveOn = today, OldValue = unit.NameAr, NewValue = s.NameAr, Reason = $"هيكل الجامعة الرسمي ({file.Source})", At = now });
                unit.NameAr = s.NameAr;
                changed = true;
            }
            if (unit.Path != path)
            {
                // نقل الجهة ينقل مسار ما تحتها، فيبقى فحص النطاق صحيحًا.
                var old = unit.Path;
                foreach (var d in all.Values.Where(x => x.Path.StartsWith(old, StringComparison.Ordinal) && x.Id != unit.Id))
                    d.Path = path + d.Path[old.Length..];
                db.OrgUnitChanges.Add(new OrgUnitChange { UnitId = unit.Id, Kind = OrgUnitChangeKind.Moved, EffectiveOn = today, OldValue = old, NewValue = path, Reason = $"هيكل الجامعة الرسمي ({file.Source})", At = now });
                unit.ParentId = parent.Id;
                unit.Path = path;
                changed = true;
            }
            if (unit.Kind != s.Kind || unit.SortOrder != s.SortOrder || (s.ViewsWholeUniversity && !unit.ViewsWholeUniversity) || !unit.Active)
            {
                unit.Kind = s.Kind;
                unit.SortOrder = s.SortOrder;
                unit.ViewsWholeUniversity |= s.ViewsWholeUniversity;
                unit.Active = true;
                unit.ClosedOn = null;
                changed = true;
            }
            if (changed) updated++;
        }
        await db.SaveChangesAsync(ct);

        // الجهات التجريبية: تنقل نطاقات الحسابات إلى الجهات المقابلة، وتحذف بأعضاء هياكلها الوهميين، ما لم تكن عليها بيانات حقيقية.
        var demo = all.Values.Where(u => DemoMap.ContainsKey(u.Code)).ToList();
        var demoIds = demo.Select(u => u.Id).ToList();
        var scopesMoved = 0;
        if (demo.Count > 0)
        {
            var used = new List<string>();
            if (await db.Submissions.AnyAsync(s => demoIds.Contains(s.UnitId), ct)) used.Add("نماذج دورات");
            if (await db.Targets.AnyAsync(t => demoIds.Contains(t.UnitId), ct)) used.Add("مستهدفات");
            if (await db.IndicatorVersions.AnyAsync(v => v.OwnerUnitId != null && demoIds.Contains(v.OwnerUnitId.Value), ct)) used.Add("ملكية مؤشرات");
            if (await db.Initiatives.AnyAsync(i => i.OwnerUnitId != null && demoIds.Contains(i.OwnerUnitId.Value), ct)) used.Add("مبادرات");
            if (await db.Documents.AnyAsync(d => demoIds.Contains(d.OwnerId), ct)) used.Add("مستندات");
            if (used.Count > 0)
                throw new InvalidOperationException($"The demo units hold data ({string.Join("، ", used)}); reset the indicator data first (reset-kpi).");

            foreach (var role in await db.UserRoles.Where(r => r.ScopeValues.Count > 0).ToListAsync(ct))
            {
                var mapped = role.ScopeValues.Select(MapDemoCode).Distinct(StringComparer.Ordinal).ToList();
                if (mapped.SequenceEqual(role.ScopeValues)) continue;
                role.ScopeValues = mapped;
                scopesMoved++;
            }
            var demoEmails = Seeder.DemoUsers.Select(d => d.Email).ToHashSet(StringComparer.Ordinal);
            foreach (var user in await db.Users.Where(u => demoEmails.Contains(u.Email)).ToListAsync(ct))
                user.FullNameAr = MapDemoName(user.FullNameAr);

            await db.StructureMembers.Where(m => demoIds.Contains(m.CollegeId)).ExecuteDeleteAsync(ct);
            await db.IndustryPartners.Where(m => demoIds.Contains(m.CollegeId)).ExecuteDeleteAsync(ct);
            await db.UnitIndicators.Where(m => demoIds.Contains(m.UnitId)).ExecuteDeleteAsync(ct);
            await db.OrgUnitChanges.Where(c => demoIds.Contains(c.UnitId)).ExecuteDeleteAsync(ct);
            await db.SaveChangesAsync(ct);
            // الأقسام قبل كلياتها.
            foreach (var u in demo.OrderByDescending(u => u.Path.Length)) db.OrgUnits.Remove(u);
            await db.SaveChangesAsync(ct);
        }

        var codes = file.Units.Select(u => u.Code).Append(root.Code).ToHashSet(StringComparer.Ordinal);
        var untouched = await db.OrgUnits.CountAsync(u => u.Active && !codes.Contains(u.Code), ct);
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (setting is null) db.Settings.Add(setting = new SystemSetting { Key = SettingKey });
        setting.Value = file.Source;
        setting.UpdatedAt = now;
        await db.SaveChangesAsync(ct);

        var kinds = file.Units.GroupBy(u => u.Kind).ToDictionary(g => g.Key, g => g.Count());
        var text = $"Structure {file.Source}: {file.Units.Count} units ({string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}); "
                   + $"added {added}, updated {updated}, removed {demo.Count} demo units, moved {scopesMoved} account scopes; {untouched} other active units left as they were.";
        return new Summary(added, updated, demo.Count, scopesMoved, untouched, text);
    }

    internal static StructureFile Read()
    {
        using var stream = typeof(UniversityStructure).Assembly.GetManifestResourceStream("Tier1.Infrastructure.Persistence.Seed.university-structure.json")
            ?? throw new InvalidOperationException("university-structure.json is not embedded.");
        return JsonSerializer.Deserialize<StructureFile>(stream, Json) ?? throw new InvalidOperationException("university-structure.json is empty.");
    }

    internal sealed record StructureFile(string Source, string University, List<SUnit> Units);

    internal sealed record SUnit(string Code, string NameAr, OrgUnitKind Kind, string? Parent, int SortOrder, bool ViewsWholeUniversity);
}
