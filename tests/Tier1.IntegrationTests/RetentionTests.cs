using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Tier1.Infrastructure.Persistence;

namespace Tier1.IntegrationTests;

/// <summary>DB-7/BE-20/BE-19: the retention job purges what has served its purpose, and nothing that has not.</summary>
[Collection("app")]
public sealed class RetentionTests(Tier1App app)
{
    [Fact]
    public async Task Old_mail_delivered_audit_copies_and_orphan_files_go_and_the_rest_stays()
    {
        await app.As("planner");
        var tag = Calls.Tag();
        const string mail = "INSERT INTO adm.mail_outbox (kind, \"to\", subject, body_protected, created_at, next_attempt_at, attempts, sent_at, failed_at) " +
            "VALUES ('it', 'it@tier1.local', @s, 'x', now(), now(), 1, @sent, @failed)";
        async Task Mail(string subject, int? sentDaysAgo, int? failedDaysAgo) =>
            await app.ScalarAsync(mail, ("s", subject), ("sent", (object?)Ago(sentDaysAgo) ?? DBNull.Value), ("failed", (object?)Ago(failedDaysAgo) ?? DBNull.Value));
        await Mail($"old-sent-{tag}", 200, null);
        await Mail($"new-sent-{tag}", 10, null);
        await Mail($"old-failed-{tag}", null, 400);
        await Mail($"queued-{tag}", null, null);

        // Two chains of audit copies sent long ago: one day verified intact, one not verified yet.
        foreach (var (chain, intact) in new[] { ($"it-ok-{tag}", "true"), ($"it-unverified-{tag}", "NULL") })
        {
            await app.ScalarAsync($"INSERT INTO logs.chain_anchors (chain, day, first_seq, last_seq, last_hash, event_count, updated_at, intact) VALUES (@c, current_date - 60, 1, 2, 'h', 2, now(), {intact})", ("c", chain));
            for (var seq = 1; seq <= 2; seq++)
                await app.ScalarAsync("INSERT INTO logs.outbox (event_id, collection, payload, created_at, chain_seq, sent_at, attempts) VALUES (gen_random_uuid(), @c, '{}', now() - interval '60 days', @q, now() - interval '60 days', 1)",
                    ("c", chain), ("q", (long)seq));
        }

        // Files: an old orphan, an old partial write, and a fresh orphan (an upload in flight).
        string Key() => $"ev/2026/{Guid.NewGuid():N}";
        string PathOf(string key) => Path.Combine(app.FilesRoot, key.Replace('/', Path.DirectorySeparatorChar));
        var (old, partial, fresh) = (Key(), Key(), Key());
        foreach (var (key, suffix, hours) in new[] { (old, "", 3), (partial, ".part", 3), (fresh, "", 0) })
        {
            var p = PathOf(key) + suffix;
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            await File.WriteAllTextAsync(p, "x");
            File.SetLastWriteTimeUtc(p, DateTime.UtcNow.AddHours(-hours));
        }

        var job = app.Services.GetServices<IHostedService>().OfType<RetentionJob>().Single();
        await job.RunOnceAsync(CancellationToken.None);

        async Task<long> MailLeft(string subject) => await app.CountAsync("SELECT count(*) FROM adm.mail_outbox WHERE subject = @s", ("s", subject));
        Assert.Equal(0, await MailLeft($"old-sent-{tag}"));
        Assert.Equal(1, await MailLeft($"new-sent-{tag}"));
        Assert.Equal(0, await MailLeft($"old-failed-{tag}"));
        Assert.Equal(1, await MailLeft($"queued-{tag}"));
        Assert.Equal(0, await app.CountAsync("SELECT count(*) FROM logs.outbox WHERE collection = @c", ("c", $"it-ok-{tag}")));
        Assert.Equal(2, await app.CountAsync("SELECT count(*) FROM logs.outbox WHERE collection = @c", ("c", $"it-unverified-{tag}")));
        Assert.False(File.Exists(PathOf(old)), "An old file no document refers to is removed");
        Assert.False(File.Exists(PathOf(partial) + ".part"), "An old partial write is removed");
        Assert.True(File.Exists(PathOf(fresh)), "A fresh file (an upload in flight) stays");
    }

    private static DateTimeOffset? Ago(int? days) => days is { } d ? DateTimeOffset.UtcNow.AddDays(-d) : null;
}
