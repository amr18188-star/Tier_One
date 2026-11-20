using Microsoft.Extensions.Caching.Memory;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Tier1.Application.Settings;

/// <param name="SuperAdminOnly">لا يغيره إلا المدير الأعلى (D11).</param>
/// <param name="IsSwitch">تشغيل أو إيقاف.</param>
/// <param name="Editable">يستطيع المستخدم الحالي تعديله.</param>
public sealed record SettingDto(string Key, int Value, int DefaultValue, int Min, int Max, string Name, string Ar, string En, string Unit, DateTimeOffset? UpdatedAt,
    bool SuperAdminOnly = false, bool IsSwitch = false, bool Editable = false);

public sealed record SaveSettingRequest(int Value, string? Reason);

/// <summary>إعدادات النظام التشغيلية (adm.settings): القيم المحفوظة أو الافتراضية بحدودها، والتعديل بسبب مسجل.</summary>
public sealed class SettingsService(ISettingsStore db,  IClock clock, ICurrentUser current, IAuditLog audit, IAccessService access, IMemoryCache cache)
{
    public const string Feature = "adm.settings";

    private static readonly TimeSpan SwitchCache = TimeSpan.FromSeconds(30);

    /// <summary>
    /// مفتاح «المصادقة الثنائية لأصحاب القرارات» (D11)، يقرأ عند الدخول وفي كل طلب، فيحفظ 30 ثانية ويمسح عند تعديله.
    /// </summary>
    public async Task<bool> MfaForDecisionsAsync(CancellationToken ct) =>
        await cache.GetOrCreateAsync("setting:" + SettingKeys.MfaForDecisions, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = SwitchCache;
            return await IntAsync(SettingKeys.MfaForDecisions, ct) == 1;
        });

    /// <summary>إعداد للمدير الأعلى وحده: لا يعدله غيره ولو ملك تعديل الإعدادات.</summary>
    public static void EnsureCanEdit(SettingDefinition d, EffectiveAccess me)
    {
        if (d.SuperAdminOnly && !me.IsSuperAdmin)
            throw DomainException.SeparationOfDuties("SOD-10", $"«{d.Ar}» يغيره المدير الأعلى وحده.", $"Only a super administrator can change {d.En}.");
    }

    public async Task<IReadOnlyList<SettingDto>> ListAsync(CancellationToken ct)
    {
        var saved = await db.Settings.AsNoTracking().ToDictionaryAsync(s => s.Key, ct);
        var me = await access.CurrentAsync(ct);
        var canEdit = me.Has(Feature, Cap.Edit);
        return SettingKeys.All.Select(d => ToDto(d, saved.GetValueOrDefault(d.Key), canEdit && (!d.SuperAdminOnly || me.IsSuperAdmin))).ToList();
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
        EnsureCanEdit(d, await access.CurrentAsync(ct));
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
        cache.Remove("setting:" + key);
        return ToDto(d, s, true);
    }

    private static SettingDto ToDto(SettingDefinition d, SystemSetting? s, bool editable)
    {
        var value = s is not null && int.TryParse(s.Value, out var n) ? n : d.Default;
        return new SettingDto(d.Key, value, d.Default, d.Min, d.Max, Lang.Name(d.Ar, d.En), d.Ar, d.En, Lang.Name(d.UnitAr, d.UnitEn), s?.UpdatedAt,
            d.SuperAdminOnly, d.IsSwitch, editable);
    }
}
