using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;

namespace Tier1.Application.Kpi;

public sealed record StandardDto(Guid Id, string Code, string NameAr, string? PillarCode, string? PillarName, string? ClassificationNote, int IndicatorCount, int ApprovedCount, int OpenConflicts);

public sealed record IndicatorRowDto(
    string Code, string StandardCode, string NameAr, string Unit, Direction Direction, CollectionLevel Level, Aggregation Aggregation,
    DefinitionStatus Status, int VersionNumber, bool HasApproved, bool Suspended, int MissingCount, int OpenConflicts,
    decimal? Baseline, decimal? Target2031, string? CollegeTarget);

public sealed record VersionDto(
    Guid Id, int Number, DefinitionStatus Status, string NameAr, string? NameEn, string? AlternateName, string? Definition,
    string? Purpose, string? Exclusions, string? Numerator, string? Denominator, string Unit, Direction Direction,
    Frequency Frequency, CollectionLevel Level, Aggregation Aggregation, string? AggregationNote, string? DataSource,
    Guid? OwnerUnitId, string? OwnerUnitName, string? EvidenceRequired, string? SourcePages, string? ChangeReason,
    DateTimeOffset? SubmittedAt, DateTimeOffset? ApprovedAt, string? DecisionRef, string? ReturnNote,
    IReadOnlyList<string> Missing, uint Version, string? Formula, Accumulation Accumulation);

public sealed record IndicatorDetailDto(
    string Code, Guid StandardId, string StandardCode, string StandardName, bool Suspended,
    VersionDto Current, VersionDto? Effective, IReadOnlyList<VersionSummaryDto> History,
    IReadOnlyList<string> ConflictCodes, IndicatorActions Actions, FrameworkLinks Framework);

/// <summary>ربط المؤشر في الإطار الشامل: مناظير بطاقة أداء القطاع العام وأهداف التنمية المستدامة وأسباب الربط، وشرح خطة مستهدفه.</summary>
public sealed record FrameworkLinks(IReadOnlyList<int> Perspectives, int? SdgPrimary, int? SdgSecondary, int? SdgInduced, string? PssRationale, string? SdgRationale, string? PlanNote);

public sealed record VersionSummaryDto(int Number, DefinitionStatus Status, string? ChangeReason, DateTimeOffset CreatedAt, DateTimeOffset? ApprovedAt, string? DecisionRef);

/// <summary>ما يستطيعه المستخدم في هذا المؤشر الآن، فتظهر الأزرار المتاحة وحدها.</summary>
public sealed record IndicatorActions(bool CanEdit, bool CanSubmit, bool CanApprove, bool CanSuspend, bool EditCreatesVersion);

public sealed record SaveDefinitionRequest(
    string NameAr, string? NameEn, string? Definition, string? Purpose, string? Exclusions, string? Numerator, string? Denominator,
    string Unit, Direction Direction, Frequency Frequency, CollectionLevel Level, Aggregation Aggregation, string? AggregationNote,
    string? DataSource, Guid? OwnerUnitId, string? EvidenceRequired, string? ChangeReason, uint? Version,
    string? Formula = null, Accumulation? Accumulation = null);

public sealed record CreateIndicatorRequest(string StandardCode, string NameAr, string? Unit, CollectionLevel? Level, string? Reason);

public sealed record CommitteeDecisionRequest(string? DecisionRef, string? Note);

/// <summary>
/// قاموس المؤشرات: القراءة للجميع بصلاحية العرض، والتعديل ينشئ إصدارًا جديدًا للمعتمد، والاعتماد بقرار اللجنة العليا
/// من غير من أعدّ التعريف.
/// </summary>
public sealed class DictionaryService(IKpiStore db, IReadModel read, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit)
{
    private const string Feature = Features.Dictionary;

    public async Task<IReadOnlyList<StandardDto>> StandardsAsync(CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        var pillars = await db.Pillars.AsNoTracking().ToDictionaryAsync(p => p.Id, ct);
        var standards = await db.Standards.AsNoTracking().OrderBy(s => s.SortOrder).ToListAsync(ct);
        var indicators = await db.Indicators.AsNoTracking().Include(i => i.Versions).ToListAsync(ct);
        var openConflicts = await OpenConflictScopesAsync(ct);

        return standards.Select(s =>
        {
            var mine = indicators.Where(i => i.StandardId == s.Id).ToList();
            var pillar = s.PillarId is { } pid ? pillars.GetValueOrDefault(pid) : null;
            return new StandardDto(s.Id, s.Code, s.NameAr, pillar?.Code, pillar?.NameAr, s.ClassificationNote, mine.Count,
                mine.Count(i => i.Effective is not null),
                openConflicts.Count(scope => scope.Any(code => code == s.Code || code.StartsWith(s.Code + "-", StringComparison.Ordinal))));
        }).ToList();
    }

    public async Task<IReadOnlyList<IndicatorRowDto>> ListAsync(string? standard, DefinitionStatus? status, string? q, bool? withConflicts, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        var standards = await db.Standards.AsNoTracking().ToDictionaryAsync(s => s.Id, s => s.Code, ct);
        var indicators = await db.Indicators.AsNoTracking().Include(i => i.Versions).OrderBy(i => i.SortOrder).ToListAsync(ct);
        var university = await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => u.Kind == Domain.Organization.OrgUnitKind.University).Select(u => u.Id).FirstOrDefaultAsync(ct);
        var targets = await db.Targets.AsNoTracking().Where(t => t.UnitId == university).ToListAsync(ct);
        var openConflicts = await OpenConflictScopesAsync(ct);
        var term = q?.Trim();

        return indicators
            .Select(i =>
            {
                var v = i.Current;
                var mine = targets.Where(t => t.IndicatorId == i.Id).ToList();
                var stdCode = standards[i.StandardId];
                return new IndicatorRowDto(
                    i.Code, stdCode, v.NameAr, v.Unit, v.Direction, v.Level, v.Aggregation, v.Status, v.Number, i.Effective is not null, i.Suspended,
                    IndicatorRules.MissingForApproval(v).Count,
                    openConflicts.Count(scope => scope.Contains(i.Code) || scope.Contains(stdCode)),
                    mine.FirstOrDefault(t => t.Kind == TargetKind.Baseline)?.Value,
                    mine.FirstOrDefault(t => t.Kind == TargetKind.Target && t.Year == 2031)?.Value,
                    mine.FirstOrDefault(t => t.Kind == TargetKind.EachCollege) is { } each ? each.Text ?? each.Value?.ToString(System.Globalization.CultureInfo.InvariantCulture) : null);
            })
            .Where(r => standard is null || r.StandardCode == standard)
            .Where(r => status is null || r.Status == status)
            .Where(r => withConflicts is not true || r.OpenConflicts > 0)
            .Where(r => string.IsNullOrEmpty(term) || r.Code.Contains(term, StringComparison.OrdinalIgnoreCase) || r.NameAr.Contains(term, StringComparison.Ordinal))
            .ToList();
    }

    public async Task<IndicatorDetailDto> GetAsync(string code, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var indicator = await LoadAsync(code, tracking: false, ct);
        var standard = await db.Standards.AsNoTracking().FirstAsync(s => s.Id == indicator.StandardId, ct);
        var owners = await UnitNamesAsync(indicator.Versions.Select(v => v.OwnerUnitId), ct);
        var conflictCodes = (await db.Conflicts.AsNoTracking().Select(c => new { c.Code, c.Scope }).ToListAsync(ct))
            .Where(c => c.Scope.Contains(indicator.Code) || c.Scope.Contains(standard.Code))
            .Select(c => c.Code).Order(StringComparer.Ordinal).ToList();

        var current = indicator.Current;
        var canEdit = me.Has(Feature, Cap.Edit) && !indicator.Suspended;
        var actions = new IndicatorActions(
            CanEdit: canEdit,
            CanSubmit: canEdit && current.Status == DefinitionStatus.Draft && IndicatorRules.MissingForApproval(current).Count == 0,
            // فصل المهام يظهر في الواجهة أيضًا: من أعدّ التعريف أو أرسله لا يرى زر الاعتماد.
            CanApprove: me.HasSpecial(Feature, Specials.Approve) && current.Status == DefinitionStatus.UnderReview
                        && current.SubmittedBy != me.UserId && current.EditedBy != me.UserId,
            CanSuspend: me.Has(Feature, Cap.Delete),
            EditCreatesVersion: indicator.Working is null);

        return new IndicatorDetailDto(
            indicator.Code, standard.Id, standard.Code, standard.NameAr, indicator.Suspended,
            ToDto(current, owners), indicator.Effective is { } eff && eff.Id != current.Id ? ToDto(eff, owners) : null,
            indicator.Versions.OrderByDescending(v => v.Number).Select(v => new VersionSummaryDto(v.Number, v.Status, v.ChangeReason, v.CreatedAt, v.ApprovedAt, v.DecisionRef)).ToList(),
            conflictCodes, actions,
            new FrameworkLinks(indicator.Perspectives, indicator.SdgPrimary, indicator.SdgSecondary, indicator.SdgInduced, indicator.PssRationale, indicator.SdgRationale, indicator.PlanNote));
    }

    /// <summary>
    /// مؤشر جديد في معيار: رمزه التالي في المعيار (S04-11 مثلًا) لا يتغير بعدها، ويبدأ مسودة باسمه فقط
    /// تكمل بياناتها من صفحته ثم ترسل للاعتماد كأي تعريف.
    /// </summary>
    public async Task<IndicatorDetailDto> CreateAsync(CreateIndicatorRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var standard = await db.Standards.FirstOrDefaultAsync(s => s.Code == req.StandardCode, ct)
                       ?? throw new DomainException("standard_not_found", "المعيار غير موجود.", "Standard not found.", 400);
        var name = Text(req.NameAr, 300, "اسم المؤشر") ?? throw new DomainException("name_required", "اكتب اسم المؤشر.", "Enter the indicator name.", 400);
        var siblings = await db.Indicators.Where(i => i.StandardId == standard.Id).Select(i => new { i.Id, i.Code, i.SortOrder }).ToListAsync(ct);
        var siblingIds = siblings.Select(x => x.Id).ToList();
        if (await db.IndicatorVersions.AnyAsync(v => siblingIds.Contains(v.IndicatorId) && v.NameAr == name, ct))
            throw new DomainException("duplicate_name", "يوجد مؤشر بالاسم نفسه في هذا المعيار.", "An indicator with this name exists in the standard.");
        // أكواد الإطار الشامل «المعيار.الرقم» (10.09)، والقديمة «المعيار-الرقم» (S10-12).
        var framework = standard.Code.All(char.IsDigit);
        var next = siblings.Select(x => int.TryParse(x.Code.Split('-', '.').LastOrDefault(), out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
        var now = clock.UtcNow;
        var indicator = new Indicator
        {
            Code = framework ? $"{standard.Code}.{next:00}" : $"{standard.Code}-{next:00}",
            StandardId = standard.Id, SortOrder = siblings.Select(x => x.SortOrder).DefaultIfEmpty(0).Max() + 1, CreatedAt = now,
        };
        var version = new IndicatorVersion
        {
            IndicatorId = indicator.Id, Number = 1, Status = DefinitionStatus.Draft, NameAr = name, Unit = Text(req.Unit, 50, "وحدة القياس") ?? "%",
            Level = req.Level ?? CollectionLevel.College, Frequency = Frequency.Annual, EditedBy = current.UserId, EditedAt = now, CreatedAt = now,
            ChangeReason = Require.Optional(req.Reason, 500),
        };
        db.Indicators.Add(indicator);
        db.IndicatorVersions.Add(version);
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.indicator_created", "إضافة مؤشر جديد", "Added an indicator") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Reason = version.ChangeReason,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(indicator.Code, ct);
    }

    public async Task<IndicatorDetailDto> SaveAsync(string code, SaveDefinitionRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var indicator = await LoadAsync(code, tracking: true, ct);
        var now = clock.UtcNow;
        var createsVersion = indicator.Working is null;
        var v = IndicatorRules.EditableVersion(indicator, now, req.ChangeReason);
        if (!createsVersion) Concurrency.Expect(db, v, req.Version);
        if (req.OwnerUnitId is { } owner && !await read.Query<Domain.Organization.OrgUnit>().AnyAsync(u => u.Id == owner && u.Active, ct))
            throw new DomainException("owner_not_found", "الجهة المالكة غير موجودة أو معطلة.", "The owner unit was not found.", 400);

        var changes = new ChangeSet()
            .Add("definition", v.Definition, req.Definition).Add("unit", v.Unit, req.Unit).Add("direction", v.Direction, req.Direction)
            .Add("aggregation", v.Aggregation, req.Aggregation).Add("numerator", v.Numerator, req.Numerator).Add("denominator", v.Denominator, req.Denominator)
            .Add("dataSource", v.DataSource, req.DataSource).Add("ownerUnitId", v.OwnerUnitId, req.OwnerUnitId).Add("level", v.Level, req.Level);

        v.NameAr = Text(req.NameAr, 300, "اسم المؤشر") ?? throw new DomainException("name_required", "اكتب اسم المؤشر.", "Enter the indicator name.", 400);
        v.NameEn = Require.Optional(req.NameEn, 300);
        v.Definition = Require.Optional(req.Definition, 2000);
        v.Purpose = Require.Optional(req.Purpose, 1000);
        v.Exclusions = Require.Optional(req.Exclusions, 1000);
        v.Numerator = Require.Optional(req.Numerator, 500);
        v.Denominator = Require.Optional(req.Denominator, 500);
        v.Unit = Text(req.Unit, 50, "وحدة القياس") ?? "غير محدد";
        v.Direction = req.Direction;
        v.Frequency = req.Frequency;
        v.Level = req.Level;
        v.Aggregation = req.Aggregation;
        v.AggregationNote = Require.Optional(req.AggregationNote, 500);
        v.DataSource = Require.Optional(req.DataSource, 500);
        v.OwnerUnitId = req.OwnerUnitId;
        v.EvidenceRequired = Require.Optional(req.EvidenceRequired, 1000);
        v.Formula = Require.Optional(req.Formula, 2000);
        if (req.Accumulation is { } acc) v.Accumulation = acc;
        v.EditedBy = current.UserId;
        v.EditedAt = now;
        if (createsVersion) db.IndicatorVersions.Add(v);

        audit.Record(AuditEvents.Audit("kpi", Feature, createsVersion ? "kpi.definition_versioned" : "kpi.definition_saved",
            createsVersion ? "إنشاء إصدار جديد من تعريف مؤشر" : "تعديل مسودة تعريف مؤشر",
            createsVersion ? "Created a new indicator definition version" : "Edited an indicator definition draft") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Changes = changes.ToList(),
            Reason = createsVersion ? v.ChangeReason : null,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(code, ct);
    }

    public async Task<IndicatorDetailDto> SubmitAsync(string code, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var indicator = await LoadAsync(code, tracking: true, ct);
        var v = indicator.Working ?? throw new DomainException("no_draft", "لا توجد مسودة لهذا المؤشر.", "There is no draft for this indicator.");
        IndicatorRules.Submit(v, current.UserId!.Value, clock.UtcNow);
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.definition_submitted", "إرسال تعريف مؤشر للاعتماد", "Submitted an indicator definition") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Details = new { version = v.Number },
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(code, ct);
    }

    public async Task<IndicatorDetailDto> ApproveAsync(string code, CommitteeDecisionRequest req, CancellationToken ct)
    {
        await access.RequireSpecialAsync(Feature, Specials.Approve, ct);
        var indicator = await LoadAsync(code, tracking: true, ct);
        var v = indicator.Working ?? throw new DomainException("not_under_review", "لا يوجد تعريف ينتظر الاعتماد.", "Nothing is waiting for approval.");
        IndicatorRules.Approve(indicator, v, current.UserId!.Value, clock.UtcNow, req.DecisionRef ?? "");
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.definition_approved", "اعتماد تعريف مؤشر", "Approved an indicator definition") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Details = new { version = v.Number, v.DecisionRef },
            Permission = $"{Feature}:{Specials.Approve}",
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(code, ct);
    }

    public async Task<IndicatorDetailDto> ReturnAsync(string code, CommitteeDecisionRequest req, CancellationToken ct)
    {
        await access.RequireSpecialAsync(Feature, Specials.Approve, ct);
        var note = Require.Reason(req.Note, 5, "note_required", 1000);
        var indicator = await LoadAsync(code, tracking: true, ct);
        var v = indicator.Working ?? throw new DomainException("not_under_review", "لا يوجد تعريف ينتظر الاعتماد.", "Nothing is waiting for approval.");
        IndicatorRules.Return(v, note);
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.definition_returned", "إعادة تعريف مؤشر بملاحظات", "Returned an indicator definition") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Reason = note,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(code, ct);
    }

    public async Task<IndicatorDetailDto> SetSuspendedAsync(string code, bool suspended, string? reason, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(reason);
        var indicator = await LoadAsync(code, tracking: true, ct);
        indicator.Suspended = suspended;
        audit.Record(AuditEvents.Audit("kpi", Feature, suspended ? "kpi.indicator_suspended" : "kpi.indicator_reactivated",
            suspended ? "إيقاف مؤشر" : "إعادة تفعيل مؤشر", suspended ? "Suspended an indicator" : "Reactivated an indicator") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Reason = why,
        });
        await db.SaveChangesAsync(ct);
        return await GetAsync(code, ct);
    }

    /// <summary>
    /// حذف مؤشر لم يدخل أي دورة قياس (أضيف خطأ أو تكرر): يحذف بتعريفاته ومستهدفاته، ويزال من المبادرات المرتبطة.
    /// المؤشر الذي قيس مرة لا يحذف لأن نتائجه قائمة عليه؛ يوقف بدلًا من ذلك.
    /// </summary>
    public async Task DeleteAsync(string code, DeleteRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var why = Require.Reason(req.Reason);
        var indicator = await LoadAsync(code, tracking: true, ct);
        if (await read.Query<Domain.Collection.CycleIndicator>().AnyAsync(c => c.IndicatorId == indicator.Id, ct))
            throw new DomainException("indicator_measured", "المؤشر دخل دورة قياس ونتائجه قائمة عليه، فلا يحذف. أوقفه بدلًا من ذلك.",
                "The indicator was measured in a cycle; suspend it instead.", 409);
        var targets = await db.Targets.Where(t => t.IndicatorId == indicator.Id).ToListAsync(ct);
        var targetIds = targets.Select(t => t.Id).ToList();
        // مؤشر لم يقس يحذف كله ومعه سجل مستهدفاته (الحذف نفسه في سجل التدقيق)؛ سطور السجل ترتبط بالمؤشر مباشرة أيضًا (D8).
        db.TargetRevisions.RemoveRange(await db.TargetRevisions.Where(r => r.IndicatorId == indicator.Id || (r.TargetId != null && targetIds.Contains(r.TargetId.Value))).ToListAsync(ct));
        db.Targets.RemoveRange(targets);
        foreach (var initiative in await db.Initiatives.Where(i => i.IndicatorCodes.Contains(indicator.Code)).ToListAsync(ct))
            initiative.IndicatorCodes = initiative.IndicatorCodes.Where(c => c != indicator.Code).ToList();
        db.IndicatorVersions.RemoveRange(indicator.Versions);
        db.Indicators.Remove(indicator);
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.indicator_deleted", "حذف مؤشر لم يقس", "Deleted an unmeasured indicator") with
        {
            Target = new AuditTarget("indicator", indicator.Id.ToString(), indicator.Code),
            Reason = why,
            Details = new { name = indicator.Current.NameAr, targets = targets.Count },
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task<Indicator> LoadAsync(string code, bool tracking, CancellationToken ct)
    {
        var query = db.Indicators.Include(i => i.Versions).Where(i => i.Code == code.Trim().ToUpperInvariant());
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
    }

    private async Task<List<List<string>>> OpenConflictScopesAsync(CancellationToken ct) =>
        await db.Conflicts.AsNoTracking()
            .Where(c => c.Status != ConflictStatus.Resolved)
            .Select(c => c.Scope)
            .ToListAsync(ct);

    private async Task<Dictionary<Guid, string>> UnitNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var list = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().ToList();
        return await read.Query<Domain.Organization.OrgUnit>().AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.NameAr, ct);
    }

    private static string? Text(string? value, int max, string field)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        return v.Length <= max ? v : throw new DomainException("text_too_long", $"«{field}» أطول من {max} حرف.", $"\"{field}\" is too long.", 400);
    }

    private static VersionDto ToDto(IndicatorVersion v, IReadOnlyDictionary<Guid, string> owners) =>
        new(v.Id, v.Number, v.Status, v.NameAr, v.NameEn, v.AlternateName, v.Definition, v.Purpose, v.Exclusions, v.Numerator, v.Denominator,
            v.Unit, v.Direction, v.Frequency, v.Level, v.Aggregation, v.AggregationNote, v.DataSource, v.OwnerUnitId,
            v.OwnerUnitId is { } o ? owners.GetValueOrDefault(o) : null, v.EvidenceRequired, v.SourcePages, v.ChangeReason,
            v.SubmittedAt, v.ApprovedAt, v.DecisionRef, v.ReturnNote, IndicatorRules.MissingForApproval(v), v.Version, v.Formula, v.Accumulation);
}
