using System.Text.Json;
using System.Text.RegularExpressions;
using Tier1.Application.Abstractions;
using Tier1.Application.Notifications;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Tier1.Application.Access;

/// <summary>
/// إدارة الأدوار ومصفوفة الصلاحيات. تعديل منح دور لا يسري مباشرة: ينشئ طلبًا بالفرق والسبب،
/// يعتمده مدير أعلى غير الذي طلبه (SOD-10)، ثم يرتفع رقم إصدار الصلاحيات فيسري خلال دقيقة.
/// </summary>
public sealed partial class RbacAdminService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, Integration.IEventOutbox events)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static CatalogDto Catalog() => new(
        PermissionCatalog.Modules.Select(m => new NamedDto(m.Key, m.Name.Pick())).ToList(),
        PermissionCatalog.Features.Select(f => new FeatureDto(
            f.Key,
            f.Module,
            f.Name.Pick(),
            f.CapMeanings.OrderBy(c => c.Key).Select(c => new CapMeaningDto(PermissionCatalog.Letters(c.Key), c.Value)).ToList(),
            f.Specials.Select(s => new NamedDto(s.Key, s.Name.Pick())).ToList())).ToList(),
        ScopeNames.All(),
        PermissionCatalog.Roles.Select(r => new RoleDefinitionDto(r.Code, r.Name.Pick(), r.DescriptionAr, r.IsNew)).ToList());

    public async Task<IReadOnlyList<RoleSummaryDto>> ListRolesAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        var roles = await db.Roles.AsNoTracking().OrderBy(r => r.CreatedAt).ThenBy(r => r.Code).ToListAsync(ct);
        var holders = await db.UserRoles.AsNoTracking()
            .Where(a => a.RevokedAt == null && a.ApprovedAt != null && a.ValidFrom <= now && (a.ValidTo == null || a.ValidTo > now))
            .Join(db.Users.Where(u => u.Active), a => a.UserId, u => u.Id, (a, u) => a)
            .GroupBy(a => a.RoleId).Select(g => new { g.Key, Count = g.Select(a => a.UserId).Distinct().Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var pending = await db.RbacChanges.AsNoTracking().Where(c => c.Status == RbacChangeStatus.Pending)
            .Select(c => new { c.RoleId, c.Id }).ToListAsync(ct);
        var order = PermissionCatalog.Roles.Select((r, i) => (r.Code, i)).ToDictionary(x => x.Code, x => x.i, StringComparer.Ordinal);
        return roles
            .OrderBy(r => order.GetValueOrDefault(r.Code, int.MaxValue)).ThenBy(r => r.CreatedAt)
            .Select(r => Summary(r, holders.GetValueOrDefault(r.Id), pending.FirstOrDefault(p => p.RoleId == r.Id)?.Id))
            .ToList();
    }

    public async Task<RoleDetailDto> GetRoleAsync(Guid id, CancellationToken ct)
    {
        var role = await db.Roles.AsNoTracking().Include(r => r.Grants).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound();
        var roles = await ListRolesAsync(ct);
        return new RoleDetailDto(roles.First(r => r.Id == id), GrantsOf(role));
    }

    /// <summary>دور مخصص جديد، فارغ أو منسوخ من منح دور قائم سبق اعتمادها.</summary>
    public async Task<RoleSummaryDto> CreateRoleAsync(CreateRoleRequest req, CancellationToken ct)
    {
        var code = (req.Code ?? "").Trim().ToUpperInvariant();
        if (!RoleCode().IsMatch(code))
            throw new DomainException("invalid_code", "رمز الدور من 2 إلى 16 حرفًا إنجليزيًا كبيرًا أو رقمًا أو شرطة سفلية، ويبدأ بحرف.", "The role code is 2–16 capital letters, digits or underscores, starting with a letter.", 400) { Field = "code" };
        if (PermissionCatalog.Roles.Any(r => r.Code == code) || await db.Roles.AnyAsync(r => r.Code == code, ct))
            throw new DomainException("duplicate_code", "يوجد دور بهذا الرمز.", "A role with this code already exists.", 409);
        if (string.IsNullOrWhiteSpace(req.NameAr))
            throw new DomainException("name_required", "اكتب اسم الدور بالعربية.", "Enter the Arabic role name.", 400) { Field = "nameAr" };
        var role = new Role
        {
            Code = code,
            NameAr = req.NameAr.Trim(),
            NameEn = string.IsNullOrWhiteSpace(req.NameEn) ? null : req.NameEn.Trim(),
            DescriptionAr = string.IsNullOrWhiteSpace(req.DescriptionAr) ? null : req.DescriptionAr.Trim(),
            CreatedAt = clock.UtcNow,
        };
        // الدور الجديد يبدأ فارغًا. النسخ لا يمنح شيئًا مباشرة: يصبح طلب تعديل بالمنح المنسوخة ينتظر مديرًا أعلى آخر.
        RbacChange? copy = null;
        string? copiedFrom = null;
        if (req.CopyFromRoleId is { } sourceId)
        {
            var source = await db.Roles.AsNoTracking().Include(r => r.Grants).FirstOrDefaultAsync(r => r.Id == sourceId, ct) ?? throw DomainException.NotFound();
            if (PermissionCatalog.ReservedRoles.Contains(source.Code))
                throw DomainException.SeparationOfDuties("SOD-9", "الأدوار المحجوزة (المدير الأعلى، مدير المركز، مسؤول النظام، المراجع الرقابي) لا تنسخ.", "Reserved roles (super administrator, center director, system administrator, oversight auditor) cannot be copied.");
            var grants = RoleRules.Validate(code, false, source.Grants.Select(ToInput));
            copiedFrom = source.Code;
            if (grants.Count > 0)
            {
                var proposed = grants.ToDictionary(g => g.Feature, GrantView.From);
                copy = new RbacChange
                {
                    RoleId = role.Id,
                    RequestedBy = current.UserId ?? throw DomainException.Forbidden(),
                    RequestedAt = clock.UtcNow,
                    Reason = $"نسخ صلاحيات الدور {source.Code}",
                    BaseVersion = role.GrantsVersion,
                    ProposedJson = JsonSerializer.Serialize(proposed, Json),
                    DiffJson = JsonSerializer.Serialize(RoleRules.Diff([], grants), Json),
                };
            }
        }
        db.Roles.Add(role);
        if (copy is not null) db.RbacChanges.Add(copy);
        audit.Record(AuditEvents.Security("iam.role_create", Features.Roles, "إنشاء دور", "Created a role") with
        {
            Permission = $"{Features.Roles}:A",
            Target = new AuditTarget("Role", role.Id.ToString(), code),
            Related = copy is null ? null : [new AuditTarget("RbacChange", copy.Id.ToString())],
            Details = new { copiedFrom, pendingCopy = copy is not null },
        });
        // G-04: copying a role's permissions is a change request like any other; another super administrator decides it.
        if (copy is not null)
            events.Notify(new WorkNotice("iam.change", $"طلب نسخ صلاحيات إلى الدور الجديد {role.NameAr} ينتظر اعتمادك",
                $"أنشأ {current.DisplayName} الدور {role.NameAr} ({code}) بنسخ صلاحيات دور آخر. لا تسري الصلاحيات حتى يعتمدها مدير أعلى آخر من «طلبات التعديل».",
                "/changes", new WorkAudience(SuperAdmins: true, Except: [copy.RequestedBy])), "rbac_change", copy.Id);
        await db.SaveChangesAsync(ct);
        return Summary(role, 0, copy?.Id);
    }

    public async Task DeleteRoleAsync(Guid id, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var role = await db.Roles.Include(r => r.Grants).FirstOrDefaultAsync(r => r.Id == id, ct) ?? throw DomainException.NotFound();
        if (role.IsSystem)
            throw new DomainException("system_role", "أدوار الكتالوج لا تحذف.", "Catalog roles cannot be deleted.", 409);
        if (await db.UserRoles.AnyAsync(a => a.RoleId == id, ct))
            throw new DomainException("role_in_use", "للدور إسناد حالي أو سابق، فلا يحذف حفاظًا على تاريخه.", "The role has current or past assignments, so it is kept for history.", 409);
        if (await db.RbacChanges.AnyAsync(c => c.RoleId == id && c.Status == RbacChangeStatus.Approved, ct))
            throw new DomainException("role_has_history", "للدور صلاحيات معتمدة سابقًا، فلا يحذف حفاظًا على تاريخه.", "The role has approved permission history, so it is kept.", 409);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.RbacChanges.Where(c => c.RoleId == id).ExecuteDeleteAsync(ct);
        db.Roles.Remove(role);
        audit.Record(AuditEvents.Security("iam.role_delete", Features.Roles, "حذف دور", "Deleted a role") with
        {
            Permission = $"{Features.Roles}:D",
            Target = new AuditTarget("Role", role.Id.ToString(), role.Code),
            Reason = reason,
            Details = new { grants = role.Grants.Select(g => new { g.Feature, caps = PermissionCatalog.Letters(g.Caps), scope = GrantView.ScopeCode(g.Scope), g.Specials }) },
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>طلب تعديل منح دور كاملة. يتحقق من القيود الثابتة الآن، ويحفظ الفرق للاعتماد.</summary>
    public async Task<RbacChangeDto> ProposeAsync(Guid roleId, ProposeChangeRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        var role = await db.Roles.AsNoTracking().Include(r => r.Grants).FirstOrDefaultAsync(r => r.Id == roleId, ct) ?? throw DomainException.NotFound();
        if (role.GrantsVersion != req.BaseVersion) throw DomainException.Conflict();
        if (await db.RbacChanges.AnyAsync(c => c.RoleId == roleId && c.Status == RbacChangeStatus.Pending, ct))
            throw new DomainException("change_pending", "لهذا الدور طلب تعديل ينتظر الاعتماد. اعتمده أو ارفضه أو اسحبه أولًا.", "This role already has a change waiting for approval. Decide or withdraw it first.", 409);
        List<GrantInput> inputs;
        try
        {
            inputs = (req.Grants ?? new Dictionary<string, GrantView>()).Select(kv => (kv.Value ?? throw new FormatException("grant")).ToInput(kv.Key)).ToList();
        }
        catch (FormatException)
        {
            throw new DomainException("invalid_grant", "إمكانية أو نطاق غير معروف في الطلب.", "An unknown capability or scope in the request.", 400);
        }
        var proposed = RoleRules.Validate(role.Code, role.IsLocked, inputs);
        var currentGrants = role.Grants.Select(ToInput).ToList();
        var diff = RoleRules.Diff(currentGrants, proposed);
        if (diff.Count == 0)
            throw new DomainException("no_change", "لا فرق بين المقترح والصلاحيات الحالية.", "The proposal is the same as the current permissions.", 400);
        var me = current.UserId ?? throw DomainException.Forbidden();
        var change = new RbacChange
        {
            RoleId = roleId,
            RequestedBy = me,
            RequestedAt = clock.UtcNow,
            Reason = req.Reason.Trim(),
            BaseVersion = role.GrantsVersion,
            ProposedJson = JsonSerializer.Serialize(proposed.ToDictionary(g => g.Feature, GrantView.From), Json),
            DiffJson = JsonSerializer.Serialize(diff, Json),
        };
        db.RbacChanges.Add(change);
        audit.Record(AuditEvents.Security("iam.change_request", Features.Roles, "طلب تعديل صلاحيات دور", "Requested a role permission change") with
        {
            Permission = $"{Features.Roles}:E",
            Target = new AuditTarget("Role", role.Id.ToString(), role.Code),
            Related = [new AuditTarget("RbacChange", change.Id.ToString())],
            Changes = DiffChanges(diff),
            Reason = change.Reason,
        });
        // G-04: the change waits for another super administrator, who was told only by a badge on the changes page.
        events.Notify(new WorkNotice("iam.change", $"طلب تعديل صلاحيات دور {role.NameAr} ينتظر اعتمادك",
            $"طلب {current.DisplayName} تعديل صلاحيات دور {role.NameAr} بالسبب:\n{change.Reason}\nاعتمده أو ارفضه من «طلبات التعديل».",
            "/changes", new WorkAudience(SuperAdmins: true, Except: [me])), "rbac_change", change.Id);
        await db.SaveChangesAsync(ct);
        return (await ListChangesAsync(null, change.Id, ct)).Single();
    }

    public async Task<IReadOnlyList<RbacChangeDto>> ListChangesAsync(RbacChangeStatus? status, Guid? id, CancellationToken ct)
    {
        var me = current.UserId;
        var mine = await access.CurrentAsync(ct);
        var canApprove = mine.IsSuperAdmin;
        var q = db.RbacChanges.AsNoTracking().AsQueryable();
        if (status is { } s) q = q.Where(c => c.Status == s);
        if (id is { } cid) q = q.Where(c => c.Id == cid);
        var rows = await q.OrderByDescending(c => c.RequestedAt).Take(200)
            .Join(db.Roles, c => c.RoleId, r => r.Id, (c, r) => new { Change = c, r.Code, r.NameAr, r.NameEn })
            .ToListAsync(ct);
        var people = await PeopleAsync(rows.SelectMany(r => new[] { r.Change.RequestedBy, r.Change.DecidedBy ?? Guid.Empty }), ct);
        return rows.Select(r => new RbacChangeDto(
            r.Change.Id,
            r.Change.RoleId,
            r.Code,
            Lang.Name(r.NameAr, r.NameEn),
            people[r.Change.RequestedBy],
            r.Change.RequestedAt,
            r.Change.Reason,
            r.Change.BaseVersion,
            r.Change.Status,
            r.Change.DecidedBy is { } d ? people[d] : null,
            r.Change.DecidedAt,
            r.Change.DecisionNote,
            JsonSerializer.Deserialize<List<GrantDiff>>(r.Change.DiffJson, Json) ?? [],
            r.Change.Status == RbacChangeStatus.Pending && canApprove && r.Change.RequestedBy != me,
            r.Change.Status == RbacChangeStatus.Pending && r.Change.RequestedBy == me)).ToList();
    }

    /// <summary>اعتماد طلب التعديل وتطبيقه في معاملة واحدة مع رفع رقم إصدار الصلاحيات.</summary>
    public async Task<RbacChangeDto> ApproveAsync(Guid changeId, string? note, CancellationToken ct)
    {
        var me = current.UserId ?? throw DomainException.Forbidden();
        await RequireSuperAdminAsync(ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var change = await db.RbacChanges.FirstOrDefaultAsync(c => c.Id == changeId, ct) ?? throw DomainException.NotFound();
        EnsurePending(change);
        if (change.RequestedBy == me)
        {
            await audit.RecordNowAsync(Denied("iam.change_approve", change, "SOD-10"), ct);
            throw DomainException.SeparationOfDuties("SOD-10", "لا يعتمد صاحب الطلب طلبه. يعتمده مدير أعلى آخر.", "You cannot approve your own request. Another super administrator must approve it.");
        }
        // المعتمد مستقل عن الطالب: لم يُنشئ الطالبُ حسابه، ولم يسند له دور المدير الأعلى أو يعتمده.
        if (await IsDependentOnAsync(me, change.RequestedBy, ct))
        {
            await audit.RecordNowAsync(Denied("iam.change_approve", change, "SOD-10"), ct);
            throw DomainException.SeparationOfDuties("SOD-10", "حسابك أو دورك أنشأه صاحب الطلب أو اعتمده، فلا تعتمد طلبه. يعتمده مدير أعلى مستقل.", "Your account or role was created or approved by the requester, so you cannot approve their request. An independent super administrator must approve it.");
        }
        var role = await db.Roles.Include(r => r.Grants).FirstAsync(r => r.Id == change.RoleId, ct);
        if (role.GrantsVersion != change.BaseVersion)
        {
            change.Status = RbacChangeStatus.Stale;
            change.DecidedBy = me;
            change.DecidedAt = clock.UtcNow;
            change.DecisionNote = "تغيرت صلاحيات الدور بعد الطلب.";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw new DomainException("change_stale", "تغيرت صلاحيات الدور بعد هذا الطلب، فلم يعد صالحًا. اطلب تعديلًا جديدًا.", "The role changed after this request, so it is no longer valid. Request a new change.", 409);
        }
        var proposed = (JsonSerializer.Deserialize<Dictionary<string, GrantView>>(change.ProposedJson, Json) ?? [])
            .Select(kv => kv.Value.ToInput(kv.Key)).ToList();
        // القيود الثابتة تعاد عند التطبيق أيضًا، فلا يمر طلب قديم خالف قاعدة أضيفت بعده.
        proposed = [.. RoleRules.Validate(role.Code, role.IsLocked, proposed)];
        // SOD-11 على الصلاحية المجمعة: من يحمل هذا الدور مع دور مقيد (مسؤول النظام أو المراجع الرقابي) يبقى في حدود قيده.
        foreach (var constrained in await ConstrainedCodesOfHoldersAsync(role.Id, ct))
            RoleRules.Validate(constrained, false, proposed);
        var diff = RoleRules.Diff(role.Grants.Select(ToInput), proposed);
        db.RoleGrants.RemoveRange(role.Grants);
        foreach (var g in proposed)
            db.RoleGrants.Add(new RoleGrant { RoleId = role.Id, Feature = g.Feature, Caps = g.Caps, Scope = g.Scope, Specials = [.. g.Specials] });
        role.GrantsVersion++;
        change.Status = RbacChangeStatus.Approved;
        change.DecidedBy = me;
        change.DecidedAt = clock.UtcNow;
        change.DecisionNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        audit.Record(AuditEvents.Security("iam.change_approve", Features.Roles, "اعتماد تعديل صلاحيات دور", "Approved a role permission change") with
        {
            Permission = $"{Features.Roles}:approve",
            Target = new AuditTarget("Role", role.Id.ToString(), role.Code),
            Related = [new AuditTarget("RbacChange", change.Id.ToString(), OwnerId: change.RequestedBy.ToString())],
            Changes = DiffChanges(diff),
            Reason = change.Reason,
            Details = new { note = change.DecisionNote, version = role.GrantsVersion },
        });
        events.Notify(new WorkNotice("iam.change", $"اعتُمد طلب تعديل صلاحيات دور {role.NameAr}",
            $"اعتمد {current.DisplayName} طلبك تعديل صلاحيات دور {role.NameAr}، وسرى الآن.", "/changes", new WorkAudience(Users: [change.RequestedBy])), "rbac_change", change.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
        return (await ListChangesAsync(null, change.Id, ct)).Single();
    }

    public async Task<RbacChangeDto> RejectAsync(Guid changeId, string? note, CancellationToken ct)
    {
        var me = current.UserId ?? throw DomainException.Forbidden();
        await RequireSuperAdminAsync(ct);
        var change = await db.RbacChanges.FirstOrDefaultAsync(c => c.Id == changeId, ct) ?? throw DomainException.NotFound();
        EnsurePending(change);
        if (change.RequestedBy == me)
            throw new DomainException("use_cancel", "اسحب طلبك بدل رفضه.", "Withdraw your own request instead of rejecting it.", 409);
        if (string.IsNullOrWhiteSpace(note))
            throw new DomainException("note_required", "اكتب سبب الرفض.", "Enter the reason for rejecting.", 400) { Field = "note" };
        change.Status = RbacChangeStatus.Rejected;
        change.DecidedBy = me;
        change.DecidedAt = clock.UtcNow;
        change.DecisionNote = note.Trim();
        audit.Record(AuditEvents.Security("iam.change_reject", Features.Roles, "رفض تعديل صلاحيات دور", "Rejected a role permission change") with
        {
            Permission = $"{Features.Roles}:approve",
            Target = new AuditTarget("RbacChange", change.Id.ToString(), OwnerId: change.RequestedBy.ToString()),
            Reason = change.DecisionNote,
        });
        events.Notify(new WorkNotice("iam.change", "رُفض طلبك تعديل صلاحيات دور",
            $"رفض {current.DisplayName} طلبك تعديل صلاحيات الدور بالسبب:\n{change.DecisionNote}", "/changes", new WorkAudience(Users: [change.RequestedBy])), "rbac_change", change.Id);
        await db.SaveChangesAsync(ct);
        return (await ListChangesAsync(null, change.Id, ct)).Single();
    }

    public async Task<RbacChangeDto> CancelAsync(Guid changeId, CancellationToken ct)
    {
        var me = current.UserId ?? throw DomainException.Forbidden();
        var change = await db.RbacChanges.FirstOrDefaultAsync(c => c.Id == changeId, ct) ?? throw DomainException.NotFound();
        EnsurePending(change);
        if (change.RequestedBy != me) throw DomainException.Forbidden();
        change.Status = RbacChangeStatus.Cancelled;
        change.DecidedBy = me;
        change.DecidedAt = clock.UtcNow;
        audit.Record(AuditEvents.Security("iam.change_cancel", Features.Roles, "سحب طلب تعديل صلاحيات", "Withdrew a permission change request") with
        {
            Target = new AuditTarget("RbacChange", change.Id.ToString()),
        });
        await db.SaveChangesAsync(ct);
        return (await ListChangesAsync(null, change.Id, ct)).Single();
    }

    /// <summary>القرار في طلبات المصفوفة للمدير الأعلى وحده، أيًا كانت المنح.</summary>
    private async Task RequireSuperAdminAsync(CancellationToken ct)
    {
        if (!(await access.CurrentAsync(ct)).IsSuperAdmin) throw DomainException.Forbidden();
    }

    /// <summary>هل أنشأ «other» حساب «user» أو أسند له دور المدير الأعلى أو اعتمده؟</summary>
    private async Task<bool> IsDependentOnAsync(Guid user, Guid other, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Id == user && u.CreatedBy == other, ct)) return true;
        return await db.UserRoles.Where(a => a.UserId == user && a.RevokedAt == null && (a.GrantedBy == other || a.ApprovedBy == other))
            .Join(db.Roles.Where(r => r.Code == PermissionCatalog.SuperAdminRole), a => a.RoleId, r => r.Id, (a, r) => a.Id)
            .AnyAsync(ct);
    }

    /// <summary>أدوار مقيدة (SYS وAUD) يحملها أحد حاملي الدور المعطى.</summary>
    private async Task<IReadOnlyList<string>> ConstrainedCodesOfHoldersAsync(Guid roleId, CancellationToken ct)
    {
        var holders = db.UserRoles.Where(a => a.RoleId == roleId && a.RevokedAt == null).Select(a => a.UserId);
        return await db.UserRoles.Where(a => a.RevokedAt == null && holders.Contains(a.UserId) && a.RoleId != roleId)
            .Join(db.Roles.Where(r => r.Code == RoleRules.SystemAdminRole), a => a.RoleId, r => r.Id, (a, r) => r.Code)
            .Distinct().ToListAsync(ct);
    }

    internal static Dictionary<string, GrantView> GrantsOf(Role role) =>
        role.Grants.OrderBy(g => g.Feature, StringComparer.Ordinal).ToDictionary(g => g.Feature, g => GrantView.From(ToInput(g)), StringComparer.Ordinal);

    private static GrantInput ToInput(RoleGrant g) => new(g.Feature, g.Caps, g.Scope, g.Specials);

    private static List<FieldChange> DiffChanges(IEnumerable<GrantDiff> diff) =>
        diff.Select(d => new FieldChange(d.Feature, Format(d.Before), Format(d.After))).ToList();

    private static string? Format(GrantView? g) =>
        g is null ? null : $"{g.Caps}:{g.Scope}" + (g.Specials.Count > 0 ? "+" + string.Join(',', g.Specials) : "");

    private static void EnsurePending(RbacChange change)
    {
        if (change.Status != RbacChangeStatus.Pending)
            throw new DomainException("not_pending", "هذا الطلب لم يعد ينتظر قرارًا.", "This request is no longer waiting for a decision.", 409);
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new DomainException("reason_required", "اكتب سببًا واضحًا (5 أحرف على الأقل).", "Enter a clear reason (at least 5 characters).", 400) { Field = "reason" };
    }

    private static AuditEntry Denied(string action, RbacChange change, string rule) =>
        AuditEvents.Security(action, Features.Roles, "رفض اعتماد بقاعدة فصل", "Blocked an approval by a separation rule") with
        {
            Outcome = "denied",
            Target = new AuditTarget("RbacChange", change.Id.ToString(), OwnerId: change.RequestedBy.ToString()),
            Details = new { rule },
        };

    private Task<Dictionary<Guid, PersonDto>> PeopleAsync(IEnumerable<Guid> ids, CancellationToken ct) =>
        People.LookupAsync(db.Users, ids.Where(i => i != Guid.Empty), ct);

    private static RoleSummaryDto Summary(Role r, int holders, Guid? pending) =>
        new(r.Id, r.Code, Lang.Name(r.NameAr, r.NameEn), r.NameEn, r.DescriptionAr, r.IsSystem, r.IsLocked, r.GrantsVersion, holders, pending);

    [GeneratedRegex("^[A-Z][A-Z0-9_]{1,15}$")]
    private static partial Regex RoleCode();
}
