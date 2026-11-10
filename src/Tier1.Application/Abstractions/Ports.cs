using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Tier1.Domain.Access;
using Tier1.Domain.Calendar;
using Tier1.Domain.Identity;
using Tier1.Domain.Kpi;
using Tier1.Domain.Logging;
using Tier1.Domain.Notifications;
using Tier1.Domain.Organization;
using Tier1.Domain.Structure;

namespace Tier1.Application.Abstractions;

/// <summary>قاعدة بيانات العمل (PostgreSQL) كما تراها طبقة التطبيق.</summary>
public interface IAppDb
{
    DbSet<AppUser> Users { get; }

    DbSet<UserSession> Sessions { get; }

    DbSet<Role> Roles { get; }

    DbSet<RoleGrant> RoleGrants { get; }

    DbSet<UserRole> UserRoles { get; }

    DbSet<UserOverride> UserOverrides { get; }

    DbSet<RbacChange> RbacChanges { get; }

    DbSet<AccessState> AccessStates { get; }

    DbSet<OutboxEvent> Outbox { get; }

    DbSet<ChainState> ChainStates { get; }

    DbSet<ChainAnchor> ChainAnchors { get; }

    DbSet<ChainCheckpoint> ChainCheckpoints { get; }

    DbSet<Holiday> Holidays { get; }

    DbSet<Domain.Common.SystemSetting> Settings { get; }

    DbSet<OutgoingMail> OutgoingMails { get; }

    DbSet<MessageTemplate> MessageTemplates { get; }

    DbSet<OrgUnit> OrgUnits { get; }

    DbSet<OrgUnitChange> OrgUnitChanges { get; }

    DbSet<StructureUnit> StructureUnits { get; }

    DbSet<StructurePosition> StructurePositions { get; }

    DbSet<StructureMember> StructureMembers { get; }

    DbSet<IndustryPartner> IndustryPartners { get; }

    DbSet<Pillar> Pillars { get; }

    DbSet<Standard> Standards { get; }

    DbSet<Indicator> Indicators { get; }

    DbSet<IndicatorVersion> IndicatorVersions { get; }

    DbSet<IndicatorTarget> Targets { get; }

    DbSet<TargetRevision> TargetRevisions { get; }

    DbSet<DataConflict> Conflicts { get; }

    DatabaseFacade Database { get; }

    /// <summary>لمحو ما تتبعه العملية بعد خطوة فاشلة اختيارية، فتكمل بحالة القاعدة.</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

    /// <summary>لضبط الإصدار المتوقع لصف قبل حفظه (التعديل المتزامن).</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Entry<TEntity>(TEntity entity) where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

/// <summary>هوية الطلب الحالي. الوظائف الخلفية لها هوية «النظام» بلا عنوان IP.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    Guid? SessionId { get; }

    string? DisplayName { get; }

    string? Ip { get; }

    string? UserAgent { get; }

    string TraceId { get; }

    /// <summary>web أو job.</summary>
    string Channel { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    (bool Valid, bool NeedsRehash) Verify(string hash, string password);
}

/// <summary>رمز QR لتسجيل تطبيق المصادقة عند تفعيل التحقق بخطوتين.</summary>
public interface IQrCodes
{
    string PngDataUri(string content, int pixelsPerModule);
}

public interface ISecretProtector
{
    string Protect(string plain);

    string? Unprotect(string protectedValue);
}

/// <summary>
/// الصندوق الصادر للبريد: يضاف في معاملة العملية نفسها (فلا يرسل بريد لعملية فشلت)، ويرسله عامل الخلفية بإعادة المحاولة.
/// </summary>
public interface IMailOutbox
{
    void Enqueue(string kind, string to, string subject, string body);
}

/// <summary>تخزين الملفات (الأدلة وقرارات التشكيل) خارج القاعدة بمفتاح لا باسم الملف الأصلي.</summary>
public interface IFileStore
{
    /// <summary>أقصى حجم للملف بالبايت.</summary>
    long MaxBytes { get; }

    Task SaveAsync(string key, Stream content, CancellationToken ct);

    Stream OpenRead(string key);
}
