using Microsoft.EntityFrameworkCore;
using Tier1.Domain.Collection;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;

namespace Tier1.Infrastructure.Persistence;

/// <summary>جمع البيانات (dc): الدورات ولقطة مؤشراتها، ونماذج الجهات وقيمها ومسار اعتمادها، والمستندات المرفوعة.</summary>
public sealed partial class Tier1DbContext
{
    public DbSet<CollectionCycle> Cycles => Set<CollectionCycle>();

    public DbSet<CycleIndicator> CycleIndicators => Set<CycleIndicator>();

    public DbSet<Submission> Submissions => Set<Submission>();

    public DbSet<SubmittedValue> SubmittedValues => Set<SubmittedValue>();

    public DbSet<SubmissionEvent> SubmissionEvents => Set<SubmissionEvent>();

    public DbSet<ReopenRequest> ReopenRequests => Set<ReopenRequest>();

    public DbSet<UniversityValueChange> UniversityValueChanges => Set<UniversityValueChange>();

    public DbSet<ValueSnapshot> ValueSnapshots => Set<ValueSnapshot>();

    public DbSet<StoredDocument> Documents => Set<StoredDocument>();

    public DbSet<UniversityValue> UniversityValues => Set<UniversityValue>();

    private static void ConfigureCollection(ModelBuilder b)
    {
        b.Entity<CollectionCycle>(e =>
        {
            e.ToTable("cycles", "dc", t => t.HasCheckConstraint("ck_cycles_dates", "due_on >= opens_on"));
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.PeriodLabel).HasMaxLength(200);
            e.Property(x => x.Version).IsRowVersion();
        });
        b.Entity<CycleIndicator>(e =>
        {
            e.ToTable("cycle_indicators", "dc");
            e.HasKey(x => new { x.CycleId, x.IndicatorId });
            e.Property(x => x.Code).HasMaxLength(16);
            e.HasOne<CollectionCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Indicator>().WithMany().HasForeignKey(x => x.IndicatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<IndicatorVersion>().WithMany().HasForeignKey(x => x.VersionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Submission>(e =>
        {
            e.ToTable("submissions", "dc");
            // نموذج واحد لكل جهة في الدورة.
            e.HasIndex(x => new { x.CycleId, x.UnitId }).IsUnique();
            e.HasIndex(x => new { x.CycleId, x.Status });
            e.Property(x => x.ReturnNote).HasMaxLength(2000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<CollectionCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SubmittedValue>(e =>
        {
            e.ToTable("values", "dc", t =>
            {
                // الرقم وحده يحمل قيمة؛ «غير متاح» و«غير منطبق» بلا قيمة وبسبب.
                t.HasCheckConstraint("ck_values_kind", "(kind = 1) = (value IS NOT NULL) AND (kind = 1 OR reason IS NOT NULL)");
                t.HasCheckConstraint("ck_values_ratio", "denominator IS NULL OR denominator > 0");
            });
            // تكرار قيمة المؤشر نفسه للجهة والدورة ممنوع في القاعدة أيضًا.
            e.HasIndex(x => new { x.SubmissionId, x.IndicatorId }).IsUnique();
            e.Property(x => x.Value).HasPrecision(20, 4);
            e.Property(x => x.Numerator).HasPrecision(20, 4);
            e.Property(x => x.Denominator).HasPrecision(20, 4);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Explanation).HasMaxLength(1000);
            e.Property(x => x.ReviewNote).HasMaxLength(1000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Indicator>().WithMany().HasForeignKey(x => x.IndicatorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<SubmissionEvent>(e =>
        {
            e.ToTable("submission_events", "dc");
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.SubmissionId);
            e.Property(x => x.ByName).HasMaxLength(200);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReopenRequest>(e =>
        {
            e.ToTable("reopen_requests", "dc", t =>
            {
                // الاعتماد برقم محضر، والرفض بسبب (D4).
                t.HasCheckConstraint("ck_reopen_requests_decision",
                    "(status = 1 AND decided_at IS NULL) OR (status = 2 AND decided_at IS NOT NULL AND decision_ref IS NOT NULL) OR (status = 3 AND decided_at IS NOT NULL AND decision_note IS NOT NULL)");
                t.HasCheckConstraint("ck_reopen_requests_sod", "decided_by IS NULL OR decided_by <> requested_by");
            });
            // طلب معلق واحد لكل نموذج.
            e.HasIndex(x => x.SubmissionId).IsUnique().HasFilter("status = 1").HasDatabaseName("ux_reopen_requests_one_pending");
            e.HasIndex(x => new { x.SubmissionId, x.Status });
            e.Property(x => x.Reason).HasMaxLength(2000);
            e.Property(x => x.RequestedByName).HasMaxLength(200);
            e.Property(x => x.DecidedByName).HasMaxLength(200);
            e.Property(x => x.DecisionRef).HasMaxLength(200);
            e.Property(x => x.DecisionNote).HasMaxLength(2000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ValueSnapshot>(e =>
        {
            e.ToTable("value_snapshots", "dc");
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.RequestId, x.IndicatorId }).IsUnique();
            e.HasIndex(x => x.SubmissionId);
            e.Property(x => x.Value).HasPrecision(20, 4);
            e.Property(x => x.Numerator).HasPrecision(20, 4);
            e.Property(x => x.Denominator).HasPrecision(20, 4);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.Explanation).HasMaxLength(1000);
            e.HasOne<ReopenRequest>().WithMany().HasForeignKey(x => x.RequestId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Submission>().WithMany().HasForeignKey(x => x.SubmissionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Indicator>().WithMany().HasForeignKey(x => x.IndicatorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UniversityValueChange>(e =>
        {
            e.ToTable("university_value_changes", "dc");
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => new { x.IndicatorId, x.Year, x.Quarter, x.ChangedAt });
            e.Property(x => x.OldValue).HasPrecision(20, 4);
            e.Property(x => x.NewValue).HasPrecision(20, 4);
            e.Property(x => x.ComputedValue).HasPrecision(20, 4);
            e.Property(x => x.Reason).HasMaxLength(1000);
            e.Property(x => x.ChangedByName).HasMaxLength(200);
            e.HasOne<Indicator>().WithMany().HasForeignKey(x => x.IndicatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CollectionCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<UniversityValue>(e =>
        {
            e.ToTable("university_values", "dc", t => t.HasCheckConstraint("ck_university_values_quarter", "quarter BETWEEN 1 AND 4"));
            // قيمة رسمية واحدة لكل مؤشر في كل ربع.
            e.HasIndex(x => new { x.IndicatorId, x.Year, x.Quarter }).IsUnique();
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.TransferredByName).HasMaxLength(200);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Indicator>().WithMany().HasForeignKey(x => x.IndicatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CollectionCycle>().WithMany().HasForeignKey(x => x.CycleId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StoredDocument>(e =>
        {
            e.ToTable("documents", "dc");
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
            e.Property(x => x.OwnerType).HasMaxLength(16);
            e.Property(x => x.FileName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(120);
            e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.StorageKey).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(300);
            e.Property(x => x.VoidReason).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
        });
    }
}
