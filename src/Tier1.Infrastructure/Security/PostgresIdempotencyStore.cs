using Npgsql;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Security;

/// <summary>
/// Idempotency-Keys in iam.idempotency_keys (BE-8, ADR-0018). The claim is one INSERT … ON CONFLICT statement: the primary key
/// (user, key) lets exactly one request with a key run, on any instance; a row older than the window is taken over as new.
/// Each call runs on its own connection and commits at once, so a request that fails cannot undo the claim of another.
/// </summary>
public sealed class PostgresIdempotencyStore(NpgsqlDataSource db) : IIdempotencyStore
{
    private const string ClaimSql = """
        INSERT INTO iam.idempotency_keys AS k (user_id, key, route, request_hash, created_at)
        VALUES ($1, $2, $3, $4, $5)
        ON CONFLICT (user_id, key) DO UPDATE SET
          route = EXCLUDED.route, request_hash = EXCLUDED.request_hash, created_at = EXCLUDED.created_at,
          status_code = NULL, content_type = NULL, response = NULL, completed_at = NULL
        WHERE k.created_at < $6
        RETURNING true
        """;

    public async Task<IdempotencyClaim> ClaimAsync(Guid user, string key, string route, string requestHash, DateTimeOffset now, CancellationToken ct)
    {
        // A row released between the claim and the read is claimed again on the next turn.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await using (var claim = db.CreateCommand(ClaimSql))
            {
                claim.Parameters.AddWithValue(user);
                claim.Parameters.AddWithValue(key);
                claim.Parameters.AddWithValue(route);
                claim.Parameters.AddWithValue(requestHash);
                claim.Parameters.AddWithValue(now);
                claim.Parameters.AddWithValue(now - IIdempotencyStore.Window);
                if (await claim.ExecuteScalarAsync(ct) is true) return new IdempotencyClaim.Started();
            }

            await using var read = db.CreateCommand(
                "SELECT route, request_hash, status_code, content_type, response FROM iam.idempotency_keys WHERE user_id = $1 AND key = $2");
            read.Parameters.AddWithValue(user);
            read.Parameters.AddWithValue(key);
            await using var row = await read.ExecuteReaderAsync(ct);
            if (!await row.ReadAsync(ct)) continue;
            if (row.GetString(0) != route || row.GetString(1) != requestHash) return new IdempotencyClaim.Mismatch();
            if (row.IsDBNull(2)) return new IdempotencyClaim.InProgress();
            return new IdempotencyClaim.Replay(row.GetInt32(2), row.IsDBNull(3) ? null : row.GetString(3), row.IsDBNull(4) ? [] : (byte[])row[4]);
        }
        return new IdempotencyClaim.InProgress();
    }

    public async Task CompleteAsync(Guid user, string key, int statusCode, string? contentType, byte[] body, DateTimeOffset now, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand(
            "UPDATE iam.idempotency_keys SET status_code = $3, content_type = $4, response = $5, completed_at = $6 WHERE user_id = $1 AND key = $2");
        cmd.Parameters.AddWithValue(user);
        cmd.Parameters.AddWithValue(key);
        cmd.Parameters.AddWithValue(statusCode);
        cmd.Parameters.AddWithValue((object?)contentType ?? DBNull.Value);
        cmd.Parameters.AddWithValue(body);
        cmd.Parameters.AddWithValue(now);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task ReleaseAsync(Guid user, string key, CancellationToken ct)
    {
        await using var cmd = db.CreateCommand("DELETE FROM iam.idempotency_keys WHERE user_id = $1 AND key = $2 AND status_code IS NULL");
        cmd.Parameters.AddWithValue(user);
        cmd.Parameters.AddWithValue(key);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
