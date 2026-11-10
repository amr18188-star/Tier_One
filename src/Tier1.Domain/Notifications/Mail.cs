namespace Tier1.Domain.Notifications;

/// <summary>
/// بريد في الصندوق الصادر: يحفظ مع العملية نفسها ويرسله عامل في الخلفية بإعادة المحاولة، فلا يضيع بتعطل خادم البريد.
/// النص مشفر لأنه قد يحمل رابط تأكيد أو استعادة، ويمحى بعد الإرسال ويقنّع العنوان.
/// إشعارات المستفيد (notify.*) تحمل ما يعرضه سجل الإرسال: المستفيد والقالب والطلب المرتبط، بلا النص.
/// </summary>
public sealed class OutgoingMail
{
    public long Id { get; set; }

    /// <summary>portal.verify أو portal.reset أو notify.* أو staff.digest.</summary>
    public string Kind { get; set; } = "";

    public string To { get; set; } = "";

    public string Subject { get; set; } = "";

    public string BodyProtected { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>موعد المحاولة التالية؛ في المستقبل للإشعار المجدول.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public int Attempts { get; set; }

    /// <summary>حجز مؤقت لعامل الإرسال، فلا ترسل نسختان من التطبيق الرسالة نفسها.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? SentAt { get; set; }

    /// <summary>توقف بعد استنفاد المحاولات.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>beneficiary أو staff أو account (رسائل حساب البوابة).</summary>
    public string Audience { get; set; } = MailAudience.Account;

    /// <summary>الوحدة لنطاق سجل الإرسال: req وexm وtrn وtrl وdoc، وptl وadm لرسائل الحساب والموظفين.</summary>
    public string Module { get; set; } = "ptl";

    public Guid? BeneficiaryId { get; set; }

    public string? TemplateKey { get; set; }

    public string? RelatedType { get; set; }

    public Guid? RelatedId { get; set; }

    /// <summary>رقم الطلب أو رمز الجلسة أو المجموعة للعرض والبحث.</summary>
    public string? RelatedRef { get; set; }

    /// <summary>يمنع تكرار الإشعار الدوري (تذكير الامتحان مثلًا)؛ فريد إن وجد.</summary>
    public string? DedupKey { get; set; }

    /// <summary>إشعار لمجموعة أو جلسة: كل رسائله تحمل الرقم نفسه فتلغى معًا.</summary>
    public Guid? BatchId { get; set; }

    public Guid? CreatedBy { get; set; }

    /// <summary>لم يرسل لسبب معروف (no_email: لا بريد للمستفيد، template_off: القالب موقوف).</summary>
    public string? SkippedReason { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    public Guid? CancelledBy { get; set; }

    public string? CancelReason { get; set; }

    /// <summary>الرسالة الفاشلة أو المتخطاة التي أعيد إرسالها بهذه.</summary>
    public long? ResentFromId { get; set; }
}

public static class MailAudience
{
    public const string Beneficiary = "beneficiary";
    public const string Staff = "staff";
    public const string Account = "account";
}

/// <summary>
/// قالب رسالة بالعربية والإنجليزية (cat.msgtpl). قوالب الأحداث مبذورة ولا تحذف بل توقف؛ القوالب المضافة للإشعارات اليدوية تحذف.
/// النص نص عادي فيه عناصر {{key}} تملأ عند الإرسال.
/// </summary>
public sealed class MessageTemplate
{
    public string Key { get; set; } = "";

    /// <summary>الوحدة صاحبة الحدث (exm، trn…) أو custom لقالب إشعار يدوي.</summary>
    public string Module { get; set; } = "";

    /// <summary>قالب حدث في المنظومة؛ لا يحذف.</summary>
    public bool EventBound { get; set; }

    public string NameAr { get; set; } = "";

    public string? NameEn { get; set; }

    public string SubjectAr { get; set; } = "";

    public string? SubjectEn { get; set; }

    public string BodyAr { get; set; } = "";

    public string? BodyEn { get; set; }

    /// <summary>موقوف: لا يرسل حدثه ويسجل متخطى في سجل الإرسال.</summary>
    public bool Active { get; set; } = true;

    /// <summary>إصدار النص؛ يزيد مع كل تعديل.</summary>
    public int TextVersion { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public uint Version { get; set; }
}
