namespace Tier1.Domain.Common;

/// <summary>
/// خطأ قاعدة عمل برسالة ثنائية اللغة ورمز ثابت تتعامل معه الواجهة. الحالة الافتراضية 422.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string code, string messageAr, string messageEn, int status = 422)
        : base(Lang.Pick(messageAr, messageEn))
    {
        Code = code;
        Status = status;
    }

    public DomainException()
        : this("error", "حدث خطأ.", "An error occurred.")
    {
    }

    public DomainException(string message)
        : this("error", message, message)
    {
    }

    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = "error";
        Status = 422;
    }

    public string Code { get; } = "error";

    public int Status { get; } = 422;

    /// <summary>السجل خارج نطاق المستخدم يعامل كأنه غير موجود، فلا تتسرب معلومة عن وجوده.</summary>
    public static DomainException NotFound() =>
        new("not_found", "العنصر غير موجود أو لا تملك صلاحية الاطلاع عليه.", "Not found, or you are not allowed to see it.", 404);

    public static DomainException Forbidden() =>
        new("forbidden", "ليست لديك صلاحية لهذا الإجراء.", "You are not allowed to do this.", 403);

    public static DomainException Conflict() =>
        new("conflict", "عدّل مستخدم آخر هذا العنصر. حدّث الصفحة ثم أعد المحاولة.", "Someone else changed this item. Refresh and try again.", 409);

    /// <summary>رفض بقاعدة فصل ثابتة (SOD-n) لا تتجاوزها المصفوفة.</summary>
    public static DomainException SeparationOfDuties(string rule, string messageAr, string messageEn) =>
        new("sod_" + rule.ToLowerInvariant().Replace('-', '_'), messageAr, messageEn, 403);
}
