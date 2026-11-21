namespace Tier1.Application.Abstractions;

/// <summary>What happened when a request claimed its Idempotency-Key (BE-8, ADR-0018).</summary>
public abstract record IdempotencyClaim
{
    /// <summary>The key is new (or its window has passed): this request runs, and its answer is recorded.</summary>
    public sealed record Started : IdempotencyClaim;

    /// <summary>The same request already ran with this key: its answer is sent again, and nothing runs.</summary>
    public sealed record Replay(int StatusCode, string? ContentType, byte[] Body) : IdempotencyClaim;

    /// <summary>The key was used for a different request (another path or body).</summary>
    public sealed record Mismatch : IdempotencyClaim;

    /// <summary>The first request with this key is still running, or ended without its answer being recorded.</summary>
    public sealed record InProgress : IdempotencyClaim;
}

/// <summary>
/// The Idempotency-Keys of every API instance (PostgreSQL). A claim is one atomic statement, so two requests with the same key
/// on two instances cannot both run. Each call commits on its own, apart from the request's work.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>How long a recorded answer is replayed; after it the key is free again.</summary>
    static readonly TimeSpan Window = TimeSpan.FromHours(24);

    Task<IdempotencyClaim> ClaimAsync(Guid user, string key, string route, string requestHash, DateTimeOffset now, CancellationToken ct);

    /// <summary>Records the answer of a request that succeeded, for replay to its retries.</summary>
    Task CompleteAsync(Guid user, string key, int statusCode, string? contentType, byte[] body, DateTimeOffset now, CancellationToken ct);

    /// <summary>Frees the key of a request that failed or whose answer is not kept, so a retry runs again.</summary>
    Task ReleaseAsync(Guid user, string key, CancellationToken ct);
}
