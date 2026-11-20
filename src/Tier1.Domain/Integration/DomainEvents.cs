namespace Tier1.Domain.Integration;

/// <summary>
/// A business event saved in the same transaction as the change that raised it (int.outbox_events). A background dispatcher hands
/// it to its handlers after the commit, so a notification never goes for a change that failed, and is never lost for one that
/// succeeded. The audit trail has its own outbox (logs.outbox) and chain; this one is for the work that follows a change.
/// </summary>
public sealed class DomainEventRecord
{
    public Guid Id { get; set; }

    /// <summary>Stable event name, for example form.moved.</summary>
    public string Type { get; set; } = "";

    public string AggregateType { get; set; } = "";

    public Guid AggregateId { get; set; }

    /// <summary>The event as JSON.</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Not handed to a handler before this time (the retry delay after a failure).</summary>
    public DateTimeOffset AvailableAt { get; set; }

    public int Attempts { get; set; }

    /// <summary>A dispatcher's lease on the row, so two instances do not handle it at once.</summary>
    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    /// <summary>Gave up after the last attempt; shown by the readiness check until someone looks.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    public string? LastError { get; set; }

    /// <summary>Who caused the event (null for a background job).</summary>
    public Guid? ActorId { get; set; }
}

/// <summary>
/// One handler finished one event (int.processed_events). The handler's writes and this row commit together, so a retried event
/// skips the handlers that already ran: each handler's effect happens once.
/// </summary>
public sealed class ProcessedEvent
{
    public string Handler { get; set; } = "";

    public Guid EventId { get; set; }

    public DateTimeOffset ProcessedAt { get; set; }
}
