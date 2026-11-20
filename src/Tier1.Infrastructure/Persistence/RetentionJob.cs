using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// الاحتفاظ بالبيانات: يمحو مرة كل ست ساعات ما انتهت حاجته ولا قيمة رقابية له: الجلسات المنتهية أو الملغاة، وروابط البريد المستعملة أو المنتهية،
/// ومحاولات صفحة التحقق العامة (فيها عناوين الزوار) بعد مدتها. ما يلزم الرقابة باق في سجلات MongoDB.
/// DB-7/BE-20: نسخة قيد التدقيق في صندوق الإرسال بعد وصولها إلى MongoDB وثبوت سلامة يومها، والبريد المرسل أو الفاشل بعد مدته.
/// Phase 3: business events whose handlers ran, after Retention:EventDays, and in-app notifications after Retention:InboxDays.
/// BE-19: الملفات التي لا مستند لها (رفع فشل حفظه) بعد ساعة.
/// </summary>
public sealed class RetentionJob(IServiceScopeFactory scopes, IConfiguration config, ILogger<RetentionJob> log) : BackgroundService
{
    private static readonly TimeSpan Every = TimeSpan.FromHours(6);

    /// <summary>Rows deleted per statement, so a large first purge does not hold one long lock.</summary>
    private const int Batch = 5000;

    // An audit row's Postgres copy goes once it reached MongoDB and its day's chain was verified intact; MongoDB keeps the record.
    private const string OutboxSql =
        "DELETE FROM logs.outbox WHERE id IN (" +
        " SELECT o.id FROM logs.outbox o" +
        " JOIN logs.chain_anchors a ON a.chain = o.collection AND o.chain_seq BETWEEN a.first_seq AND a.last_seq" +
        " WHERE o.sent_at < {0} AND a.intact IS TRUE LIMIT {1})";

    // A handled business event (and its handler records, by cascade) is only history after its handlers ran.
    private const string EventSql =
        "DELETE FROM \"int\".outbox_events WHERE id IN (SELECT id FROM \"int\".outbox_events WHERE processed_at < {0} LIMIT {1})";

    // In-app notifications, read or not, are kept for a year.
    private const string InboxSql =
        "DELETE FROM notify.inbox WHERE id IN (SELECT id FROM notify.inbox WHERE created_at < {0} LIMIT {1})";

    private const string MailSql =
        "DELETE FROM adm.mail_outbox WHERE id IN (SELECT id FROM adm.mail_outbox WHERE sent_at < {0} OR failed_at < {2} LIMIT {1})";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                await RunOnceAsync(stoppingToken);
                await Task.Delay(Every, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Retention cleanup failed; it retries later.");
            }
        }
    }

    internal async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        var now = DateTimeOffset.UtcNow;
        var sessions = now.AddDays(-config.GetValue("Retention:SessionDays", 30));
        var staff = await db.Sessions.Where(s => s.ExpiresAt < sessions || s.RevokedAt < sessions).ExecuteDeleteAsync(ct);
        if (staff > 0) log.LogInformation("Retention removed {Staff} expired staff sessions.", staff);

        var outbox = await DeleteInBatchesAsync(db, OutboxSql, [now.AddDays(-config.GetValue("Retention:OutboxDays", 30)), Batch], ct);
        var mail = await DeleteInBatchesAsync(db, MailSql,
            [now.AddDays(-config.GetValue("Retention:MailSentDays", 180)), Batch, now.AddDays(-config.GetValue("Retention:MailFailedDays", 365))], ct);
        var events = await DeleteInBatchesAsync(db, EventSql, [now.AddDays(-config.GetValue("Retention:EventDays", 30)), Batch], ct);
        var inbox = await DeleteInBatchesAsync(db, InboxSql, [now.AddDays(-config.GetValue("Retention:InboxDays", 365)), Batch], ct);
        if (outbox + mail + events + inbox > 0)
            log.LogInformation("Retention removed {Outbox} delivered audit copies, {Mail} old mail rows, {Events} handled events and {Inbox} old notifications.",
                outbox, mail, events, inbox);

        var files = scope.ServiceProvider.GetRequiredService<IFileStore>();
        var orphans = await SweepFilesAsync(db, files, now.AddHours(-1), ct);
        if (orphans > 0) log.LogInformation("Retention removed {Files} stored files that no document refers to.", orphans);
    }

    private static async Task<int> DeleteInBatchesAsync(Tier1DbContext db, string sql, object[] args, CancellationToken ct)
    {
        var total = 0;
        while (true)
        {
#pragma warning disable EF1002 // The SQL is a constant of this class; the values go in as parameters.
            var n = await db.Database.ExecuteSqlRawAsync(sql, args, ct);
#pragma warning restore EF1002
            total += n;
            if (n < Batch) return total;
        }
    }

    /// <summary>
    /// BE-19: a file written by an upload whose save then failed has no document row. Older than <paramref name="before"/> it is not
    /// in flight, so it goes, with any partial write a failed upload left.
    /// </summary>
    internal static async Task<int> SweepFilesAsync(Tier1DbContext db, IFileStore files, DateTimeOffset before, CancellationToken ct)
    {
        var stale = files.List().Where(f => f.WrittenAt < before).ToList();
        if (stale.Count == 0) return 0;
        var keys = stale.Where(f => !f.Partial).Select(f => f.Key).Distinct().ToList();
        var known = (await db.Documents.AsNoTracking().Where(d => keys.Contains(d.StorageKey)).Select(d => d.StorageKey).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);
        var removed = 0;
        foreach (var f in stale.Where(f => f.Partial || !known.Contains(f.Key)))
        {
            if (f.Partial) files.DeletePartial(f.Key);
            else await files.DeleteAsync(f.Key, ct);
            removed++;
        }
        return removed;
    }
}
