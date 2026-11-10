using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Application.Notifications;
using Tier1.Domain.Logging;
using Tier1.Domain.Notifications;
using Tier1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Tier1.Infrastructure.Email;

public sealed class MailSignal : IMailSignal, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // إشارة معلقة بالفعل.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>
/// عامل إرسال البريد: يحجز دفعة بتحديث شرطي (FOR UPDATE SKIP LOCKED) فلا ترسل نسختان الرسالة نفسها، ويرسلها، ثم يمحو نصها
/// ويقنّع عنوانها. الفشل يعاد بتأخير متزايد حتى ثماني محاولات. كل إرسال أو فشل حدث في سجل التكاملات بلا نص ولا رابط.
/// </summary>
public sealed class MailDispatcher(IServiceScopeFactory scopes, IEmailSender sender, MailSignal signal, ILogger<MailDispatcher> log) : BackgroundService
{
    public const int MaxAttempts = 8;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                handled = await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Mail dispatch cycle failed.");
            }
            if (handled > 0) continue;
            try
            {
                await signal.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<ISecretProtector>();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var now = DateTimeOffset.UtcNow;
        // عشر رسائل بمهلة 45 ثانية لكل منها تنتهي قبل القفل، فلا تلتقطها نسخة أخرى أثناء إرسالها.
        var until = now.AddMinutes(10);
        var ids = await db.Database.SqlQuery<long>($"""
            UPDATE adm.mail_outbox SET locked_until = {until}, attempts = attempts + 1
            WHERE id IN (SELECT id FROM adm.mail_outbox
                         WHERE sent_at IS NULL AND failed_at IS NULL AND cancelled_at IS NULL AND skipped_reason IS NULL
                           AND next_attempt_at <= {now} AND (locked_until IS NULL OR locked_until < {now})
                         ORDER BY id LIMIT 10 FOR UPDATE SKIP LOCKED)
            RETURNING id AS "Value"
            """).ToListAsync(ct);
        if (ids.Count == 0) return 0;
        var mails = await db.OutgoingMails.Where(m => ids.Contains(m.Id)).OrderBy(m => m.Id).ToListAsync(ct);
        foreach (var m in mails)
        {
            var masked = Mask.Email(m.To);
            try
            {
                var body = protector.Unprotect(m.BodyProtected) ?? throw new InvalidOperationException("The mail body cannot be decrypted.");
                await sender.SendAsync([m.To], m.Subject, body, ct);
                m.SentAt = DateTimeOffset.UtcNow;
                m.BodyProtected = "";
                m.To = masked ?? "";
                m.LockedUntil = null;
                m.LastError = null;
                // الإرسال يثبت قبل السجل: تعذر كتابة السجل لا يعيد إرسال رسالة وصلت.
                await db.SaveChangesAsync(ct);
                try
                {
                    await audit.RecordNowAsync(Event(m, "success", masked, null), ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    log.LogWarning(e, "Mail {Id} was sent but its log entry failed.", m.Id);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                var error = e.GetType().Name + ": " + (e.Message.Length > 400 ? e.Message[..400] : e.Message);
                m.LastError = error;
                m.LockedUntil = null;
                if (m.Attempts >= MaxAttempts)
                {
                    // استنفدت المحاولات: لا يبقى رابط صالح مخزنًا. إشعار المستفيد بلا روابط سرية يبقى نصه مشفرًا لإعادة إرساله من السجل.
                    m.FailedAt = DateTimeOffset.UtcNow;
                    if (m.Audience != MailAudience.Beneficiary) m.BodyProtected = "";
                }
                else
                {
                    m.NextAttemptAt = DateTimeOffset.UtcNow.AddSeconds(Math.Min(3600, 15 * Math.Pow(2, m.Attempts - 1)));
                }
                log.LogWarning("Sending mail {Id} failed (attempt {Attempt}): {Error}", m.Id, m.Attempts, error);
                await audit.RecordNowAsync(Event(m, "failure", masked, error), ct);
            }
            await db.SaveChangesAsync(ct);
        }
        return mails.Count;
    }

    private static AuditEntry Event(OutgoingMail m, string outcome, string? masked, string? error) =>
        new(LogCollections.Integration, "adm", "sup.notify", "mail.send", "إرسال بريد", "Sent an email")
        {
            Outcome = outcome,
            Target = new AuditTarget("Mail", m.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), masked),
            Related = m.RelatedRef is null ? [] : [new AuditTarget(m.RelatedType ?? "Related", m.RelatedId?.ToString(), m.RelatedRef)],
            Details = new { kind = m.Kind, template = m.TemplateKey, attempt = m.Attempts, error, gaveUp = m.FailedAt is not null },
        };
}
