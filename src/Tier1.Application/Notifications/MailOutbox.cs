using Tier1.Application.Abstractions;
using Tier1.Domain.Notifications;

namespace Tier1.Application.Notifications;

/// <summary>إشعار عامل البريد بوجود رسائل جديدة دون انتظار دورته التالية.</summary>
public interface IMailSignal
{
    void Notify();
}

/// <summary>يضيف الرسالة إلى الصندوق الصادر في معاملة العملية؛ النص مشفر حتى يرسل ثم يمحى.</summary>
public sealed class MailOutbox(INotificationStore db,  IClock clock, ICurrentUser current, ISecretProtector protector, IMailSignal signal) : IMailOutbox
{
    public void Enqueue(string kind, string to, string subject, string body, string? dedupKey = null, string? relatedType = null, Guid? relatedId = null, long? resentFromId = null)
    {
        var now = clock.UtcNow;
        db.OutgoingMails.Add(new OutgoingMail
        {
            Kind = kind,
            To = to,
            Subject = subject.Length > 300 ? subject[..300] : subject,
            BodyProtected = protector.Protect(body),
            CreatedAt = now,
            NextAttemptAt = now,
            Audience = MailAudience.Staff,
            Module = "adm",
            CreatedBy = current.UserId,
            DedupKey = dedupKey,
            RelatedType = relatedType,
            RelatedId = relatedId,
            ResentFromId = resentFromId,
        });
        signal.Notify();
    }
}
