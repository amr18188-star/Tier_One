using Npgsql;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// BE-15: a scheduled job runs on one instance at a time. Each run takes a PostgreSQL session advisory lock for its job on a
/// connection of its own; an instance that does not get it skips this run, and the lock is released when the run ends (or when
/// the connection closes, if the instance dies). The work itself stays idempotent, so a skipped or repeated run is harmless.
/// </summary>
public sealed class JobLock(NpgsqlDataSource dataSource)
{
    public const long Reminders = 7_401_001;
    public const long Retention = 7_401_002;
    public const long NightlyChain = 7_401_003;

    /// <summary>Runs the job unless another instance is running it now; false when this run was skipped.</summary>
    public async Task<bool> RunExclusiveAsync(long key, Func<CancellationToken, Task> job, CancellationToken ct)
    {
        await using var conn = await dataSource.OpenConnectionAsync(ct);
        await using (var take = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", conn))
        {
            take.Parameters.AddWithValue(key);
            if (await take.ExecuteScalarAsync(ct) is not true) return false;
        }
        try
        {
            await job(ct);
            return true;
        }
        finally
        {
            await using var release = new NpgsqlCommand("SELECT pg_advisory_unlock($1)", conn);
            release.Parameters.AddWithValue(key);
            await release.ExecuteScalarAsync(CancellationToken.None);
        }
    }
}
