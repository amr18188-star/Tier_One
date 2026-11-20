using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Domain.Collection;

namespace Tier1.Application.Collection;

/// <summary>
/// PERF-B3: results snapshots kept across requests. A snapshot is reused only while its fingerprint is unchanged, and the
/// fingerprint covers every row the snapshot reads: the cycle, its indicators and their versions, its forms, the approved
/// values, reopen requests and their saved values, and the shared units, standards, indicators and targets. So an approved
/// target changed after a cycle closed (D8), a renamed unit or a reopened form gives a new fingerprint and a rebuilt snapshot.
/// The fingerprint is the row count and the sum of row versions (xmin) per table: an insert, an update or a delete changes it.
/// It is read before the snapshot is built, so a change made during a build leaves a stale fingerprint, never a stale snapshot.
/// </summary>
public sealed class CycleSnapshotCache
{
    /// <summary>Cycles kept; about 20 quarterly cycles by 2031, and the least recently used one leaves first.</summary>
    public const int Capacity = 32;

    private readonly ConcurrentDictionary<Guid, Entry> entries = new();
    private long ticks;
    private long builds;

    /// <summary>Snapshots built since start, for tests and diagnostics.</summary>
    public long Builds => Interlocked.Read(ref builds);

    public bool TryGet(Guid cycleId, string fingerprint, out object snapshot)
    {
        if (entries.TryGetValue(cycleId, out var entry) && entry.Fingerprint == fingerprint)
        {
            entry.LastUsed = Interlocked.Increment(ref ticks);
            snapshot = entry.Snapshot;
            return true;
        }
        snapshot = null!;
        return false;
    }

    public void Put(Guid cycleId, string fingerprint, object snapshot)
    {
        Interlocked.Increment(ref builds);
        entries[cycleId] = new Entry(fingerprint, snapshot) { LastUsed = Interlocked.Increment(ref ticks) };
        while (entries.Count > Capacity)
        {
            var oldest = entries.OrderBy(e => e.Value.LastUsed).First();
            entries.TryRemove(oldest);
        }
    }

    /// <summary>The fingerprints of several cycles in one query (a trend or a college's quarters read many).</summary>
    public static async Task<Dictionary<Guid, string>> FingerprintsAsync(IAppDb db, Guid[] cycleIds, CancellationToken ct)
    {
        var approved = (int)SubmissionStatus.Approved;
        var rows = await db.Database.SqlQuery<FingerprintRow>($"""
            WITH shared AS (
              SELECT concat_ws('/',
                (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM org.units),
                (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM kpi.standards),
                (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM kpi.indicators),
                (SELECT count(*) || ':' || coalesce(sum(xmin::text::bigint), 0) FROM kpi.targets)) AS fp)
            SELECT c.id AS cycle_id, concat_ws('/', shared.fp, c.xmin::text,
              (SELECT count(*) || ':' || coalesce(sum(ci.xmin::text::bigint), 0) || ':' || coalesce(sum(v.xmin::text::bigint), 0)
                 FROM dc.cycle_indicators ci LEFT JOIN kpi.indicator_versions v ON v.id = ci.version_id WHERE ci.cycle_id = c.id),
              (SELECT count(*) || ':' || coalesce(sum(s.xmin::text::bigint), 0) FROM dc.submissions s WHERE s.cycle_id = c.id),
              (SELECT count(*) || ':' || coalesce(sum(v.xmin::text::bigint), 0)
                 FROM dc.values v JOIN dc.submissions s ON s.id = v.submission_id WHERE s.cycle_id = c.id AND s.status = {approved}),
              (SELECT count(*) || ':' || coalesce(sum(r.xmin::text::bigint), 0)
                 FROM dc.reopen_requests r JOIN dc.submissions s ON s.id = r.submission_id WHERE s.cycle_id = c.id),
              (SELECT count(*) || ':' || coalesce(sum(vs.xmin::text::bigint), 0)
                 FROM dc.value_snapshots vs JOIN dc.submissions s ON s.id = vs.submission_id WHERE s.cycle_id = c.id)) AS fingerprint
            FROM dc.cycles c CROSS JOIN shared
            WHERE c.id = ANY({cycleIds})
            """).ToListAsync(ct);
        return rows.ToDictionary(r => r.CycleId, r => r.Fingerprint);
    }

    private sealed class Entry(string fingerprint, object snapshot)
    {
        public string Fingerprint { get; } = fingerprint;
        public object Snapshot { get; } = snapshot;
        public long LastUsed { get; set; }
    }

    private sealed class FingerprintRow
    {
        public Guid CycleId { get; set; }
        public string Fingerprint { get; set; } = "";
    }
}
