using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tier1.Application.Abstractions;
using Tier1.Application.Integration;
using Tier1.Domain.Integration;
using Tier1.Infrastructure.Persistence;

namespace Tier1.Infrastructure.Integration;

public sealed class EventSignal : IEventSignal, IDisposable
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
            // A wake-up is already pending.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>
/// Hands committed business events (int.outbox_events) to their handlers. A batch is claimed with a lease (FOR UPDATE SKIP LOCKED),
/// so instances share the work without handling one event twice at once. Each handler's writes commit with its row in
/// int.processed_events, so a retried event skips the handlers that already ran. A failure retries with a growing delay, up to
/// <see cref="MaxAttempts"/>; then the event is marked failed and the readiness check reports it.
/// </summary>
public sealed class DomainEventDispatcher(IServiceScopeFactory scopes, EventSignal signal, IClock clock, ILogger<DomainEventDispatcher> log) : BackgroundService
{
    public const int MaxAttempts = 10;

    /// <summary>Longer than a batch takes, so no other instance claims an event while it is being handled.</summary>
    private static readonly TimeSpan Lease = TimeSpan.FromMinutes(5);

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
                log.LogWarning(e, "Event dispatch cycle failed.");
            }
            if (handled > 0) continue;
            try
            {
                // A signal comes from a change that adds events, usually just before its commit: the next pass a second later finds them.
                if (await signal.WaitAsync(TimeSpan.FromSeconds(5), stoppingToken)) await Task.Delay(TimeSpan.FromMilliseconds(300), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Claims and handles one batch; returns how many events it took.</summary>
    internal async Task<int> RunOnceAsync(CancellationToken ct)
    {
        List<Guid> ids;
        await using (var scope = scopes.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
            var now = clock.UtcNow;
            var until = now + Lease;
            ids = await db.Database.SqlQuery<Guid>($"""
                UPDATE "int".outbox_events SET locked_until = {until}, attempts = attempts + 1
                WHERE id IN (SELECT id FROM "int".outbox_events
                             WHERE processed_at IS NULL AND failed_at IS NULL AND available_at <= {now}
                               AND (locked_until IS NULL OR locked_until < {now})
                             ORDER BY available_at, id LIMIT 20 FOR UPDATE SKIP LOCKED)
                RETURNING id AS "Value"
                """).ToListAsync(ct);
        }
        // Ids are version 7 GUIDs: their order is the order the events were raised.
        foreach (var id in ids.Order()) await HandleAsync(id, ct);
        return ids.Count;
    }

    private async Task HandleAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        var record = await db.DomainEvents.FirstAsync(e => e.Id == id, ct);
        var done = await db.ProcessedEvents.Where(p => p.EventId == id).Select(p => p.Handler).ToListAsync(ct);
        var handlers = scope.ServiceProvider.GetServices<IEventHandler>().Where(h => h.EventType == record.Type && !done.Contains(h.Name));
        foreach (var handler in handlers)
        {
            try
            {
                await handler.HandleAsync(record, ct);
                db.ProcessedEvents.Add(new ProcessedEvent { Handler = handler.Name, EventId = id, ProcessedAt = clock.UtcNow });
                await db.SaveChangesAsync(ct);
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                db.ChangeTracker.Clear();
                await FailedAsync(db, id, handler.Name, e, ct);
                return;
            }
        }
        record.ProcessedAt = clock.UtcNow;
        record.LockedUntil = null;
        record.LastError = null;
        await db.SaveChangesAsync(ct);
    }

    private async Task FailedAsync(Tier1DbContext db, Guid id, string handler, Exception e, CancellationToken ct)
    {
        var record = await db.DomainEvents.FirstAsync(r => r.Id == id, ct);
        var error = $"{handler}: {e.GetType().Name}: {e.Message}";
        record.LastError = error.Length > 1000 ? error[..1000] : error;
        record.LockedUntil = null;
        if (record.Attempts >= MaxAttempts) record.FailedAt = clock.UtcNow;
        else record.AvailableAt = clock.UtcNow.AddSeconds(Math.Min(3600, 5 * Math.Pow(2, record.Attempts - 1)));
        await db.SaveChangesAsync(ct);
        log.LogWarning(e, "Event {Type} {Id} failed in {Handler} (attempt {Attempt}{GaveUp}).", record.Type, id, handler, record.Attempts,
            record.FailedAt is null ? "" : ", gave up");
    }
}
