using Microsoft.EntityFrameworkCore.Storage;

namespace Tier1.Application.Abstractions;

/// <summary>
/// BE-14: a transaction that first takes a named lock, so one check-then-write runs at a time across requests and instances
/// (for example BE-5, one unit per university-level indicator). The database mechanism lives in Infrastructure.
/// </summary>
public interface ITransactionLock
{
    Task<IDbContextTransaction> BeginAsync(long key, CancellationToken ct);
}
