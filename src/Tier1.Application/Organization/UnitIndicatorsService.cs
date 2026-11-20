using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Organization;

public sealed record UnitIndicatorItem(Guid Id, string Code, string NameAr, CollectionLevel Level, Aggregation Aggregation, bool Selected, string? TakenBy);

public sealed record UnitIndicatorStandard(string Code, string NameAr, string PillarCode, string PillarName, IReadOnlyList<UnitIndicatorItem> Indicators);

/// <param name="Custom">قائمة الجهة مخصصة؛ وإلا تدخل الافتراضي لمستواها.</param>
/// <param name="FollowsUnit">القسم غير المخصص يتبع قائمة كليته المخصصة: اسمها.</param>
public sealed record UnitIndicatorsDto(Guid UnitId, string UnitName, OrgUnitKind Kind, bool Custom, string? FollowsUnit, bool CanEdit, int OpenCycles,
    IReadOnlyList<UnitIndicatorStandard> Standards);

public sealed record SaveUnitIndicatorsRequest(bool Custom, IReadOnlyList<Guid>? IndicatorIds);

/// <summary>
/// تكليف كل جهة بالمؤشرات التي تدخل بياناتها. يسري على الدورات التي تفتح بعده؛ نماذج الدورة المفتوحة تبقى كما فتحت.
/// </summary>
public sealed class UnitIndicatorsService(IOrgStore db, IReadModel read, ITransactionLock locks, IAccessService access, IAuditLog audit)
{
    private const string Feature = Features.Units;

    public async Task<UnitIndicatorsDto> GetAsync(Guid unitId, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var units = await db.OrgUnits.AsNoTracking().ToListAsync(ct);
        var unit = units.FirstOrDefault(u => u.Id == unitId) ?? throw DomainException.NotFound();
        UnitScope.For(me, Feature, Cap.View).Require(unit);
        var map = new AssignmentMap(units, await db.UnitIndicators.AsNoTracking().ToListAsync(ct));
        var indicators = await IndicatorsAsync(ct);
        var standards = await read.Query<Domain.Kpi.Standard>().AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var pillars = await read.Query<Domain.Kpi.Pillar>().AsNoTracking().ToDictionaryAsync(p => p.Id, ct);

        var groups = standards.Select(s => new UnitIndicatorStandard(s.Code, s.NameAr,
                s.PillarId is { } p && pillars.TryGetValue(p, out var pillar) ? pillar.Code : "", s.PillarId is { } q && pillars.TryGetValue(q, out var pl) ? pl.NameAr : "",
                indicators.Where(i => i.StandardId == s.Id && AssignmentMap.Assignable(unit.Kind, i.Ref.Level))
                    .Select(i =>
                    {
                        var taken = i.Ref.Level == CollectionLevel.University && map.ResponsibleFor(i.Ref.Id) is { } r && r.Id != unit.Id ? r.NameAr : null;
                        return new UnitIndicatorItem(i.Ref.Id, i.Code, i.NameAr, i.Ref.Level, i.Aggregation, map.Selected(unit, i.Ref), taken);
                    }).ToList()))
            .Where(g => g.Indicators.Count > 0).ToList();
        var open = await read.Query<Domain.Collection.CollectionCycle>().CountAsync(c => c.Status == Domain.Collection.CycleStatus.Open, ct);
        // مؤشر مسند إلى جهة خارج نطاق المستخدم: يظهر مشغولًا دون اسم الجهة.
        var view = UnitScope.For(me, Feature, Cap.View);
        if (!view.All)
            groups = groups.Select(g => g with
            {
                Indicators = g.Indicators.Select(i => i.TakenBy is null ? i : i with { TakenBy = "جهة أخرى" }).ToList(),
            }).ToList();
        return new UnitIndicatorsDto(unit.Id, unit.NameAr, unit.Kind, map.IsCustom(unit.Id), map.FollowedCollege(unit)?.NameAr,
            me.Has(Feature, Cap.Edit) && UnitScope.For(me, Feature, Cap.Edit).Covers(unit) && unit.Active && unit.Kind != OrgUnitKind.University, open, groups);
    }

    /// <summary>Advisory lock key for unit indicator assignments (an arbitrary constant, distinct from the IAM lock).</summary>
    private const long AssignmentLockKey = 7_301_202_611;

    public async Task<UnitIndicatorsDto> SaveAsync(Guid unitId, SaveUnitIndicatorsRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var unit = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == unitId, ct) ?? throw DomainException.NotFound();
        if (unit.Kind == OrgUnitKind.University)
            throw new DomainException("university_form", "نموذج الجامعة يدخل كل مؤشر على مستوى الجامعة لم تكلف به جهة أخرى.", "The university form takes every unassigned university-level indicator.", 400);
        if (!unit.Active) throw new DomainException("unit_closed", "الجهة مغلقة.", "The unit is closed.");

        // BE-4: removals, additions and the audit row commit together. BE-5: one assignment at a time, so two units cannot
        // both take the same university-level indicator between the check below and the insert.
        await using var tx = await locks.BeginAsync(AssignmentLockKey, ct);
        var indicators = (await IndicatorsAsync(ct)).ToDictionary(i => i.Ref.Id);
        var ids = (req.IndicatorIds ?? []).Distinct().ToList();
        // الجهة المركزية لا افتراضي لها: ما يختار لها هو قائمتها، ولا شيء إن لم يختر شيء.
        var custom = req.Custom || unit.IsCentral;
        if (!custom) ids.Clear();
        foreach (var id in ids)
        {
            if (!indicators.TryGetValue(id, out var i)) throw DomainException.NotFound();
            if (!AssignmentMap.Assignable(unit.Kind, i.Ref.Level))
                throw new DomainException("not_assignable", $"المؤشر {i.Code} لا يدخل في نموذج {Kind(unit.Kind)}.", $"Indicator {i.Code} does not belong in this unit's form.", 400);
        }

        // مؤشر الجامعة كلها تدخله جهة واحدة: لا يسند إلى جهتين.
        var universityIds = ids.Where(id => indicators[id].Ref.Level == CollectionLevel.University).ToList();
        if (universityIds.Count > 0)
        {
            var taken = await (from r in db.UnitIndicators
                               join u in db.OrgUnits on r.UnitId equals u.Id
                               where universityIds.Contains(r.IndicatorId) && r.UnitId != unit.Id && u.CustomIndicators && u.Active
                               select new { r.IndicatorId, u.NameAr }).ToListAsync(ct);
            if (taken.Count > 0)
            {
                var list = string.Join("، ", taken.Select(t => $"{indicators[t.IndicatorId].Code} ({t.NameAr})"));
                throw new DomainException("already_assigned", $"مؤشر الجامعة تدخله جهة واحدة. مسند بالفعل: {list}. ألغ إسناده هناك أولًا.",
                    "A university-level indicator is entered by one unit only.", 409);
            }
        }

        var before = await db.UnitIndicators.Where(r => r.UnitId == unit.Id).Select(r => r.IndicatorId).ToListAsync(ct);
        var added = ids.Except(before).ToList();
        var removed = before.Except(ids).ToList();
        await db.UnitIndicators.Where(r => r.UnitId == unit.Id && removed.Contains(r.IndicatorId)).ExecuteDeleteAsync(ct);
        db.UnitIndicators.AddRange(added.Select(id => new UnitIndicator { UnitId = unit.Id, IndicatorId = id }));
        var wasCustom = unit.CustomIndicators;
        unit.CustomIndicators = custom;
        if (added.Count > 0 || removed.Count > 0 || wasCustom != custom)
        {
            string? Codes(List<Guid> x) => x.Count == 0 ? null : string.Join(",", x.Select(id => indicators.TryGetValue(id, out var i) ? i.Code : id.ToString()).Order(StringComparer.Ordinal));
            audit.Record(AuditEvents.Audit("org", Feature, "org.unit_indicators", "تكليف جهة بمؤشراتها", "Assigned indicators to a unit") with
            {
                Target = new AuditTarget("org_unit", unit.Id.ToString(), unit.Code),
                Changes = new ChangeSet().Add("custom", wasCustom, custom).Add("added", null, Codes(added)).Add("removed", Codes(removed), null).ToList(),
            });
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(unitId, ct);
    }

    /// <summary>عدد ما تدخله كل جهة (أو أقسامها للكلية)، لقائمة الجهات.</summary>
    public async Task<Dictionary<Guid, int>> CountsAsync(IReadOnlyList<OrgUnit> units, CancellationToken ct)
    {
        var map = new AssignmentMap(units, await db.UnitIndicators.AsNoTracking().ToListAsync(ct));
        var indicators = await IndicatorsAsync(ct);
        return units.ToDictionary(u => u.Id, u => u.Kind == OrgUnitKind.University ? 0 : indicators.Count(i => map.Selected(u, i.Ref)));
    }

    public async Task<AssignmentMap> MapAsync(CancellationToken ct) =>
        new(await db.OrgUnits.AsNoTracking().ToListAsync(ct), await db.UnitIndicators.AsNoTracking().ToListAsync(ct));

    private sealed record Item(IndicatorRef Ref, string Code, string NameAr, Aggregation Aggregation, Guid StandardId, int SortOrder);

    private async Task<List<Item>> IndicatorsAsync(CancellationToken ct)
    {
        var list = await read.Query<Domain.Kpi.Indicator>().AsNoTracking().Include(i => i.Versions).Where(i => !i.Suspended).OrderBy(i => i.SortOrder).ToListAsync(ct);
        return list.Select(i => (i, v: i.Effective ?? i.Current))
            .Select(x => new Item(new IndicatorRef(x.i.Id, x.v.Level, x.v.Aggregation == Aggregation.DistinctCount), x.i.Code, x.v.NameAr, x.v.Aggregation, x.i.StandardId, x.i.SortOrder))
            .ToList();
    }

    private static string Kind(OrgUnitKind k) => k switch
    {
        OrgUnitKind.Department => "القسم",
        OrgUnitKind.College => "الكلية",
        OrgUnitKind.Institute => "المعهد",
        _ => "الجهة المركزية",
    };
}
