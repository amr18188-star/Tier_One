using Tier1.Api.Security;
using Tier1.Application.Access;
using Tier1.Application.Home;
using Tier1.Application.Kpi;
using Tier1.Application.Organization;
using Tier1.Application.Structure;
using Tier1.Domain.Access;
using Tier1.Domain.Kpi;

namespace Tier1.Api.Endpoints;

/// <summary>
/// مسارات Tier 1. كل مسار يعلن صلاحيته الأولية هنا، والخدمة تفحص النطاق والإجراء الخاص بدقة
/// (الجهة خارج النطاق تعامل كأنها غير موجودة).
/// </summary>
public static class Tier1Endpoints
{
    public static void MapTier1(this RouteGroupBuilder api)
    {
        api.MapGet("/home", async (HomeService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(ct)))
            .RequirePermission(Features.Dashboard, Cap.View);

        MapUnits(api.MapGroup("/org/units"));
        MapStructure(api.MapGroup("/structure"));
        MapDictionary(api.MapGroup("/kpi"));
        MapConflicts(api.MapGroup("/kpi/conflicts"));
    }

    private static void MapUnits(RouteGroupBuilder g)
    {
        g.MapGet("/", async (bool? includeInactive, OrgUnitsService svc, CancellationToken ct) => Results.Ok(await svc.ListAsync(includeInactive ?? false, ct)))
            .RequirePermission(Features.Units, Cap.View);
        g.MapPost("/", async (CreateOrgUnitRequest req, OrgUnitsService svc, CancellationToken ct) => Results.Ok(await svc.CreateAsync(req, ct)))
            .RequirePermission(Features.Units, Cap.Add);
        g.MapPut("/{id:guid}", async (Guid id, UpdateOrgUnitRequest req, OrgUnitsService svc, CancellationToken ct) => Results.Ok(await svc.UpdateAsync(id, req, ct)))
            .RequirePermission(Features.Units, Cap.Edit);
        g.MapPost("/{id:guid}/close", async (Guid id, CloseOrgUnitRequest req, OrgUnitsService svc, CancellationToken ct) => Results.Ok(await svc.CloseAsync(id, req, ct)))
            .RequirePermission(Features.Units, Cap.Delete);
        g.MapGet("/{id:guid}/history", async (Guid id, OrgUnitsService svc, CancellationToken ct) => Results.Ok(await svc.HistoryAsync(id, ct)))
            .RequirePermission(Features.Units, Cap.View);
    }

    private static void MapStructure(RouteGroupBuilder g)
    {
        g.MapGet("/template", async (StructureService svc, CancellationToken ct) => Results.Ok(await svc.TemplateAsync(ct)))
            .RequirePermission(Features.Structure, Cap.View);
        g.MapGet("/completeness", async (StructureService svc, CancellationToken ct) => Results.Ok(await svc.CompletenessAsync(ct)))
            .RequirePermission(Features.Structure, Cap.View);
        g.MapGet("/colleges/{id:guid}", async (Guid id, DateOnly? asOf, StructureService svc, CancellationToken ct) => Results.Ok(await svc.ChartAsync(id, asOf, ct)))
            .RequirePermission(Features.Structure, Cap.View);

        g.MapPost("/members", async (SaveMemberRequest req, StructureService svc, CancellationToken ct) => Results.Ok(await svc.AddMemberAsync(req, ct)))
            .RequirePermission(Features.Structure, Cap.Add);
        g.MapPut("/members/{id:guid}", async (Guid id, SaveMemberRequest req, StructureService svc, CancellationToken ct) => Results.Ok(await svc.UpdateMemberAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Edit);
        g.MapPost("/members/{id:guid}/end", async (Guid id, EndRequest req, StructureService svc, CancellationToken ct) => Results.Ok(await svc.EndMemberAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Delete);

        g.MapPost("/partners", async (SavePartnerRequest req, StructureService svc, CancellationToken ct) => Results.Ok(await svc.AddPartnerAsync(req, ct)))
            .RequirePermission(Features.Structure, Cap.Add);
        g.MapPut("/partners/{id:guid}", async (Guid id, SavePartnerRequest req, StructureService svc, CancellationToken ct) => Results.Ok(await svc.UpdatePartnerAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Edit);
        g.MapPost("/partners/{id:guid}/end", async (Guid id, EndRequest req, StructureService svc, CancellationToken ct) => Results.Ok(await svc.EndPartnerAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Delete);
    }

    private static void MapDictionary(RouteGroupBuilder g)
    {
        g.MapGet("/standards", async (DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.StandardsAsync(ct)))
            .RequirePermission(Features.Dictionary, Cap.View);
        g.MapGet("/indicators", async (string? standard, DefinitionStatus? status, string? q, bool? withConflicts, DictionaryService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(standard, status, q, withConflicts, ct)))
            .RequirePermission(Features.Dictionary, Cap.View);
        g.MapPost("/indicators", async (CreateIndicatorRequest req, DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.CreateAsync(req, ct)))
            .RequirePermission(Features.Dictionary, Cap.Add);
        g.MapGet("/indicators/{code}", async (string code, DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(code, ct)))
            .RequirePermission(Features.Dictionary, Cap.View);
        g.MapPut("/indicators/{code}/definition", async (string code, SaveDefinitionRequest req, DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.SaveAsync(code, req, ct)))
            .RequirePermission(Features.Dictionary, Cap.Edit);
        g.MapPost("/indicators/{code}/submit", async (string code, DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.SubmitAsync(code, ct)))
            .RequirePermission(Features.Dictionary, Cap.Edit);
        g.MapPost("/indicators/{code}/approve", async (string code, CommitteeDecisionRequest req, DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.ApproveAsync(code, req, ct)))
            .RequireSpecial(Features.Dictionary, Specials.Approve);
        g.MapPost("/indicators/{code}/return", async (string code, CommitteeDecisionRequest req, DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.ReturnAsync(code, req, ct)))
            .RequireSpecial(Features.Dictionary, Specials.Approve);
        g.MapPost("/indicators/{code}/suspend", async (string code, SuspendRequest req, DictionaryService svc, CancellationToken ct) => Results.Ok(await svc.SetSuspendedAsync(code, req.Suspended, req.Reason, ct)))
            .RequirePermission(Features.Dictionary, Cap.Delete);

        g.MapGet("/indicators/{code}/targets", async (string code, TargetsService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(code, ct)))
            .RequirePermission(Features.Targets, Cap.View);
        g.MapPut("/indicators/{code}/targets", async (string code, SaveTargetRequest req, TargetsService svc, CancellationToken ct) => Results.Ok(await svc.SaveAsync(code, req, ct)))
            .RequirePermission(Features.Targets, Cap.Edit);
        g.MapGet("/indicators/{code}/distribution", async (string code, int? year, TargetsService svc, CancellationToken ct) => Results.Ok(await svc.DistributionAsync(code, year ?? 2027, ct)))
            .RequirePermission(Features.Targets, Cap.View);
        g.MapPost("/targets/{id:guid}/approve", async (Guid id, CommitteeDecisionRequest req, TargetsService svc, CancellationToken ct) => Results.Ok(await svc.ApproveAsync(id, req, ct)))
            .RequireSpecial(Features.Targets, Specials.Approve);
    }

    private static void MapConflicts(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ConflictStatus? status, ConflictSeverity? severity, string? scope, string? q, ConflictsService svc, CancellationToken ct) =>
                Results.Ok(await svc.ListAsync(status, severity, scope, q, ct)))
            .RequirePermission(Features.Conflicts, Cap.View);
        g.MapGet("/agenda", async (ConflictsService svc, CancellationToken ct) => Results.Ok(await svc.AgendaAsync(ct)))
            .RequirePermission(Features.Conflicts, Cap.View);
        g.MapGet("/{code}", async (string code, ConflictsService svc, CancellationToken ct) => Results.Ok(await svc.GetAsync(code, ct)))
            .RequirePermission(Features.Conflicts, Cap.View);
        g.MapPost("/{code}/prepare", async (string code, PrepareConflictRequest req, ConflictsService svc, CancellationToken ct) => Results.Ok(await svc.PrepareAsync(code, req, ct)))
            .RequirePermission(Features.Conflicts, Cap.Edit);
        g.MapPost("/{code}/decide", async (string code, DecideConflictRequest req, ConflictsService svc, CancellationToken ct) => Results.Ok(await svc.DecideAsync(code, req, ct)))
            .RequireSpecial(Features.Conflicts, Specials.Decide);
    }
}

public sealed record SuspendRequest(bool Suspended, string? Reason);
