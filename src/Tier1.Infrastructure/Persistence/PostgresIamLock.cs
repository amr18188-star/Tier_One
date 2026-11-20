using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Persistence;

/// <summary>The IAM lock as a PostgreSQL transaction-scoped advisory lock, released by the commit or rollback.</summary>
internal sealed class PostgresIamLock(Tier1DbContext db) : IIamLock
{
    /// <summary>The advisory lock key for reserved-role operations.</summary>
    private const long Key = 0x4C43_4941_4D;

    public async Task<IDbContextTransaction> BeginAsync(CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({Key})", ct);
        return tx;
    }
}
