using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Tier1.Application.Abstractions;

namespace Tier1.Infrastructure.Persistence;

/// <summary>A named lock as a PostgreSQL transaction-scoped advisory lock, released by the commit or rollback.</summary>
internal sealed class PostgresTransactionLock(Tier1DbContext db) : ITransactionLock
{
    public async Task<IDbContextTransaction> BeginAsync(long key, CancellationToken ct)
    {
        var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({key})", ct);
        return tx;
    }
}
