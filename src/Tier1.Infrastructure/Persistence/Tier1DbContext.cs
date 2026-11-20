using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Domain.Access;
using Tier1.Domain.Calendar;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;
using Tier1.Domain.Logging;
using Tier1.Domain.Notifications;

namespace Tier1.Infrastructure.Persistence;

/// <summary>
/// بيانات العمل في PostgreSQL. لكل وحدة schema خاص بها: iam للهوية والصلاحيات، وlogs للصندوق الصادر ومراسي السلاسل،
/// وadm للإعدادات والبريد، وorg للجهات والهيكل، وkpi للمؤشرات والمستهدفات. السجلات نفسها في MongoDB منفصلة.
/// </summary>
public sealed partial class Tier1DbContext(DbContextOptions<Tier1DbContext> options) : DbContext(options), IAppDb, IReadModel, INotificationStore, ISettingsStore, IEventStore, IScorecardStore, ICollectionStore
{
    /// <summary>BE-14: other modules' rows, read only and untracked.</summary>
    public IQueryable<TEntity> Query<TEntity>() where TEntity : class => Set<TEntity>().AsNoTracking();

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<UserSession> Sessions => Set<UserSession>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<RoleGrant> RoleGrants => Set<RoleGrant>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<UserOverride> UserOverrides => Set<UserOverride>();

    public DbSet<RbacChange> RbacChanges => Set<RbacChange>();

    public DbSet<AccessState> AccessStates => Set<AccessState>();

    public DbSet<OutboxEvent> Outbox => Set<OutboxEvent>();

    public DbSet<ChainState> ChainStates => Set<ChainState>();

    public DbSet<ChainAnchor> ChainAnchors => Set<ChainAnchor>();

    public DbSet<ChainCheckpoint> ChainCheckpoints => Set<ChainCheckpoint>();

    public DbSet<Holiday> Holidays => Set<Holiday>();

    public DbSet<SystemSetting> Settings => Set<SystemSetting>();

    public DbSet<OutgoingMail> OutgoingMails => Set<OutgoingMail>();

    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            e.ToTable("users", "iam");
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(254);
            e.Property(u => u.FullNameAr).HasMaxLength(200);
            e.Property(u => u.FullNameEn).HasMaxLength(200);
            e.Property(u => u.PreferredLanguage).HasMaxLength(5);
            e.Property(u => u.Version).IsRowVersion();
        });
        b.Entity<UserSession>(e =>
        {
            e.ToTable("sessions", "iam");
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasIndex(s => s.UserId);
            e.Property(s => s.TokenHash).HasMaxLength(64);
            e.Property(s => s.Ip).HasMaxLength(64);
            e.Property(s => s.UserAgent).HasMaxLength(300);
            e.HasOne<AppUser>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<Role>(e =>
        {
            e.ToTable("roles", "iam");
            e.HasIndex(r => r.Code).IsUnique();
            e.Property(r => r.Code).HasMaxLength(16);
            e.Property(r => r.NameAr).HasMaxLength(100);
            e.Property(r => r.NameEn).HasMaxLength(100);
            e.Property(r => r.DescriptionAr).HasMaxLength(500);
            e.Property(r => r.GrantsVersion).IsConcurrencyToken();
            e.HasMany(r => r.Grants).WithOne().HasForeignKey(g => g.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RoleGrant>(e =>
        {
            e.ToTable("role_grants", "iam");
            e.HasKey(g => new { g.RoleId, g.Feature });
            e.Property(g => g.Feature).HasMaxLength(32);
        });
        b.Entity<UserRole>(e =>
        {
            e.ToTable("user_roles", "iam");
            e.HasIndex(a => a.UserId);
            e.HasIndex(a => a.RoleId);
            e.Property(a => a.Version).IsRowVersion();
            e.Property(a => a.Reason).HasMaxLength(500);
            e.Property(a => a.RevokeReason).HasMaxLength(500);
            e.HasOne<AppUser>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Role>().WithMany().HasForeignKey(a => a.RoleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UserOverride>(e =>
        {
            e.ToTable("user_overrides", "iam");
            e.HasIndex(o => o.UserId);
            e.Property(o => o.Feature).HasMaxLength(32);
            e.Property(o => o.Reason).HasMaxLength(500);
            e.HasOne<AppUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<RbacChange>(e =>
        {
            e.ToTable("rbac_changes", "iam");
            e.HasIndex(c => new { c.RoleId, c.Status });
            // طلب واحد معلق لكل دور، حتى مع طلبين متزامنين.
            e.HasIndex(c => c.RoleId).IsUnique().HasFilter("status = 1").HasDatabaseName("ux_rbac_changes_one_pending");
            e.Property(c => c.Version).IsRowVersion();
            e.Property(c => c.Reason).HasMaxLength(1000);
            e.Property(c => c.DecisionNote).HasMaxLength(1000);
            e.HasOne<Role>().WithMany().HasForeignKey(c => c.RoleId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<AccessState>(e =>
        {
            e.ToTable("access_state", "iam");
            e.Property(s => s.Id).ValueGeneratedNever();
            e.HasData(new AccessState { Id = 1, Version = 1 });
        });

        b.Entity<OutboxEvent>(e =>
        {
            e.ToTable("outbox", "logs");
            e.Property(o => o.Id).UseIdentityAlwaysColumn();
            e.HasIndex(o => o.EventId).IsUnique();
            e.HasIndex(o => o.Id).HasFilter("sent_at IS NULL").HasDatabaseName("ix_outbox_unsent");
            e.Property(o => o.Collection).HasMaxLength(40);
            e.Property(o => o.LastError).HasMaxLength(1000);
            e.Property(o => o.PrevHash).HasMaxLength(64);
            e.Property(o => o.Hash).HasMaxLength(64);
            e.Property(o => o.HashAlg).HasMaxLength(40);
        });
        b.Entity<ChainState>(e =>
        {
            e.ToTable("chain_state", "logs");
            e.HasKey(s => s.Chain);
            e.Property(s => s.Chain).HasMaxLength(40);
            e.Property(s => s.Hash).HasMaxLength(64);
            // حجز متزامن من نسختين لا يعطي الرقم نفسه مرتين.
            e.Property(s => s.Seq).IsConcurrencyToken();
        });
        b.Entity<ChainAnchor>(e =>
        {
            e.ToTable("chain_anchors", "logs");
            e.HasKey(a => new { a.Chain, a.Day });
            e.Property(a => a.Chain).HasMaxLength(40);
            e.Property(a => a.LastHash).HasMaxLength(64);
            e.Property(a => a.VerifyNote).HasMaxLength(500);
        });

        b.Entity<ChainCheckpoint>(e =>
        {
            e.ToTable("chain_checkpoints", "logs");
            e.Property(c => c.Id).UseIdentityAlwaysColumn();
            e.HasIndex(c => new { c.Chain, c.Seq });
            e.Property(c => c.Chain).HasMaxLength(40);
            e.Property(c => c.Hash).HasMaxLength(64);
            e.Property(c => c.Signature).HasMaxLength(64);
            e.Property(c => c.PublishedTo).HasMaxLength(100);
        });

        b.Entity<Holiday>(e =>
        {
            e.ToTable("holidays", "adm");
            e.HasKey(x => x.Date);
            e.Property(x => x.NameAr).HasMaxLength(150);
            e.Property(x => x.NameEn).HasMaxLength(150);
        });
        b.Entity<SystemSetting>(e =>
        {
            e.ToTable("settings", "adm");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(60);
            e.Property(x => x.Value).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
        });
        b.Entity<OutgoingMail>(e =>
        {
            e.ToTable("mail_outbox", "adm");
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.NextAttemptAt).HasFilter("sent_at IS NULL AND failed_at IS NULL").HasDatabaseName("ix_mail_outbox_pending");
            e.Property(x => x.Kind).HasMaxLength(40);
            e.Property(x => x.To).HasMaxLength(254);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.LastError).HasMaxLength(500);
            // المرحلة السادسة: سجل إشعارات المستفيد والموظفين.
            e.Property(x => x.Audience).HasMaxLength(16).HasDefaultValue(MailAudience.Account);
            e.Property(x => x.Module).HasMaxLength(8).HasDefaultValue("ptl");
            e.Property(x => x.TemplateKey).HasMaxLength(60);
            e.Property(x => x.RelatedType).HasMaxLength(40);
            e.Property(x => x.RelatedRef).HasMaxLength(60);
            e.Property(x => x.DedupKey).HasMaxLength(120);
            e.Property(x => x.SkippedReason).HasMaxLength(40);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.HasIndex(x => x.DedupKey).IsUnique().HasFilter("dedup_key IS NOT NULL").HasDatabaseName("ux_mail_outbox_dedup_key");
            e.HasIndex(x => x.BeneficiaryId).HasDatabaseName("ix_mail_outbox_beneficiary_id");
            e.HasIndex(x => x.BatchId).HasFilter("batch_id IS NOT NULL").HasDatabaseName("ix_mail_outbox_batch_id");
            e.HasIndex(x => x.CreatedAt).HasDatabaseName("ix_mail_outbox_created_at");
            // التذكير ورسالة التأخر اليومية تبحثان بالنوع والتاريخ كل عشر دقائق.
            e.HasIndex(x => new { x.Kind, x.CreatedAt }).HasDatabaseName("ix_mail_outbox_kind_created_at");
            e.HasIndex(x => x.ResentFromId).IsUnique().HasFilter("resent_from_id IS NOT NULL").HasDatabaseName("ux_mail_outbox_resent_from_id");
        });
        b.Entity<MessageTemplate>(e =>
        {
            e.ToTable("message_templates", "adm");
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(60);
            e.Property(x => x.Module).HasMaxLength(8);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.SubjectAr).HasMaxLength(200);
            e.Property(x => x.SubjectEn).HasMaxLength(200);
            e.Property(x => x.BodyAr).HasMaxLength(4000);
            e.Property(x => x.BodyEn).HasMaxLength(4000);
            e.Property(x => x.Version).IsRowVersion();
        });

        ConfigureTier1(b);
        ConfigureIntegration(b);
    }
}


/// <summary>تخزين قيمة مركبة (أقسام الدرجات، درجات الأقسام) في عمود jsonb بمقارنة تتبع التغيير بالقيمة.</summary>
internal static class JsonColumns
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> HasJsonConversion<T>(this Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<T> p)
        where T : class, new()
    {
        var comparer = new Microsoft.EntityFrameworkCore.ChangeTracking.ValueComparer<T>(
            (a, b) => System.Text.Json.JsonSerializer.Serialize(a, Options) == System.Text.Json.JsonSerializer.Serialize(b, Options),
            v => System.Text.Json.JsonSerializer.Serialize(v, Options).GetHashCode(StringComparison.Ordinal),
            v => System.Text.Json.JsonSerializer.Deserialize<T>(System.Text.Json.JsonSerializer.Serialize(v, Options), Options)!);
        p.HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, Options),
                v => System.Text.Json.JsonSerializer.Deserialize<T>(v, Options) ?? new T(),
                comparer)
            .HasColumnType("jsonb");
        return p;
    }
}
