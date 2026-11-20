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

/// <summary>
/// BE-14: the tables the indicators module owns: the strategy tree (pillars, standards, initiatives), the indicator dictionary with
/// its definition versions, targets with their revisions, and data conflicts.
/// </summary>
public interface IKpiStore : IUnitOfWork
{
    DbSet<Domain.Kpi.Pillar> Pillars { get; }

    DbSet<Domain.Kpi.Standard> Standards { get; }

    DbSet<Domain.Kpi.Initiative> Initiatives { get; }

    DbSet<Domain.Kpi.Indicator> Indicators { get; }

    DbSet<Domain.Kpi.IndicatorVersion> IndicatorVersions { get; }

    DbSet<Domain.Kpi.IndicatorTarget> Targets { get; }

    DbSet<Domain.Kpi.TargetRevision> TargetRevisions { get; }

    DbSet<Domain.Kpi.DataConflict> Conflicts { get; }
}

/// <summary>BE-14: the tables the units module owns: the unit tree, its change history, and which unit enters each indicator.</summary>
public interface IOrgStore : IUnitOfWork
{
    DbSet<Domain.Organization.OrgUnit> OrgUnits { get; }

    DbSet<Domain.Organization.OrgUnitChange> OrgUnitChanges { get; }

    DbSet<Domain.Organization.UnitIndicator> UnitIndicators { get; }
}

/// <summary>BE-14: the tables the structures module owns: Tier 1 structures of colleges, their positions and members, and industry partners.</summary>
public interface IStructureStore : IUnitOfWork
{
    DbSet<Domain.Structure.StructureUnit> StructureUnits { get; }

    DbSet<Domain.Structure.StructurePosition> StructurePositions { get; }

    DbSet<Domain.Structure.StructureMember> StructureMembers { get; }

    DbSet<Domain.Structure.IndustryPartner> IndustryPartners { get; }
}

/// <summary>
/// BE-14: the tables the identity and access module owns (namespaces Access and Accounts): accounts and sessions, roles with
/// their grants, role assignments, individual overrides, proposed role changes, and the access version.
/// </summary>
public interface IIdentityStore : IUnitOfWork
{
    DbSet<Domain.Identity.AppUser> Users { get; }

    DbSet<Domain.Identity.UserSession> Sessions { get; }

    DbSet<Domain.Access.Role> Roles { get; }

    DbSet<Domain.Access.RoleGrant> RoleGrants { get; }

    DbSet<Domain.Access.UserRole> UserRoles { get; }

    DbSet<Domain.Access.UserOverride> UserOverrides { get; }

    DbSet<Domain.Access.RbacChange> RbacChanges { get; }

    DbSet<Domain.Access.AccessState> AccessStates { get; }
}

/// <summary>BE-14: the tables the audit log owns (namespaces Auditing and Logs): the audit outbox and the hash chain state, anchors and checkpoints.</summary>
public interface IAuditStore : IUnitOfWork
{
    DbSet<Domain.Logging.OutboxEvent> Outbox { get; }

    DbSet<Domain.Logging.ChainState> ChainStates { get; }

    DbSet<Domain.Logging.ChainAnchor> ChainAnchors { get; }

    DbSet<Domain.Logging.ChainCheckpoint> ChainCheckpoints { get; }
}
