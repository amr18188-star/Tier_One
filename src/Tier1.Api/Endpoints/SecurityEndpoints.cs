using Tier1.Api.Security;
using Tier1.Api.Startup;
using Tier1.Application.Access;
using Tier1.Domain.Access;

namespace Tier1.Api.Endpoints;

/// <summary>
/// ADR-0016: approved university email domains, the global IP allowlist and each account's own list (adm.security). The services
/// check the permission again, and a user's own list is managed by another administrator (SOD-9).
/// </summary>
public static class SecurityEndpoints
{
    public static void MapSecurity(this RouteGroupBuilder api)
    {
        var security = api.MapGroup("/security").RequireRateLimiting(ApiSetup.SensitiveLimit);

        // ----- approved university email domains
        var domains = security.MapGroup("/domains");
        domains.MapGet("/", async (UniversityDomainsService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(ct)))
            .RequirePermission(Features.Security, Cap.View);
        // The active roots alone, to check an email in the account forms before sending it.
        domains.MapGet("/active", async (UniversityDomainsService svc, CancellationToken ct) => TypedResults.Ok(await svc.ActiveRootsAsync(ct)))
            .RequireAnyPermission("adm.users:A", "adm.users:E", "adm.security:V");
        domains.MapPost("/", async (SaveEmailDomainRequest req, UniversityDomainsService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateAsync(req, ct)))
            .RequirePermission(Features.Security, Cap.Add);
        domains.MapPut("/{id:guid}", async (Guid id, SaveEmailDomainRequest req, UniversityDomainsService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdateAsync(id, req, ct)))
            .RequirePermission(Features.Security, Cap.Edit);
        domains.MapPost("/{id:guid}/delete", async (Guid id, DeleteEmailDomainRequest req, UniversityDomainsService svc, CancellationToken ct) =>
        {
            await svc.DeleteAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Security, Cap.Delete);

        // ----- the global IP allowlist
        var global = security.MapGroup("/ip-allowlist");
        global.MapGet("/", async (IpAllowlistService svc, CancellationToken ct) => TypedResults.Ok(await svc.GlobalAsync(ct)))
            .RequirePermission(Features.Security, Cap.View);
        global.MapPut("/enabled", async (SetIpRestrictionRequest req, IpAllowlistService svc, CancellationToken ct) => TypedResults.Ok(await svc.SetGlobalAsync(req, ct)))
            .RequirePermission(Features.Security, Cap.Edit);
        global.MapPost("/entries", async (SaveIpEntryRequest req, IpAllowlistService svc, CancellationToken ct) => TypedResults.Ok(await svc.AddGlobalAsync(req, ct)))
            .RequirePermission(Features.Security, Cap.Add);
        global.MapPut("/entries/{entryId:guid}", async (Guid entryId, SaveIpEntryRequest req, IpAllowlistService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateGlobalAsync(entryId, req, ct))).RequirePermission(Features.Security, Cap.Edit);
        global.MapPost("/entries/{entryId:guid}/delete", async (Guid entryId, RemoveIpEntryRequest req, IpAllowlistService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RemoveGlobalAsync(entryId, req, ct))).RequirePermission(Features.Security, Cap.Delete);

        // ----- an account's own IP allowlist (shown in the account's details)
        var user = api.MapGroup("/iam/users/{id:guid}/ip-allowlist").RequireRateLimiting(ApiSetup.SensitiveLimit);
        user.MapGet("/", async (Guid id, IpAllowlistService svc, CancellationToken ct) => TypedResults.Ok(await svc.UserAsync(id, ct)))
            .RequirePermission(Features.Security, Cap.View);
        user.MapPut("/enabled", async (Guid id, SetIpRestrictionRequest req, IpAllowlistService svc, CancellationToken ct) => TypedResults.Ok(await svc.SetUserAsync(id, req, ct)))
            .RequirePermission(Features.Security, Cap.Edit);
        user.MapPost("/entries", async (Guid id, SaveIpEntryRequest req, IpAllowlistService svc, CancellationToken ct) => TypedResults.Ok(await svc.AddUserAsync(id, req, ct)))
            .RequirePermission(Features.Security, Cap.Add);
        user.MapPut("/entries/{entryId:guid}", async (Guid id, Guid entryId, SaveIpEntryRequest req, IpAllowlistService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.UpdateUserAsync(id, entryId, req, ct))).RequirePermission(Features.Security, Cap.Edit);
        user.MapPost("/entries/{entryId:guid}/delete", async (Guid id, Guid entryId, RemoveIpEntryRequest req, IpAllowlistService svc, CancellationToken ct) =>
            TypedResults.Ok(await svc.RemoveUserAsync(id, entryId, req, ct))).RequirePermission(Features.Security, Cap.Delete);
    }
}
