using Tier1.Application.Abstractions;
using Tier1.Application.Auditing;
using Tier1.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Tier1.Application.Settings;

public sealed record SettingDto(string Key, int Value, int DefaultValue, int Min, int Max, string Name, string Ar, string En, string Unit, DateTimeOffset? UpdatedAt);

public sealed record SaveSettingRequest(int Value, string? Reason);

/// <summary>إعدادات النظام التشغيلية (adm.settings): القيم المحفوظة أو الافتراضية بحدودها، والتعديل بسبب مسجل.</summary>
public sealed class SettingsService(IAppDb db, IClock clock, ICurrentUser current, IAuditLog audit)
{
    public const string Feature = "adm.settings";

    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken ct)
    {
        var saved = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, ct);
        return SettingKeys.All.Select(d => ToDto(d, saved.GetValueOrDefault(d.Key))).ToList();
    }

    public async Task<int> IntAsync(string key, CancellationToken ct)
    {
        var d = SettingKeys.Find(key) ?? throw new InvalidOperationException($"Unknown setting {key}.");
        var v = await db.Settings.AsNoTracking().Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return int.TryParse(v, out var n) && n >= d.Min && n <= d.Max ? n : d.Default;
    }

    public async Task<SettingDto> SaveAsync(string key, SaveSettingRequest req, CancellationToken ct)
    {
        var d = SettingKeys.Find(key) ?? throw DomainException.NotFound();
        var reason = Require.Reason(req.Reason);
        if (req.Value < d.Min || req.Value > d.Max)
            throw new DomainException("invalid_value", $"القيمة من {d.Min} إلى {d.Max} {d.UnitAr}.", $"The value is {d.Min} to {d.Max} {d.UnitEn}.", 400);
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Key == key, ct);
        var before = s?.Value ?? d.Default.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (s is null)
        {
            s = new SystemSetting { Key = key };
            db.Settings.Add(s);
        }
        s.Value = req.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        s.UpdatedAt = clock.UtcNow;
        s.UpdatedBy = current.UserId;
        audit.Record(AuditEvents.Audit("adm", Feature, "adm.setting_update", "تعديل إعداد النظام", "Updated a system setting") with
        {
            Permission = $"{Feature}:E",
            Target = new AuditTarget("Setting", key, d.Ar),
            Changes = [new FieldChange(key, before, s.Value)],
            Reason = reason,
        });
        await db.SaveChangesAsync(ct);
        return ToDto(d, s);
    }

    private static SettingDto ToDto(SettingDefinition d, SystemSetting? s)
    {
        var value = s is not null && int.TryParse(s.Value, out var n) ? n : d.Default;
        return new SettingDto(d.Key, value, d.Default, d.Min, d.Max, Lang.Name(d.Ar, d.En), d.Ar, d.En, Lang.Name(d.UnitAr, d.UnitEn), s?.UpdatedAt);
    }
}
