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
public interface IAppDb : IUnitOfWork
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

    DbSet<UnitIndicator> UnitIndicators { get; }

    DbSet<StructureUnit> StructureUnits { get; }

    DbSet<StructurePosition> StructurePositions { get; }

    DbSet<StructureMember> StructureMembers { get; }

    DbSet<IndustryPartner> IndustryPartners { get; }

    DbSet<Pillar> Pillars { get; }

    DbSet<Standard> Standards { get; }

    DbSet<Initiative> Initiatives { get; }

    DbSet<Indicator> Indicators { get; }

    DbSet<IndicatorVersion> IndicatorVersions { get; }

    DbSet<IndicatorTarget> Targets { get; }

    DbSet<TargetRevision> TargetRevisions { get; }

    DbSet<DataConflict> Conflicts { get; }

    DbSet<Domain.Collection.CollectionCycle> Cycles { get; }

    DbSet<Domain.Collection.CycleIndicator> CycleIndicators { get; }

    DbSet<Domain.Collection.Submission> Submissions { get; }

    DbSet<Domain.Collection.SubmittedValue> SubmittedValues { get; }

    DbSet<Domain.Collection.SubmissionEvent> SubmissionEvents { get; }

    DbSet<Domain.Collection.ReopenRequest> ReopenRequests { get; }

    DbSet<Domain.Collection.UniversityValueChange> UniversityValueChanges { get; }

    DbSet<Domain.Collection.ValueSnapshot> ValueSnapshots { get; }

    DbSet<Domain.Collection.StoredDocument> Documents { get; }

    DbSet<Domain.Collection.UniversityValue> UniversityValues { get; }

    DbSet<Domain.Integration.DomainEventRecord> DomainEvents { get; }

    DbSet<Domain.Integration.ProcessedEvent> ProcessedEvents { get; }

    DbSet<InboxItem> Inbox { get; }

    /// <summary>لمحو ما تتبعه العملية بعد خطوة فاشلة اختيارية، فتكمل بحالة القاعدة.</summary>
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker { get; }

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
    /// <param name="dedupKey">مفتاح فريد يمنع تكرار الرسالة نفسها (التذكير اليومي).</param>
    /// <param name="relatedType">What the mail carries (notification: an inbox item it can be resent from).</param>
    /// <param name="resentFromId">The failed mail this one resends (BE-11).</param>
    void Enqueue(string kind, string to, string subject, string body, string? dedupKey = null, string? relatedType = null, Guid? relatedId = null, long? resentFromId = null);
}

/// <summary>تخزين الملفات (الأدلة وقرارات التشكيل) خارج القاعدة بمفتاح لا باسم الملف الأصلي.</summary>
public interface IFileStore
{
    /// <summary>أقصى حجم للملف بالبايت.</summary>
    long MaxBytes { get; }

    Task SaveAsync(string key, Stream content, CancellationToken ct);

    Stream OpenRead(string key);

    /// <summary>يحذف الملف نهائيًا. عند إعادة ضبط البيانات فقط؛ في الاستخدام العادي تبطل الأدلة ولا تحذف.</summary>
    Task DeleteAsync(string key, CancellationToken ct);

    /// <summary>BE-19: every stored key with its last write time, and leftover partial writes, for the orphan sweep.</summary>
    IEnumerable<(string Key, DateTimeOffset WrittenAt, bool Partial)> List();

    /// <summary>Removes a partial write left by a failed upload.</summary>
    void DeletePartial(string key);
}
