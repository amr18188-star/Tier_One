using System.Net.Mail;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tier1.Application.Abstractions;
using Tier1.Application.Accounts;
using Tier1.Application.Auditing;
using Tier1.Application.Security;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;

namespace Tier1.Application.Access;

/// <summary>
/// ARCH-2: role assignments: assign, approve a super administrator assignment, edit scope and end date, revoke.
/// Changes that can touch the number of super administrators run in a transaction under the IAM lock.
/// </summary>
public sealed class RoleAssignmentService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, IamPolicy policy, IIamLock iamLock, UsersAdminService users)
{
    public async Task<AssignmentDto> AssignRoleAsync(Guid userId, AssignRoleRequest req, CancellationToken ct)
    {
        IamPolicy.RequireReason(req.Reason);
        await using var tx = await iamLock.BeginAsync(ct);
        var user = await policy.LoadManagedAsync(userId, "iam.role_assign", Features.Grants, ct);
        if (!user.Active) throw new DomainException("inactive", "الحساب موقوف. أعد تفعيله أولًا.", "The account is deactivated. Reactivate it first.", 409);
        var role = await db.Roles.AsNoTracking().Include(r => r.Grants).FirstOrDefaultAsync(r => r.Id == req.RoleId, ct) ?? throw DomainException.NotFound();
        var mine = await access.CurrentAsync(ct);
        if (PermissionCatalog.ReservedRoles.Contains(role.Code) && !mine.IsSuperAdmin)
            await policy.DenyAsync(user, "iam.role_assign", Features.Grants, "SOD-9", new { role = role.Code },
                "هذا الدور يسنده المدير الأعلى وحده.", "Only a super administrator can assign this role.", ct);
        await policy.EnsureCompatibleRoleAsync(user, role, ct);
        var now = clock.UtcNow;
        var from = req.ValidFrom ?? now;
        if (req.ValidTo is { } to && to <= from)
            throw new DomainException("invalid_dates", "تاريخ النهاية يجب أن يكون بعد البداية.", "The end date must be after the start date.", 400) { Field = "validTo" };
        var scopeValues = IamPolicy.CleanScope(req.ScopeValues);
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
        audit.Record(IamPolicy.UserEvent("iam.role_assign", needsApproval ? "طلب إسناد دور المدير الأعلى" : "إسناد دور", needsApproval ? "Requested a super administrator assignment" : "Assigned a role", user, "A", Features.Grants) with
        {
            Related = [new AuditTarget("Role", role.Id.ToString(), role.Code)],
            Changes = [new FieldChange("role", null, role.Code), new FieldChange("scope", null, string.Join(',', scopeValues)), new FieldChange("validTo", null, req.ValidTo?.ToString("O"))],
            Reason = assignment.Reason,
            Details = new { pendingApproval = needsApproval },
        });
        await db.SaveChangesAsync(ct);
        if (!needsApproval) await policy.AfterRoleChangeAsync(user.Id, role.Code, ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
        return (await users.GetAsync(userId, ct)).Assignments.First(a => a.Id == assignment.Id);
    }

    /// <summary>اعتماد إسناد دور المدير الأعلى بمدير أعلى غير الذي أسنده وغير صاحب الحساب.</summary>
    public async Task ApproveAssignmentAsync(Guid assignmentId, CancellationToken ct)
    {
        var me = current.UserId ?? throw DomainException.Forbidden();
        if (!(await access.CurrentAsync(ct)).IsSuperAdmin) throw DomainException.Forbidden();
        await using var tx = await iamLock.BeginAsync(ct);
        var a = await db.UserRoles.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw DomainException.NotFound();
        if (!a.IsPendingApproval)
            throw new DomainException("not_pending", "هذا الإسناد لا ينتظر اعتمادًا.", "This assignment is not waiting for approval.", 409);
        var user = await policy.LoadManagedAsync(a.UserId, "iam.role_approve", Features.Grants, ct);
        var role = await db.Roles.AsNoTracking().FirstAsync(r => r.Id == a.RoleId, ct);
        if (a.GrantedBy == me || (a.GrantedBy is { } granter && await policy.DependsOnAsync(me, granter, ct)))
            await policy.DenyAsync(user, "iam.role_approve", Features.Grants, "SOD-10", new { role = role.Code },
                "لا يعتمد الإسنادَ من طلبه أو من أنشأه أو اعتمده صاحبُ الطلب. يعتمده مدير أعلى مستقل.", "The requester, or anyone they created or approved, cannot approve this assignment. An independent super administrator must approve it.", ct);
        a.ApprovedBy = me;
        a.ApprovedAt = clock.UtcNow;
        audit.Record(IamPolicy.UserEvent("iam.role_approve", "اعتماد إسناد دور", "Approved a role assignment", user, "approve", Features.Grants) with
        {
            Related = [new AuditTarget("Role", role.Id.ToString(), role.Code)],
            Changes = [new FieldChange("approved", "false", "true")],
            Details = new { grantedBy = a.GrantedBy },
        });
        await db.SaveChangesAsync(ct);
        await policy.AfterRoleChangeAsync(user.Id, role.Code, ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task UpdateAssignmentAsync(Guid assignmentId, UpdateAssignmentRequest req, CancellationToken ct)
    {
        IamPolicy.RequireReason(req.Reason);
        await using var tx = await iamLock.BeginAsync(ct);
        var a = await db.UserRoles.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw DomainException.NotFound();
        var user = await policy.LoadManagedAsync(a.UserId, "iam.role_assignment_update", Features.Grants, ct);
        var role = await db.Roles.AsNoTracking().FirstAsync(r => r.Id == a.RoleId, ct);
        await policy.EnsureMayManageRoleAsync(role, user, ct);
        if (a.RevokedAt is not null) throw new DomainException("revoked", "الإسناد مسحوب.", "The assignment was revoked.", 409);
        if (req.ValidTo is { } to && to <= a.ValidFrom)
            throw new DomainException("invalid_dates", "تاريخ النهاية يجب أن يكون بعد البداية.", "The end date must be after the start date.", 400) { Field = "validTo" };
        if (role.Code == PermissionCatalog.SuperAdminRole && req.ValidTo is not null && a.ApprovedAt is not null)
            await policy.EnsureSuperAdminsRemainAsync(excludeUser: null, excludeAssignment: a.Id, ct);
        var scope = IamPolicy.CleanScope(req.ScopeValues);
        var changes = new List<FieldChange>();
        if (!scope.SequenceEqual(a.ScopeValues)) changes.Add(new FieldChange("scope", string.Join(',', a.ScopeValues), string.Join(',', scope)));
        if (a.ValidTo != req.ValidTo) changes.Add(new FieldChange("validTo", a.ValidTo?.ToString("O"), req.ValidTo?.ToString("O")));
        if (changes.Count == 0) return;
        a.ScopeValues = scope;
        a.ValidTo = req.ValidTo;
        audit.Record(IamPolicy.UserEvent("iam.role_assignment_update", "تعديل إسناد دور", "Updated a role assignment", user, "E", Features.Grants) with
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
        IamPolicy.RequireReason(reason);
        await using var tx = await iamLock.BeginAsync(ct);
        var a = await db.UserRoles.FirstOrDefaultAsync(x => x.Id == assignmentId, ct) ?? throw DomainException.NotFound();
        if (a.RevokedAt is not null) return;
        var user = await policy.LoadManagedAsync(a.UserId, "iam.role_revoke", Features.Grants, ct);
        var role = await db.Roles.AsNoTracking().FirstAsync(r => r.Id == a.RoleId, ct);
        await policy.EnsureMayManageRoleAsync(role, user, ct);
        if (role.Code == PermissionCatalog.SuperAdminRole && a.ApprovedAt is not null)
            await policy.EnsureSuperAdminsRemainAsync(excludeUser: null, excludeAssignment: a.Id, ct);
        a.RevokedAt = clock.UtcNow;
        a.RevokedBy = current.UserId;
        a.RevokeReason = reason.Trim();
        audit.Record(IamPolicy.UserEvent("iam.role_revoke", a.ApprovedAt is null ? "رفض إسناد دور" : "سحب دور", a.ApprovedAt is null ? "Rejected a role assignment" : "Revoked a role", user, "D", Features.Grants) with
        {
            Related = [new AuditTarget("Role", role.Id.ToString(), role.Code)],
            Changes = [new FieldChange("role", role.Code, null)],
            Reason = a.RevokeReason,
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await access.BumpVersionAsync(ct);
    }
}
