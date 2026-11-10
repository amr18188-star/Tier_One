using System.Net.Mail;
using Tier1.Application.Abstractions;
using Tier1.Application.Accounts;
using Tier1.Application.Auditing;
using Tier1.Application.Security;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;

namespace Tier1.Application.Access;

public sealed record UserListItemDto(Guid Id, string Email, string Name, bool Active, bool MfaEnabled, bool Locked, DateTimeOffset? LastLoginAt, IReadOnlyList<string> Roles);

public sealed record AssignmentDto(Guid Id, Guid RoleId, string RoleCode, string RoleName, IReadOnlyList<string> ScopeValues, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo,
    PersonDto? GrantedBy, DateTimeOffset GrantedAt, string? Reason, DateTimeOffset? RevokedAt, string? RevokeReason, bool Active, bool PendingApproval, PersonDto? ApprovedBy, bool CanApprove);

public sealed record OverrideDto(Guid Id, string Feature, string Caps, IReadOnlyList<string> Specials, OverrideEffect Effect, string Scope, DateTimeOffset ValidFrom, DateTimeOffset? ValidTo,
    PersonDto? GrantedBy, string Reason, DateTimeOffset? RevokedAt, bool Active);

public sealed record UserDetailDto(Guid Id, string Email, string FullNameAr, string? FullNameEn, bool Active, bool MfaEnabled, bool MustChangePassword, DateTimeOffset? LockedUntil,
    DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt, bool HoldsReservedRole, IReadOnlyList<AssignmentDto> Assignments, IReadOnlyList<OverrideDto> Overrides);

public sealed record CreateUserRequest(string Email, string FullNameAr, string? FullNameEn);

public sealed record UpdateUserRequest(string FullNameAr, string? FullNameEn);

/// <summary>كلمة المرور المؤقتة تعرض مرة واحدة فقط ولا تحفظ إلا بصمتها.</summary>
public sealed record TemporaryPasswordDto(Guid UserId, string TemporaryPassword);

public sealed record AssignRoleRequest(Guid RoleId, IReadOnlyList<string>? ScopeValues, DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo, string Reason);

public sealed record UpdateAssignmentRequest(IReadOnlyList<string>? ScopeValues, DateTimeOffset? ValidTo, string Reason);

public sealed record AddOverrideRequest(string Feature, string Caps, IReadOnlyList<string>? Specials, OverrideEffect Effect, string? Scope, DateTimeOffset? ValidTo, string Reason);

/// <summary>
/// حسابات الموظفين وإسناد أدوارهم واستثناءاتهم. القواعد الثابتة هنا:
/// <list type="bullet">
/// <item>SOD-9: لا أحد يدير حسابه. حامل دور محجوز (SA، SYS، AUD، DIR) لا يدير حسابه ولا أدواره ولا استثناءاته إلا المدير الأعلى.
/// المنح الفردي، وسحب المنع الفردي، لا يتجاوزان ما يملكه المانح.</item>
/// <item>SOD-10: إسناد دور المدير الأعلى لا يسري حتى يعتمده مدير أعلى مستقل، ولا يقل المديرون الأعلون الدائمون عن اثنين.</item>
/// <item>SOD-11: من يحمل دور مسؤول النظام أو المراجع الرقابي تبقى صلاحيته المجمعة في حدود قيده.</item>
/// </list>
/// العمليات التي تمس عدد المديرين الأعلين تجري في معاملة بقفل استشاري، فلا يتجاوزها طلبان متزامنان.
/// </summary>
public sealed class UsersAdminService(
    IAppDb db, IClock clock, ICurrentUser current, IAccessService access, IPasswordHasher hasher, ISessionService sessions, IAuditLog audit, IOptions<AuthOptions> authOptions)
{
    public const int MinimumSuperAdmins = 2;

    /// <summary>مفتاح القفل الاستشاري لعمليات الأدوار المحجوزة.</summary>
    private const long IamLockKey = 0x4C43_4941_4D;

    public async Task<IReadOnlyList<UserListItemDto>> ListAsync(string? search, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var q = db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLowerInvariant();
            q = q.Where(u => u.Email.Contains(s) || u.FullNameAr.Contains(search.Trim()) || (u.FullNameEn != null && u.FullNameEn.ToLower().Contains(s)));
        }
        var users = await q.OrderBy(u => u.FullNameAr).Take(500).ToListAsync(ct);
        var ids = users.Select(u => u.Id).ToList();
        var roles = await db.UserRoles.AsNoTracking()
            .Where(a => ids.Contains(a.UserId) && a.RevokedAt == null && a.ApprovedAt != null && a.ValidFrom <= now && (a.ValidTo == null || a.ValidTo > now))
            .Join(db.Roles, a => a.RoleId, r => r.Id, (a, r) => new { a.UserId, r.Code })
            .ToListAsync(ct);
        return users.Select(u => new UserListItemDto(u.Id, u.Email, Lang.Name(u.FullNameAr, u.FullNameEn), u.Active, u.MfaEnabled, u.LockedUntil > now, u.LastLoginAt,
            roles.Where(r => r.UserId == u.Id).Select(r => r.Code).Distinct().Order(StringComparer.Ordinal).ToList())).ToList();
    }

    public async Task<UserDetailDto> GetAsync(Guid id, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var u = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw DomainException.NotFound();
        var assignments = await db.UserRoles.AsNoTracking().Where(a => a.UserId == id)
            .Join(db.Roles, a => a.RoleId, r => r.Id, (a, r) => new { A = a, r.Code, r.NameAr, r.NameEn })
            .OrderByDescending(x => x.A.GrantedAt).ToListAsync(ct);
        var overrides = await db.UserOverrides.AsNoTracking().Where(o => o.UserId == id).OrderByDescending(o => o.GrantedAt).ToListAsync(ct);
        var people = await PeopleAsync(assignments.SelectMany(a => new[] { a.A.GrantedBy, a.A.ApprovedBy }).Concat(overrides.Select(o => o.GrantedBy)), ct);
        var me = current.UserId;
        var iAmSa = (await access.CurrentAsync(ct)).IsSuperAdmin;
        return new UserDetailDto(u.Id, u.Email, u.FullNameAr, u.FullNameEn, u.Active, u.MfaEnabled, u.MustChangePassword, u.LockedUntil > now ? u.LockedUntil : null, u.CreatedAt, u.LastLoginAt,
            assignments.Any(x => x.A.RevokedAt == null && PermissionCatalog.ReservedRoles.Contains(x.Code)),
            assignments.Select(x => new AssignmentDto(x.A.Id, x.A.RoleId, x.Code, Lang.Name(x.NameAr, x.NameEn), x.A.ScopeValues, x.A.ValidFrom, x.A.ValidTo,
                x.A.GrantedBy is { } g ? people.GetValueOrDefault(g) : null, x.A.GrantedAt, x.A.Reason, x.A.RevokedAt, x.A.RevokeReason, x.A.IsActiveAt(now),
                x.A.IsPendingApproval, x.A.ApprovedBy is { } ab ? people.GetValueOrDefault(ab) : null,
                x.A.IsPendingApproval && iAmSa && me != x.A.GrantedBy && me != id)).ToList(),
            overrides.Select(o => new OverrideDto(o.Id, o.Feature, PermissionCatalog.Letters(o.Caps), o.Specials, o.Effect, GrantView.ScopeCode(o.Scope), o.ValidFrom, o.ValidTo,
                o.GrantedBy is { } g ? people.GetValueOrDefault(g) : null, o.Reason, o.RevokedAt, o.IsActiveAt(now))).ToList());
    }

    public async Task<TemporaryPasswordDto> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        var email = NormalizeEmail(req.Email);
        if (string.IsNullOrWhiteSpace(req.FullNameAr))
            throw new DomainException("name_required", "اكتب الاسم بالعربية.", "Enter the Arabic name.", 400);
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new DomainException("duplicate_email", "يوجد حساب بهذا البريد.", "An account with this email already exists.", 409);
        var temporary = Tokens.NewTemporaryPassword();
        var user = new AppUser
        {
            Email = email,
            FullNameAr = req.FullNameAr.Trim(),
            FullNameEn = string.IsNullOrWhiteSpace(req.FullNameEn) ? null : req.FullNameEn.Trim(),
            PasswordHash = hasher.Hash(temporary),
            MustChangePassword = true,
            CreatedAt = clock.UtcNow,
            CreatedBy = current.UserId,
        };
        db.Users.Add(user);
        audit.Record(AuditEvents.Security("iam.user_create", Features.Users, "إنشاء حساب موظف", "Created a staff account") with
        {
            Permission = $"{Features.Users}:A",
            Target = new AuditTarget("User", user.Id.ToString(), email),
            Changes = [new FieldChange("email", null, email), new FieldChange("fullNameAr", null, user.FullNameAr)],
        });
        await db.SaveChangesAsync(ct);
        return new TemporaryPasswordDto(user.Id, temporary);
    }

    public async Task UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        var user = await LoadManagedAsync(id, "iam.user_update", Features.Users, ct);
        if (string.IsNullOrWhiteSpace(req.FullNameAr))
            throw new DomainException("name_required", "اكتب الاسم بالعربية.", "Enter the Arabic name.", 400);
        var changes = new List<FieldChange>();
        var ar = req.FullNameAr.Trim();
        var en = string.IsNullOrWhiteSpace(req.FullNameEn) ? null : req.FullNameEn.Trim();
        if (user.FullNameAr != ar) changes.Add(new FieldChange("fullNameAr", user.FullNameAr, ar));
        if (user.FullNameEn != en) changes.Add(new FieldChange("fullNameEn", user.FullNameEn, en));
        if (changes.Count == 0) return;
        user.FullNameAr = ar;
        user.FullNameEn = en;
        audit.Record(UserEvent("iam.user_update", "تعديل بيانات حساب", "Updated an account", user, "E") with { Changes = changes });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        await using var tx = await BeginLockedAsync(ct);
        var user = await LoadManagedAsync(id, "iam.user_deactivate", Features.Users, ct);
        if (!user.Active) return;
        await EnsureSuperAdminsRemainAsync(excludeUser: id, excludeAssignment: null, ct);
        user.Active = false;
        audit.Record(UserEvent("iam.user_deactivate", "إيقاف حساب", "Deactivated an account", user, "D") with
        {
            Changes = [new FieldChange("active", "true", "false")],
            Reason = reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(user.Id, ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task ReactivateAsync(Guid id, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var user = await LoadManagedAsync(id, "iam.user_reactivate", Features.Users, ct);
        if (user.Active) return;
        user.Active = true;
        audit.Record(UserEvent("iam.user_reactivate", "إعادة تفعيل حساب", "Reactivated an account", user, "E") with
        {
            Changes = [new FieldChange("active", "false", "true")],
            Reason = reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task UnlockAsync(Guid id, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var user = await LoadManagedAsync(id, "iam.user_unlock", Features.Users, ct);
        user.LockedUntil = null;
        user.FailedLogins = 0;
        user.MfaLockedUntil = null;
        user.FailedMfa = 0;
        audit.Record(UserEvent("iam.user_unlock", "فك قفل حساب", "Unlocked an account", user, "E") with { Reason = reason.Trim() });
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetMfaAsync(Guid id, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var user = await LoadManagedAsync(id, "iam.user_mfa_reset", Features.Users, ct);
        user.MfaEnabled = false;
        user.MfaSecretProtected = null;
        user.LastTotpStep = 0;
        audit.Record(UserEvent("iam.user_mfa_reset", "إعادة ضبط التحقق بخطوتين", "Reset two-step verification", user, "E") with
        {
            Changes = [new FieldChange("mfaEnabled", "true", "false")],
            Reason = reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(user.Id, ct);
    }

    /// <summary>كلمة مرور مؤقتة يلزم تغييرها عند الدخول التالي (إجراء للمدير الأعلى وحده).</summary>
    public async Task<TemporaryPasswordDto> SetTemporaryPasswordAsync(Guid id, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        if (!(await access.CurrentAsync(ct)).IsSuperAdmin) throw DomainException.Forbidden();
        var user = await LoadManagedAsync(id, "iam.user_set_password", Features.Users, ct);
        var temporary = Tokens.NewTemporaryPassword();
        user.PasswordHash = hasher.Hash(temporary);
        user.MustChangePassword = true;
        user.FailedLogins = 0;
        user.LockedUntil = null;
        audit.Record(UserEvent("iam.user_set_password", "تعيين كلمة مرور مؤقتة", "Set a temporary password", user, "set_password") with { Reason = reason.Trim() });
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(user.Id, ct);
        return new TemporaryPasswordDto(user.Id, temporary);
    }

    public async Task<AssignmentDto> AssignRoleAsync(Guid userId, AssignRoleRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        await using var tx = await BeginLockedAsync(ct);
        var user = await LoadManagedAsync(userId, "iam.role_assign", Features.Grants, ct);
        if (!user.Active) throw new DomainException("inactive", "الحساب موقوف. أعد تفعيله أولًا.", "The account is deactivated. Reactivate it first.", 409);
        var role = await db.Roles.AsNoTracking().Include(r => r.Grants).FirstOrDefaultAsync(r => r.Id == req.RoleId, ct) ?? throw DomainException.NotFound();
        var mine = await access.CurrentAsync(ct);
        if (PermissionCatalog.ReservedRoles.Contains(role.Code) && !mine.IsSuperAdmin)
            await DenyAsync(user, "iam.role_assign", Features.Grants, "SOD-9", new { role = role.Code },
                "هذا الدور يسنده المدير الأعلى وحده.", "Only a super administrator can assign this role.", ct);
        await EnsureCompatibleRoleAsync(user, role, ct);
        var now = clock.UtcNow;
        var from = req.ValidFrom ?? now;
        if (req.ValidTo is { } to && to <= from)
            throw new DomainException("invalid_dates", "تاريخ النهاية يجب أن يكون بعد البداية.", "The end date must be after the start date.", 400);
        var scopeValues = CleanScope(req.ScopeValues);
        if (await db.UserRoles.AnyAsync(a => a.UserId == userId && a.RoleId == role.Id && a.RevokedAt == null && (a.ValidTo == null || a.ValidTo > now), ct))
            throw new DomainException("already_assigned", "الدور مسند لهذا المستخدم بالفعل أو ينتظر الاعتماد. عدّل الإسناد القائم.", "This role is already assigned or waiting for approval. Edit the existing assignment.", 409);
        // دور المدير الأعلى لا يسري حتى يعتمده مدير أعلى مستقل؛ فلا ينشئ مدير أعلى واحد مديرًا ثانيًا يعتمد طلباته.
        var needsApproval = role.Code == PermissionCatalog.SuperAdminRole;
        var assignment = new UserRole
        {
            UserId = userId,
            RoleId = role.Id,
            ScopeValues = scopeValues,
            ValidFrom = from,
            ValidTo = req.ValidTo,
            GrantedBy = current.UserId,
            GrantedAt = now,
            Reason = req.Reason.Trim(),
            ApprovedBy = needsApproval ? null : current.UserId,
            ApprovedAt = needsApproval ? null : now,
        };
        db.UserRoles.Add(assignment);
        audit.Record(UserEvent("iam.role_assign", needsApproval ? "طلب إسناد دور المدير الأعلى" : "إسناد دور", needsApproval ? "Requested a super administrator assignment" : "Assigned a role", user, "A", Features.Grants) with
        {
            Related = [new AuditTarget("Role", role.Id.ToString(), role.Code)],
            Changes = [new FieldChange("role", null, role.Code), new FieldChange("scope", null, string.Join(',', scopeValues)), new FieldChange("validTo", null, req.ValidTo?.ToString("O"))],
            Reason = assignment.Reason,
            Details = new { pendingApproval = needsApproval },
        });
        await db.SaveChangesAsync(ct);
        if (!needsApproval) await AfterRoleChangeAsync(user.Id, role.Code, ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
        return (await GetAsync(userId, ct)).Assignments.First(a => a.Id == assignment.Id);
    }

    /// <summary>اعتماد إسناد دور المدير الأعلى بمدير أعلى غير الذي أسنده وغير صاحب الحساب.</summary>
    public async Task ApproveAssignmentAsync(Guid assignmentId, CancellationToken ct)
    {
        var me = current.UserId ?? throw DomainException.Forbidden();
        if (!(await access.CurrentAsync(ct)).IsSuperAdmin) throw DomainException.Forbidden();
        await using var tx = await BeginLockedAsync(ct);
        var a = await db.UserRoles.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw DomainException.NotFound();
        if (!a.IsPendingApproval)
            throw new DomainException("not_pending", "هذا الإسناد لا ينتظر اعتمادًا.", "This assignment is not waiting for approval.", 409);
        var user = await LoadManagedAsync(a.UserId, "iam.role_approve", Features.Grants, ct);
        var role = await db.Roles.AsNoTracking().FirstAsync(r => r.Id == a.RoleId, ct);
        if (a.GrantedBy == me || (a.GrantedBy is { } granter && await DependsOnAsync(me, granter, ct)))
            await DenyAsync(user, "iam.role_approve", Features.Grants, "SOD-10", new { role = role.Code },
                "لا يعتمد الإسنادَ من طلبه أو من أنشأه أو اعتمده صاحبُ الطلب. يعتمده مدير أعلى مستقل.", "The requester, or anyone they created or approved, cannot approve this assignment. An independent super administrator must approve it.", ct);
        a.ApprovedBy = me;
        a.ApprovedAt = clock.UtcNow;
        audit.Record(UserEvent("iam.role_approve", "اعتماد إسناد دور", "Approved a role assignment", user, "approve", Features.Grants) with
        {
            Related = [new AuditTarget("Role", role.Id.ToString(), role.Code)],
            Changes = [new FieldChange("approved", "false", "true")],
            Details = new { grantedBy = a.GrantedBy },
        });
        await db.SaveChangesAsync(ct);
        await AfterRoleChangeAsync(user.Id, role.Code, ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task UpdateAssignmentAsync(Guid assignmentId, UpdateAssignmentRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        await using var tx = await BeginLockedAsync(ct);
        var a = await db.UserRoles.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw DomainException.NotFound();
        var user = await LoadManagedAsync(a.UserId, "iam.role_assignment_update", Features.Grants, ct);
        var role = await db.Roles.AsNoTracking().FirstAsync(r => r.Id == a.RoleId, ct);
        await EnsureMayManageRoleAsync(role, user, ct);
        if (a.RevokedAt is not null) throw new DomainException("revoked", "الإسناد مسحوب.", "The assignment was revoked.", 409);
        if (req.ValidTo is { } to && to <= a.ValidFrom)
            throw new DomainException("invalid_dates", "تاريخ النهاية يجب أن يكون بعد البداية.", "The end date must be after the start date.", 400);
        if (role.Code == PermissionCatalog.SuperAdminRole && req.ValidTo is not null && a.ApprovedAt is not null)
            await EnsureSuperAdminsRemainAsync(excludeUser: null, excludeAssignment: a.Id, ct);
        var scope = CleanScope(req.ScopeValues);
        var changes = new List<FieldChange>();
        if (!scope.SequenceEqual(a.ScopeValues)) changes.Add(new FieldChange("scope", string.Join(',', a.ScopeValues), string.Join(',', scope)));
        if (a.ValidTo != req.ValidTo) changes.Add(new FieldChange("validTo", a.ValidTo?.ToString("O"), req.ValidTo?.ToString("O")));
        if (changes.Count == 0) return;
        a.ScopeValues = scope;
        a.ValidTo = req.ValidTo;
        audit.Record(UserEvent("iam.role_assignment_update", "تعديل إسناد دور", "Updated a role assignment", user, "E", Features.Grants) with
        {
            Related = [new AuditTarget("Role", role.Id.ToString(), role.Code)],
            Changes = changes,
            Reason = req.Reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task RevokeAssignmentAsync(Guid assignmentId, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        await using var tx = await BeginLockedAsync(ct);
        var a = await db.UserRoles.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw DomainException.NotFound();
        if (a.RevokedAt is not null) return;
        var user = await LoadManagedAsync(a.UserId, "iam.role_revoke", Features.Grants, ct);
        var role = await db.Roles.AsNoTracking().FirstAsync(r => r.Id == a.RoleId, ct);
        await EnsureMayManageRoleAsync(role, user, ct);
        if (role.Code == PermissionCatalog.SuperAdminRole && a.ApprovedAt is not null)
            await EnsureSuperAdminsRemainAsync(excludeUser: null, excludeAssignment: a.Id, ct);
        a.RevokedAt = clock.UtcNow;
        a.RevokedBy = current.UserId;
        a.RevokeReason = reason.Trim();
        audit.Record(UserEvent("iam.role_revoke", a.ApprovedAt is null ? "رفض إسناد دور" : "سحب دور", a.ApprovedAt is null ? "Rejected a role assignment" : "Revoked a role", user, "D", Features.Grants) with
        {
            Related = [new AuditTarget("Role", role.Id.ToString(), role.Code)],
            Changes = [new FieldChange("role", role.Code, null)],
            Reason = a.RevokeReason,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task<OverrideDto> AddOverrideAsync(Guid userId, AddOverrideRequest req, CancellationToken ct)
    {
        RequireReason(req.Reason);
        var user = await LoadManagedAsync(userId, "iam.override_add", Features.Grants, ct);
        if (string.IsNullOrWhiteSpace(req.Feature)) throw new DomainException("unknown_feature", "اختر خاصية.", "Choose a feature.", 400);
        var def = PermissionCatalog.Find(req.Feature) ?? throw new DomainException("unknown_feature", "الخاصية غير موجودة.", "Unknown feature.", 400);
        Cap caps;
        AccessScope scope;
        try
        {
            caps = PermissionCatalog.ParseCaps(req.Caps ?? "");
            scope = PermissionCatalog.ParseScope(string.IsNullOrEmpty(req.Scope) ? "A" : req.Scope);
        }
        catch (FormatException)
        {
            throw new DomainException("invalid_grant", "الإمكانيات أو النطاق غير صالحة.", "Invalid capabilities or scope.", 400);
        }
        // «نطاقه» يحتاج قيم وحدات لا يحملها الاستثناء الفردي؛ فيقتصر على الكل والمسند إليه وسجلاته.
        if (scope == AccessScope.Unit)
            throw new DomainException("invalid_scope", "الاستثناء الفردي لا يأخذ نطاق «نطاقه». اختر الكل أو المسند إليه أو سجلاته.", "An individual override cannot use the unit scope. Choose all, assigned or own.", 400);
        var specials = (req.Specials ?? []).Distinct(StringComparer.Ordinal).ToList();
        if ((caps & ~def.AllowedCaps) != Cap.None || specials.Any(s => !def.HasSpecial(s)))
            throw new DomainException("invalid_cap", $"إمكانية أو إجراء لا معنى له في «{def.Name.Ar}».", $"A capability or action that does not exist in '{def.Key}'.", 400);
        if (caps == Cap.None && specials.Count == 0)
            throw new DomainException("empty_override", "اختر إمكانية أو إجراءً واحدًا على الأقل.", "Choose at least one capability or action.", 400);
        if (!Enum.IsDefined(req.Effect)) throw new DomainException("invalid_effect", "نوع الاستثناء غير صالح.", "Invalid override type.", 400);
        if (req.Effect == OverrideEffect.Allow)
        {
            if (specials.Any(s => PermissionCatalog.IsSuperAdminOnly(def.Key, s)))
                await DenyAsync(user, "iam.override_add", Features.Grants, "SOD-10", new { feature = def.Key, specials },
                    "هذا الإجراء للمدير الأعلى وحده ولا يمنح فرديًا.", "This action belongs to super administrators only and cannot be granted individually.", ct);
            await EnsureGranterCoversAsync(user, def, caps, scope, specials, "iam.override_add", ct);
            foreach (var constrained in await ConstrainedCodesAsync(userId, ct))
                RoleRules.Validate(constrained, false, [new GrantInput(def.Key, caps, scope, specials)]);
        }
        if (req.ValidTo is { } to && to <= clock.UtcNow)
            throw new DomainException("invalid_dates", "تاريخ النهاية يجب أن يكون في المستقبل.", "The end date must be in the future.", 400);
        var o = new UserOverride
        {
            UserId = userId,
            Feature = def.Key,
            Caps = caps,
            Specials = specials,
            Effect = req.Effect,
            Scope = scope,
            ValidFrom = clock.UtcNow,
            ValidTo = req.ValidTo,
            GrantedBy = current.UserId,
            GrantedAt = clock.UtcNow,
            Reason = req.Reason.Trim(),
        };
        db.UserOverrides.Add(o);
        var verbAr = req.Effect == OverrideEffect.Allow ? "منح صلاحية فردية" : "منع صلاحية فردية";
        var verbEn = req.Effect == OverrideEffect.Allow ? "Granted an individual permission" : "Denied an individual permission";
        audit.Record(UserEvent("iam.override_add", verbAr, verbEn, user, "A", Features.Grants) with
        {
            Changes = [new FieldChange(def.Key, null, $"{(req.Effect == OverrideEffect.Allow ? "+" : "-")}{PermissionCatalog.Letters(caps)}:{GrantView.ScopeCode(scope)}" + (specials.Count > 0 ? "+" + string.Join(',', specials) : ""))],
            Reason = o.Reason,
        });
        await db.SaveChangesAsync(ct);
        await access.BumpVersionAsync(ct);
        return (await GetAsync(userId, ct)).Overrides.First(x => x.Id == o.Id);
    }

    public async Task RevokeOverrideAsync(Guid overrideId, string reason, CancellationToken ct)
    {
        RequireReason(reason);
        var o = await db.UserOverrides.FirstOrDefaultAsync(x => x.Id == overrideId, ct) ?? throw DomainException.NotFound();
        if (o.RevokedAt is not null) return;
        var user = await LoadManagedAsync(o.UserId, "iam.override_revoke", Features.Grants, ct);
        // سحب منع فردي يعيد صلاحية، فهو منح في أثره: لا يتجاوز ما يملكه الساحب.
        if (o.Effect == OverrideEffect.Deny)
            await EnsureGranterCoversAsync(user, PermissionCatalog.Get(o.Feature), o.Caps, o.Scope, o.Specials, "iam.override_revoke", ct);
        o.RevokedAt = clock.UtcNow;
        o.RevokedBy = current.UserId;
        audit.Record(UserEvent("iam.override_revoke", "سحب استثناء فردي", "Revoked an individual override", user, "D", Features.Grants) with
        {
            Changes = [new FieldChange(o.Feature, $"{(o.Effect == OverrideEffect.Allow ? "+" : "-")}{PermissionCatalog.Letters(o.Caps)}", null)],
            Reason = reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task<EffectiveDto> EffectiveAsync(Guid userId, CancellationToken ct)
    {
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw DomainException.NotFound();
        return EffectiveDto.From(await access.GetAsync(userId, ct));
    }

    /// <summary>
    /// SOD-9: لا أحد يدير حسابه. وحامل دور محجوز (ولو بإسناد ينتظر الاعتماد) لا يديره إلا المدير الأعلى.
    /// </summary>
    private async Task<AppUser> LoadManagedAsync(Guid id, string action, string feature, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw DomainException.NotFound();
        if (current.UserId == id)
            await DenyAsync(user, action, feature, "SOD-9", null,
                "لا تعدل حسابك أو أدوارك أو صلاحياتك بنفسك. يتولاها مسؤول آخر.", "You cannot manage your own account, roles or permissions. Another administrator must do it.", ct);
        string[] reservedCodes = [.. PermissionCatalog.ReservedRoles];
        var reserved = await db.UserRoles.Where(a => a.UserId == id && a.RevokedAt == null)
            .Join(db.Roles.Where(r => reservedCodes.Contains(r.Code)), a => a.RoleId, r => r.Id, (a, r) => r.Code)
            .FirstOrDefaultAsync(ct);
        if (reserved is not null && !(await access.CurrentAsync(ct)).IsSuperAdmin)
            await DenyAsync(user, action, feature, "SOD-9", new { holds = reserved },
                "هذا الحساب يحمل دورًا محجوزًا، فلا يديره إلا المدير الأعلى.", "This account holds a reserved role, so only a super administrator can manage it.", ct);
        return user;
    }

    private async Task EnsureMayManageRoleAsync(Role role, AppUser user, CancellationToken ct)
    {
        if (!PermissionCatalog.ReservedRoles.Contains(role.Code)) return;
        if ((await access.CurrentAsync(ct)).IsSuperAdmin) return;
        await DenyAsync(user, "iam.role_manage", Features.Grants, "SOD-9", new { role = role.Code },
            "هذا الدور يديره المدير الأعلى وحده.", "Only a super administrator can manage this role.", ct);
    }

    /// <summary>SOD-9: المنح الفردي (أو ما في أثره) لا يتجاوز ما يملكه المانح، ولا يمنح غير المدير الأعلى إجراء قرار.</summary>
    private async Task EnsureGranterCoversAsync(AppUser user, FeatureDefinition def, Cap caps, AccessScope scope, IReadOnlyCollection<string> specials, string action, CancellationToken ct)
    {
        var mine = await access.CurrentAsync(ct);
        if (mine.IsSuperAdmin) return;
        if (!mine.Covers(def.Key, caps, scope, specials) || specials.Any(PermissionCatalog.DecisionSpecials.Contains))
            await DenyAsync(user, action, Features.Grants, "SOD-9", new { feature = def.Key, caps = PermissionCatalog.Letters(caps), specials },
                "لا تمنح غيرك ما لا تملكه أنت.", "You cannot grant what you do not have yourself.", ct);
    }

    /// <summary>
    /// SOD-11 على الصلاحية المجمعة: من يحمل دور مسؤول النظام أو المراجع الرقابي لا يضاف له دور يتجاوز قيده،
    /// ولا يسند أحدهما لمن يحمل أدوارًا تتجاوزه. المدير الأعلى لا يجتمع مع أي منهما.
    /// </summary>
    private async Task EnsureCompatibleRoleAsync(AppUser user, Role role, CancellationToken ct)
    {
        var heldIds = await db.UserRoles.Where(a => a.UserId == user.Id && a.RevokedAt == null && a.RoleId != role.Id).Select(a => a.RoleId).Distinct().ToListAsync(ct);
        var held = await db.Roles.AsNoTracking().Include(r => r.Grants).Where(r => heldIds.Contains(r.Id)).ToListAsync(ct);
        // قيد المحتوى على مسؤول النظام وحده منذ D6؛ واستقلال الرقابة يبقى: المدير الأعلى لا يجتمع مع مسؤول النظام ولا المراجع الرقابي.
        static bool Constrained(string code) => code is RoleRules.SystemAdminRole;
        static bool ApartFromSuperAdmin(string code) => code is RoleRules.SystemAdminRole or RoleRules.AuditorRole;
        static IEnumerable<GrantInput> Inputs(Role r) => r.Grants.Select(g => new GrantInput(g.Feature, g.Caps, g.Scope, g.Specials));
        var sa = PermissionCatalog.SuperAdminRole;
        if ((role.Code == sa && held.Any(h => ApartFromSuperAdmin(h.Code))) || (ApartFromSuperAdmin(role.Code) && held.Any(h => h.Code == sa)))
            await DenyAsync(user, "iam.role_assign", Features.Grants, "SOD-11", new { role = role.Code },
                "دور المدير الأعلى لا يجتمع مع مسؤول النظام أو المراجع الرقابي.", "The super administrator role cannot be combined with the system administrator or the oversight auditor.", ct);
        try
        {
            foreach (var constrained in held.Where(h => Constrained(h.Code)))
                RoleRules.Validate(constrained.Code, false, Inputs(role));
            if (Constrained(role.Code))
            {
                foreach (var other in held) RoleRules.Validate(role.Code, false, Inputs(other));
                var allows = await db.UserOverrides.Where(o => o.UserId == user.Id && o.RevokedAt == null && o.Effect == OverrideEffect.Allow).ToListAsync(ct);
                RoleRules.Validate(role.Code, false, allows.Select(o => new GrantInput(o.Feature, o.Caps, o.Scope, o.Specials)).GroupBy(g => g.Feature).Select(g => g.First()));
            }
        }
        catch (DomainException e) when (e.Code == "sod_sod_11")
        {
            await DenyAsync(user, "iam.role_assign", Features.Grants, "SOD-11", new { role = role.Code }, e.Message, e.Message, ct);
        }
    }

    private async Task<IReadOnlyList<string>> ConstrainedCodesAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles.Where(a => a.UserId == userId && a.RevokedAt == null)
            .Join(db.Roles.Where(r => r.Code == RoleRules.SystemAdminRole), a => a.RoleId, r => r.Id, (a, r) => r.Code)
            .Distinct().ToListAsync(ct);

    /// <summary>هل أنشأ «other» حساب «user» أو أسند له دور المدير الأعلى أو اعتمده؟</summary>
    private async Task<bool> DependsOnAsync(Guid user, Guid other, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Id == user && u.CreatedBy == other, ct)) return true;
        return await db.UserRoles.Where(a => a.UserId == user && a.RevokedAt == null && (a.GrantedBy == other || a.ApprovedBy == other))
            .Join(db.Roles.Where(r => r.Code == PermissionCatalog.SuperAdminRole), a => a.RoleId, r => r.Id, (a, r) => a.Id)
            .AnyAsync(ct);
    }

    /// <summary>
    /// SOD-10: لا يقل المديرون الأعلون الدائمون (معتمدون، بلا تاريخ نهاية، بحسابات نشطة) عن اثنين.
    /// الإسناد المؤقت لا يحسب، فلا يمر جعل كل مدير مؤقتًا واحدًا بعد الآخر.
    /// </summary>
    private async Task EnsureSuperAdminsRemainAsync(Guid? excludeUser, Guid? excludeAssignment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var saRole = db.Roles.Where(r => r.Code == PermissionCatalog.SuperAdminRole).Select(r => r.Id);
        var affected = excludeUser is { } u
            ? await db.UserRoles.AnyAsync(a => a.UserId == u && a.RevokedAt == null && a.ApprovedAt != null && saRole.Contains(a.RoleId), ct)
            : true;
        if (!affected) return;
        var remaining = await db.UserRoles
            .Where(a => a.RevokedAt == null && a.ApprovedAt != null && a.ValidFrom <= now && a.ValidTo == null && saRole.Contains(a.RoleId) && a.Id != excludeAssignment && a.UserId != excludeUser)
            .Join(db.Users.Where(x => x.Active), a => a.UserId, x => x.Id, (a, x) => a.UserId)
            .Distinct().CountAsync(ct);
        if (remaining < MinimumSuperAdmins)
            throw DomainException.SeparationOfDuties("SOD-10", "لا يقل عدد حسابات المدير الأعلى الدائمة النشطة عن اثنين. أضف مديرًا أعلى آخر أولًا.", "There must be at least two permanent active super administrators. Add another one first.");
    }

    /// <summary>معاملة تبدأ بقفل استشاري يسلسل عمليات الأدوار المحجوزة بين الطلبات والنسخ.</summary>
    private async Task<IDbContextTransaction> BeginLockedAsync(CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({IamLockKey})", ct);
        return tx;
    }

    /// <summary>دور يتطلب التحقق بخطوتين: تنتهي جلسات صاحبه ليدخل من جديد بالتحقق.</summary>
    private async Task AfterRoleChangeAsync(Guid userId, string roleCode, CancellationToken ct)
    {
        if (authOptions.Value.MfaEnforcedFor(roleCode)) await sessions.RevokeAllAsync(userId, ct);
    }

    private async Task DenyAsync(AppUser user, string action, string feature, string rule, object? details, string messageAr, string messageEn, CancellationToken ct)
    {
        await audit.RecordNowAsync(UserEvent(action, "رفض بقاعدة فصل", "Blocked by a separation rule", user, "-", feature) with
        {
            Outcome = "denied",
            Details = new { rule, details },
        }, ct);
        throw DomainException.SeparationOfDuties(rule, messageAr, messageEn);
    }

    private AuditEntry UserEvent(string action, string messageAr, string messageEn, AppUser user, string cap, string feature = Features.Users) =>
        AuditEvents.Security(action, feature, messageAr, messageEn) with
        {
            Permission = $"{feature}:{cap}",
            Target = new AuditTarget("User", user.Id.ToString(), user.Email),
        };

    private static List<string> CleanScope(IReadOnlyList<string>? values) =>
        (values ?? []).Where(v => v is not null).Select(v => v.Trim()).Where(v => v.Length is > 0 and <= 64).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(50).ToList();

    private static string NormalizeEmail(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (e.Length is < 5 or > 254 || !MailAddress.TryCreate(e, out var parsed) || parsed.Address != e)
            throw new DomainException("invalid_email", "البريد الإلكتروني غير صالح.", "Invalid email address.", 400);
        return e;
    }

    private static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new DomainException("reason_required", "اكتب سببًا واضحًا (5 أحرف على الأقل).", "Enter a clear reason (at least 5 characters).", 400);
    }

    private Task<Dictionary<Guid, PersonDto>> PeopleAsync(IEnumerable<Guid?> ids, CancellationToken ct) =>
        People.LookupAsync(db, ids.Where(i => i is not null).Select(i => i!.Value), ct);
}
