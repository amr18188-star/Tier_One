namespace Tier1.Domain.Identity;

/// <summary>
/// الرقم القومي المصري (ADR-0021): 14 رقمًا. الأول القرن (2 لمواليد 1900–1999، 3 لمواليد 2000–2099)، ثم تاريخ الميلاد سنة وشهرًا
/// ويومًا، ثم رمز المحافظة، ثم أربعة أرقام مسلسلة، ثم رقم التحقق. يفحص الشكل وتاريخ الميلاد ورمز المحافظة؛ رقم التحقق لا يفحص
/// لأن طريقة حسابه غير منشورة رسميًا.
/// </summary>
public static class NationalIds
{
    public const int Length = 14;

    /// <summary>رموز المحافظات في الرقم القومي؛ 88 لمن ولد خارج الجمهورية.</summary>
    private static readonly HashSet<string> Governorates =
    [
        "01", "02", "03", "04", "11", "12", "13", "14", "15", "16", "17", "18", "19",
        "21", "22", "23", "24", "25", "26", "27", "28", "29", "31", "32", "33", "34", "35", "88",
    ];

    /// <summary>الأرقام فقط بالأرقام اللاتينية (تقبل الأرقام الهندية والمسافات والشرطات عند الكتابة)؛ أو لا شيء إن لم يكتب شيء.</summary>
    public static string? Normalize(string? value)
    {
        var digits = PersonalText.Digits(value);
        return digits.Length == 0 ? null : digits;
    }

    /// <param name="id">الرقم بعد <see cref="Normalize"/>.</param>
    /// <param name="today">تاريخ اليوم: تاريخ الميلاد لا يكون بعده.</param>
    public static bool IsValid(string id, DateOnly today)
    {
        if (id.Length != Length || id.Any(c => c is < '0' or > '9')) return false;
        var century = id[0] switch { '2' => 1900, '3' => 2000, _ => 0 };
        if (century == 0) return false;
        var (year, month, day) = (century + int.Parse(id.AsSpan(1, 2)), int.Parse(id.AsSpan(3, 2)), int.Parse(id.AsSpan(5, 2)));
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        return new DateOnly(year, month, day) <= today && Governorates.Contains(id.Substring(7, 2));
    }

    /// <summary>ما يعرض ويسجل بعد الإدخال: آخر أربعة أرقام فقط. الرقم الكامل لا يخرج من الخادم.</summary>
    public static string Mask(string id) => id.Length <= 4 ? new string('*', id.Length) : new string('*', id.Length - 4) + id[^4..];
}

/// <summary>رقم المحمول المصري (ADR-0021): 11 رقمًا تبدأ بـ 010 أو 011 أو 012 أو 015، ويحفظ بهذا الشكل.</summary>
public static class MobileNumbers
{
    public const int MaxLength = 20;

    /// <summary>الرقم بشكله المحفوظ (01XXXXXXXXX)، ويقبل عند الكتابة +20 و0020 والمسافات والشرطات والأرقام الهندية؛ أو لا شيء إن لم يكن رقم محمول مصريًا.</summary>
    public static string? Normalize(string? value)
    {
        var digits = PersonalText.Digits(value);
        if (digits.StartsWith("0020", StringComparison.Ordinal)) digits = "0" + digits[4..];
        else if (digits.Length == 12 && digits.StartsWith("20", StringComparison.Ordinal)) digits = "0" + digits[2..];
        return digits.Length == 11 && digits[0] == '0' && digits[1] == '1' && digits[2] is '0' or '1' or '2' or '5' ? digits : null;
    }
}

/// <summary>الاسم بالإنجليزية (ADR-0021): بحروف لاتينية، بلا حروف عربية.</summary>
public static class EnglishNames
{
    public static bool IsValid(string name) =>
        name.Any(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') && !name.Any(c => c is >= '؀' and <= 'ۿ');
}

internal static class PersonalText
{
    /// <summary>أرقام النص بالأرقام اللاتينية، بأي شكل كتبت (٠١٢… أو 012…)، دون ما عداها.</summary>
    public static string Digits(string? value) =>
        new((value ?? "").Where(char.IsDigit).Select(c => (char)('0' + (int)char.GetNumericValue(c))).ToArray());
}
