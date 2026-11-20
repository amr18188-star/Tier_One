using Microsoft.Extensions.Configuration;
using Tier1.Application.Abstractions;
using Tier1.Domain.Notifications;

namespace Tier1.Application.Notifications;

/// <summary>A staff member to notify: the inbox is theirs by id, the mail goes to their address.</summary>
public sealed record Recipient(Guid UserId, string Email);

/// <summary>
/// One message to one person on every channel the message goes by: the in-app inbox always (requirement 4-15), and mail.
/// Writes only; the caller's unit of work commits it (an event handler's, so the message follows the change that caused it).
/// </summary>
public interface INotificationPublisher
{
    /// <param name="link">An in-app path such as /forms/{id}; the mail carries it as a full address.</param>
    /// <param name="dedupKey">Makes the mail unique (the daily reminder).</param>
    void Publish(Recipient to, string kind, string title, string body, string? link, string? dedupKey = null);
}

public sealed class NotificationPublisher(INotificationStore db,  IClock clock, IMailOutbox mail, IConfiguration config) : INotificationPublisher
{
    public const string RelatedType = "notification";

    public void Publish(Recipient to, string kind, string title, string body, string? link, string? dedupKey = null)
    {
        var now = clock.UtcNow;
        var item = new InboxItem
        {
            Id = Guid.CreateVersion7(now),
            UserId = to.UserId,
            Kind = kind,
            Title = title.Length > 300 ? title[..300] : title,
            Body = body.Length > 4000 ? body[..4000] : body,
            Link = link,
            CreatedAt = now,
        };
        db.Inbox.Add(item);
        mail.Enqueue(kind, to.Email, item.Title, MailBody(item), dedupKey, RelatedType, item.Id);
    }

    /// <summary>The mail text of an inbox item: its body and the full address of its link. Used again to resend a failed mail.</summary>
    public string MailBody(InboxItem item) => item.Link is null ? item.Body : $"{item.Body}\n{PublicUrl.Of(config)}{item.Link}";
}

/// <summary>The address users open the system at, for links in mail (BE-9: startup refuses a production host without it).</summary>
public static class PublicUrl
{
    public const string DevelopmentDefault = "http://localhost:5150";

    public static string Of(IConfiguration config) => (config["App:PublicUrl"] is { Length: > 0 } u ? u : DevelopmentDefault).TrimEnd('/');
}
