using Tier1.Api.Security;
using Tier1.Api.Startup;
using Tier1.Application.Logs;

namespace Tier1.Api.Endpoints;

public sealed record VerifyRequest(DateOnly? From, DateOnly? To);

public static class LogEndpoints
{
    /// <summary>كل من يملك عرض أي سجل؛ الخدمة تتحقق من خاصية المجموعة المطلوبة ونطاقها.</summary>
    private static readonly string[] AnyLog = ["log.audit:V", "log.security:V", "log.access:V", "log.tech:V"];

    public static void MapLogs(this RouteGroupBuilder api)
    {
        var logs = api.MapGroup("/logs");
        logs.MapGet("/status", async (int? days, LogsService svc, CancellationToken ct) => Results.Ok(await svc.StatusAsync(days ?? 14, ct)))
            .RequireAnyPermission(AnyLog);
        logs.MapPost("/verify", async (VerifyRequest req, LogsService svc, CancellationToken ct) => Results.Ok(await svc.VerifyAsync(req.From, req.To, ct)))
            .RequireAnyPermission(AnyLog).RequireRateLimiting(ApiSetup.SensitiveLimit);
        logs.MapGet("/{collection}", async ([AsParameters] LogQuery q, string collection, LogsService svc, CancellationToken ct) =>
            Results.Ok(await svc.SearchAsync(q.ToRequest(collection), ct))).RequireAnyPermission(AnyLog);
        logs.MapGet("/{collection}/export", async ([AsParameters] LogQuery q, string collection, LogsService svc, CancellationToken ct) =>
        {
            var file = await svc.ExportAsync(q.ToRequest(collection), ct);
            return Results.File(file.Content, "text/csv; charset=utf-8", file.FileName);
        }).RequireAnyPermission(AnyLog).RequireRateLimiting(ApiSetup.SensitiveLimit);
    }
}

public sealed record LogQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    Guid? ActorId,
    string? Module,
    string? Feature,
    string? Action,
    string? Outcome,
    string? Ref,
    string? TargetId,
    string? Ip,
    string? CorrelationId,
    int? Page,
    int? PageSize)
{
    public LogSearchRequest ToRequest(string collection) =>
        new(collection, From, To, ActorId, Module, Feature, Action, Outcome, Ref, TargetId, Ip, CorrelationId, Page ?? 1, PageSize ?? 50);
}
