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
        api.MapGet("/home", async (HomeService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(ct)))
            .RequirePermission(Features.Dashboard, Cap.View);

        MapUnits(api.MapGroup("/org/units"));
        MapStructure(api.MapGroup("/structure"));
        MapDictionary(api.MapGroup("/kpi"));
        MapStrategy(api.MapGroup("/strategy"));
        MapConflicts(api.MapGroup("/kpi/conflicts"));
    }

    private static void MapUnits(RouteGroupBuilder g)
    {
        g.MapGet("/", async (bool? includeInactive, OrgUnitsService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(includeInactive ?? false, ct)))
            .RequirePermission(Features.Units, Cap.View);
        g.MapPost("/", async (CreateOrgUnitRequest req, OrgUnitsService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateAsync(req, ct)))
            .RequirePermission(Features.Units, Cap.Add);
        g.MapPut("/{id:guid}", async (Guid id, UpdateOrgUnitRequest req, OrgUnitsService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdateAsync(id, req, ct)))
            .RequirePermission(Features.Units, Cap.Edit);
        g.MapPost("/{id:guid}/close", async (Guid id, CloseOrgUnitRequest req, OrgUnitsService svc, CancellationToken ct) => TypedResults.Ok(await svc.CloseAsync(id, req, ct)))
            .RequirePermission(Features.Units, Cap.Delete);
        g.MapGet("/{id:guid}/history", async (Guid id, OrgUnitsService svc, CancellationToken ct) => TypedResults.Ok(await svc.HistoryAsync(id, ct)))
            .RequirePermission(Features.Units, Cap.View);
        // المؤشرات التي تكلف بها الجهة فتظهر في نموذجها.
        g.MapGet("/{id:guid}/indicators", async (Guid id, UnitIndicatorsService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(id, ct)))
            .RequirePermission(Features.Units, Cap.View);
        g.MapPut("/{id:guid}/indicators", async (Guid id, SaveUnitIndicatorsRequest req, UnitIndicatorsService svc, CancellationToken ct) => TypedResults.Ok(await svc.SaveAsync(id, req, ct)))
            .RequirePermission(Features.Units, Cap.Edit);
    }

    private static void MapStructure(RouteGroupBuilder g)
    {
        g.MapGet("/template", async (StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.TemplateAsync(ct)))
            .RequirePermission(Features.Structure, Cap.View);
        g.MapGet("/completeness", async (StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.CompletenessAsync(ct)))
            .RequirePermission(Features.Structure, Cap.View);
        g.MapGet("/colleges/{id:guid}", async (Guid id, DateOnly? asOf, StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.ChartAsync(id, asOf, ct)))
            .RequirePermission(Features.Structure, Cap.View);

        g.MapPost("/members", async (SaveMemberRequest req, StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.AddMemberAsync(req, ct)))
            .RequirePermission(Features.Structure, Cap.Add);
        g.MapPut("/members/{id:guid}", async (Guid id, SaveMemberRequest req, StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdateMemberAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Edit);
        g.MapPost("/members/{id:guid}/end", async (Guid id, EndRequest req, StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.EndMemberAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Delete);

        g.MapPost("/partners", async (SavePartnerRequest req, StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.AddPartnerAsync(req, ct)))
            .RequirePermission(Features.Structure, Cap.Add);
        g.MapPut("/partners/{id:guid}", async (Guid id, SavePartnerRequest req, StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdatePartnerAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Edit);
        g.MapPost("/partners/{id:guid}/end", async (Guid id, EndRequest req, StructureService svc, CancellationToken ct) => TypedResults.Ok(await svc.EndPartnerAsync(id, req, ct)))
            .RequirePermission(Features.Structure, Cap.Delete);
    }

    private static void MapDictionary(RouteGroupBuilder g)
    {
        g.MapGet("/standards", async (DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.StandardsAsync(ct)))
            .RequirePermission(Features.Dictionary, Cap.View);
        g.MapGet("/indicators", async (string? standard, DefinitionStatus? status, string? q, bool? withConflicts, DictionaryService svc, CancellationToken ct) =>
                TypedResults.Ok(await svc.ListAsync(standard, status, q, withConflicts, ct)))
            .RequirePermission(Features.Dictionary, Cap.View);
        g.MapPost("/indicators", async (CreateIndicatorRequest req, DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateAsync(req, ct)))
            .RequirePermission(Features.Dictionary, Cap.Add);
        g.MapGet("/indicators/{code}", async (string code, DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(code, ct)))
            .RequirePermission(Features.Dictionary, Cap.View);
        g.MapPut("/indicators/{code}/definition", async (string code, SaveDefinitionRequest req, DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.SaveAsync(code, req, ct)))
            .RequirePermission(Features.Dictionary, Cap.Edit);
        g.MapPost("/indicators/{code}/submit", async (string code, DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.SubmitAsync(code, ct)))
            .RequirePermission(Features.Dictionary, Cap.Edit);
        g.MapPost("/indicators/{code}/approve", async (string code, CommitteeDecisionRequest req, DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.ApproveAsync(code, req, ct)))
            .RequireSpecial(Features.Dictionary, Specials.Approve);
        g.MapPost("/indicators/{code}/return", async (string code, CommitteeDecisionRequest req, DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.ReturnAsync(code, req, ct)))
            .RequireSpecial(Features.Dictionary, Specials.Approve);
        g.MapPost("/indicators/{code}/suspend", async (string code, SuspendRequest req, DictionaryService svc, CancellationToken ct) => TypedResults.Ok(await svc.SetSuspendedAsync(code, req.Suspended, req.Reason, ct)))
            .RequirePermission(Features.Dictionary, Cap.Delete);

        g.MapGet("/indicators/{code}/targets", async (string code, TargetsService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(code, ct)))
            .RequirePermission(Features.Targets, Cap.View);
        g.MapPut("/indicators/{code}/targets", async (string code, SaveTargetRequest req, TargetsService svc, CancellationToken ct) => TypedResults.Ok(await svc.SaveAsync(code, req, ct)))
            .RequirePermission(Features.Targets, Cap.Edit);
        g.MapGet("/indicators/{code}/distribution", async (string code, int? year, TargetsService svc, CancellationToken ct) => TypedResults.Ok(await svc.DistributionAsync(code, year ?? 2027, ct)))
            .RequirePermission(Features.Targets, Cap.View);
        g.MapPost("/targets/{id:guid}/approve", async (Guid id, CommitteeDecisionRequest req, TargetsService svc, CancellationToken ct) => TypedResults.Ok(await svc.ApproveAsync(id, req, ct)))
            .RequireSpecial(Features.Targets, Specials.Approve);
        g.MapPost("/targets/{id:guid}/delete", async (Guid id, DeleteTargetRequest req, TargetsService svc, CancellationToken ct) =>
            {
                await svc.DeleteAsync(id, req, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Targets, Cap.Delete);
        g.MapPut("/indicators/{code}/plan", async (string code, SavePlanRequest req, TargetsService svc, CancellationToken ct) =>
            {
                await svc.SavePlanAsync(code, req, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Targets, Cap.Edit);
        g.MapPost("/indicators/{code}/delete", async (string code, DeleteRequest req, DictionaryService svc, CancellationToken ct) =>
            {
                await svc.DeleteAsync(code, req, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Dictionary, Cap.Delete);
    }

    /// <summary>شجرة الخطة: الركائز والمعايير والمبادرات.</summary>
    private static void MapStrategy(RouteGroupBuilder g)
    {
        g.MapGet("/tree", async (StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.TreeAsync(ct)))
            .RequirePermission(Features.Strategy, Cap.View);
        g.MapPost("/pillars", async (SavePillarRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreatePillarAsync(req, ct)))
            .RequirePermission(Features.Strategy, Cap.Add);
        g.MapPut("/pillars/{id:guid}", async (Guid id, SavePillarRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdatePillarAsync(id, req, ct)))
            .RequirePermission(Features.Strategy, Cap.Edit);
        g.MapPost("/pillars/{id:guid}/delete", async (Guid id, DeleteRequest req, StrategyService svc, CancellationToken ct) =>
            {
                await svc.DeletePillarAsync(id, req, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Strategy, Cap.Delete);
        g.MapPost("/standards", async (SaveStandardRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateStandardAsync(req, ct)))
            .RequirePermission(Features.Strategy, Cap.Add);
        g.MapPut("/standards/{id:guid}", async (Guid id, SaveStandardRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdateStandardAsync(id, req, ct)))
            .RequirePermission(Features.Strategy, Cap.Edit);
        g.MapPost("/standards/{id:guid}/delete", async (Guid id, DeleteRequest req, StrategyService svc, CancellationToken ct) =>
            {
                await svc.DeleteStandardAsync(id, req, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Strategy, Cap.Delete);
        // ADR-0017: an initiative's own activities, with the initiatives' permissions.
        g.MapGet("/initiatives/{id:guid}/activities", async (Guid id, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.ActivitiesAsync(id, ct)))
            .RequirePermission(Features.Strategy, Cap.View);
        g.MapPost("/initiatives/{id:guid}/activities", async (Guid id, SaveActivityRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateActivityAsync(id, req, ct)))
            .RequirePermission(Features.Strategy, Cap.Add);
        g.MapPut("/activities/{id:guid}", async (Guid id, SaveActivityRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdateActivityAsync(id, req, ct)))
            .RequirePermission(Features.Strategy, Cap.Edit);
        g.MapPost("/activities/{id:guid}/delete", async (Guid id, DeleteRequest req, StrategyService svc, CancellationToken ct) =>
            {
                await svc.DeleteActivityAsync(id, req, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Strategy, Cap.Delete);
        g.MapPost("/initiatives", async (SaveInitiativeRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.CreateInitiativeAsync(req, ct)))
            .RequirePermission(Features.Strategy, Cap.Add);
        g.MapPut("/initiatives/{id:guid}", async (Guid id, SaveInitiativeRequest req, StrategyService svc, CancellationToken ct) => TypedResults.Ok(await svc.UpdateInitiativeAsync(id, req, ct)))
            .RequirePermission(Features.Strategy, Cap.Edit);
        g.MapPost("/initiatives/{id:guid}/delete", async (Guid id, DeleteRequest req, StrategyService svc, CancellationToken ct) =>
            {
                await svc.DeleteInitiativeAsync(id, req, ct);
                return Results.NoContent();
            })
            .RequirePermission(Features.Strategy, Cap.Delete);
    }

    private static void MapConflicts(RouteGroupBuilder g)
    {
        g.MapGet("/", async (ConflictStatus? status, ConflictSeverity? severity, string? scope, string? q, ConflictsService svc, CancellationToken ct) =>
                TypedResults.Ok(await svc.ListAsync(status, severity, scope, q, ct)))
            .RequirePermission(Features.Conflicts, Cap.View);
        g.MapGet("/agenda", async (ConflictsService svc, CancellationToken ct) => TypedResults.Ok(await svc.AgendaAsync(ct)))
            .RequirePermission(Features.Conflicts, Cap.View);
        g.MapGet("/{code}", async (string code, ConflictsService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(code, ct)))
            .RequirePermission(Features.Conflicts, Cap.View);
        g.MapPost("/{code}/prepare", async (string code, PrepareConflictRequest req, ConflictsService svc, CancellationToken ct) => TypedResults.Ok(await svc.PrepareAsync(code, req, ct)))
            .RequirePermission(Features.Conflicts, Cap.Edit);
        g.MapPost("/{code}/decide", async (string code, DecideConflictRequest req, ConflictsService svc, CancellationToken ct) => TypedResults.Ok(await svc.DecideAsync(code, req, ct)))
            .RequireSpecial(Features.Conflicts, Specials.Decide);
    }
}

public sealed record SuspendRequest(bool Suspended, string? Reason);
