using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Application.Security;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Tier1.Application.Accounts;

public sealed record LoginCommand(string Email, string Password);

/// <summary>next: "done" أو "mfa_verify" أو "mfa_enroll" أو "change_password".</summary>
public sealed record LoginResult(string Token, DateTimeOffset ExpiresAt, string Next);

public sealed record MfaSetup(string Secret, string OtpAuthUri, string QrDataUri);

public sealed class SignInService(
    IIdentityStore db,
    IClock clock,
    ICurrentUser current,
    IPasswordHasher hasher,
    ISecretProtector protector,
    ISessionService sessions,
    IAuditLog audit,
    IQrCodes qr,
    ILoginAttempts attempts,
    IOptions<AuthOptions> options,
    Access.IAccessService access,
    Settings.SettingsService settings,
    NetworkAccessService network)
{
    private const string Surface = "staff";

    private static readonly DomainException BadCredentials =
        new("invalid_credentials", "البريد أو كلمة المرور غير صحيحة.", "Wrong email or password.", 401);

    // تجزئة ثابتة لتوحيد زمن الاستجابة حين لا يوجد الحساب (منع كشف وجود الحسابات بالتوقيت).
    private static string? _dummyHash;

    public async Task<LoginResult> LoginAsync(LoginCommand cmd, CancellationToken ct)
    {
        // tx-exempt: attempt counters commit on their own, before the check, so parallel or failing requests cannot undo them (BE-4).
        var email = (cmd.Email ?? "").Trim().ToLowerInvariant();
        var now = clock.UtcNow;
        var ip = current.Ip;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        // Every refusal below answers like a wrong password and takes as long (a dummy hash), so it reveals neither the account
        // nor the block. Counters and the attempt log are shared by every API instance (ILoginAttempts).
        // An address that failed too often across accounts (spraying, stuffing) is refused, except for an account that signed in
        // from it recently: the campus behind one address keeps working while someone there guesses other accounts.
        if (await attempts.AddressBlockedAsync(Surface, ip, now, ct)
            && !(user is not null && await attempts.KnownAddressAsync(user.Id, ip, now.AddDays(-options.Value.KnownAddressDays), ct)))
            throw await RefuseAsync(user, email, "address_blocked", cmd.Password, counts: false, ct);
        if (user is null || !user.Active || user.PasswordHash is null)
            throw await RefuseAsync(user, email, user is null ? "unknown_email" : "inactive_or_unset", cmd.Password, counts: true, ct);
        // العنوان المحجوب عن هذا الحساب (محاولات كثيرة منه) يرد كالخطأ العادي وبزمنه، ولا يمس دخول صاحب الحساب من مكان آخر.
        if (await attempts.AccountAddressBlockedAsync(Surface, user.Id, ip, now, ct))
            throw await RefuseAsync(user, email, "account_address_blocked", cmd.Password, counts: false, ct);
        // حجز المحاولة ذريًا قبل التحقق: الحساب المقفل أو المستنفد لا يتحقق منه، فلا تتجاوز الطلبات المتوازية الحد.
        var max = options.Value.MaxFailedLoginsPerAccount;
        var reserved = await db.Users.Where(u => u.Id == user.Id && (u.LockedUntil == null || u.LockedUntil <= now) && u.FailedLogins < max)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLogins, u => u.FailedLogins + 1), ct);
        if (reserved == 0)
        {
            await db.Users.Where(u => u.Id == user.Id && u.FailedLogins >= max).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedUntil, now.AddMinutes(options.Value.LockoutMinutes)).SetProperty(u => u.FailedLogins, 0), ct);
            await audit.RecordNowAsync(AuditEvents.Security("auth.locked", Features.Users, "قفل حساب", "Locked an account") with
            {
                Outcome = "denied",
                Target = new AuditTarget("User", user.Id.ToString(), user.Email),
                ActorOverride = user.Id,
            }, ct);
            // الرد نفسه للحساب المقفل وغير الموجود، وبالزمن نفسه (تجزئة وهمية)، فلا يكشف الرد وجود الحساب ولا قفله؛ القفل مسجل في السجل الأمني.
            throw await RefuseAsync(user, email, "account_locked", cmd.Password, counts: false, ct);
        }
        var (valid, rehash) = hasher.Verify(user.PasswordHash!, cmd.Password ?? "");
        if (!valid)
        {
            await CountFailureAsync(user, email, ip, now, ct);
            var failed = await db.Users.Where(u => u.Id == user.Id).Select(u => u.FailedLogins).FirstAsync(ct);
            if (failed >= max)
                await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedUntil, now.AddMinutes(options.Value.LockoutMinutes)).SetProperty(u => u.FailedLogins, 0), ct);
            await attempts.RecordAsync(Surface, user.Id, email, ip, LoginOutcome.Failed, "wrong_password", now, ct);
            await audit.RecordNowAsync(LoginDenied(user, email, "wrong_password"), ct);
            throw BadCredentials;
        }
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLogins, 0).SetProperty(u => u.LockedUntil, (DateTimeOffset?)null), ct);
        await attempts.ClearAccountAddressAsync(Surface, user.Id, ip, ct);
        // ADR-0016: the address is checked only after the right password, so a guesser learns nothing of the account or its rules.
        var ipAccess = await network.DecideAsync(user.Id, ip, ct);
        if (ipAccess == IpDecision.Denied)
        {
            await attempts.RecordAsync(Surface, user.Id, email, ip, LoginOutcome.Blocked, "ip_not_allowed", now, ct);
            await audit.RecordNowAsync(NetworkAccessService.DeniedEvent(user, ip, "login"), ct);
            throw NetworkAccessService.Denied(ip);
        }
        await attempts.RecordAsync(Surface, user.Id, email, ip, LoginOutcome.Succeeded, "password", now, ct);
        db.Users.Entry(user).State = EntityState.Detached;
        user = await db.Users.FirstAsync(u => u.Id == user.Id, ct);
        if (rehash) user.PasswordHash = hasher.Hash(cmd.Password!);
        user.LastLoginAt = now;
        // D11: أدوار الأمان الثابتة دائمًا، وأصحاب القرارات والصلاحيات الحساسة بحسب صلاحياتهم الفعلية حين يشغل المدير الأعلى المفتاح.
        var mfaRequired = options.Value.RequireMfa
            && (user.MfaEnabled || MfaPolicy.Requires(await access.GetAsync(user.Id, ct), options.Value, await settings.MfaForDecisionsAsync(ct)));
        var (token, session) = await sessions.CreateAsync(user, mfaRequired, ct);
        audit.Record(AuditEvents.Security("auth.login", Features.Users, "تسجيل دخول", "Signed in") with
        {
            Target = new AuditTarget("User", user.Id.ToString(), user.Email),
            Details = new { mfa = mfaRequired, ipAccess = IpAllowlist.Name(ipAccess) },
            ActorOverride = user.Id,
            ActorName = Lang.Name(user.FullNameAr, user.FullNameEn),
        });
        await db.SaveChangesAsync(ct);
        // من فعّل التحقق بخطوتين يتحقق أولًا ثم يغير الكلمة المؤقتة؛ تغييرها لا يقبل من جلسة لم تتحقق.
        var next = mfaRequired && user.MfaEnabled ? "mfa_verify"
            : user.MustChangePassword ? "change_password"
            : !mfaRequired ? "done" : "mfa_enroll";
        return new LoginResult(token, session.ExpiresAt, next);
    }

    /// <summary>يعيد رمز جلسة جديدًا: الرمز قبل التحقق لا يصير رمزًا كامل الصلاحية.</summary>
    public async Task<string> VerifyMfaAsync(string code, CancellationToken ct)
    {
        var (user, sessionId) = await CurrentAsync(ct);
        if (!user.MfaEnabled || user.MfaSecretProtected is null)
            throw new DomainException("mfa_not_enrolled", "لم يفعل التحقق بخطوتين لهذا الحساب بعد.", "Two-step verification is not set up for this account yet.");
        await AcceptCodeAsync(user, code, "auth.mfa_verify", "تحقق بخطوتين", "Verified a two-step code", ct);
        await sessions.MarkMfaVerifiedAsync(sessionId, ct);
        return await sessions.RotateAsync(sessionId, ct);
    }

    public async Task<MfaSetup> BeginMfaSetupAsync(CancellationToken ct)
    {
        var (user, _) = await CurrentAsync(ct);
        if (user.MfaEnabled)
            throw new DomainException("mfa_enabled", "التحقق بخطوتين مفعل بالفعل. إعادة ضبطه تتم عبر مسؤول الحسابات.", "Two-step verification is already on. Ask an administrator to reset it.", 409);
        var secret = Totp.NewSecret();
        user.MfaSecretProtected = protector.Protect(secret);
        await db.SaveChangesAsync(ct);
        var uri = Totp.OtpAuthUri(options.Value.TotpIssuer, user.Email, secret);
        return new MfaSetup(secret, uri, qr.PngDataUri(uri, 5));
    }

    public async Task<string> EnableMfaAsync(string code, CancellationToken ct)
    {
        var (user, sessionId) = await CurrentAsync(ct);
        if (user.MfaEnabled || user.MfaSecretProtected is null)
            throw new DomainException("mfa_setup_missing", "ابدأ إعداد التحقق بخطوتين أولًا.", "Start the two-step setup first.", 409);
        await AcceptCodeAsync(user, code, "auth.mfa_enable", "تفعيل التحقق بخطوتين", "Enabled two-step verification", ct, enabling: true);
        await sessions.MarkMfaVerifiedAsync(sessionId, ct);
        return await sessions.RotateAsync(sessionId, ct);
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        if (current.SessionId is { } sid)
        {
            await sessions.RevokeAsync(sid, ct);
            audit.Record(AuditEvents.Security("auth.logout", Features.Users, "تسجيل خروج", "Signed out") with
            {
                Target = new AuditTarget("User", current.UserId?.ToString()),
            });
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task<int> LogoutAllAsync(CancellationToken ct)
    {
        var uid = current.UserId ?? throw DomainException.Forbidden();
        var count = await sessions.RevokeAllAsync(uid, ct);
        audit.Record(AuditEvents.Security("auth.logout_all", Features.Users, "إنهاء كل الجلسات", "Ended all sessions") with
        {
            Target = new AuditTarget("User", uid.ToString()),
            Details = new { sessions = count },
        });
        await db.SaveChangesAsync(ct);
        return count;
    }

    /// <summary>يغير الكلمة ويعيد رمزًا جديدًا للجلسة الحالية، وينهي غيرها.</summary>
    public async Task<string> ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct)
    {
        // tx-exempt: attempt counters commit on their own, before the check, so parallel or failing requests cannot undo them (BE-4).
        var (user, sessionId) = await CurrentAsync(ct);
        // جلسة تنتظر التحقق بخطوتين لا تغير كلمة المرور (وإلا أنهت جلسات المالك بكلمة مرور مسروقة وحدها).
        var session = await db.Sessions.AsNoTracking().FirstAsync(s => s.Id == sessionId, ct);
        if (session.MfaRequired && session.MfaVerifiedAt is null && user.MfaEnabled) throw DomainException.Forbidden();
        // حجز المحاولة ذريًا كالدخول: جلسة مسروقة لا تخمن كلمة المرور الحالية بلا حد.
        var now = clock.UtcNow;
        var max = options.Value.MaxFailedLoginsPerAccount;
        if (await attempts.AccountAddressBlockedAsync(Surface, user.Id, current.Ip, now, ct)) throw BadCredentials;
        var reserved = await db.Users.Where(u => u.Id == user.Id && (u.LockedUntil == null || u.LockedUntil <= now) && u.FailedLogins < max)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLogins, u => u.FailedLogins + 1), ct);
        if (reserved == 0 || user.PasswordHash is null || !hasher.Verify(user.PasswordHash, currentPassword ?? "").Valid)
        {
            await CountFailureAsync(user, user.Email, current.Ip, now, ct);
            await attempts.RecordAsync(Surface, user.Id, user.Email, current.Ip, LoginOutcome.Failed, "change_password", now, ct);
            var failed = await db.Users.Where(u => u.Id == user.Id).Select(u => u.FailedLogins).FirstAsync(ct);
            if (reserved == 0 || failed >= max)
            {
                await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.LockedUntil, now.AddMinutes(options.Value.LockoutMinutes)).SetProperty(u => u.FailedLogins, 0), ct);
                await sessions.RevokeAllAsync(user.Id, ct);
            }
            await audit.RecordNowAsync(AuditEvents.Security("auth.change_password", Features.Users, "فشل تغيير كلمة المرور", "Failed to change a password") with
            {
                Outcome = "denied",
                Target = new AuditTarget("User", user.Id.ToString()),
            }, ct);
            throw BadCredentials;
        }
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedLogins, 0), ct);
        await db.Users.Entry(user).ReloadAsync(ct);
        PasswordPolicy.Ensure(newPassword, user.Email);
        user.PasswordHash = hasher.Hash(newPassword);
        user.MustChangePassword = false;
        audit.Record(AuditEvents.Security("auth.change_password", Features.Users, "تغيير كلمة المرور", "Changed a password") with
        {
            Target = new AuditTarget("User", user.Id.ToString()),
        });
        await db.SaveChangesAsync(ct);
        await sessions.InvalidateAsync(sessionId, ct);
        foreach (var other in await db.Sessions.Where(s => s.UserId == user.Id && s.RevokedAt == null && s.Id != sessionId).Select(s => s.Id).ToListAsync(ct))
            await sessions.RevokeAsync(other, ct);
        return await sessions.RotateAsync(sessionId, ct);
    }

    private AttemptLimit AccountAddressLimit =>
        new(options.Value.MaxFailedLogins, TimeSpan.FromMinutes(options.Value.LockoutMinutes), TimeSpan.FromMinutes(options.Value.LockoutMinutes));

    private AttemptLimit AddressLimit =>
        new(options.Value.MaxFailedLoginsPerAddress, TimeSpan.FromMinutes(options.Value.AddressWindowMinutes), TimeSpan.FromMinutes(options.Value.AddressBlockMinutes));

    /// <summary>Counts a failure for the account at this address and for the address across accounts, and audits a block it set.</summary>
    private async Task CountFailureAsync(AppUser? user, string email, string? ip, DateTimeOffset now, CancellationToken ct)
    {
        var result = await attempts.FailAsync(Surface, user?.Id, ip, AccountAddressLimit, AddressLimit, now, ct);
        if (result.AccountAddressBlocked && user is not null)
            await audit.RecordNowAsync(AuditEvents.Security("auth.locked", Features.Users, "حجب عنوان عن حساب بعد محاولات فاشلة", "Blocked an address from an account after failed attempts") with
            {
                Outcome = "denied",
                Target = new AuditTarget("User", user.Id.ToString(), user.Email),
                Details = new { scope = "address", minutes = options.Value.LockoutMinutes },
                ActorOverride = user.Id,
            }, ct);
        if (result.AddressBlocked)
            await audit.RecordNowAsync(AuditEvents.Security("auth.address_blocked", Features.Users, "حجب عنوان عن الدخول بعد محاولات فاشلة على حسابات كثيرة", "Blocked an address after failed sign-ins on many accounts") with
            {
                Outcome = "denied",
                Target = new AuditTarget("Address", ip, ip),
                Details = new { scope = "all_accounts", failures = options.Value.MaxFailedLoginsPerAddress, minutes = options.Value.AddressBlockMinutes, lastEmail = Mask.Email(email) },
            }, ct);
    }

    /// <summary>
    /// A refusal that answers like a wrong password and takes as long (a dummy hash), recorded in the attempt log and the security
    /// log. counts: whether it adds to the counters (an unknown email counts against its address; a refusal by a block does not).
    /// </summary>
    private async Task<DomainException> RefuseAsync(AppUser? user, string email, string reason, string? password, bool counts, CancellationToken ct)
    {
        hasher.Verify(_dummyHash ??= hasher.Hash("not-a-real-password"), password ?? "");
        var now = clock.UtcNow;
        if (counts) await CountFailureAsync(user, email, current.Ip, now, ct);
        await attempts.RecordAsync(Surface, user?.Id, email, current.Ip, counts ? LoginOutcome.Failed : LoginOutcome.Blocked, reason, now, ct);
        await audit.RecordNowAsync(LoginDenied(user, email, reason), ct);
        return BadCredentials;
    }

    private static AuditEntry LoginDenied(AppUser? user, string email, string reason) =>
        AuditEvents.Security("auth.login", Features.Users, "فشل تسجيل دخول", "Failed to sign in") with
        {
            Outcome = "denied",
            Target = new AuditTarget("User", user?.Id.ToString(), Mask.Email(email)),
            Details = new { reason },
            ActorOverride = user?.Id,
        };

    private async Task AcceptCodeAsync(AppUser user, string? code, string action, string messageAr, string messageEn, CancellationToken ct, bool enabling = false)
    {
        // tx-exempt: attempt counters commit on their own, before the check, so parallel or failing requests cannot undo them (BE-4).
        var now = clock.UtcNow;
        // حجز ذري للمحاولة قبل التحقق: مقفل أو مستنفد ⇒ رفض دون مقارنة (لا تخمين لرمز من ستة أرقام بجلسة مسروقة).
        var maxMfa = options.Value.MaxMfaFailures;
        var reserved = await db.Users.Where(u => u.Id == user.Id && (u.MfaLockedUntil == null || u.MfaLockedUntil <= now) && u.FailedMfa < maxMfa)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedMfa, u => u.FailedMfa + 1), ct);
        var target = new AuditTarget("User", user.Id.ToString());
        if (reserved == 0)
        {
            await db.Users.Where(u => u.Id == user.Id && u.FailedMfa >= maxMfa).ExecuteUpdateAsync(s => s.SetProperty(u => u.MfaLockedUntil, now.AddMinutes(options.Value.MfaLockoutMinutes)).SetProperty(u => u.FailedMfa, 0), ct);
            await audit.RecordNowAsync(AuditEvents.Security(action, Features.Users, "قفل التحقق بخطوتين", "Locked two-step verification") with { Outcome = "denied", Target = target }, ct);
            throw new DomainException("mfa_locked", "تجاوزت عدد محاولات رمز تطبيق المصادقة. حاول بعد قليل.", "Too many wrong codes. Try again later.", 423);
        }
        var secret = protector.Unprotect(user.MfaSecretProtected!) ?? throw new DomainException("mfa_unreadable", "تعذر قراءة إعداد التحقق بخطوتين. اطلب إعادة ضبطه.", "Two-step settings cannot be read. Ask for a reset.", 500);
        var step = Totp.Verify(secret, code, now, user.LastTotpStep);
        if (step is null)
        {
            await audit.RecordNowAsync(AuditEvents.Security(action, Features.Users, "رفض رمز تحقق", "Rejected a two-step code") with { Outcome = "denied", Target = target }, ct);
            throw new DomainException("invalid_code", "رمز تطبيق المصادقة غير صحيح أو مستخدم من قبل.", "The code is wrong or was already used.", 400) { Field = "code" };
        }
        // نجاح: إعادة العداد ذريًا ثم تحديث الخطوة المقبولة على نسخة حديثة (رمز التزامن يمنع قبول الرمز نفسه مرتين).
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.FailedMfa, 0), ct);
        await db.Users.Entry(user).ReloadAsync(ct);
        if (step.Value <= user.LastTotpStep) throw new DomainException("invalid_code", "رمز تطبيق المصادقة غير صحيح أو مستخدم من قبل.", "The code is wrong or was already used.", 400) { Field = "code" };
        user.LastTotpStep = step.Value;
        if (enabling) user.MfaEnabled = true;
        audit.Record(AuditEvents.Security(action, Features.Users, messageAr, messageEn) with { Target = target });
        await db.SaveChangesAsync(ct);
    }

    private async Task<(AppUser User, Guid SessionId)> CurrentAsync(CancellationToken ct)
    {
        var uid = current.UserId ?? throw DomainException.Forbidden();
        var sid = current.SessionId ?? throw DomainException.Forbidden();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == uid && u.Active, ct) ?? throw DomainException.Forbidden();
        return (user, sid);
    }
}

public static class PasswordPolicy
{
    public static void Ensure(string password, string email)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 12)
            throw new DomainException("weak_password", "كلمة المرور يجب ألا تقل عن 12 حرفًا.", "The password must be at least 12 characters.", 400) { Field = "newPassword" };
        if (password.Length > 128)
            throw new DomainException("weak_password", "كلمة المرور أطول من المسموح.", "The password is too long.", 400) { Field = "newPassword" };
        var local = email.Split('@')[0];
        if (local.Length >= 4 && password.Contains(local, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("weak_password", "لا تضمّن بريدك في كلمة المرور.", "Do not include your email in the password.", 400) { Field = "newPassword" };
        if (password.Distinct().Count() < 6)
            throw new DomainException("weak_password", "كلمة المرور متكررة الأحرف أكثر من اللازم.", "The password repeats too few characters.", 400) { Field = "newPassword" };
    }
}
