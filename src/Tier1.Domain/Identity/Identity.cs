namespace Tier1.Domain.Identity;

/// <summary>حساب موظف. حسابات المستفيدين تضاف مع بوابة المستفيد في المرحلة الأولى.</summary>
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>البريد بعد التطبيع (حروف صغيرة بلا مسافات)، فريد.</summary>
    public string Email { get; set; } = "";

    public string FullNameAr { get; set; } = "";

    public string? FullNameEn { get; set; }

    public bool Active { get; set; } = true;

    public string? PasswordHash { get; set; }

    public bool MustChangePassword { get; set; }

    public bool MfaEnabled { get; set; }

    /// <summary>سر TOTP مشفر بمفاتيح Data Protection خارج القاعدة.</summary>
    public string? MfaSecretProtected { get; set; }

    /// <summary>آخر خطوة TOTP مقبولة؛ تمنع إعادة استخدام الرمز نفسه.</summary>
    public long LastTotpStep { get; set; }

    public int FailedLogins { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public int FailedMfa { get; set; }

    public DateTimeOffset? MfaLockedUntil { get; set; }

    public string PreferredLanguage { get; set; } = "ar";

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>رقم إصدار للتزامن المتفائل (xmin في PostgreSQL).</summary>
    public uint Version { get; set; }
}

/// <summary>جلسة معتمة: القاعدة تحفظ بصمة الرمز فقط.</summary>
public sealed class UserSession
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid UserId { get; set; }

    public string TokenHash { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public bool MfaRequired { get; set; }

    public DateTimeOffset? MfaVerifiedAt { get; set; }

    public DateTimeOffset? RevokedAt { get; set; }

    public string? Ip { get; set; }

    public string? UserAgent { get; set; }
}
