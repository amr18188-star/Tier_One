namespace Tier1.Application.Abstractions;

/// <summary>How a sign-in attempt ended, as recorded.</summary>
public enum LoginOutcome
{
    Succeeded,
    Failed,
    Blocked,
}

/// <summary>The limits of one counter: failures allowed within the window, then a block for its duration.</summary>
public sealed record AttemptLimit(int MaxFailures, TimeSpan Window, TimeSpan Block);

/// <summary>Whether this failure just blocked the account at this address, or the address for every account.</summary>
public sealed record FailureResult(bool AccountAddressBlocked, bool AddressBlocked);

/// <summary>
/// Sign-in attempt counters and log shared by every API instance (PostgreSQL today; Redis could implement the same port).
/// Counting is atomic per key, so parallel attempts on several instances cannot exceed a limit. Each call commits on its own,
/// apart from the request's work, so a failing request cannot undo its count.
/// </summary>
public interface ILoginAttempts
{
    Task<bool> AddressBlockedAsync(string surface, string? ip, DateTimeOffset now, CancellationToken ct);

    Task<bool> AccountAddressBlockedAsync(string surface, Guid account, string? ip, DateTimeOffset now, CancellationToken ct);

    /// <summary>Whether the account signed in successfully from this address since the given time (a known place for its owner).</summary>
    Task<bool> KnownAddressAsync(Guid account, string? ip, DateTimeOffset since, CancellationToken ct);

    /// <summary>Counts a failure for the address and, when the account is known, for the account at that address.</summary>
    Task<FailureResult> FailAsync(string surface, Guid? account, string? ip, AttemptLimit accountAddress, AttemptLimit address, DateTimeOffset now, CancellationToken ct);

    /// <summary>A successful sign-in clears the account's count at this address (not the address's count across accounts).</summary>
    Task ClearAccountAddressAsync(string surface, Guid account, string? ip, CancellationToken ct);

    Task RecordAsync(string surface, Guid? account, string email, string? ip, LoginOutcome outcome, string reason, DateTimeOffset at, CancellationToken ct);
}
