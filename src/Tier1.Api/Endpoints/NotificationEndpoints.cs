using Tier1.Api.Security;
using Tier1.Application.Notifications;
using Tier1.Domain.Access;

namespace Tier1.Api.Endpoints;

/// <summary>The user's in-app inbox (requirement 4-15), and the outgoing mail for administrators (BE-11, adm.mail).</summary>
public static class NotificationEndpoints
{
    public static void MapNotifications(this RouteGroupBuilder api)
    {
        var inbox = api.MapGroup("/notifications").RequireAuthorization(Policies.Staff);
        // The bell polls this; the X-Passive header keeps the poll from extending an idle session.
        inbox.MapGet("/", async (InboxService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(ct)));
        inbox.MapPost("/{id:guid}/read", async (Guid id, InboxService svc, CancellationToken ct) =>
        {
            await svc.ReadAsync(id, ct);
            return Results.NoContent();
        });
        inbox.MapPost("/read-all", async (InboxService svc, CancellationToken ct) => Results.Ok(new { read = await svc.ReadAllAsync(ct) }));

        var mail = api.MapGroup("/admin/mail");
        mail.MapGet("/", async (string? status, MailAdminService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(status, ct)))
            .RequirePermission(MailAdminService.Feature, Cap.View);
        mail.MapPost("/{id:long}/resend", async (long id, MailAdminService svc, CancellationToken ct) => Results.Ok(await svc.ResendAsync(id, ct)))
            .RequirePermission(MailAdminService.Feature, Cap.Edit);
    }
}
