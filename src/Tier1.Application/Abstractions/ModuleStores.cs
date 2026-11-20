using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tier1.Domain.Notifications;

namespace Tier1.Application.Abstractions;

/// <summary>
/// BE-14: saving a request's changes, and the row version hook used by optimistic concurrency. One unit of work per request:
/// a module's writes, its audit records and its events commit together.
/// </summary>
public interface IUnitOfWork
{
    /// <summary>To set the expected version of a row before saving it (concurrent edits).</summary>
    EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);

    /// <summary>Transactions and the few set-based statements a module runs on its own tables.</summary>
    DatabaseFacade Database { get; }
}

/// <summary>
/// BE-14: read-only, untracked queries of any module's data, for what a module needs from others (names, scope, references).
/// A module writes only through its own store; what it reads from here cannot be saved.
/// </summary>
public interface IReadModel
{
    IQueryable<TEntity> Query<TEntity>() where TEntity : class;
}

/// <summary>BE-14: the tables the notifications module owns: outgoing mail, in-app inbox and message templates.</summary>
public interface INotificationStore : IUnitOfWork
{
    DbSet<OutgoingMail> OutgoingMails { get; }

    DbSet<InboxItem> Inbox { get; }
}

/// <summary>BE-14: the system settings table, owned by the settings module.</summary>
public interface ISettingsStore : IUnitOfWork
{
    DbSet<Domain.Common.SystemSetting> Settings { get; }
}

/// <summary>BE-14: the domain event outbox and its processed marks, owned by the integration module.</summary>
public interface IEventStore : IUnitOfWork
{
    DbSet<Domain.Integration.DomainEventRecord> DomainEvents { get; }

    DbSet<Domain.Integration.ProcessedEvent> ProcessedEvents { get; }
}

/// <summary>BE-14: the official university values and their change history, owned by the scorecard module.</summary>
public interface IScorecardStore : IUnitOfWork
{
    DbSet<Domain.Collection.UniversityValue> UniversityValues { get; }

    DbSet<Domain.Collection.UniversityValueChange> UniversityValueChanges { get; }
}

/// <summary>
/// BE-14: the tables the forms module owns: cycles and their indicators, unit forms, values, the path of each form, reopen requests
/// with the values kept at reopening, and stored documents (evidence and plan documents).
/// </summary>
public interface ICollectionStore : IUnitOfWork
{
    DbSet<Domain.Collection.CollectionCycle> Cycles { get; }

    DbSet<Domain.Collection.CycleIndicator> CycleIndicators { get; }

    DbSet<Domain.Collection.Submission> Submissions { get; }

    DbSet<Domain.Collection.SubmittedValue> SubmittedValues { get; }

    DbSet<Domain.Collection.SubmissionEvent> SubmissionEvents { get; }

    DbSet<Domain.Collection.ReopenRequest> ReopenRequests { get; }

    DbSet<Domain.Collection.ValueSnapshot> ValueSnapshots { get; }

    DbSet<Domain.Collection.StoredDocument> Documents { get; }
}
