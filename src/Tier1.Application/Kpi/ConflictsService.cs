using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Kpi;

namespace Tier1.Application.Kpi;

public sealed record ConflictDto(
    Guid Id, string Code, IReadOnlyList<string> Scope, string Type, ConflictSeverity Severity, string Description,
    string? ValueA, string? ValueB, string? Proposal, ConflictStatus Status, string? PreparationNote, DateTimeOffset? PreparedAt,
    string? Decision, string DecidingBody, DateOnly? DecisionDate, string? MinutesRef, uint Version);

public sealed record ConflictActions(bool CanPrepare, bool CanDecide);

public sealed record ConflictListDto(IReadOnlyList<ConflictDto> Items, ConflictActions Actions);

public sealed record PrepareConflictRequest(string? Proposal, string? PreparationNote, uint? Version);

public sealed record DecideConflictRequest(bool Defer, string? Decision, DateOnly? DecisionDate, string? MinutesRef, uint? Version);

/// <summary>
/// سجل التعارضات: يجهز مدير متابعة الخطة الحالة والبدائل، ويسجل قرار اللجنة العليا لـ Tier 1 برقم المحضر وتاريخه.
/// القرار لا يغير الأرقام تلقائيًا؛ تطبيقه تعديل مسجل في القاموس أو المستهدفات.
/// </summary>
public sealed class ConflictsService(IAppDb db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit)
{
    private const string Feature = Features.Conflicts;

    public async Task<ConflictListDto> ListAsync(ConflictStatus? status, ConflictSeverity? severity, string? scope, string? q, CancellationToken ct)
    {
        var me = await access.RequireAsync(Feature, Cap.View, ct);
        var all = await db.Conflicts.AsNoTracking().ToListAsync(ct);
        var term = q?.Trim();
        var items = all
            .Where(c => status is null || c.Status == status)
            .Where(c => severity is null || c.Severity == severity)
            .Where(c => string.IsNullOrEmpty(scope) || c.Scope.Any(s => s == scope || s.StartsWith(scope + "-", StringComparison.Ordinal) || scope.StartsWith(s + "-", StringComparison.Ordinal)))
            .Where(c => string.IsNullOrEmpty(term) || c.Code.Contains(term, StringComparison.OrdinalIgnoreCase) || c.Description.Contains(term, StringComparison.Ordinal))
            .OrderBy(c => c.IsOpen ? 0 : 1).ThenByDescending(c => c.Severity).ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(ToDto)
            .ToList();
        return new ConflictListDto(items, Actions(me));
    }

    public async Task<ConflictDto> GetAsync(string code, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        return ToDto(await LoadAsync(code, tracking: false, ct));
    }

    /// <summary>جدول أعمال جلسة اللجنة العليا: الحالات الجاهزة والمؤجلة، الأخطر أولًا.</summary>
    public async Task<IReadOnlyList<ConflictDto>> AgendaAsync(CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        return (await db.Conflicts.AsNoTracking().Where(c => c.Status == ConflictStatus.Ready || c.Status == ConflictStatus.Deferred).ToListAsync(ct))
            .OrderByDescending(c => c.Severity).ThenBy(c => c.Code, StringComparer.Ordinal)
            .Select(ToDto).ToList();
    }

    public async Task<ConflictDto> PrepareAsync(string code, PrepareConflictRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var c = await LoadAsync(code, tracking: true, ct);
        Concurrency.Expect(db, c, req.Version);
        if (c.Status == ConflictStatus.Resolved)
            throw new DomainException("conflict_resolved", "حسمت اللجنة هذا التعارض؛ لا يعدل بعد القرار.", "This conflict is resolved.");
        var changes = new ChangeSet().Add("proposal", c.Proposal, req.Proposal).Add("preparationNote", c.PreparationNote, req.PreparationNote);
        c.Proposal = Require.Optional(req.Proposal, 2000);
        c.PreparationNote = Require.Optional(req.PreparationNote, 2000);
        if (string.IsNullOrEmpty(c.Proposal))
            throw new DomainException("proposal_required", "اكتب المعالجة المقترحة التي تعرض على اللجنة.", "Enter the proposal for the committee.", 400);
        c.Status = ConflictStatus.Ready;
        c.PreparedBy = current.UserId;
        c.PreparedAt = clock.UtcNow;
        audit.Record(AuditEvents.Audit("kpi", Feature, "kpi.conflict_prepared", "تجهيز تعارض للعرض على اللجنة العليا", "Prepared a conflict for the committee") with
        {
            Target = new AuditTarget("conflict", c.Id.ToString(), c.Code),
            Changes = changes.ToList(),
        });
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    public async Task<ConflictDto> DecideAsync(string code, DecideConflictRequest req, CancellationToken ct)
    {
        await access.RequireSpecialAsync(Feature, Specials.Decide, ct);
        var c = await LoadAsync(code, tracking: true, ct);
        Concurrency.Expect(db, c, req.Version);
        if (c.Status == ConflictStatus.Resolved)
            throw new DomainException("conflict_resolved", "سجل قرار هذا التعارض من قبل.", "A decision was already recorded.");
        c.MinutesRef = Require.Optional(req.MinutesRef, 200)
            ?? throw new DomainException("minutes_required", "اكتب رقم محضر جلسة اللجنة العليا.", "Enter the committee minutes reference.", 400);
        c.DecisionDate = req.DecisionDate ?? throw new DomainException("date_required", "اختر تاريخ الجلسة.", "Choose the session date.", 400);
        c.Decision = req.Defer ? Require.Optional(req.Decision, 2000) : Require.Reason(req.Decision, 5, "decision_required", 2000);
        c.Status = req.Defer ? ConflictStatus.Deferred : ConflictStatus.Resolved;
        c.DecisionRecordedBy = current.UserId;
        c.DecisionRecordedAt = clock.UtcNow;
        audit.Record(AuditEvents.Audit("kpi", Feature, req.Defer ? "kpi.conflict_deferred" : "kpi.conflict_resolved",
            req.Defer ? "تأجيل حسم تعارض" : "تسجيل قرار اللجنة العليا في تعارض",
            req.Defer ? "Deferred a conflict" : "Recorded the committee decision on a conflict") with
        {
            Target = new AuditTarget("conflict", c.Id.ToString(), c.Code),
            Details = new { c.MinutesRef, c.DecisionDate },
            Permission = $"{Feature}:{Specials.Decide}",
        });
        await db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    private static ConflictActions Actions(EffectiveAccess me) => new(me.Has(Feature, Cap.Edit), me.HasSpecial(Feature, Specials.Decide));

    private async Task<DataConflict> LoadAsync(string code, bool tracking, CancellationToken ct)
    {
        var query = db.Conflicts.Where(c => c.Code == code.Trim().ToUpperInvariant());
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct) ?? throw DomainException.NotFound();
    }

    private static ConflictDto ToDto(DataConflict c) =>
        new(c.Id, c.Code, c.Scope, c.Type, c.Severity, c.Description, c.ValueA, c.ValueB, c.Proposal, c.Status, c.PreparationNote, c.PreparedAt,
            c.Decision, c.DecidingBody, c.DecisionDate, c.MinutesRef, c.Version);
}
