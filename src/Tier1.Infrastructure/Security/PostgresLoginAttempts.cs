using System.Security.Cryptography;
using System.Text;
using Npgsql;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Security;

/// <summary>
/// Sign-in counters and the attempt log in PostgreSQL, shared by every API instance. A failure is counted by one
/// INSERT … ON CONFLICT … RETURNING statement per key: the row lock serialises concurrent attempts on any instance, so the
/// count and the block are exact. Each call runs on its own connection and commits at once, apart from the request's work.
/// </summary>
public sealed class PostgresLoginAttempts(NpgsqlDataSource db) : ILoginAttempts
{
    internal static string AccountAddressKey(string surface, Guid account, string? ip) => $"{surface}|acct|{account:N}|{ip ?? "unknown"}";

    internal static string AddressKey(string surface, string? ip) => $"{surface}|addr|{ip ?? "unknown"}";

    public Task<bool> AddressBlockedAsync(string surface, string? ip, DateTimeOffset now, CancellationToken ct) =>
        BlockedAsync(AddressKey(surface, ip), now, ct);

    public Task<bool> AccountAddressBlockedAsync(string surface, Guid account, string? ip, DateTimeOffset now, CancellationToken ct) =>
        BlockedAsync(AccountAddressKey(surface, account, ip), now, ct);

    private async Task<bool> BlockedAsync(string key, DateTimeOffset now, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("SELECT EXISTS (SELECT 1 FROM iam.login_counters WHERE key = $1 AND blocked_until > $2)");
        cmd.Parameters.AddWithValue(key);
        cmd.Parameters.AddWithValue(now);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    public async Task<bool> KnownAddressAsync(Guid account, string? ip, DateTimeOffset since, CancellationToken ct)
    {
        if (ip is null) return false;
        await using var cmd = db.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM iam.login_attempts WHERE user_id = $1 AND ip = $2 AND outcome = 'succeeded' AND at > $3)");
        cmd.Parameters.AddWithValue(account);
        cmd.Parameters.AddWithValue(ip);
        cmd.Parameters.AddWithValue(since);
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    public async Task<FailureResult> FailAsync(string surface, Guid? account, string? ip, AttemptLimit accountAddress, AttemptLimit address, DateTimeOffset now, CancellationToken ct)
    {
        var pair = account is { } a && await CountAsync(AccountAddressKey(surface, a, ip), accountAddress, now, ct);
        var wide = await CountAsync(AddressKey(surface, ip), address, now, ct);
        return new FailureResult(pair, wide);
    }

    /// <summary>
    /// One failure on one key. Within an active block nothing changes; a window that has passed starts again at 1; reaching the
    /// limit sets the block. Returns true when this failure set the block.
    /// </summary>
    private async Task<bool> CountAsync(string key, AttemptLimit limit, DateTimeOffset now, CancellationToken ct)
    {
        const string Sql = """
            INSERT INTO iam.login_counters AS c (key, failures, window_start, blocked_until)
            VALUES ($1, 1, $2, CASE WHEN 1 >= $4 THEN $5 END)
            ON CONFLICT (key) DO UPDATE SET
              failures = CASE WHEN c.blocked_until > $2 THEN c.failures
                              WHEN c.window_start <= $3 THEN 1 ELSE c.failures + 1 END,
              window_start = CASE WHEN c.blocked_until > $2 THEN c.window_start
                                  WHEN c.window_start <= $3 THEN $2 ELSE c.window_start END,
              blocked_until = CASE WHEN c.blocked_until > $2 THEN c.blocked_until
                                   WHEN (CASE WHEN c.window_start <= $3 THEN 1 ELSE c.failures + 1 END) >= $4 THEN $5
                                   ELSE NULL END
            RETURNING (blocked_until IS NOT NULL AND blocked_until = $5)
            """;
        var blockUntil = now + limit.Block;
        await using var cmd = db.CreateCommand(Sql);
        cmd.Parameters.AddWithValue(key);
        cmd.Parameters.AddWithValue(now);
        cmd.Parameters.AddWithValue(now - limit.Window);
        cmd.Parameters.AddWithValue(limit.MaxFailures);
        cmd.Parameters.AddWithValue(blockUntil);
        // Compared in SQL: PostgreSQL keeps microseconds, so the value read back would not equal .NET's 100 ns ticks.
        return await cmd.ExecuteScalarAsync(ct) is true;
    }

    public async Task ClearAccountAddressAsync(string surface, Guid account, string? ip, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("DELETE FROM iam.login_counters WHERE key = $1");
        cmd.Parameters.AddWithValue(AccountAddressKey(surface, account, ip));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RecordAsync(string surface, Guid? account, string email, string? ip, LoginOutcome outcome, string reason, DateTimeOffset at, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            "INSERT INTO iam.login_attempts (at, surface, user_id, email_hash, ip, outcome, reason) VALUES ($1, $2, $3, $4, $5, $6, $7)");
        cmd.Parameters.AddWithValue(at);
        cmd.Parameters.AddWithValue(surface);
        cmd.Parameters.AddWithValue(account is { } a ? a : DBNull.Value);
        cmd.Parameters.AddWithValue(EmailHash(email));
        cmd.Parameters.AddWithValue(ip is null ? DBNull.Value : ip);
        cmd.Parameters.AddWithValue(outcome.ToString().ToLowerInvariant());
        cmd.Parameters.AddWithValue(reason);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    internal static string EmailHash(string email) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes((email ?? "").Trim().ToLowerInvariant())));
}
