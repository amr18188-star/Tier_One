using Microsoft.EntityFrameworkCore;
using Tier1.Domain.Kpi;
using Tier1.Domain.Organization;
using Tier1.Domain.Structure;

namespace Tier1.Infrastructure.Persistence;

/// <summary>جداول Tier 1: الجهات والهيكل (org)، والمؤشرات والمستهدفات والتعارضات (kpi).</summary>
public sealed partial class Tier1DbContext
{
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();

    public DbSet<OrgUnitChange> OrgUnitChanges => Set<OrgUnitChange>();

    public DbSet<StructureUnit> StructureUnits => Set<StructureUnit>();

    public DbSet<StructurePosition> StructurePositions => Set<StructurePosition>();

    public DbSet<StructureMember> StructureMembers => Set<StructureMember>();

    public DbSet<IndustryPartner> IndustryPartners => Set<IndustryPartner>();

    public DbSet<Pillar> Pillars => Set<Pillar>();

    public DbSet<Standard> Standards => Set<Standard>();

    public DbSet<Initiative> Initiatives => Set<Initiative>();

    public DbSet<Indicator> Indicators => Set<Indicator>();

    public DbSet<IndicatorVersion> IndicatorVersions => Set<IndicatorVersion>();

    public DbSet<IndicatorTarget> Targets => Set<IndicatorTarget>();

    public DbSet<TargetRevision> TargetRevisions => Set<TargetRevision>();

    public DbSet<DataConflict> Conflicts => Set<DataConflict>();

    private static void ConfigureTier1(ModelBuilder b)
    {
        ConfigureOrganization(b);
        ConfigureDictionary(b);
        ConfigureTargets(b);
        ConfigureCollection(b);
    }

    private static void ConfigureOrganization(ModelBuilder b)
    {
        b.Entity<OrgUnit>(e =>
        {
            e.ToTable("units", "org", t => t.HasCheckConstraint("ck_units_code", "code ~ '^[A-Z0-9][A-Z0-9-]{0,31}$'"));
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Path);
            e.HasIndex(x => x.ParentId);
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Path).HasMaxLength(400);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.MergedIntoId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<OrgUnitChange>(e =>
        {
            e.ToTable("unit_changes", "org");
            e.HasIndex(x => new { x.UnitId, x.EffectiveOn });
            e.Property(x => x.OldValue).HasMaxLength(400);
            e.Property(x => x.NewValue).HasMaxLength(400);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<StructureUnit>(e =>
        {
            e.ToTable("structure_units", "org");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(32);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.RoleAr).HasMaxLength(300);
            e.HasMany(x => x.Positions).WithOne().HasForeignKey(p => p.UnitId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StructurePosition>(e =>
        {
            e.ToTable("structure_positions", "org");
            e.Property(x => x.TitleAr).HasMaxLength(150);
            e.Property(x => x.SuggestedRoleCode).HasMaxLength(16);
        });
        b.Entity<StructureMember>(e =>
        {
            e.ToTable("structure_members", "org", t => t.HasCheckConstraint("ck_members_period", "end_date IS NULL OR end_date >= start_date"));
            e.HasIndex(x => new { x.CollegeId, x.PositionId });
            e.HasIndex(x => x.DepartmentId);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Employer).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.EndReason).HasMaxLength(500);
            e.Property(x => x.DecisionRef).HasMaxLength(200);
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.CollegeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<StructurePosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<IndustryPartner>(e =>
        {
            e.ToTable("industry_partners", "org");
            e.HasIndex(x => x.DepartmentId);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.Sector).HasMaxLength(150);
            e.Property(x => x.CoordinatorName).HasMaxLength(200);
            e.Property(x => x.CoordinatorEmail).HasMaxLength(254);
            e.Property(x => x.CoordinatorPhone).HasMaxLength(30);
            e.Property(x => x.AgreementRef).HasMaxLength(200);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.CollegeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureDictionary(ModelBuilder b)
    {
        b.Entity<Pillar>(e =>
        {
            e.ToTable("pillars", "kpi");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Version).IsRowVersion();
        });
        b.Entity<Standard>(e =>
        {
            e.ToTable("standards", "kpi");
            e.HasIndex(x => x.Code).IsUnique();
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.NameAr).HasMaxLength(200);
            e.Property(x => x.NameEn).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.SourcePages).HasMaxLength(50);
            e.Property(x => x.ClassificationNote).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Pillar>().WithMany().HasForeignKey(x => x.PillarId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Initiative>(e =>
        {
            e.ToTable("initiatives", "kpi", t => t.HasCheckConstraint("ck_initiatives_years", "end_year >= start_year"));
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.StandardId);
            e.Property(x => x.Code).HasMaxLength(20);
            e.Property(x => x.NameAr).HasMaxLength(300);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.ExpectedOutcome).HasMaxLength(2000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Standard>().WithMany().HasForeignKey(x => x.StandardId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OwnerUnitId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Indicator>(e =>
        {
            e.ToTable("indicators", "kpi");
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.StandardId);
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.PlanNote).HasMaxLength(2000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Standard>().WithMany().HasForeignKey(x => x.StandardId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.IndicatorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<IndicatorVersion>(e =>
        {
            e.ToTable("indicator_versions", "kpi");
            e.HasIndex(x => new { x.IndicatorId, x.Number }).IsUnique();
            // إصدار واحد قيد العمل لكل مؤشر، حتى مع تعديلين متزامنين.
            e.HasIndex(x => x.IndicatorId).IsUnique().HasFilter("status IN (1, 2)").HasDatabaseName("ux_indicator_versions_one_working");
            e.Property(x => x.NameAr).HasMaxLength(300);
            e.Property(x => x.NameEn).HasMaxLength(300);
            e.Property(x => x.AlternateName).HasMaxLength(300);
            e.Property(x => x.Definition).HasMaxLength(2000);
            e.Property(x => x.Purpose).HasMaxLength(1000);
            e.Property(x => x.Exclusions).HasMaxLength(1000);
            e.Property(x => x.Numerator).HasMaxLength(500);
            e.Property(x => x.Denominator).HasMaxLength(500);
            e.Property(x => x.Unit).HasMaxLength(50);
            e.Property(x => x.AggregationNote).HasMaxLength(500);
            e.Property(x => x.DataSource).HasMaxLength(500);
            e.Property(x => x.EvidenceRequired).HasMaxLength(1000);
            e.Property(x => x.SourcePages).HasMaxLength(100);
            e.Property(x => x.ChangeReason).HasMaxLength(500);
            e.Property(x => x.DecisionRef).HasMaxLength(200);
            e.Property(x => x.ReturnNote).HasMaxLength(1000);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.OwnerUnitId).OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureTargets(ModelBuilder b)
    {
        b.Entity<IndicatorTarget>(e =>
        {
            e.ToTable("targets", "kpi", t => t.HasCheckConstraint("ck_targets_year", "year BETWEEN 2000 AND 2100"));
            e.HasIndex(x => new { x.IndicatorId, x.UnitId, x.Year, x.Kind }).IsUnique();
            e.Property(x => x.Value).HasPrecision(20, 4);
            e.Property(x => x.Numerator).HasPrecision(20, 4);
            e.Property(x => x.Denominator).HasPrecision(20, 4);
            e.Property(x => x.Text).HasMaxLength(300);
            e.Property(x => x.Source).HasMaxLength(300);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.Property(x => x.DecisionRef).HasMaxLength(200);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<Indicator>().WithMany().HasForeignKey(x => x.IndicatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<OrgUnit>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TargetRevision>(e =>
        {
            e.ToTable("target_revisions", "kpi");
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.HasIndex(x => x.TargetId);
            e.Property(x => x.OldValue).HasPrecision(20, 4);
            e.Property(x => x.NewValue).HasPrecision(20, 4);
            e.Property(x => x.OldText).HasMaxLength(300);
            e.Property(x => x.NewText).HasMaxLength(300);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.DecisionRef).HasMaxLength(200);
            e.HasOne<IndicatorTarget>().WithMany().HasForeignKey(x => x.TargetId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DataConflict>(e =>
        {
            e.ToTable("conflicts", "kpi");
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.Status);
            e.Property(x => x.Code).HasMaxLength(16);
            e.Property(x => x.Type).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.ValueA).HasMaxLength(2000);
            e.Property(x => x.ValueB).HasMaxLength(2000);
            e.Property(x => x.Proposal).HasMaxLength(2000);
            e.Property(x => x.PreparationNote).HasMaxLength(2000);
            e.Property(x => x.Decision).HasMaxLength(2000);
            e.Property(x => x.DecidingBody).HasMaxLength(200);
            e.Property(x => x.MinutesRef).HasMaxLength(200);
            e.Property(x => x.Version).IsRowVersion();
        });
    }
}
