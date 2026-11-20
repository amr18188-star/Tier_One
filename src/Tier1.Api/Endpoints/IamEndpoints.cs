using Tier1.Api.Security;
using Tier1.Api.Startup;
using Tier1.Application.Access;
using Tier1.Domain.Access;

namespace Tier1.Api.Endpoints;

public static class IamEndpoints
{
    public static void MapIam(this RouteGroupBuilder api)
    {
        var iam = api.MapGroup("/iam").RequireRateLimiting(ApiSetup.SensitiveLimit);

        // الكتالوج أسماء ومعانٍ فقط، يحتاجه كل من يرى شاشة صلاحيات أو حسابات.
        iam.MapGet("/catalog", () => TypedResults.Ok(RbacAdminService.Catalog()))
            .RequireAnyPermission("adm.roles:V", "adm.grants:V", "adm.users:V");

        // ----- الأدوار والمصفوفة -----
        iam.MapGet("/roles", async (RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListRolesAsync(ct)))
            .RequireAnyPermission("adm.roles:V", "adm.grants:V");
        iam.MapGet("/roles/{id:guid}", async (Guid id, RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetRoleAsync(id, ct)))
            .RequireAnyPermission("adm.roles:V", "adm.grants:V");
        iam.MapPost("/roles", async (CreateRoleRequest req, RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateRoleAsync(req, ct)))
            .RequirePermission(Features.Roles, Cap.Add);
        iam.MapPost("/roles/{id:guid}/delete", async (Guid id, ReasonRequest req, RbacAdminService svc, CancellationToken ct) =>
        {
            await svc.DeleteRoleAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Roles, Cap.Delete);
        iam.MapPut("/roles/{id:guid}/grants", async (Guid id, ProposeChangeRequest req, RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.ProposeAsync(id, req, ct)))
            .RequirePermission(Features.Roles, Cap.Edit);

        // ----- طلبات تعديل المصفوفة -----
        iam.MapGet("/changes", async (RbacChangeStatus? status, RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListChangesAsync(status, null, ct)))
            .RequirePermission(Features.Roles, Cap.View);
        iam.MapPost("/changes/{id:guid}/approve", async (Guid id, DecisionRequest req, RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.ApproveAsync(id, req.Note, ct)))
            .RequireSpecial(Features.Roles, Specials.Approve);
        iam.MapPost("/changes/{id:guid}/reject", async (Guid id, DecisionRequest req, RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.RejectAsync(id, req.Note, ct)))
            .RequireSpecial(Features.Roles, Specials.Approve);
        iam.MapPost("/changes/{id:guid}/cancel", async (Guid id, RbacAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.CancelAsync(id, ct)))
            .RequirePermission(Features.Roles, Cap.Edit);

        // ----- حسابات الموظفين -----
        var users = iam.MapGroup("/users");
        users.MapGet("/", async (string? q, UsersAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(q, ct)))
            .RequireAnyPermission("adm.users:V", "adm.grants:V");
        users.MapGet("/{id:guid}", async (Guid id, UsersAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(id, ct)))
            .RequireAnyPermission("adm.users:V", "adm.grants:V");
        users.MapPost("/", async (CreateUserRequest req, UserAccountsService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateAsync(req, ct)))
            .RequirePermission(Features.Users, Cap.Add);
        users.MapPut("/{id:guid}", async (Guid id, UpdateUserRequest req, UserAccountsService svc, CancellationToken ct) =>
        {
            await svc.UpdateAsync(id, req, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Users, Cap.Edit);
        users.MapPost("/{id:guid}/deactivate", async (Guid id, ReasonRequest req, UserAccountsService svc, CancellationToken ct) =>
        {
            await svc.DeactivateAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Users, Cap.Delete);
        users.MapPost("/{id:guid}/reactivate", async (Guid id, ReasonRequest req, UserAccountsService svc, CancellationToken ct) =>
        {
            await svc.ReactivateAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Users, Cap.Edit);
        users.MapPost("/{id:guid}/unlock", async (Guid id, ReasonRequest req, UserAccountsService svc, CancellationToken ct) =>
        {
            await svc.UnlockAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Users, Cap.Edit);
        users.MapPost("/{id:guid}/reset-mfa", async (Guid id, ReasonRequest req, UserAccountsService svc, CancellationToken ct) =>
        {
            await svc.ResetMfaAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Users, Cap.Edit);
        users.MapPost("/{id:guid}/temporary-password", async (Guid id, ReasonRequest req, UserAccountsService svc, CancellationToken ct) => TypedResults.Ok(await svc.SetTemporaryPasswordAsync(id, req.Reason, ct)))
            .RequireSpecial(Features.Users, Specials.SetPassword);

        // ----- الإسناد والاستثناءات والصلاحيات الفعلية -----
        users.MapGet("/{id:guid}/effective", async (Guid id, UsersAdminService svc, CancellationToken ct) => TypedResults.Ok(await svc.EffectiveAsync(id, ct)))
            .RequirePermission(Features.Grants, Cap.View);
        users.MapPost("/{id:guid}/roles", async (Guid id, AssignRoleRequest req, RoleAssignmentService svc, CancellationToken ct) => TypedResults.Ok(await svc.AssignRoleAsync(id, req, ct)))
            .RequirePermission(Features.Grants, Cap.Add);
        users.MapPost("/{id:guid}/overrides", async (Guid id, AddOverrideRequest req, UserOverridesService svc, CancellationToken ct) => TypedResults.Ok(await svc.AddOverrideAsync(id, req, ct)))
            .RequirePermission(Features.Grants, Cap.Add);
        iam.MapPut("/assignments/{id:guid}", async (Guid id, UpdateAssignmentRequest req, RoleAssignmentService svc, CancellationToken ct) =>
        {
            await svc.UpdateAssignmentAsync(id, req, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Grants, Cap.Edit);
        // اعتماد إسناد دور المدير الأعلى: للمدير الأعلى وحده، والخدمة تمنع الطالب ومن يتبعه.
        iam.MapPost("/assignments/{id:guid}/approve", async (Guid id, RoleAssignmentService svc, CancellationToken ct) =>
        {
            await svc.ApproveAssignmentAsync(id, ct);
            return Results.NoContent();
        }).RequireSpecial(Features.Roles, Specials.Approve);
        iam.MapPost("/assignments/{id:guid}/revoke", async (Guid id, ReasonRequest req, RoleAssignmentService svc, CancellationToken ct) =>
        {
            await svc.RevokeAssignmentAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Grants, Cap.Delete);
        iam.MapPost("/overrides/{id:guid}/revoke", async (Guid id, ReasonRequest req, UserOverridesService svc, CancellationToken ct) =>
        {
            await svc.RevokeOverrideAsync(id, req.Reason, ct);
            return Results.NoContent();
        }).RequirePermission(Features.Grants, Cap.Delete);
    }
}
