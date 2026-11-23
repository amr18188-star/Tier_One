namespace Tier1.Domain.Identity;

/// <summary>حساب موظف. حسابات المستفيدين تضاف مع بوابة المستفيد في المرحلة الأولى.</summary>
public sealed class AppUser
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>البريد بعد التطبيع (حروف صغيرة بلا مسافات)، فريد.</summary>
    public string Email { get; set; } = "";

    public string FullNameAr { get; set; } = "";

    public string? FullNameEn { get; set; }

    /// <summary>اللقب قبل الاسم (أ.د.، السيد، عقيد...) من <see cref="PersonTitles"/>، أو فارغ لحساب قديم بلا لقب (ADR-0019).</summary>
    public string? Title { get; set; }

    /// <summary>الاسم كما يعرض: اللقب ثم الاسم ثنائيًا؛ وبلا لقب الاسم الكامل.</summary>
    public string DisplayName => PersonName.Display(Title, FullNameAr, FullNameEn);

    /// <summary>الرقم القومي (14 رقمًا، فريد)، أو فارغ لحساب أنشئ قبل إلزام البيانات (ADR-0021). لا يخرج من الخادم إلا مقنّعًا.</summary>
    public string? NationalId { get; set; }

    /// <summary>رقم المحمول بشكله المحفوظ (01XXXXXXXXX)، أو فارغ لحساب أنشئ قبل إلزام البيانات (ADR-0021).</summary>
    public string? Phone { get; set; }

    /// <summary>ما ينقص الحساب من البيانات الإلزامية بأسماء حقولها (ADR-0021)؛ فارغة حين تكتمل.</summary>
    public IReadOnlyList<string> MissingData =>
    [
        .. string.IsNullOrWhiteSpace(FullNameEn) ? ["fullNameEn"] : Array.Empty<string>(),
        .. NationalId is null ? ["nationalId"] : Array.Empty<string>(),
        .. Phone is null ? ["phone"] : Array.Empty<string>(),
    ];

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

    /// <summary>Signs in and works only from the addresses of the global list or of this user's own list (iam.ip_allow_rules).</summary>
    public bool IpRestricted { get; set; }

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

/// <summary>
/// A shared sign-in counter (iam.login_counters): failures within a window for one key, and the block it earned. Keys are
/// "{surface}|acct|{user}|{address}" (one account from one address) and "{surface}|addr|{address}" (all accounts from one
/// address). Every API instance reads and writes the same row, so switching instances does not reset a count.
/// </summary>
public sealed class LoginCounter
{
    public string Key { get; set; } = "";

    public int Failures { get; set; }

    public DateTimeOffset WindowStart { get; set; }

    public DateTimeOffset? BlockedUntil { get; set; }
}

/// <summary>One sign-in attempt (iam.login_attempts): who (the account if known, else a hash of the typed email), from where, and how it ended.</summary>
public sealed class LoginAttempt
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public string Surface { get; set; } = "";

    public Guid? UserId { get; set; }

    /// <summary>SHA-256 of the normalised email typed, to see one address trying many emails without storing them.</summary>
    public string EmailHash { get; set; } = "";

    public string? Ip { get; set; }

    /// <summary>succeeded, failed or blocked.</summary>
    public string Outcome { get; set; } = "";

    public string Reason { get; set; } = "";
}

/// <summary>
/// One Idempotency-Key a user sent with a POST (iam.idempotency_keys, BE-8, ADR-0018). While <see cref="StatusCode"/> is null the
/// first request with the key is still running (or its outcome was never recorded); once set, the same request with the same key
/// gets this answer back instead of being done again. A key is its user's own, and is free again after the replay window.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid UserId { get; set; }

    public string Key { get; set; } = "";

    /// <summary>The path and query the key was first used on.</summary>
    public string Route { get; set; } = "";

    /// <summary>SHA-256 (hex) of the method, path, query and body: the same key with another request is refused.</summary>
    public string RequestHash { get; set; } = "";

    public int? StatusCode { get; set; }

    public string? ContentType { get; set; }

    public byte[]? Response { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
