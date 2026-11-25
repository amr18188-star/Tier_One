using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Kpi;

public sealed record YearTargetDto(Guid Id, int Year, decimal? Value, string? Text, TargetStatus Status, uint Version);

public sealed record IndicatorNode(
    string Code, string NameAr, string? Definition, string? Numerator, string? Denominator, string Unit, Direction Direction, CollectionLevel Level, Aggregation Aggregation,
    DefinitionStatus Status, bool HasApproved, bool Suspended, int? PlanStartYear, int? PlanEndYear, string? PlanNote,
    decimal? Baseline, IReadOnlyList<YearTargetDto> Targets, string? EachCollege, uint Version,
    string? NameEn, string? Formula, Accumulation Accumulation, IReadOnlyList<int> Perspectives, int? SdgPrimary, int? SdgSecondary, int? SdgInduced);

/// <param name="ActivityCount">ADR-0017: the initiative's activities.</param>
/// <param name="ActivityProgress">Their average completion (0–100), or null without activities.</param>
public sealed record InitiativeNode(
    Guid Id, string Code, string NameAr, string? Description, string? ExpectedOutcome, Guid? OwnerUnitId, string? OwnerUnitName,
    int StartYear, int EndYear, InitiativeStatus Status, IReadOnlyList<string> IndicatorCodes, uint Version,
    int ActivityCount = 0, int? ActivityProgress = null);

/// <summary>ADR-0017: one activity of an initiative.</summary>
public sealed record ActivityDto(
    Guid Id, Guid InitiativeId, string TitleAr, string? Description, Guid? OwnerUnitId, string? OwnerUnitName,
    DateOnly? StartDate, DateOnly? EndDate, InitiativeStatus Status, int Progress, int SortOrder, uint Version);

public sealed record SaveActivityRequest(
    string TitleAr, string? Description, Guid? OwnerUnitId, DateOnly? StartDate, DateOnly? EndDate, InitiativeStatus Status, int Progress,
    int? SortOrder, uint? Version);

public sealed record StandardNode(
    Guid Id, string Code, string NameAr, string? NameEn, string? Description, Guid? PillarId, int SortOrder, uint Version,
    IReadOnlyList<InitiativeNode> Initiatives, IReadOnlyList<IndicatorNode> Indicators);

public sealed record PillarNode(Guid Id, string Code, string NameAr, string? NameEn, string? Description, int SortOrder, uint Version, IReadOnlyList<StandardNode> Standards);

/// <summary>ما يستطيعه المستخدم في الشجرة، فتظهر الأزرار المتاحة وحدها.</summary>
public sealed record StrategyActions(
    bool CanEdit, bool CanAdd, bool CanDelete, bool CanAddIndicator, bool CanEditIndicator, bool CanDeleteIndicator,
    bool CanEditTargets, bool CanDeleteTargets, bool CanApproveTargets);

public sealed record StrategyTreeDto(
    int BaselineYear, IReadOnlyList<int> Years, IReadOnlyList<PillarNode> Pillars, IReadOnlyList<StandardNode> Unassigned, StrategyActions Actions);

public sealed record SavePillarRequest(string NameAr, string? NameEn, string? Description, int? SortOrder, uint? Version);

public sealed record SaveStandardRequest(Guid? PillarId, string NameAr, string? NameEn, string? Description, int? SortOrder, uint? Version);

public sealed record SaveInitiativeRequest(
    Guid StandardId, string NameAr, string? Description, string? ExpectedOutcome, Guid? OwnerUnitId, int StartYear, int EndYear,
    InitiativeStatus Status, IReadOnlyList<string>? IndicatorCodes, uint? Version);

public sealed record DeleteRequest(string? Reason);

/// <summary>
/// شجرة الخطة الاستراتيجية: الجامعة ← الركائز ← المعايير ← المبادرات والمؤشرات ← مستهدفات كل سنة.
/// إضافة الركائز والمعايير والمبادرات وتعديلها وحذفها بصلاحية «الخطة الاستراتيجية»؛ المؤشرات وتعريفاتها ومستهدفاتها بصلاحياتها في القاموس والمستهدفات.
/// لا يحذف ما يقوم عليه غيره: الركيزة بمعاييرها والمعيار بمؤشراته ومبادراته تنقل أو تحذف أولًا.
/// </summary>
public sealed class StrategyService(IKpiStore db, IReadModel read, IClock clock, IAccessService access, IAuditLog audit, Settings.PlanPeriodService planPeriod)
{
    private const string Feature = Features.Strategy;

    public async Task<StrategyTreeDto> TreeAsync(CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var pillars = await db.Pillars.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Code).ToListAsync(ct);
        var standards = await db.Standards.AsNoTracking().OrderBy(s => s.SortOrder).ThenBy(s => s.Code).ToListAsync(ct);
        var initiatives = await db.Initiatives.AsNoTracking().OrderBy(i => i.SortOrder).ThenBy(i => i.Code).ToListAsync(ct);
        var activities = await ActivitySummaryAsync(null, ct);
        var indicators = await db.Indicators.AsNoTracking().Include(i => i.Versions).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var university = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.Kind == OrgUnitKind.University).Select(u => u.Id).FirstOrDefaultAsync(ct);
        var targets = (await db.Targets.AsNoTracking().Where(t => t.UnitId == university).ToListAsync(ct)).ToLookup(t => t.IndicatorId);
        var ownerIds = initiatives.Where(i => i.OwnerUnitId != null).Select(i => i.OwnerUnitId!.Value).Distinct().ToList();
        var owners = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => ownerIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NameAr, ct);

        StandardNode Node(Standard s) => new(s.Id, s.Code, s.NameAr, s.NameEn, s.Description, s.PillarId, s.SortOrder, s.Version,
            initiatives.Where(i => i.StandardId == s.Id).Select(i => new InitiativeNode(i.Id, i.Code, i.NameAr, i.Description, i.ExpectedOutcome, i.OwnerUnitId,
                i.OwnerUnitId is { } o ? owners.GetValueOrDefault(o) : null, i.StartYear, i.EndYear, i.Status, i.IndicatorCodes, i.Version,
                activities.GetValueOrDefault(i.Id)?.Count ?? 0, activities.GetValueOrDefault(i.Id)?.Progress)).ToList(),
            indicators.Where(i => i.StandardId == s.Id).Select(i =>
            {
                var v = i.Current;
                var mine = targets[i.Id].ToList();
                var each = mine.FirstOrDefault(t => t.Kind == TargetKind.EachCollege);
                return new IndicatorNode(i.Code, v.NameAr, v.Definition, v.Numerator, v.Denominator, v.Unit, v.Direction, v.Level, v.Aggregation, v.Status, i.Effective is not null, i.Suspended,
                    i.PlanStartYear, i.PlanEndYear, i.PlanNote, mine.FirstOrDefault(t => t.Kind == TargetKind.Baseline)?.Value,
                    mine.Where(t => t.Kind == TargetKind.Target).OrderBy(t => t.Year).Select(t => new YearTargetDto(t.Id, t.Year, t.Value, t.Text, t.Status, t.Version)).ToList(),
                    each is null ? null : each.Value is { } ev ? DistributionRules.Fmt(ev) : each.Text, i.Version,
                    v.NameEn, v.Formula, v.Accumulation, i.Perspectives, i.SdgPrimary, i.SdgSecondary, i.SdgInduced);
            }).ToList());

        var targetsEdit = UnitScope.For(me, Features.Targets, Cap.Edit).All;
        // ADR-0017: the plan years of the period in force.
        var plan = await planPeriod.CurrentAsync(ct);
        return new StrategyTreeDto(
            plan.BaselineYear,
            plan.Years.ToList(),
            pillars.Select(p => new PillarNode(p.Id, p.Code, p.NameAr, p.NameEn, p.Description, p.SortOrder, p.Version,
                standards.Where(s => s.PillarId == p.Id).Select(Node).ToList())).ToList(),
            standards.Where(s => s.PillarId is null || pillars.All(p => p.Id != s.PillarId)).Select(Node).ToList(),
            new StrategyActions(
                me.Has(Feature, Cap.Edit), me.Has(Feature, Cap.Add), me.Has(Feature, Cap.Delete),
                me.Has(Features.Dictionary, Cap.Add), me.Has(Features.Dictionary, Cap.Edit), me.Has(Features.Dictionary, Cap.Delete),
                targetsEdit, targetsEdit && me.Has(Features.Targets, Cap.Delete), me.HasSpecial(Features.Targets, Specials.Approve)));
    }

    // ----------------------------------------------------------------- الركائز

    public async Task<PillarNode> CreatePillarAsync(SavePillarRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var codes = await db.Pillars.Select(p => p.Code).ToListAsync(ct);
        var pillar = new Pillar
        {
            Code = $"P{NextNumber(codes, "P") }",
            SortOrder = req.SortOrder ?? (await db.Pillars.Select(p => (int?)p.SortOrder).MaxAsync(ct) ?? 0) + 1,
        };
        await ApplyAsync(pillar, req, ct);
        db.Pillars.Add(pillar);
        Record("kpi.pillar_created", "إضافة ركيزة", "Added a pillar", "pillar", pillar.Id, pillar.Code, null);
        await db.SaveChangesAsync(ct);
        return PillarDto(pillar);
    }

    public async Task<PillarNode> UpdatePillarAsync(Guid id, SavePillarRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var pillar = await db.Pillars.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, pillar, req.Version);
        if (req.SortOrder is { } order) pillar.SortOrder = order;
        await ApplyAsync(pillar, req, ct);
        Record("kpi.pillar_updated", "تعديل ركيزة", "Edited a pillar", "pillar", pillar.Id, pillar.Code, null);
        await db.SaveChangesAsync(ct);
        return PillarDto(pillar);
    }

    public async Task DeletePillarAsync(Guid id, DeleteRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(req.Reason);
        var pillar = await db.Pillars.FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw DomainException.NotFound();
        var count = await db.Standards.CountAsync(s => s.PillarId == id, ct);
        if (count > 0)
            throw new DomainException("pillar_in_use", $"تحت الركيزة {count} معيار؛ انقلها إلى ركيزة أخرى أو احذفها أولًا.", "Move or delete the pillar's standards first.", 409);
        db.Pillars.Remove(pillar);
        Record("kpi.pillar_deleted", "حذف ركيزة", "Deleted a pillar", "pillar", pillar.Id, pillar.Code, why);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Pillar pillar, SavePillarRequest req, CancellationToken ct)
    {
        pillar.NameAr = Name(req.NameAr, 200, "اسم الركيزة");
        if (await db.Pillars.AnyAsync(p => p.Id != pillar.Id && p.NameAr == pillar.NameAr, ct))
            throw new DomainException("duplicate_name", "توجد ركيزة بالاسم نفسه.", "A pillar with this name exists.");
        pillar.NameEn = Require.Optional(req.NameEn, 200);
        pillar.Description = Require.Optional(req.Description, 4000);
    }

    // ----------------------------------------------------------------- المعايير

    public async Task<StandardNode> CreateStandardAsync(SaveStandardRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var codes = await db.Standards.Select(s => s.Code).ToListAsync(ct);
        var standard = new Standard
        {
            Code = $"S{NextNumber(codes, "S"):00}",
            SortOrder = req.SortOrder ?? (await db.Standards.Select(s => (int?)s.SortOrder).MaxAsync(ct) ?? 0) + 1,
        };
        await ApplyAsync(standard, req, ct);
        db.Standards.Add(standard);
        Record("kpi.standard_created", "إضافة معيار", "Added a standard", "standard", standard.Id, standard.Code, null);
        await db.SaveChangesAsync(ct);
        return StandardDto(standard);
    }

    public async Task<StandardNode> UpdateStandardAsync(Guid id, SaveStandardRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var standard = await db.Standards.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, standard, req.Version);
        if (req.SortOrder is { } order) standard.SortOrder = order;
        await ApplyAsync(standard, req, ct);
        Record("kpi.standard_updated", "تعديل معيار", "Edited a standard", "standard", standard.Id, standard.Code, null);
        await db.SaveChangesAsync(ct);
        return StandardDto(standard);
    }

    public async Task DeleteStandardAsync(Guid id, DeleteRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(req.Reason);
        var standard = await db.Standards.FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw DomainException.NotFound();
        var indicators = await db.Indicators.CountAsync(i => i.StandardId == id, ct);
        var initiatives = await db.Initiatives.CountAsync(i => i.StandardId == id, ct);
        if (indicators + initiatives > 0)
            throw new DomainException("standard_in_use", $"تحت المعيار {indicators} مؤشر و{initiatives} مبادرة؛ احذفها أو أوقف المؤشرات أولًا.",
                "Delete the standard's indicators and initiatives first.", 409);
        db.Standards.Remove(standard);
        Record("kpi.standard_deleted", "حذف معيار", "Deleted a standard", "standard", standard.Id, standard.Code, why);
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Standard standard, SaveStandardRequest req, CancellationToken ct)
    {
        standard.NameAr = Name(req.NameAr, 200, "اسم المعيار");
        if (await db.Standards.AnyAsync(s => s.Id != standard.Id && s.NameAr == standard.NameAr, ct))
            throw new DomainException("duplicate_name", "يوجد معيار بالاسم نفسه.", "A standard with this name exists.");
        if (req.PillarId is { } p && !await db.Pillars.AnyAsync(x => x.Id == p, ct))
            throw new DomainException("pillar_not_found", "الركيزة غير موجودة.", "Pillar not found.", 400) { Field = "pillarId" };
        standard.PillarId = req.PillarId;
        standard.NameEn = Require.Optional(req.NameEn, 200);
        standard.Description = Require.Optional(req.Description, 4000);
    }

    // ----------------------------------------------------------------- المبادرات

    public async Task<InitiativeNode> CreateInitiativeAsync(SaveInitiativeRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var standard = await db.Standards.AsNoTracking().FirstOrDefaultAsync(s => s.Id == req.StandardId, ct)
                       ?? throw new DomainException("standard_not_found", "المعيار غير موجود.", "Standard not found.", 400) { Field = "standardId" };
        var siblings = await db.Initiatives.Where(i => i.StandardId == standard.Id).Select(i => new { i.Code, i.SortOrder }).ToListAsync(ct);
        var now = clock.UtcNow;
        var initiative = new Initiative
        {
            Code = $"{standard.Code}-M{NextNumber(siblings.Select(s => s.Code.Split('-').Last()), "M"):00}",
            SortOrder = siblings.Select(s => s.SortOrder).DefaultIfEmpty(0).Max() + 1,
            CreatedAt = now,
        };
        await ApplyAsync(initiative, req, now, ct);
        db.Initiatives.Add(initiative);
        Record("kpi.initiative_created", "إضافة مبادرة", "Added an initiative", "initiative", initiative.Id, initiative.Code, null);
        await db.SaveChangesAsync(ct);
        return await InitiativeDtoAsync(initiative, ct);
    }

    public async Task<InitiativeNode> UpdateInitiativeAsync(Guid id, SaveInitiativeRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var initiative = await db.Initiatives.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, initiative, req.Version);
        if (req.StandardId != initiative.StandardId && !await db.Standards.AnyAsync(s => s.Id == req.StandardId, ct))
            throw new DomainException("standard_not_found", "المعيار غير موجود.", "Standard not found.", 400) { Field = "standardId" };
        await ApplyAsync(initiative, req, clock.UtcNow, ct);
        Record("kpi.initiative_updated", "تعديل مبادرة", "Edited an initiative", "initiative", initiative.Id, initiative.Code, null);
        await db.SaveChangesAsync(ct);
        return await InitiativeDtoAsync(initiative, ct);
    }

    public async Task DeleteInitiativeAsync(Guid id, DeleteRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(req.Reason);
        var initiative = await db.Initiatives.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw DomainException.NotFound();
        // G-16 (system integration review 2026-11-24): the activities go with the initiative (cascade); the audit named none of them.
        var activities = await db.InitiativeActivities.AsNoTracking().Where(a => a.InitiativeId == id).OrderBy(a => a.SortOrder).Select(a => a.TitleAr).ToListAsync(ct);
        db.Initiatives.Remove(initiative);
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.initiative_deleted", "حذف مبادرة", "Deleted an initiative") with
        {
            Target = new AuditTarget("initiative", initiative.Id.ToString(), initiative.Code),
            Reason = why,
            Details = activities.Count == 0 ? null : new { activitiesDeleted = activities },
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task ApplyAsync(Initiative initiative, SaveInitiativeRequest req, DateTimeOffset now, CancellationToken ct)
    {
        initiative.NameAr = Name(req.NameAr, 300, "اسم المبادرة");
        PlanRules.ValidateYears(req.StartYear, req.EndYear, await planPeriod.CurrentAsync(ct), "مدة المبادرة");
        if (req.OwnerUnitId is { } owner && !await read.Query<Domain.Organization.OrgUnit>().AnyAsync(u => u.Id == owner && u.Active, ct))
            throw new DomainException("owner_not_found", "الجهة المسؤولة غير موجودة أو معطلة.", "The owner unit was not found.", 400) { Field = "ownerUnitId" };
        var codes = (req.IndicatorCodes ?? []).Select(c => c.Trim().ToUpperInvariant()).Where(c => c.Length > 0).Distinct().ToList();
        var known = await db.Indicators.Where(i => codes.Contains(i.Code)).Select(i => i.Code).ToListAsync(ct);
        if (codes.Except(known).FirstOrDefault() is { } unknown)
            throw new DomainException("indicator_not_found", $"المؤشر {unknown} غير موجود.", $"Indicator {unknown} was not found.", 400);
        initiative.StandardId = req.StandardId;
        initiative.Description = Require.Optional(req.Description, 4000);
        initiative.ExpectedOutcome = Require.Optional(req.ExpectedOutcome, 2000);
        initiative.OwnerUnitId = req.OwnerUnitId;
        initiative.StartYear = req.StartYear;
        initiative.EndYear = req.EndYear;
        initiative.Status = Enum.IsDefined(req.Status) ? req.Status : InitiativeStatus.Planned;
        initiative.IndicatorCodes = codes.Order(StringComparer.Ordinal).ToList();
        initiative.UpdatedAt = now;
    }

    private async Task<InitiativeNode> InitiativeDtoAsync(Initiative i, CancellationToken ct)
    {
        var owner = i.OwnerUnitId is { } o ? await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.Id == o).Select(u => u.NameAr).FirstOrDefaultAsync(ct) : null;
        var summary = (await ActivitySummaryAsync(i.Id, ct)).GetValueOrDefault(i.Id);
        return new InitiativeNode(i.Id, i.Code, i.NameAr, i.Description, i.ExpectedOutcome, i.OwnerUnitId, owner, i.StartYear, i.EndYear, i.Status, i.IndicatorCodes, i.Version,
            summary?.Count ?? 0, summary?.Progress);
    }

    private sealed record ActivitySummary(int Count, int Progress);

    /// <summary>The number of activities of each initiative and their average completion, in one query.</summary>
    private async Task<Dictionary<Guid, ActivitySummary>> ActivitySummaryAsync(Guid? initiativeId, CancellationToken ct) =>
        (await db.InitiativeActivities.AsNoTracking().Where(a => initiativeId == null || a.InitiativeId == initiativeId)
            .GroupBy(a => a.InitiativeId).Select(g => new { g.Key, Count = g.Count(), Progress = g.Average(a => (double)a.Progress) }).ToListAsync(ct))
        .ToDictionary(x => x.Key, x => new ActivitySummary(x.Count, (int)Math.Round(x.Progress)));

    // ----------------------------------------------------------------- أنشطة المبادرة (ADR-0017)

    /// <summary>The activities of an initiative, in their order.</summary>
    public async Task<IReadOnlyList<ActivityDto>> ActivitiesAsync(Guid initiativeId, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        if (!await db.Initiatives.AnyAsync(i => i.Id == initiativeId, ct)) throw DomainException.NotFound();
        var list = await db.InitiativeActivities.AsNoTracking().Where(a => a.InitiativeId == initiativeId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.CreatedAt).ToListAsync(ct);
        var owners = await OwnerNamesAsync(list.Select(a => a.OwnerUnitId), ct);
        return list.Select(a => ActivityDto(a, owners)).ToList();
    }

    public async Task<ActivityDto> CreateActivityAsync(Guid initiativeId, SaveActivityRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var initiative = await db.Initiatives.AsNoTracking().FirstOrDefaultAsync(i => i.Id == initiativeId, ct) ?? throw DomainException.NotFound();
        var now = clock.UtcNow;
        var last = await db.InitiativeActivities.Where(a => a.InitiativeId == initiativeId).Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? 0;
        var activity = new InitiativeActivity { InitiativeId = initiativeId, CreatedAt = now, SortOrder = req.SortOrder ?? last + 1 };
        await ApplyActivityAsync(activity, req, now, ct);
        db.InitiativeActivities.Add(activity);
        RecordActivity("kpi.activity_created", "إضافة نشاط لمبادرة", "Added an initiative activity", activity, initiative.Code, null, null);
        await db.SaveChangesAsync(ct);
        return ActivityDto(activity, await OwnerNamesAsync([activity.OwnerUnitId], ct));
    }

    public async Task<ActivityDto> UpdateActivityAsync(Guid id, SaveActivityRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var activity = await db.InitiativeActivities.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, activity, req.Version);
        var before = new { activity.TitleAr, activity.Status, activity.Progress, activity.StartDate, activity.EndDate, activity.OwnerUnitId };
        await ApplyActivityAsync(activity, req, clock.UtcNow, ct);
        if (req.SortOrder is { } order) activity.SortOrder = order;
        var changes = new ChangeSet().Add("titleAr", before.TitleAr, activity.TitleAr).Add("status", before.Status, activity.Status)
            .Add("progress", before.Progress, activity.Progress).Add("startDate", before.StartDate, activity.StartDate)
            .Add("endDate", before.EndDate, activity.EndDate).Add("ownerUnitId", before.OwnerUnitId, activity.OwnerUnitId);
        var code = await db.Initiatives.Where(i => i.Id == activity.InitiativeId).Select(i => i.Code).FirstAsync(ct);
        RecordActivity("kpi.activity_updated", "تعديل نشاط مبادرة", "Edited an initiative activity", activity, code, null, changes);
        await db.SaveChangesAsync(ct);
        return ActivityDto(activity, await OwnerNamesAsync([activity.OwnerUnitId], ct));
    }

    public async Task DeleteActivityAsync(Guid id, DeleteRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(req.Reason);
        var activity = await db.InitiativeActivities.FirstOrDefaultAsync(a => a.Id == id, ct) ?? throw DomainException.NotFound();
        var code = await db.Initiatives.Where(i => i.Id == activity.InitiativeId).Select(i => i.Code).FirstAsync(ct);
        db.InitiativeActivities.Remove(activity);
        RecordActivity("kpi.activity_deleted", "حذف نشاط مبادرة", "Deleted an initiative activity", activity, code, why, null);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// The rules of an activity: a title; the end on or after the start; completion from 0 to 100, and 100 once completed;
    /// an active responsible unit if one is given.
    /// </summary>
    private async Task ApplyActivityAsync(InitiativeActivity activity, SaveActivityRequest req, DateTimeOffset now, CancellationToken ct)
    {
        activity.TitleAr = Name(req.TitleAr, 300, "اسم النشاط", "titleAr");
        if (!Enum.IsDefined(req.Status))
            throw new DomainException("invalid_status", "حالة النشاط غير صالحة.", "Invalid activity status.", 400) { Field = "status" };
        if (req.Progress is < 0 or > 100)
            throw new DomainException("invalid_progress", "نسبة الإنجاز من 0 إلى 100.", "Progress is from 0 to 100.", 400) { Field = "progress" };
        if (req.StartDate is { } start && req.EndDate is { } end && end < start)
            throw new DomainException("end_before_start", "تاريخ نهاية النشاط قبل بدايته.", "The activity ends before it starts.", 400) { Field = "endDate" };
        if (req.OwnerUnitId is { } owner && !await read.Query<Domain.Organization.OrgUnit>().AnyAsync(u => u.Id == owner && u.Active, ct))
            throw new DomainException("owner_not_found", "الجهة المسؤولة غير موجودة أو معطلة.", "The owner unit was not found.", 400) { Field = "ownerUnitId" };
        activity.Description = Require.Optional(req.Description, 2000);
        activity.OwnerUnitId = req.OwnerUnitId;
        activity.StartDate = req.StartDate;
        activity.EndDate = req.EndDate;
        activity.Status = req.Status;
        activity.Progress = req.Status == InitiativeStatus.Completed ? 100 : req.Progress;
        activity.UpdatedAt = now;
    }

    private async Task<Dictionary<Guid, string>> OwnerNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.OfType<Guid>().Distinct().ToList();
        return list.Count == 0 ? [] : await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NameAr, ct);
    }

    private static ActivityDto ActivityDto(InitiativeActivity a, Dictionary<Guid, string> owners) =>
        new(a.Id, a.InitiativeId, a.TitleAr, a.Description, a.OwnerUnitId, a.OwnerUnitId is { } o ? owners.GetValueOrDefault(o) : null,
            a.StartDate, a.EndDate, a.Status, a.Progress, a.SortOrder, a.Version);

    private void RecordActivity(string verb, string ar, string en, InitiativeActivity a, string initiativeCode, string? reason, ChangeSet? changes) =>
        audit.Record(AuditEvents.Audit("kpi", Feature, verb, ar, en) with
        {
            Target = new AuditTarget("initiative_activity", a.Id.ToString(), a.TitleAr),
            Related = [new AuditTarget("initiative", a.InitiativeId.ToString(), initiativeCode)],
            Reason = reason,
            Changes = changes?.ToList(),
        });

    // ----------------------------------------------------------------- مشترك

    /// <summary>الرقم التالي بعد أكبر رقم مستخدم (P7 بعد P6)؛ الرموز لا يعاد استخدامها لأن المحذوف يبقى في السجل.</summary>
    public static int NextNumber(IEnumerable<string> codes, string prefix) =>
        codes.Select(c => c.StartsWith(prefix, StringComparison.Ordinal) && int.TryParse(c.AsSpan(prefix.Length), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;

    /// <param name="jsonField">The request field the name came from, for the error (BE-6).</param>
    private static string Name(string? value, int max, string field, string jsonField = "nameAr")
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException("name_required", $"اكتب {field}.", "Enter the name.", 400) { Field = jsonField };
        return v.Length <= max ? v : throw new DomainException("text_too_long", $"«{field}» أطول من {max} حرف.", "The name is too long.", 400) { Field = jsonField };
    }

    private void Record(string verb, string ar, string en, string type, Guid id, string code, string? reason) =>
        audit.Record(AuditEvents.Audit("kpi", Feature, verb, ar, en) with { Target = new AuditTarget(type, id.ToString(), code), Reason = reason });

    private static PillarNode PillarDto(Pillar p) => new(p.Id, p.Code, p.NameAr, p.NameEn, p.Description, p.SortOrder, p.Version, []);

    private static StandardNode StandardDto(Standard s) => new(s.Id, s.Code, s.NameAr, s.NameEn, s.Description, s.PillarId, s.SortOrder, s.Version, [], []);
}

/// <summary>قواعد مدد الخطة (دوال نقية).</summary>
public static class PlanRules
{
    /// <summary>سنتا البداية والنهاية داخل سنوات الخطة، والنهاية لا تسبق البداية.</summary>
    public static void ValidateYears(int? start, int? end, Scorecard.PlanPeriod plan, string field)
    {
        var first = plan.FirstYear;
        var last = plan.LastYear;
        if (start is { } s && (s < first || s > last))
            throw new DomainException("plan_year", $"{field}: سنة البداية من {first} إلى {last}.", "Start year is outside the plan.", 400) { Field = "startYear" };
        if (end is { } e && (e < first || e > last))
            throw new DomainException("plan_year", $"{field}: سنة النهاية من {first} إلى {last}.", "End year is outside the plan.", 400) { Field = "endYear" };
        if (start is { } a && end is { } b && b < a)
            throw new DomainException("plan_order", $"{field}: سنة النهاية لا تسبق سنة البداية.", "The end year precedes the start year.", 400) { Field = "endYear" };
    }

    /// <summary>وصف مدة المستهدف كما يقرؤه الناس: قصير أو متوسط أو طويل المدى، ومتى يبدأ.</summary>
    public static string Horizon(int? start, int? end, int firstYear, int lastYear)
    {
        var s = start ?? firstYear;
        var e = end ?? lastYear;
        var years = e - s + 1;
        var kind = years <= 2 ? "قصير المدى" : years <= 3 ? "متوسط المدى" : "طويل المدى";
        var span = years == 1 ? "سنة واحدة" : years == 2 ? "سنتان" : $"{years} سنوات";
        return s > firstYear ? $"{kind} ({span})، يبدأ في {s} ويتحقق في {e}" : $"{kind} ({span})، يتحقق في {e}";
    }
}
