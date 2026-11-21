using Tier1.Api.Security;
using Tier1.Application.Settings;
using Tier1.Domain.Access;

namespace Tier1.Api.Endpoints;

/// <summary>إعدادات النظام التشغيلية (adm.settings): مهلة الحجز المؤقت للمقعد وأخواتها.</summary>
public static class SettingsEndpoints
{
    public static void MapSettings(this RouteGroupBuilder api)
    {
        var settings = api.MapGroup("/admin/settings");
        settings.MapGet("/", async (SettingsService svc, CancellationToken ct) => TypedResults.Ok(await svc.ListAsync(ct)))
            .RequirePermission(SettingsService.Feature, Cap.View);
        settings.MapPut("/{key}", async (string key, SaveSettingRequest body, SettingsService svc, CancellationToken ct) => TypedResults.Ok(await svc.SaveAsync(key, body, ct)))
            .RequirePermission(SettingsService.Feature, Cap.Edit);

        // ADR-0017: the plan's period. Anyone who sees the settings sees it; only the super administrator changes it (SOD-10).
        // The literal segment wins over "/{key}", so the period's four settings never go through the single-setting save.
        settings.MapGet("/plan-period", async (PlanPeriodService svc, CancellationToken ct) => TypedResults.Ok(await svc.GetAsync(ct)))
            .RequirePermission(SettingsService.Feature, Cap.View);
        settings.MapPut("/plan-period", async (SavePlanPeriodRequest body, PlanPeriodService svc, CancellationToken ct) => TypedResults.Ok(await svc.SaveAsync(body, ct)))
            .RequirePermission(SettingsService.Feature, Cap.Edit);
    }
}
