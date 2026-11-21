using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Collection;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Application.Collection;

public sealed record CycleDto(
    Guid Id, string Name, string PeriodLabel, int TargetYear, DateOnly OpensOn, DateOnly DueOn, CycleStatus Status, bool Trial,
    int Indicators, int Forms, IReadOnlyDictionary<string, int> FormsByStatus, int Overdue, uint Version, int? Quarter);

public sealed record SaveCycleRequest(string Name, string? PeriodLabel, int TargetYear, DateOnly OpensOn, DateOnly DueOn, bool Trial, uint? Version, int? Quarter = null);

public sealed record OpenCyclePreview(int Indicators, int Forms, int Departments, int Colleges, int Central, IReadOnlyList<string> Skipped);

/// <summary>
/// دورات جمع البيانات: الإنشاء والفتح والإغلاق. الفتح يأخذ لقطة من التعريفات المعتمدة (أو قيد العمل في الدورة التجريبية)،
/// وينشئ نموذجًا لكل جهة تنطبق عليها مؤشرات بحسب مستوى جمع بياناتها: القسم، أو الكلية، أو الجهة المالكة للمؤشر المركزي.
/// </summary>
public sealed class CyclesService(ICollectionStore db, IReadModel read, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, Settings.PlanPeriodService plan)
{
    private const string Feature = Features.Cycles;

    public async Task<IReadOnlyList<CycleDto>> ListAsync(CancellationToken ct)
    {
        // قائمة الدورات يحتاجها من يتابع الدورات أو يدخل النماذج أو يطالع النتائج.
        var me = await access.CurrentAsync(ct);
        if (!me.Has(Feature, Cap.View) && !me.Has(Features.Forms, Cap.View) && !me.Has(Features.Results, Cap.View)) throw DomainException.Forbidden();
        var cycles = await db.Cycles.AsNoTracking().OrderByDescending(c => c.CreatedAt).ToListAsync(ct);
        // مدخلو البيانات لا يرون الدورة قيد الإعداد.
        if (!me.Has(Feature, Cap.Edit)) cycles = cycles.Where(c => c.Status != CycleStatus.Draft).ToList();
        var ids = cycles.Select(c => c.Id).ToList();
        // أعداد النماذج لمستخدم الكلية أو الإدارة من نماذج جهته وحدها، لا الجامعة كلها.
        var scope = me.Has(Features.Forms, Cap.View) ? UnitScope.For(me, Features.Forms, Cap.View) : UnitScope.For(me, Feature, Cap.View);
        var forms = db.Submissions.AsNoTracking().Where(s => ids.Contains(s.CycleId));
        if (!scope.All)
        {
            var visible = (await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Select(u => new { u.Id, u.Path }).ToListAsync(ct)).Where(u => scope.Covers(u.Path)).Select(u => u.Id).ToList();
            forms = forms.Where(s => visible.Contains(s.UnitId));
        }
        // PERF-B2: counted in SQL per cycle, status and lateness, instead of reading every form of every cycle.
        var today = CairoTime.DateOf(clock.UtcNow);
        var counts = await forms.GroupBy(s => new { s.CycleId, s.Status, Late = s.DueOn < today })
            .Select(g => new { g.Key.CycleId, g.Key.Status, g.Key.Late, Count = g.Count() }).ToListAsync(ct);
        var indicators = await db.CycleIndicators.AsNoTracking().Where(i => ids.Contains(i.CycleId)).GroupBy(i => i.CycleId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return cycles.Select(c =>
        {
            var mine = counts.Where(f => f.CycleId == c.Id).ToList();
            return new CycleDto(c.Id, c.Name, c.PeriodLabel, c.TargetYear, c.OpensOn, c.DueOn, c.Status, c.Trial,
                indicators.FirstOrDefault(i => i.Key == c.Id)?.Count ?? 0, mine.Sum(f => f.Count),
                mine.GroupBy(f => f.Status.ToString()).ToDictionary(g => g.Key, g => g.Sum(f => f.Count)),
                c.Status == CycleStatus.Open ? mine.Where(f => f.Status != SubmissionStatus.Approved && f.Late).Sum(f => f.Count) : 0,
                c.Version, c.Quarter);
        }).ToList();
    }

    public async Task<CycleDto> CreateAsync(SaveCycleRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var cycle = new CollectionCycle { CreatedBy = current.UserId, CreatedAt = clock.UtcNow };
        Apply(cycle, req, await plan.CurrentAsync(ct));
        await EnsureQuarterFreeAsync(cycle, ct);
        db.Cycles.Add(cycle);
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.cycle_created", "إنشاء دورة جمع بيانات", "Created a collection cycle") with
        {
            Target = new AuditTarget("cycle", cycle.Id.ToString(), cycle.Name),
        });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == cycle.Id);
    }

    public async Task<CycleDto> UpdateAsync(Guid id, SaveCycleRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        // BE-4: the forms' deadlines and the cycle with its audit row commit together or not at all.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var cycle = await db.Cycles.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, cycle, req.Version);
        if (cycle.Status == CycleStatus.Closed) throw new DomainException("cycle_closed", "الدورة مغلقة.", "The cycle is closed.");
        if (cycle.Status == CycleStatus.Open && (req.TargetYear != cycle.TargetYear || req.Trial != cycle.Trial || req.Quarter != cycle.Quarter))
            throw new DomainException("cycle_open", "بعد الفتح يعدل الاسم والمواعيد فقط.", "After opening only the name and dates can change.");
        var oldDue = cycle.DueOn;
        Apply(cycle, req, await plan.CurrentAsync(ct));
        await EnsureQuarterFreeAsync(cycle, ct);
        if (cycle.Status == CycleStatus.Open && oldDue != cycle.DueOn)
        {
            // تمديد الدورة يمدد موعد النماذج التي لم تمدد لها مهلة خاصة.
            await db.Submissions.Where(s => s.CycleId == id && s.DueOn == oldDue).ExecuteUpdateAsync(s => s.SetProperty(x => x.DueOn, cycle.DueOn), ct);
        }
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.cycle_updated", "تعديل دورة جمع بيانات", "Updated a collection cycle") with
        {
            Target = new AuditTarget("cycle", cycle.Id.ToString(), cycle.Name),
            Changes = new ChangeSet().Add("dueOn", oldDue, cycle.DueOn).ToList(),
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == id);
    }

    /// <summary>ما سيحدث عند الفتح: عدد المؤشرات والنماذج، وما يستبعد ولماذا. يعرض قبل التأكيد.</summary>
    public async Task<OpenCyclePreview> PreviewOpenAsync(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var cycle = await db.Cycles.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound();
        var plan = await PlanAsync(cycle, ct);
        return new OpenCyclePreview(plan.Indicators.Count, plan.Units.Count, plan.Units.Count(u => u.Kind == OrgUnitKind.Department),
            plan.Units.Count(u => u.IsCollegeLevel), plan.Units.Count(u => u.Kind is not OrgUnitKind.Department && !u.IsCollegeLevel), plan.Skipped);
    }

    public async Task<CycleDto> OpenAsync(Guid id, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var cycle = await db.Cycles.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound();
        if (cycle.Status != CycleStatus.Draft) throw new DomainException("cycle_not_draft", "تفتح الدورة قيد الإعداد فقط.", "Only a draft cycle can be opened.");
        var plan = await PlanAsync(cycle, ct);
        if (plan.Indicators.Count == 0)
            throw new DomainException("no_indicators", cycle.Trial
                ? "لا توجد مؤشرات قابلة للقياس (غير موقوفة)."
                : "لا توجد مؤشرات معتمدة بعد. اعتمد التعريفات أولًا، أو اجعل الدورة بروفة داخلية.", "No indicators to measure.");
        if (plan.Units.Count == 0) throw new DomainException("no_units", "لا توجد جهات نشطة تنطبق عليها المؤشرات.", "No active units apply.");

        db.CycleIndicators.AddRange(plan.Indicators);
        db.Submissions.AddRange(plan.Units.Select(u => new Submission { CycleId = cycle.Id, UnitId = u.Id, DueOn = cycle.DueOn }));
        cycle.Status = CycleStatus.Open;
        cycle.OpenedAt = clock.UtcNow;
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.cycle_opened", "فتح دورة جمع بيانات", "Opened a collection cycle") with
        {
            Target = new AuditTarget("cycle", cycle.Id.ToString(), cycle.Name),
            Details = new { indicators = plan.Indicators.Count, forms = plan.Units.Count, cycle.Trial },
        });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == id);
    }

    public async Task<CycleDto> CloseAsync(Guid id, string? reason, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(reason, field: "note"); // The endpoint takes it as NoteRequest.Note.
        var cycle = await db.Cycles.FirstOrDefaultAsync(c => c.Id == id, ct) ?? throw DomainException.NotFound();
        if (cycle.Status != CycleStatus.Open) throw new DomainException("cycle_not_open", "تغلق الدورة المفتوحة فقط.", "Only an open cycle can be closed.");
        cycle.Status = CycleStatus.Closed;
        cycle.ClosedAt = clock.UtcNow;
        var pending = await db.Submissions.CountAsync(s => s.CycleId == id && s.Status != SubmissionStatus.Approved, ct);
        audit.Record(AuditEvents.Audit("dc", Feature, "dc.cycle_closed", "إغلاق دورة جمع بيانات", "Closed a collection cycle") with
        {
            Target = new AuditTarget("cycle", cycle.Id.ToString(), cycle.Name),
            Reason = why,
            Details = new { notApproved = pending },
        });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(ct)).First(c => c.Id == id);
    }

    private sealed record Plan(List<CycleIndicator> Indicators, List<OrgUnit> Units, List<string> Skipped);

    private async Task<Plan> PlanAsync(CollectionCycle cycle, CancellationToken ct)
    {
        var indicators = await read.Query<Domain.Kpi.Indicator>().AsNoTracking().Include(i => i.Versions).Where(i => !i.Suspended).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var units = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.Active).ToListAsync(ct);
        var root = units.First(u => u.Kind == OrgUnitKind.University);
        var map = new Organization.AssignmentMap(units, await read.Query<Domain.Organization.UnitIndicator>().AsNoTracking().ToListAsync(ct));
        var skipped = new List<string>();
        var picked = new List<CycleIndicator>();
        foreach (var i in indicators)
        {
            var v = i.Effective ?? (cycle.Trial ? i.Current : null);
            if (v is null)
            {
                skipped.Add($"{i.Code}: لم يعتمد تعريفه بعد");
                continue;
            }
            // جهة الإدخال المركزية: لمؤشر الجامعة كلها، وللعدد المميز الذي يدخل عدد الجامعة فيه بعد إزالة التكرار لأنه لا يجمع من الكليات.
            var distinct = v.Aggregation == Aggregation.DistinctCount;
            var owner = v.OwnerUnitId is { } o ? units.FirstOrDefault(u => u.Id == o) : null;
            // مؤشر الجامعة كلها: الجهة المكلفة به، ثم الجهة المالكة في التعريف، ثم نموذج الجامعة.
            Guid? central = v.Level == CollectionLevel.University ? map.ResponsibleFor(i.Id)?.Id ?? owner?.Id ?? root.Id
                : distinct ? (owner is { IsCollegeLevel: false, Kind: not OrgUnitKind.Department } ? owner.Id : root.Id)
                : null;
            var collegeEntry = distinct && v.Level == CollectionLevel.Department;
            // الكليات والأقسام المكلفة به؛ فارغ إن كلفت به كل جهات مستواه.
            List<Guid>? unitIds = null;
            if (v.Level != CollectionLevel.University)
            {
                var reference = new Organization.IndicatorRef(i.Id, v.Level, distinct);
                var eligible = units.Where(u => v.Level == CollectionLevel.Department ? u.Kind == OrgUnitKind.Department || (collegeEntry && u.IsCollegeLevel) : u.IsCollegeLevel).ToList();
                var entering = eligible.Where(u => map.Enters(u, reference)).Select(u => u.Id).ToList();
                if (entering.Count < eligible.Count) unitIds = entering;
            }
            picked.Add(new CycleIndicator
            {
                CycleId = cycle.Id, IndicatorId = i.Id, VersionId = v.Id, Code = i.Code, Level = v.Level, CentralUnitId = central,
                CollegeEntry = collegeEntry, UnitIds = unitIds,
            });
        }
        // نموذج لكل جهة تدخل مؤشرًا واحدًا على الأقل.
        var needed = units.Where(u => u.Kind != OrgUnitKind.University && picked.Any(p => FormAssignmentRules.Applies(p, u))).ToList();
        foreach (var c in picked.Where(p => p.CentralUnitId is not null).Select(p => p.CentralUnitId!.Value).Distinct())
            if (needed.All(u => u.Id != c)) needed.Add(units.First(u => u.Id == c));
        return new Plan(picked, needed, skipped);
    }

    /// <summary>دورة رسمية واحدة لكل ربع: قيمة الربع في بطاقة الأداء من دورته وحدها. البروفة لا تحسب فلا تمنع.</summary>
    private async Task EnsureQuarterFreeAsync(CollectionCycle c, CancellationToken ct)
    {
        if (c.Quarter is not { } q || c.Trial) return;
        if (await db.Cycles.AnyAsync(x => x.Id != c.Id && !x.Trial && x.TargetYear == c.TargetYear && x.Quarter == q, ct))
            throw new DomainException("quarter_taken", $"توجد دورة رسمية لـ{(await plan.CurrentAsync(ct)).Label(c.TargetYear, q)} بالفعل.", "A cycle for this quarter already exists.", 409);
    }

    /// <summary>The cycle's fields, checked against the plan period in force (ADR-0017): a quarter of a plan year, a target year of the plan.</summary>
    private static void Apply(CollectionCycle c, SaveCycleRequest req, Scorecard.PlanPeriod plan)
    {
        c.Name = string.IsNullOrWhiteSpace(req.Name) ? throw new DomainException("name_required", "اكتب اسم الدورة.", "Enter the cycle name.", 400) { Field = "name" } : req.Name.Trim();
        if (req.Quarter is { } q && !plan.HasQuarter(req.TargetYear, q))
            throw new DomainException("invalid_quarter", $"الدورة الربعية لسنة من سنوات الخطة ({plan.FirstYear}–{plan.LastYear}) وربع من 1 إلى 4.",
                $"Quarterly cycles: a plan year ({plan.FirstYear}-{plan.LastYear}), quarter 1-4.", 400);
        c.Quarter = req.Quarter;
        c.PeriodLabel = !string.IsNullOrWhiteSpace(req.PeriodLabel) ? req.PeriodLabel.Trim()
            : req.Quarter is { } quarter ? plan.Label(req.TargetYear, quarter)
            : throw new DomainException("period_required", "اكتب الفترة التي تقيسها الدورة.", "Enter the period.", 400) { Field = "periodLabel" };
        if (req.TargetYear < plan.BaselineYear || req.TargetYear > plan.LastYear)
            throw new DomainException("invalid_year", $"سنة المستهدف من {plan.BaselineYear} إلى {plan.LastYear}.", $"Target year {plan.BaselineYear}-{plan.LastYear}.", 400) { Field = "targetYear" };
        if (req.DueOn < req.OpensOn) throw new DomainException("dates", "الموعد النهائي بعد تاريخ الفتح.", "The deadline is after the opening date.", 400) { Field = "dueOn" };
        c.TargetYear = req.TargetYear;
        c.OpensOn = req.OpensOn;
        c.DueOn = req.DueOn;
        c.Trial = req.Trial;
    }
}

/// <summary>لقطة تعريف المؤشر كما في الدورة، لحساب القيم والتحقق منها.</summary>
public static class CycleSpec
{
    public static ValueSpec Of(IndicatorVersion v) => new(v.Unit, v.Aggregation, !string.IsNullOrWhiteSpace(v.EvidenceRequired));
}
