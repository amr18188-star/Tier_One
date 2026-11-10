namespace Tier1.Domain.Logging;

/// <summary>
/// صف في الصندوق الصادر: يكتب في معاملة العملية نفسها، ثم ينقله عامل الإرسال إلى MongoDB.
/// الحمولة نص لا jsonb، لأن jsonb يعيد ترتيب المفاتيح.
/// </summary>
public sealed class OutboxEvent
{
    public long Id { get; set; }

    public Guid EventId { get; set; }

    /// <summary>المجموعة الهدف في MongoDB، وهي اسم السلسلة أيضًا.</summary>
    public string Collection { get; set; } = "";

    public string Payload { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// موضع الحدث في سلسلته. يحجز ويحفظ في PostgreSQL قبل الإرسال (المرحلة الأولى)، ثم يرسل الحدث بالقيم نفسها
    /// (المرحلة الثانية). إعادة الإرسال بعد عطل تنتج الوثيقة نفسها حرفيًا، فلا تنكسر السلسلة ولا يتكرر حدث.
    /// </summary>
    public long? ChainSeq { get; set; }

    public string? PrevHash { get; set; }

    public string? Hash { get; set; }

    /// <summary>خوارزمية البصمة ومعرف المفتاح («hmac-sha256:keyId»)، أو null للأحداث السابقة للمفتاح.</summary>
    public string? HashAlg { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

/// <summary>آخر رقم تسلسل وبصمة في كل سلسلة؛ يتقدم في معاملة واحدة مع تعليم الأحداث مرسلة.</summary>
public sealed class ChainState
{
    public string Chain { get; set; } = "";

    public long Seq { get; set; }

    public string Hash { get; set; } = "";

    /// <summary>
    /// آخر يوم مرساة في السلسلة. يوم الحدث لا يرجع إلى الخلف: حدث مؤرخ قبل منتصف الليل ويحجز بعده يدخل اليوم الجديد،
    /// فتبقى أرقام كل يوم متصلة ولا يتداخل يومان.
    /// </summary>
    public DateOnly? LastDay { get; set; }

    /// <summary>
    /// أول رقم بُصم بالمفتاح. ما بعده لا يقبل بصمة بلا مفتاح، فلا يمر «خفض» حدث إلى SHA-256 لإعادة بنائه.
    /// </summary>
    public long? HmacFromSeq { get; set; }
}

/// <summary>
/// نقطة تثبيت: آخر رقم وبصمة في سلسلة لحظة الفحص، موقعة بالمفتاح ومنشورة خارج المنظومة
/// (ملف إلحاق على خادم آخر وبريد المراجع الرقابي). الفحص يقارن السلسلة بها.
/// </summary>
public sealed class ChainCheckpoint
{
    public long Id { get; set; }

    public string Chain { get; set; } = "";

    public long Seq { get; set; }

    public string Hash { get; set; } = "";

    public DateTimeOffset At { get; set; }

    public string Signature { get; set; } = "";

    /// <summary>أين نُشرت: file وemail.</summary>
    public string PublishedTo { get; set; } = "";
}

/// <summary>مرساة يومية: آخر بصمة في السلسلة لكل يوم (بتوقيت القاهرة) ونتيجة الفحص.</summary>
public sealed class ChainAnchor
{
    public string Chain { get; set; } = "";

    public DateOnly Day { get; set; }

    public long FirstSeq { get; set; }

    public long LastSeq { get; set; }

    public string LastHash { get; set; } = "";

    public int EventCount { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? VerifiedAt { get; set; }

    /// <summary>null لم يفحص، true سليم، false مكسور.</summary>
    public bool? Intact { get; set; }

    public long? BrokenAtSeq { get; set; }

    public string? VerifyNote { get; set; }
}

/// <summary>أسماء المجموعات؛ الأربع الأولى سلاسل عبر الصندوق الصادر، والباقي كتابة مباشرة بلا سلسلة.</summary>
public static class LogCollections
{
    public const string Audit = "audit_events";
    public const string Security = "security_events";
    public const string Access = "access_events";
    public const string Integration = "integration_events";
    public const string JobRuns = "job_runs";
    public const string HttpRequests = "http_requests";
    public const string AppErrors = "app_errors";

    public static readonly IReadOnlyList<string> Chained = [Audit, Security, Access, Integration];
}
