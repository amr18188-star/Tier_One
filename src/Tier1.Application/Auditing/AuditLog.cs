using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Domain.Logging;

namespace Tier1.Application.Auditing;

public sealed record AuditTarget(string Type, string? Id, string? Ref = null, string? OwnerId = null);

/// <summary>تغير حقل واحد. القيم نصوص مقنعة مسبقًا عند الحساسية (<see cref="Mask"/>).</summary>
public sealed record FieldChange(string Field, string? Old, string? New);

/// <summary>
/// حدث سجل. الرسالة فعل ثم اسم بالعربية والإنجليزية: «اعتماد نتيجة امتحان» / «Approved an exam result».
/// </summary>
public sealed record AuditEntry(string Collection, string Module, string Feature, string Action, string MessageAr, string MessageEn)
{
    public string Outcome { get; init; } = "success";

    public string? Permission { get; init; }

    public AuditTarget? Target { get; init; }

    public IReadOnlyList<AuditTarget>? Related { get; init; }

    public IReadOnlyList<FieldChange>? Changes { get; init; }

    public string? Reason { get; init; }

    public object? Details { get; init; }

    /// <summary>للدخول قبل وجود جلسة: الحساب المستهدف بدل الفاعل المجهول.</summary>
    public Guid? ActorOverride { get; init; }

    /// <summary>اسم الفاعل حين لا جلسة بعد (الدخول الناجح).</summary>
    public string? ActorName { get; init; }
}

public interface IAuditLog
{
    /// <summary>يضيف الحدث إلى الصندوق الصادر في المعاملة الجارية؛ يحفظ مع SaveChanges للعملية نفسها أو لا يحفظ.</summary>
    void Record(AuditEntry entry);

    /// <summary>يحفظ الحدث فورًا بمعزل عن المعاملة الجارية (رفض، فشل دخول، اطلاع)، فلا يضيع إن فشلت العملية.</summary>
    Task RecordNowAsync(AuditEntry entry, CancellationToken ct);
}

/// <summary>كتابة صف الصندوق الصادر مباشرة دون المرور بتتبع EF.</summary>
public interface IOutboxDirectWriter
{
    Task InsertAsync(OutboxEvent e, CancellationToken ct);
}

/// <summary>إشعار عامل الإرسال بوجود أحداث جديدة دون انتظار دورته التالية.</summary>
public interface IOutboxSignal
{
    void Notify();
}

public sealed class AuditLog(IAuditStore db, IClock clock, ICurrentUser current, IAccessCache accessCache, IOutboxDirectWriter direct, IOutboxSignal signal) : IAuditLog
{
    private static readonly string AppVersion =
        typeof(AuditLog).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";

    public void Record(AuditEntry entry)
    {
        db.Outbox.Add(Build(entry));
        signal.Notify();
    }

    public async Task RecordNowAsync(AuditEntry entry, CancellationToken ct)
    {
        // لا يلغى بقطع العميل اتصاله: فشل الدخول والرفض يسجلان حتى لو أغلق المهاجم الاتصال فور الإرسال.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await direct.InsertAsync(Build(entry), timeout.Token);
        signal.Notify();
    }

    private OutboxEvent Build(AuditEntry e)
    {
        if (!LogCollections.Chained.Contains(e.Collection)) throw new ArgumentException($"'{e.Collection}' is not a chained log collection.", nameof(e));
        var id = Guid.CreateVersion7();
        var now = clock.UtcNow;
        return new OutboxEvent
        {
            EventId = id,
            Collection = e.Collection,
            CreatedAt = now,
            Payload = Serialize(id, now, e),
        };
    }

    private string Serialize(Guid id, DateTimeOffset ts, AuditEntry e)
    {
        using var buffer = new MemoryStream();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteString("_id", id.ToString());
            w.WriteString("ts", ts.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture));
            w.WriteString("module", e.Module);
            w.WriteString("feature", e.Feature);
            w.WriteString("action", e.Action);
            w.WriteString("permission", e.Permission);
            w.WriteString("outcome", e.Outcome);
            w.WriteString("messageAr", e.MessageAr);
            w.WriteString("messageEn", e.MessageEn);

            var actorId = current.UserId ?? e.ActorOverride;
            w.WriteStartObject("actor");
            w.WriteString("userId", actorId?.ToString());
            w.WriteString("name", ActorNameFor(current.UserId, current.Channel, current.DisplayName, e.ActorName));
            w.WriteStartArray("roles");
            foreach (var r in actorId is { } uid ? accessCache.CachedRoleCodes(uid) : []) w.WriteStringValue(r);
            w.WriteEndArray();
            w.WriteString("type", current.Channel == "job" ? "system" : current.UserId is not null ? "user" : "anonymous");
            w.WriteEndObject();

            w.WritePropertyName("target");
            WriteTarget(w, e.Target);
            w.WriteStartArray("related");
            foreach (var r in e.Related ?? []) WriteTarget(w, r);
            w.WriteEndArray();
            w.WriteStartArray("changes");
            foreach (var c in e.Changes ?? [])
            {
                w.WriteStartObject();
                w.WriteString("field", c.Field);
                w.WriteString("old", c.Old);
                w.WriteString("new", c.New);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteString("reason", e.Reason);
            w.WritePropertyName("details");
            if (e.Details is null) w.WriteNullValue();
            else JsonSerializer.SerializeToElement(e.Details, JsonOptions).WriteTo(w);

            w.WriteStartObject("context");
            w.WriteString("ip", current.Ip);
            w.WriteString("userAgent", current.UserAgent is { Length: > 300 } ua ? ua[..300] : current.UserAgent);
            w.WriteString("sessionId", current.SessionId?.ToString());
            w.WriteString("correlationId", current.TraceId);
            w.WriteString("channel", current.Channel);
            w.WriteString("app", "tier1-api");
            w.WriteString("version", AppVersion);
            w.WriteEndObject();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteTarget(Utf8JsonWriter w, AuditTarget? t)
    {
        if (t is null)
        {
            w.WriteNullValue();
            return;
        }
        w.WriteStartObject();
        w.WriteString("type", t.Type);
        w.WriteString("id", t.Id);
        w.WriteString("ref", t.Ref);
        w.WriteString("ownerId", t.OwnerId);
        w.WriteEndObject();
    }

    /// <summary>
    /// اسم الفاعل في القيد: المستخدم إن وجد؛ وفي المهام والأوامر بلا مستخدم الاسم الذي سماه القيد (أمر الإنتاج باسم من وافق، D10)، وإلا «النظام».
    /// </summary>
    public static string? ActorNameFor(Guid? userId, string? channel, string? displayName, string? eventActorName) =>
        userId is null && channel == "job" ? eventActorName ?? "النظام" : displayName ?? eventActorName;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>إقناع القيم الحساسة قبل دخولها السجل. كلمات المرور والرموز لا تمر بالسجل أصلًا.</summary>
public static class Mask
{
    /// <summary>الرقم القومي أو الجواز: آخر 4 خانات فقط.</summary>
    public static string? Identity(string? value) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= 4 ? new string('*', value.Length) : new string('*', value.Length - 4) + value[^4..];

    public static string? Email(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var at = value.IndexOf('@', StringComparison.Ordinal);
        return at <= 1 ? "***" + value[Math.Max(at, 0)..] : value[0] + new string('*', at - 1) + value[at..];
    }
}

/// <summary>أحداث جاهزة متكررة؛ كل رسالة فعل ثم اسم.</summary>
public static class AuditEvents
{
    public static AuditEntry Security(string action, string feature, string messageAr, string messageEn) =>
        new(LogCollections.Security, "adm", feature, action, messageAr, messageEn);

    public static AuditEntry Audit(string module, string feature, string action, string messageAr, string messageEn) =>
        new(LogCollections.Audit, module, feature, action, messageAr, messageEn);

    public static AuditEntry Access(string module, string feature, string action, string messageAr, string messageEn) =>
        new(LogCollections.Access, module, feature, action, messageAr, messageEn);
}
