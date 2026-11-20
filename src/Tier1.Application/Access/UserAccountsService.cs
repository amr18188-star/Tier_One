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

/// <summary>ARCH-2: staff accounts: create, edit, deactivate and reactivate, unlock, reset two-step verification, set a temporary password.</summary>
public sealed class UserAccountsService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, IPasswordHasher hasher, ISessionService sessions, IAuditLog audit, IamPolicy policy, IIamLock iamLock)
{
    public async Task<TemporaryPasswordDto> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        var email = IamPolicy.NormalizeEmail(req.Email);
        if (string.IsNullOrWhiteSpace(req.FullNameAr))
            throw new DomainException("name_required", "اكتب الاسم بالعربية.", "Enter the Arabic name.", 400) { Field = "fullNameAr" };
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
        var user = await policy.LoadManagedAsync(id, "iam.user_update", Features.Users, ct);
        if (string.IsNullOrWhiteSpace(req.FullNameAr))
            throw new DomainException("name_required", "اكتب الاسم بالعربية.", "Enter the Arabic name.", 400) { Field = "fullNameAr" };
        var changes = new List<FieldChange>();
        var ar = req.FullNameAr.Trim();
        var en = string.IsNullOrWhiteSpace(req.FullNameEn) ? null : req.FullNameEn.Trim();
        if (user.FullNameAr != ar) changes.Add(new FieldChange("fullNameAr", user.FullNameAr, ar));
        if (user.FullNameEn != en) changes.Add(new FieldChange("fullNameEn", user.FullNameEn, en));
        if (changes.Count == 0) return;
        user.FullNameAr = ar;
        user.FullNameEn = en;
        audit.Record(IamPolicy.UserEvent("iam.user_update", "تعديل بيانات حساب", "Updated an account", user, "E") with { Changes = changes });
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateAsync(Guid id, string reason, CancellationToken ct)
    {
        IamPolicy.RequireReason(reason);
        await using var tx = await iamLock.BeginAsync(ct);
        var user = await policy.LoadManagedAsync(id, "iam.user_deactivate", Features.Users, ct);
        if (!user.Active) return;
        await policy.EnsureSuperAdminsRemainAsync(excludeUser: id, excludeAssignment: null, ct);
        user.Active = false;
        audit.Record(IamPolicy.UserEvent("iam.user_deactivate", "إيقاف حساب", "Deactivated an account", user, "D") with
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
        IamPolicy.RequireReason(reason);
        var user = await policy.LoadManagedAsync(id, "iam.user_reactivate", Features.Users, ct);
        if (user.Active) return;
        user.Active = true;
        audit.Record(IamPolicy.UserEvent("iam.user_reactivate", "إعادة تفعيل حساب", "Reactivated an account", user, "E") with
        {
            Changes = [new FieldChange("active", "false", "true")],
            Reason = reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
        await access.BumpVersionAsync(ct);
    }

    public async Task UnlockAsync(Guid id, string reason, CancellationToken ct)
    {
        IamPolicy.RequireReason(reason);
        var user = await policy.LoadManagedAsync(id, "iam.user_unlock", Features.Users, ct);
        user.LockedUntil = null;
        user.FailedLogins = 0;
        user.MfaLockedUntil = null;
        user.FailedMfa = 0;
        audit.Record(IamPolicy.UserEvent("iam.user_unlock", "فك قفل حساب", "Unlocked an account", user, "E") with { Reason = reason.Trim() });
        await db.SaveChangesAsync(ct);
    }

    public async Task ResetMfaAsync(Guid id, string reason, CancellationToken ct)
    {
        IamPolicy.RequireReason(reason);
        var user = await policy.LoadManagedAsync(id, "iam.user_mfa_reset", Features.Users, ct);
        user.MfaEnabled = false;
        user.MfaSecretProtected = null;
        user.LastTotpStep = 0;
        audit.Record(IamPolicy.UserEvent("iam.user_mfa_reset", "إعادة ضبط التحقق بخطوتين", "Reset two-step verification", user, "E") with
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
        IamPolicy.RequireReason(reason);
        if (!(await access.CurrentAsync(ct)).IsSuperAdmin) throw DomainException.Forbidden();
        var user = await policy.LoadManagedAsync(id, "iam.user_set_password", Features.Users, ct);
        var temporary = Tokens.NewTemporaryPassword();
        user.PasswordHash = hasher.Hash(temporary);
        user.MustChangePassword = true;
        user.FailedLogins = 0;
        user.LockedUntil = null;
        audit.Record(IamPolicy.UserEvent("iam.user_set_password", "تعيين كلمة مرور مؤقتة", "Set a temporary password", user, "set_password") with { Reason = reason.Trim() });
        await db.SaveChangesAsync(ct);
        await sessions.RevokeAllAsync(user.Id, ct);
        return new TemporaryPasswordDto(user.Id, temporary);
    }
}
