using Microsoft.EntityFrameworkCore;
using Tier1.Domain.Integration;
using Tier1.Domain.Notifications;

namespace Tier1.Infrastructure.Persistence;

/// <summary>Schema int: the business event outbox and the record of which handler finished which event. Schema notify: the in-app inbox.</summary>
public sealed partial class Tier1DbContext
{
    public DbSet<DomainEventRecord> DomainEvents => Set<DomainEventRecord>();

    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    public DbSet<InboxItem> Inbox => Set<InboxItem>();

    private static void ConfigureIntegration(ModelBuilder b)
    {
        b.Entity<DomainEventRecord>(e =>
        {
            e.ToTable("outbox_events", "int");
            e.Property(x => x.Type).HasMaxLength(60);
            e.Property(x => x.AggregateType).HasMaxLength(40);
            e.Property(x => x.Payload).HasColumnType("jsonb");
            e.Property(x => x.LastError).HasMaxLength(1000);
            // The dispatcher's claim: events not yet handled, in order of availability.
            e.HasIndex(x => x.AvailableAt).HasFilter("processed_at IS NULL AND failed_at IS NULL").HasDatabaseName("ix_outbox_events_pending");
            e.HasIndex(x => new { x.AggregateType, x.AggregateId }).HasDatabaseName("ix_outbox_events_aggregate");
            e.HasIndex(x => x.ProcessedAt).HasFilter("processed_at IS NOT NULL").HasDatabaseName("ix_outbox_events_processed_at");
        });
        b.Entity<ProcessedEvent>(e =>
        {
            e.ToTable("processed_events", "int");
            e.HasKey(x => new { x.Handler, x.EventId });
            e.Property(x => x.Handler).HasMaxLength(80);
            e.HasOne<DomainEventRecord>().WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<InboxItem>(e =>
        {
            e.ToTable("inbox", "notify");
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Body).HasMaxLength(4000);
            e.Property(x => x.Link).HasMaxLength(300);
            // The bell: a user's latest items, and the unread count.
            e.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_inbox_user_created_at");
            e.HasIndex(x => x.UserId).HasFilter("read_at IS NULL").HasDatabaseName("ix_inbox_user_unread");
            e.HasOne<Tier1.Domain.Identity.AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
