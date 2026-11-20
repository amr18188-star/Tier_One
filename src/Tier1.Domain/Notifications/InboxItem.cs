namespace Tier1.Domain.Notifications;

/// <summary>
/// A notification in a user's in-app inbox (notify.inbox), requirement 4-15. It is written with the mail that carries the same
/// message, so the user sees what was sent even when the mail fails, and a failed mail is resent from it.
/// </summary>
public sealed class InboxItem
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The kind of message, for example dc.form or dc.reminder.</summary>
    public string Kind { get; set; } = "";

    public string Title { get; set; } = "";

    public string Body { get; set; } = "";

    /// <summary>An in-app path (for example /forms/{id}), never an outside address.</summary>
    public string? Link { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}
