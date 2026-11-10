using System.Security.Cryptography;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Tier1.Application.Common;

/// <summary>التحقق من المدخلات النصية المشتركة: السبب المكتوب للإجراءات المسجلة والنصوص الاختيارية، بحد أدنى وأقصى يطابق أعمدة القاعدة.</summary>
public static class Require
{
    public static string Reason(string? text, int min = 5, string code = "reason_required", int max = 500)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Length < min)
            throw new DomainException(code, $"اكتب سببًا واضحًا ({min} أحرف على الأقل).", $"Enter a clear reason (at least {min} characters).", 400);
        return Max(text.Trim(), max);
    }

    public static string? Optional(string? text, int max = 1000) => string.IsNullOrWhiteSpace(text) ? null : Max(text.Trim(), max);

    private static string Max(string text, int max) => text.Length <= max ? text
        : throw new DomainException("text_too_long", $"النص أطول من {max} حرف. اختصره ثم أعد المحاولة.", $"The text is longer than {max} characters. Shorten it and retry.", 400);
}

/// <summary>رموز عشوائية بلا التباس (بلا I وL وO وU): بطاقة الدخول والتحقق من المستند.</summary>
public static class Codes
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    public static string Random(int length)
    {
        Span<byte> bytes = stackalloc byte[length];
        RandomNumberGenerator.Fill(bytes);
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = Alphabet[bytes[i] & 31];
        return new string(chars);
    }

    /// <summary>توحيد الرمز كما يكتبه الإنسان: أحرف كبيرة بلا شرطات أو مسافات، وO→0 وI/L→1.</summary>
    public static string Normalize(string? code) =>
        new((code ?? "").ToUpperInvariant().Where(c => c is not ('-' or ' ')).Select(c => c switch { 'O' => '0', 'I' or 'L' => '1', _ => c }).ToArray());

    public static bool IsWellFormed(string normalized, int length) => normalized.Length == length && normalized.All(c => Alphabet.Contains(c, StringComparison.Ordinal));

    /// <summary>عرض الرمز في مجموعات من أربعة.</summary>
    public static string Pretty(string code) => string.Join('-', Enumerable.Range(0, (code.Length + 3) / 4).Select(i => code.Substring(i * 4, Math.Min(4, code.Length - i * 4))));
}

/// <summary>أسماء المستخدمين بمعرفاتهم لعرضها (المراجع، المترجم، المدرب...)، باستعلام واحد.</summary>
public static class People
{
    public static async Task<Dictionary<Guid, PersonDto>> LookupAsync(IAppDb db, IEnumerable<Guid> ids, CancellationToken ct)
    {
        var list = ids.Distinct().ToList();
        return await db.Users.AsNoTracking().Where(u => list.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => new PersonDto(u.Id, Lang.Name(u.FullNameAr, u.FullNameEn)), ct);
    }
}

/// <summary>
/// التعديل المتزامن: الشاشة ترسل إصدار الصف الذي فتحته، فإن عدّله غيرها بعد ذلك يرد الحفظ 409 «عدّل مستخدم آخر هذا العنصر»
/// بدل أن يمحو تعديله. الإصدار اختياري ليبقى التكامل القديم يعمل.
/// </summary>
public static class Concurrency
{
    public static void Expect<TEntity>(IAppDb db, TEntity entity, uint? version) where TEntity : class
    {
        if (version is { } v) db.Entry(entity).Property("Version").OriginalValue = v;
    }
}

/// <summary>الموعد بتوقيت القاهرة كما يكتب في أحداث الطلبات والسجل.</summary>
public static class CairoText
{
    public static string Format(DateTimeOffset utc) => CairoTime.ToCairo(utc).ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}
