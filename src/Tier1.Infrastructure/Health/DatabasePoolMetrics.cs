using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;

namespace Tier1.Infrastructure.Health;

/// <summary>The PostgreSQL connection pool now and its peaks since the server started (for the load report and the system admin).</summary>
public sealed record DatabasePoolSnapshot(int Used, int Idle, int? Max, int Pending, long Timeouts, int PeakUsed, int PeakPending, DateTimeOffset Since);

/// <summary>
/// Reads Npgsql's own pool metrics in the process (meter "Npgsql": <c>db.client.connection.count</c> by state,
/// <c>.max</c>, <c>.pending_requests</c>, <c>.timeouts</c>), so a load test sees pool saturation without dotnet-counters in the
/// container. Observable instruments are sampled every second, which also keeps the peaks.
/// </summary>
public sealed class DatabasePoolMetrics : IHostedService, IDisposable
{
    private const string Prefix = "db.client.connection.";

    private static readonly HashSet<string> Read = new(StringComparer.Ordinal)
    {
        Prefix + "count", Prefix + "max", Prefix + "pending_requests", Prefix + "timeouts", Prefix + "npgsql.timeouts",
    };
    private readonly MeterListener listener = new();
    private readonly ConcurrentDictionary<string, long> values = new(StringComparer.Ordinal);
    private readonly DateTimeOffset since = DateTimeOffset.UtcNow;
    private Timer? sampler;
    private int peakUsed;
    private int peakPending;

    public DatabasePoolMetrics()
    {
        listener.InstrumentPublished = (instrument, l) =>
        {
            // Only the pool's state; the per-use timing histograms would call back on every query.
            if (instrument.Meter.Name == "Npgsql" && Read.Contains(instrument.Name)) l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<int>((i, v, tags, _) => Record(i, v, tags));
        listener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i, v, tags));
        listener.Start();
    }

    /// <summary>One measurement: an observable instrument gives the current value; a counter gives a change to add.</summary>
    private void Record(Instrument instrument, long value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var state = "";
        foreach (var tag in tags)
            if (tag.Key.EndsWith(".state", StringComparison.Ordinal)) state = tag.Value?.ToString() ?? "";
        var key = instrument.Name[Prefix.Length..] + (state.Length > 0 ? "|" + state : "");
        if (instrument.IsObservable) values[key] = value;
        else values.AddOrUpdate(key, value, (_, old) => old + value);
    }

    private long Value(string key) => values.TryGetValue(key, out var v) ? v : 0;

    public DatabasePoolSnapshot Snapshot()
    {
        Sample();
        return new DatabasePoolSnapshot((int)Value("count|used"), (int)Value("count|idle"),
            values.TryGetValue("max", out var max) ? (int)max : null, (int)Value("pending_requests"), Value("timeouts") + Value("npgsql.timeouts"),
            peakUsed, peakPending, since);
    }

    private void Sample()
    {
        listener.RecordObservableInstruments();
        var used = (int)Value("count|used");
        var pending = (int)Value("pending_requests");
        if (used > Volatile.Read(ref peakUsed)) Interlocked.Exchange(ref peakUsed, used);
        if (pending > Volatile.Read(ref peakPending)) Interlocked.Exchange(ref peakPending, pending);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        sampler = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        sampler?.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        sampler?.Dispose();
        listener.Dispose();
    }
}
