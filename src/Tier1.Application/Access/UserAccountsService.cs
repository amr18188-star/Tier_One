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
public sealed class UserAccountsService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, IPasswordHasher hasher, ISessionService sessions, IAuditLog audit, IamPolicy policy, IIamLock iamLock, UniversityDomainsService domains)
{
    public async Task<TemporaryPasswordDto> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        // ADR-0016: the login name is a university email at an approved domain or one of its subdomains.
        var email = await domains.EnsureAllowedEmailAsync(req.Email, ct);
        if (string.IsNullOrWhiteSpace(req.FullNameAr))
            throw new DomainException("name_required", "اكتب الاسم بالعربية.", "Enter the Arabic name.", 400) { Field = "fullNameAr" };
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new DomainException("duplicate_email", "يوجد حساب بهذا البريد.", "An account with this email already exists.", 409) { Field = "email" };
        var title = TitleOf(req.Title);
        // ADR-0021: every account carries its holder's full data: both names, the national ID and the mobile number.
        var nameEn = EnglishNameOf(req.FullNameEn);
        var nationalId = await NationalIdOfAsync(req.NationalId, null, ct);
        var phone = PhoneOf(req.Phone);
        var temporary = Tokens.NewTemporaryPassword();
        var user = new AppUser
        {
            Email = email,
            FullNameAr = req.FullNameAr.Trim(),
            FullNameEn = nameEn,
            Title = title,
            NationalId = nationalId,
            Phone = phone,
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
            Changes =
            [
                new FieldChange("email", null, email), new FieldChange("fullNameAr", null, user.FullNameAr), new FieldChange("fullNameEn", null, nameEn),
                new FieldChange("title", null, title), new FieldChange("nationalId", null, NationalIds.Mask(nationalId)), new FieldChange("phone", null, phone),
            ],
        });
        await db.SaveChangesAsync(ct);
        return new TemporaryPasswordDto(user.Id, temporary);
    }

    /// <summary>ADR-0021: the English name is required, in Latin letters.</summary>
    private static string EnglishNameOf(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("name_en_required", "اكتب الاسم بالإنجليزية.", "Enter the English name.", 400) { Field = "fullNameEn" };
        var trimmed = name.Trim();
        return EnglishNames.IsValid(trimmed)
            ? trimmed
            : throw new DomainException("invalid_name_en", "اكتب الاسم بالإنجليزية بحروف لاتينية.", "Write the English name in Latin letters.", 400) { Field = "fullNameEn" };
    }

    /// <summary>
    /// ADR-0021: the Egyptian national ID, 14 digits, on one account only. On an edit an empty value keeps the one on file, because the
    /// server never sends the full number back for the form to resend it.
    /// </summary>
    private async Task<string> NationalIdOfAsync(string? value, AppUser? existing, CancellationToken ct)
    {
        var id = NationalIds.Normalize(value);
        if (id is null)
            return existing?.NationalId
                ?? throw new DomainException("national_id_required", "اكتب الرقم القومي.", "Enter the national ID.", 400) { Field = "nationalId" };
        if (!NationalIds.IsValid(id, CairoTime.DateOf(clock.UtcNow)))
            throw new DomainException("invalid_national_id", "الرقم القومي غير صحيح. اكتبه 14 رقمًا كما في البطاقة.", "The national ID is not valid. Enter its 14 digits as on the card.", 400) { Field = "nationalId" };
        var mine = existing?.Id;
        if (await db.Users.AnyAsync(u => u.NationalId == id && u.Id != mine, ct))
            throw new DomainException("duplicate_national_id", "يوجد حساب آخر بهذا الرقم القومي.", "Another account has this national ID.", 409) { Field = "nationalId" };
        return id;
    }

    /// <summary>ADR-0021: an Egyptian mobile number, kept as 01XXXXXXXXX.</summary>
    private static string PhoneOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException("phone_required", "اكتب رقم التليفون المحمول.", "Enter the mobile number.", 400) { Field = "phone" };
        return MobileNumbers.Normalize(value)
            ?? throw new DomainException("invalid_phone", "رقم المحمول غير صحيح. اكتبه 11 رقمًا يبدأ بـ 010 أو 011 أو 012 أو 015.",
                "The mobile number is not valid. Enter its 11 digits starting with 010, 011, 012 or 015.", 400) { Field = "phone" };
    }

    /// <summary>ADR-0019: the title is one of the closed list, or none. A free-text title is refused, so names read the same everywhere.</summary>
    private static string? TitleOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        return PersonTitles.Find(title)?.Ar
            ?? throw new DomainException("invalid_title", "اختر اللقب من القائمة.", "Choose the title from the list.", 400) { Field = "title" };
    }

    public async Task UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        var user = await policy.LoadManagedAsync(id, "iam.user_update", Features.Users, ct);
        if (string.IsNullOrWhiteSpace(req.FullNameAr))
            throw new DomainException("name_required", "اكتب الاسم بالعربية.", "Enter the Arabic name.", 400) { Field = "fullNameAr" };
        var changes = new List<FieldChange>();
        var ar = req.FullNameAr.Trim();
        // ADR-0021: an edit saves complete data too, so an account from before the rule is completed the first time it is edited.
        var en = EnglishNameOf(req.FullNameEn);
        if (user.FullNameAr != ar) changes.Add(new FieldChange("fullNameAr", user.FullNameAr, ar));
        if (user.FullNameEn != en) changes.Add(new FieldChange("fullNameEn", user.FullNameEn, en));
        var title = TitleOf(req.Title);
        if (user.Title != title) changes.Add(new FieldChange("title", user.Title, title));
        var nationalId = await NationalIdOfAsync(req.NationalId, user, ct);
        if (user.NationalId != nationalId) changes.Add(new FieldChange("nationalId", user.NationalId is { } old ? NationalIds.Mask(old) : null, NationalIds.Mask(nationalId)));
        var phone = PhoneOf(req.Phone);
        if (user.Phone != phone) changes.Add(new FieldChange("phone", user.Phone, phone));
        // ADR-0016: a new email (the login name) must be at an active approved domain and unused; the old one keeps working until changed.
        var email = user.Email;
        if (req.Email is { } wanted && !string.Equals(wanted.Trim(), user.Email, StringComparison.OrdinalIgnoreCase))
        {
            email = await domains.EnsureAllowedEmailAsync(wanted, ct);
            if (await db.Users.AnyAsync(u => u.Email == email && u.Id != user.Id, ct))
                throw new DomainException("duplicate_email", "يوجد حساب بهذا البريد.", "An account with this email already exists.", 409) { Field = "email" };
            changes.Add(new FieldChange("email", user.Email, email));
        }
        if (changes.Count == 0) return;
        user.FullNameAr = ar;
        user.FullNameEn = en;
        user.Title = title;
        user.NationalId = nationalId;
        user.Phone = phone;
        user.Email = email;
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
