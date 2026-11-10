using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Common;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Organization;

namespace Tier1.Application.Organization;

public sealed record OrgUnitDto(Guid Id, string Code, string NameAr, string? NameEn, OrgUnitKind Kind, Guid? ParentId, string Path, bool Active, int SortOrder, DateOnly? EstablishedOn, DateOnly? ClosedOn, uint Version);

public sealed record CreateOrgUnitRequest(string Code, string NameAr, string? NameEn, OrgUnitKind Kind, Guid? ParentId, DateOnly? EstablishedOn, int SortOrder);

public sealed record UpdateOrgUnitRequest(string NameAr, string? NameEn, int SortOrder, DateOnly? EffectiveOn, string? Reason, uint? Version);

public sealed record CloseOrgUnitRequest(DateOnly ClosedOn, string? Reason, Guid? MergedIntoId);

public sealed record OrgUnitChangeDto(OrgUnitChangeKind Kind, DateOnly EffectiveOn, string? OldValue, string? NewValue, string? Reason, DateTimeOffset At);

/// <summary>قواعد بناء الهيكل: أي نوع يقع تحت أي نوع.</summary>
public static partial class OrgRules
{
    [GeneratedRegex("^[A-Z0-9][A-Z0-9-]{0,31}$")]
    private static partial Regex CodePattern();

    public static string NormalizeCode(string? code)
    {
        var c = (code ?? "").Trim().ToUpperInvariant();
        return CodePattern().IsMatch(c) ? c
            : throw new DomainException("invalid_code", "الرمز حروف إنجليزية كبيرة وأرقام وشرطة فقط، حتى 32 حرفًا (مثل ENG أو ENG-CIV).",
                "The code is uppercase letters, digits and dashes, up to 32 characters.", 400);
    }

    public static void RequireParent(OrgUnitKind kind, OrgUnit? parent)
    {
        var ok = kind switch
        {
            OrgUnitKind.University => parent is null,
            OrgUnitKind.College or OrgUnitKind.Institute => parent?.Kind == OrgUnitKind.University,
            OrgUnitKind.Department => parent is { IsCollegeLevel: true },
            OrgUnitKind.Center or OrgUnitKind.Administration => parent is { Kind: OrgUnitKind.University or OrgUnitKind.College or OrgUnitKind.Institute },
            _ => false,
        };
        if (!ok)
            throw new DomainException("invalid_parent", kind switch
            {
                OrgUnitKind.University => "الجامعة جذر الهيكل ولا تتبع جهة.",
                OrgUnitKind.College or OrgUnitKind.Institute => "الكلية أو المعهد يتبع الجامعة.",
                OrgUnitKind.Department => "القسم العلمي يتبع كلية أو معهدًا.",
                _ => "المركز أو الإدارة يتبع الجامعة أو كلية.",
            }, "The parent unit is not valid for this kind.", 400);
        if (parent is { Active: false })
            throw new DomainException("parent_inactive", "الجهة الأم معطلة.", "The parent unit is inactive.", 400);
    }
}

/// <summary>الجهات: الإنشاء وتعديل الاسم بتاريخ سريان، والإغلاق أو الدمج بدل الحذف، وتاريخ كل جهة.</summary>
public sealed class OrgUnitsService(IAppDb db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit)
{
    private const string Feature = Features.Units;

    public async Task<IReadOnlyList<OrgUnitDto>> ListAsync(bool includeInactive, CancellationToken ct) =>
        await db.OrgUnits.AsNoTracking()
            .Where(u => includeInactive || u.Active)
            .OrderBy(u => u.Path.Length).ThenBy(u => u.SortOrder).ThenBy(u => u.NameAr)
            .Select(u => new OrgUnitDto(u.Id, u.Code, u.NameAr, u.NameEn, u.Kind, u.ParentId, u.Path, u.Active, u.SortOrder, u.EstablishedOn, u.ClosedOn, u.Version))
            .ToListAsync(ct);

    public async Task<OrgUnitDto> CreateAsync(CreateOrgUnitRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Add, ct);
        var code = OrgRules.NormalizeCode(req.Code);
        var name = RequireName(req.NameAr);
        var parent = req.ParentId is { } pid ? await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == pid, ct) ?? throw DomainException.NotFound() : null;
        OrgRules.RequireParent(req.Kind, parent);
        if (await db.OrgUnits.AnyAsync(u => u.Code == code, ct))
            throw new DomainException("duplicate_code", $"الرمز {code} مستخدم لجهة أخرى.", $"The code {code} is already used.", 409);

        var now = clock.UtcNow;
        var unit = new OrgUnit
        {
            Code = code,
            NameAr = name,
            NameEn = Require.Optional(req.NameEn, 200),
            Kind = req.Kind,
            ParentId = parent?.Id,
            Path = OrgUnit.PathOf(parent?.Path, code),
            SortOrder = req.SortOrder,
            EstablishedOn = req.EstablishedOn,
            CreatedAt = now,
        };
        db.OrgUnits.Add(unit);
        AddChange(unit.Id, OrgUnitChangeKind.Created, req.EstablishedOn ?? CairoTime.DateOf(now), null, name, null, now);
        audit.Record(AuditEvents.Audit("org", Feature, "org.unit_created", "إنشاء جهة", "Created a unit") with
        {
            Target = new AuditTarget("org_unit", unit.Id.ToString(), code),
        });
        await db.SaveChangesAsync(ct);
        return ToDto(unit);
    }

    public async Task<OrgUnitDto> UpdateAsync(Guid id, UpdateOrgUnitRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var unit = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw DomainException.NotFound();
        Concurrency.Expect(db, unit, req.Version);
        var name = RequireName(req.NameAr);
        var now = clock.UtcNow;
        var changes = new ChangeSet().Add("nameAr", unit.NameAr, name).Add("nameEn", unit.NameEn, Require.Optional(req.NameEn, 200)).Add("sortOrder", unit.SortOrder, req.SortOrder);
        if (unit.NameAr != name)
        {
            // تغيير الاسم حدث في تاريخ الجهة بتاريخ سريانه وسببه؛ البيانات السابقة تبقى مرتبطة بالجهة نفسها.
            AddChange(unit.Id, OrgUnitChangeKind.Renamed, req.EffectiveOn ?? CairoTime.DateOf(now), unit.NameAr, name, Require.Reason(req.Reason), now);
        }
        unit.NameAr = name;
        unit.NameEn = Require.Optional(req.NameEn, 200);
        unit.SortOrder = req.SortOrder;
        if (!changes.IsEmpty)
            audit.Record(AuditEvents.Audit("org", Feature, "org.unit_updated", "تعديل بيانات جهة", "Updated a unit") with
            {
                Target = new AuditTarget("org_unit", unit.Id.ToString(), unit.Code),
                Changes = changes.ToList(),
                Reason = req.Reason,
            });
        await db.SaveChangesAsync(ct);
        return ToDto(unit);
    }

    /// <summary>الإغلاق أو الدمج بدل الحذف: الجهة تبقى بسجلاتها، ولا تقبل جهة لها فروع نشطة.</summary>
    public async Task<OrgUnitDto> CloseAsync(Guid id, CloseOrgUnitRequest req, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Delete, ct);
        var reason = Require.Reason(req.Reason);
        var unit = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw DomainException.NotFound();
        if (!unit.Active) throw new DomainException("already_closed", "الجهة مغلقة بالفعل.", "The unit is already closed.");
        if (unit.Kind == OrgUnitKind.University) throw new DomainException("root_unit", "لا تغلق الجامعة نفسها.", "The university cannot be closed.");
        if (await db.OrgUnits.AnyAsync(u => u.ParentId == id && u.Active, ct))
            throw new DomainException("has_active_children", "أغلق الأقسام أو الجهات التابعة أولًا، أو انقلها.", "Close or move the child units first.");

        OrgUnit? into = null;
        if (req.MergedIntoId is { } mid)
        {
            into = await db.OrgUnits.FirstOrDefaultAsync(u => u.Id == mid && u.Active, ct) ?? throw DomainException.NotFound();
            if (into.Id == unit.Id) throw new DomainException("merge_self", "لا تدمج الجهة في نفسها.", "A unit cannot merge into itself.", 400);
        }

        var now = clock.UtcNow;
        unit.Active = false;
        unit.ClosedOn = req.ClosedOn;
        unit.MergedIntoId = into?.Id;
        AddChange(unit.Id, into is null ? OrgUnitChangeKind.Closed : OrgUnitChangeKind.Merged, req.ClosedOn, unit.NameAr, into?.NameAr, reason, now);
        audit.Record(AuditEvents.Audit("org", Feature, into is null ? "org.unit_closed" : "org.unit_merged",
            into is null ? "إغلاق جهة" : "دمج جهة في أخرى", into is null ? "Closed a unit" : "Merged a unit") with
        {
            Target = new AuditTarget("org_unit", unit.Id.ToString(), unit.Code),
            Related = into is null ? null : [new AuditTarget("org_unit", into.Id.ToString(), into.Code)],
            Reason = reason,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(unit);
    }

    public async Task<IReadOnlyList<OrgUnitChangeDto>> HistoryAsync(Guid id, CancellationToken ct) =>
        await db.OrgUnitChanges.AsNoTracking().Where(c => c.UnitId == id).OrderBy(c => c.EffectiveOn).ThenBy(c => c.At)
            .Select(c => new OrgUnitChangeDto(c.Kind, c.EffectiveOn, c.OldValue, c.NewValue, c.Reason, c.At))
            .ToListAsync(ct);

    private void AddChange(Guid unitId, OrgUnitChangeKind kind, DateOnly effectiveOn, string? oldValue, string? newValue, string? reason, DateTimeOffset now) =>
        db.OrgUnitChanges.Add(new OrgUnitChange
        {
            UnitId = unitId,
            Kind = kind,
            EffectiveOn = effectiveOn,
            OldValue = oldValue,
            NewValue = newValue,
            Reason = reason,
            By = current.UserId,
            At = now,
        });

    private static string RequireName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DomainException("name_required", "اكتب اسم الجهة.", "Enter the unit name.", 400) : name.Trim().Length > 200
            ? throw new DomainException("text_too_long", "الاسم أطول من 200 حرف.", "The name is too long.", 400) : name.Trim();

    private static OrgUnitDto ToDto(OrgUnit u) =>
        new(u.Id, u.Code, u.NameAr, u.NameEn, u.Kind, u.ParentId, u.Path, u.Active, u.SortOrder, u.EstablishedOn, u.ClosedOn, u.Version);
}
