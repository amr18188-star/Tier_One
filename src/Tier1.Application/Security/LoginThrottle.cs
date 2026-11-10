using Microsoft.Extensions.Caching.Memory;

namespace Tier1.Application.Security;

/// <summary>
/// إبطاء التخمين لكل (حساب، عنوان): بعد عدد من المحاولات الفاشلة من عنوان واحد يحجب ذلك العنوان عن ذلك الحساب مدة القفل،
/// فلا يقفل أحد حساب غيره من مكانه، ويبقى صاحبه يدخل من جهازه. قفل الحساب كله في القاعدة يبقى بحد أعلى لمن يخمن من عناوين كثيرة.
/// في الذاكرة لكل نسخة من التطبيق؛ الحد الأعلى في القاعدة يقيد النسخ معًا.
/// </summary>
public sealed class LoginThrottle(IMemoryCache cache)
{
    private sealed class Counter
    {
        public int Failures;
        public DateTimeOffset? BlockedUntil;
    }

    private static string Key(string surface, Guid account, string? ip) => $"throttle:{surface}:{account}:{ip ?? "unknown"}";

    public bool IsBlocked(string surface, Guid account, string? ip, DateTimeOffset now) =>
        cache.TryGetValue<Counter>(Key(surface, account, ip), out var c) && c is { BlockedUntil: { } until } && until > now;

    /// <summary>يسجل محاولة فاشلة، ويحجب العنوان عند بلوغ الحد. يرجع true إن حُجب الآن.</summary>
    public bool Fail(string surface, Guid account, string? ip, DateTimeOffset now, int max, TimeSpan block)
    {
        var key = Key(surface, account, ip);
        var c = cache.GetOrCreate(key, e =>
        {
            e.AbsoluteExpirationRelativeToNow = block;
            return new Counter();
        })!;
        lock (c)
        {
            if (c.BlockedUntil is { } until && until > now) return false;
            c.Failures++;
            if (c.Failures < max) return false;
            c.Failures = 0;
            c.BlockedUntil = now + block;
        }
        cache.Set(key, c, block);
        return true;
    }

    public void Reset(string surface, Guid account, string? ip) => cache.Remove(Key(surface, account, ip));
}
