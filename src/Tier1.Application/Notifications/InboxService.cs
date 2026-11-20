using Microsoft.EntityFrameworkCore;
using Tier1.Application.Abstractions;
using Tier1.Domain.Common;

namespace Tier1.Application.Notifications;

public sealed record InboxItemDto(Guid Id, string Kind, string Title, string Body, string? Link, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);

public sealed record InboxDto(IReadOnlyList<InboxItemDto> Items, int Unread);

/// <summary>The signed-in user's own notifications (requirement 4-15). Nobody reads another user's inbox, so there is no permission beyond the session.</summary>
public sealed class InboxService(INotificationStore db, ICurrentUser current, IClock clock)
{
    public const int Shown = 50;

    private Guid Me => current.UserId ?? throw DomainException.Forbidden();

    public async Task<InboxDto> ListAsync(CancellationToken ct)
    {
        var me = Me;
        var items = await db.Inbox.AsNoTracking().Where(i => i.UserId == me).OrderByDescending(i => i.CreatedAt).Take(Shown)
            .Select(i => new InboxItemDto(i.Id, i.Kind, i.Title, i.Body, i.Link, i.CreatedAt, i.ReadAt)).ToListAsync(ct);
        var unread = await db.Inbox.CountAsync(i => i.UserId == me && i.ReadAt == null, ct);
        return new InboxDto(items, unread);
    }

    /// <summary>Marks one item read. Another user's item is not found (404), as if it did not exist.</summary>
    public async Task ReadAsync(Guid id, CancellationToken ct)
    {
        var me = Me;
        var n = await db.Inbox.Where(i => i.Id == id && i.UserId == me && i.ReadAt == null).ExecuteUpdateAsync(u => u.SetProperty(i => i.ReadAt, clock.UtcNow), ct);
        if (n == 0 && !await db.Inbox.AnyAsync(i => i.Id == id && i.UserId == me, ct)) throw DomainException.NotFound();
    }

    public async Task<int> ReadAllAsync(CancellationToken ct)
    {
        var me = Me;
        return await db.Inbox.Where(i => i.UserId == me && i.ReadAt == null).ExecuteUpdateAsync(u => u.SetProperty(i => i.ReadAt, clock.UtcNow), ct);
    }
}
