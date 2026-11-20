using Microsoft.EntityFrameworkCore.Storage;

namespace Tier1.Application.Abstractions;

/// <summary>
/// ARCH-2: a transaction that first takes the lock serialising changes to reserved roles (super administrator count, SOD-10)
/// across requests and instances. The database mechanism lives in Infrastructure, not in the Application services.
/// </summary>
public interface IIamLock
{
    Task<IDbContextTransaction> BeginAsync(CancellationToken ct);
}
