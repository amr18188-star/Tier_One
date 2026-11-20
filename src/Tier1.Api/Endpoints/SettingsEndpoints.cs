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
    }
}
