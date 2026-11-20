using System.Text.Json;
using Tier1.Application.Abstractions;
using Tier1.Domain.Integration;

namespace Tier1.Application.Integration;

/// <summary>A business event: a record with a stable name. Its JSON form is its contract with the handlers, so fields are only added.</summary>
public interface IDomainEvent
{
    static abstract string EventType { get; }
}

/// <summary>Adds events to the outbox in the caller's unit of work; the caller's single SaveChanges commits them with the change.</summary>
public interface IEventOutbox
{
    void Add<TEvent>(TEvent evt, string aggregateType, Guid aggregateId) where TEvent : IDomainEvent;
}

/// <summary>Wakes the event dispatcher once a change with events has committed, instead of waiting for its next poll.</summary>
public interface IEventSignal
{
    void Notify();
}

/// <summary>
/// Handles one event type after the commit. It writes through the scoped <see cref="IAppDb"/> and does not save: the dispatcher saves
/// its writes together with the record that it ran, so a handler is never applied twice. A handler that calls an outside system
/// must make that call idempotent itself.
/// </summary>
public interface IEventHandler
{
    string EventType { get; }

    /// <summary>Stable handler name, the key in int.processed_events. Renaming it reruns the handler for unprocessed events only.</summary>
    string Name { get; }

    Task HandleAsync(DomainEventRecord record, CancellationToken ct);
}

/// <summary>Typed base: reads the payload as <typeparamref name="TEvent"/>.</summary>
public abstract class DomainEventHandler<TEvent> : IEventHandler where TEvent : IDomainEvent
{
    public string EventType => TEvent.EventType;

    public abstract string Name { get; }

    public Task HandleAsync(DomainEventRecord record, CancellationToken ct) =>
        HandleAsync(JsonSerializer.Deserialize<TEvent>(record.Payload, EventOutbox.Json) ?? throw new JsonException($"Empty {record.Type} payload."), record, ct);

    protected abstract Task HandleAsync(TEvent evt, DomainEventRecord record, CancellationToken ct);
}

public sealed class EventOutbox(IEventStore db,  IClock clock, ICurrentUser current, IEventSignal signal) : IEventOutbox
{
    /// <summary>Enums by name, so reordering an enum never changes what a stored event means.</summary>
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    public void Add<TEvent>(TEvent evt, string aggregateType, Guid aggregateId) where TEvent : IDomainEvent
    {
        var now = clock.UtcNow;
        db.DomainEvents.Add(new DomainEventRecord
        {
            Id = Guid.CreateVersion7(now),
            Type = TEvent.EventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            Payload = JsonSerializer.Serialize(evt, Json),
            OccurredAt = now,
            AvailableAt = now,
            ActorId = current.UserId,
        });
        // The dispatcher may wake before the commit and find nothing; it polls again within seconds.
        signal.Notify();
    }
}
