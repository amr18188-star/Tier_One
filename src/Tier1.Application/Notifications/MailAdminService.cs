using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Common;

namespace Tier1.Application.Notifications;

public sealed record MailRow(
    long Id, string Kind, string? To, string Subject, DateTimeOffset CreatedAt, int Attempts, string? LastError,
    DateTimeOffset? SentAt, DateTimeOffset? FailedAt, string Status, bool Resendable, long? ResentAs, long? ResentFrom);

/// <summary>
/// BE-11: the outgoing mail as administrators see it (adm.mail): what waits, what was sent, what failed and why, with addresses
/// masked. A failed notification is resent from its inbox item, to the user's current address; an account mail (a sign-in link)
/// is not, since the user asks for a new one.
/// </summary>
public sealed class MailAdminService(IAppDb db, IAccessService access, IAuditLog audit, IMailOutbox mail, NotificationPublisher publisher)
{
    public const string Feature = Features.Mail;

    public const int Shown = 200;

    public async Task<IReadOnlyList<MailRow>> ListAsync(string? status, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.View, ct);
        var q = db.OutgoingMails.AsNoTracking();
        q = status switch
        {
            "failed" => q.Where(m => m.FailedAt != null),
            "pending" => q.Where(m => m.SentAt == null && m.FailedAt == null && m.CancelledAt == null && m.SkippedReason == null),
            "sent" => q.Where(m => m.SentAt != null),
            null or "" or "all" => q,
            _ => throw new DomainException("invalid_status", "حالة غير معروفة.", "Unknown status.", 400),
        };
        var rows = await q.OrderByDescending(m => m.Id).Take(Shown).ToListAsync(ct);
        var ids = rows.Select(r => r.Id).ToList();
        var resent = await db.OutgoingMails.AsNoTracking().Where(m => m.ResentFromId != null && ids.Contains(m.ResentFromId.Value))
            .ToDictionaryAsync(m => m.ResentFromId!.Value, m => m.Id, ct);
        return rows.Select(m =>
        {
            var resentAs = resent.TryGetValue(m.Id, out var r) ? r : (long?)null;
            var state = m.SentAt is not null ? "sent" : m.FailedAt is not null ? "failed" : m.CancelledAt is not null ? "cancelled" : m.SkippedReason is not null ? "skipped" : "pending";
            var resendable = m.FailedAt is not null && m.RelatedType == NotificationPublisher.RelatedType && resentAs is null;
            return new MailRow(m.Id, m.Kind, Mask.Email(m.To), m.Subject, m.CreatedAt, m.Attempts, m.LastError, m.SentAt, m.FailedAt, state, resendable, resentAs, m.ResentFromId);
        }).ToList();
    }

    public async Task<MailRow> ResendAsync(long id, CancellationToken ct)
    {
        await access.RequireAsync(Feature, Cap.Edit, ct);
        var failed = await db.OutgoingMails.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct) ?? throw DomainException.NotFound();
        if (failed.FailedAt is null)
            throw new DomainException("not_failed", "تعاد الرسالة الفاشلة فقط.", "Only a failed mail can be resent.", 409);
        if (failed.RelatedType != NotificationPublisher.RelatedType || failed.RelatedId is not { } itemId)
            throw new DomainException("not_resendable", "رسائل الحساب (روابط الدخول والاستعادة) لا تعاد؛ يطلب صاحب الحساب رابطًا جديدًا.",
                "Account mails are not resent; the user asks for a new link.", 409);
        if (await db.OutgoingMails.AnyAsync(m => m.ResentFromId == id, ct))
            throw new DomainException("already_resent", "أعيد إرسال هذه الرسالة من قبل.", "This mail was already resent.", 409);
        var item = await db.Inbox.AsNoTracking().FirstOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw new DomainException("not_resendable", "لم يعد الإشعار موجودًا.", "The notification no longer exists.", 409);
        var to = await db.Users.AsNoTracking().Where(u => u.Id == item.UserId && u.Active).Select(u => u.Email).FirstOrDefaultAsync(ct)
            ?? throw new DomainException("recipient_inactive", "صاحب الإشعار موقوف أو محذوف.", "The recipient is no longer active.", 409);
        mail.Enqueue(item.Kind, to, item.Title, publisher.MailBody(item), null, NotificationPublisher.RelatedType, item.Id, resentFromId: id);
        audit.Record(AuditEvents.Audit("adm", Feature, "adm.mail_resent", "إعادة إرسال بريد فاشل", "Resent a failed mail") with
        {
            Target = new AuditTarget("mail", id.ToString(System.Globalization.CultureInfo.InvariantCulture), Mask.Email(to)),
            Details = new { kind = item.Kind, error = failed.LastError },
        });
        await db.SaveChangesAsync(ct);
        return (await ListAsync(null, ct)).First(m => m.ResentFrom == id);
    }
}
