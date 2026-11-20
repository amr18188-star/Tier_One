using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Npgsql;
using Tier1.Application.Abstractions;
using Tier1.Infrastructure.Logging;
using Tier1.Infrastructure.Persistence;

namespace Tier1.Infrastructure.Health;

/// <summary>One check: ok, degraded (the system serves, something waits), or down.</summary>
public sealed record ReadinessCheck(string Name, string State, string? Reason = null);

/// <summary>ready, degraded or unavailable, with each check. No counts or addresses: the endpoint is anonymous.</summary>
public sealed record Readiness(string Status, IReadOnlyList<ReadinessCheck> Checks);

/// <summary>
/// /health/ready. Only PostgreSQL makes the system unavailable (503): every change and its audit and events are saved there first.
/// The rest degrade it and say why, while it keeps serving:
/// MongoDB unreachable (audit records wait in logs.outbox), an audit or event backlog older than its limit, or an event or a mail
/// that failed for good in the last day (notifications are not reaching people).
/// </summary>
public sealed class ReadinessProbe(NpgsqlDataSource pg, MongoLogContext mongo, IServiceScopeFactory scopes, IClock clock, IConfiguration config)
{
    public async Task<Readiness> CheckAsync(CancellationToken ct)
    {
        if (!await DatabaseMigrator.CanConnectAsync(pg, ct))
            return new Readiness("unavailable", [new ReadinessCheck("postgres", "down", "unreachable")]);
        var checks = new List<ReadinessCheck> { new("postgres", "ok") };
        checks.Add(await MongoAsync(ct));

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        var now = clock.UtcNow;
        var auditLimit = now.AddMinutes(-config.GetValue("Health:AuditBacklogMinutes", 15));
        var eventLimit = now.AddMinutes(-config.GetValue("Health:EventBacklogMinutes", 15));
        var day = now.AddDays(-1);

        checks.Add(await db.Outbox.AnyAsync(o => o.SentAt == null && o.CreatedAt < auditLimit, ct)
            ? new ReadinessCheck("audit", "degraded", "backlog") : new ReadinessCheck("audit", "ok"));
        checks.Add(await db.DomainEvents.AnyAsync(e => e.FailedAt != null && e.FailedAt > day, ct)
            ? new ReadinessCheck("events", "degraded", "failed")
            : await db.DomainEvents.AnyAsync(e => e.ProcessedAt == null && e.FailedAt == null && e.OccurredAt < eventLimit, ct)
                ? new ReadinessCheck("events", "degraded", "backlog")
                : new ReadinessCheck("events", "ok"));
        checks.Add(await db.OutgoingMails.AnyAsync(m => m.FailedAt != null && m.FailedAt > day, ct)
            ? new ReadinessCheck("mail", "degraded", "failed") : new ReadinessCheck("mail", "ok"));

        return new Readiness(checks.All(c => c.State == "ok") ? "ready" : "degraded", checks);
    }

    private async Task<ReadinessCheck> MongoAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            await mongo.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: timeout.Token);
            return new ReadinessCheck("mongo", "ok");
        }
        catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return new ReadinessCheck("mongo", "degraded", "unreachable");
        }
    }
}
