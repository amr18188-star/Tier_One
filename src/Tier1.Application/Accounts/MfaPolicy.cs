using Tier1.Application.Access;
using Tier1.Domain.Access;

namespace Tier1.Application.Accounts;

/// <summary>
/// من يلزمه التحقق بخطوتين (D11، ADR-0007)، دالة نقية تستعمل عند الدخول وفي كل طلب:
/// <list type="number">
/// <item>أدوار الأمان الثابتة في إعدادات الخادم (Auth:MfaRoles): دائمًا، ولا تطفأ من الشاشة؛ فجلسة مدير أعلى مسروقة لا تطفئها عن نفسها.</item>
/// <item>وإن كان مفتاح المدير الأعلى «المصادقة الثنائية لأصحاب القرارات» مشغلًا: كل من يملك فعليًا إجراء قرار
/// (اعتماد أو مراجعة أو حسم أو إعادة فتح…) أو صلاحية حساسة (القيم الرسمية للجامعة، إدارة الحسابات والأدوار والصلاحيات، إعدادات النظام)،
/// أيًّا كان اسم دوره، ومنه الأدوار المخصصة والاستثناءات الفردية.</item>
/// </list>
/// Auth:RequireMfa=false (التطوير) يطفئ الكل.
/// </summary>
public static class MfaPolicy
{
    /// <summary>صلاحيات حساسة خارج إجراءات القرار: تعديل أي منها يلزمه التحقق بخطوتين حين يشغل المفتاح.</summary>
    public static readonly IReadOnlyList<(string Feature, Cap Caps)> SensitiveCapabilities =
    [
        (Features.University, Cap.Edit),
        ("adm.users", Cap.Edit | Cap.Add | Cap.Delete),
        ("adm.roles", Cap.Edit | Cap.Add | Cap.Delete),
        ("adm.grants", Cap.Edit | Cap.Add | Cap.Delete),
        ("adm.settings", Cap.Edit),
    ];

    public static bool Requires(EffectiveAccess access, AuthOptions options, bool forDecisions)
    {
        if (!options.RequireMfa) return false;
        if (access.RoleCodes.Any(options.MfaRoles.Contains)) return true;
        if (!forDecisions) return false;
        return HoldsDecision(access) || HoldsSensitive(access);
    }

    public static bool HoldsDecision(EffectiveAccess access) =>
        access.Features.Values.Any(f => f.Specials.Any(PermissionCatalog.DecisionSpecials.Contains));

    public static bool HoldsSensitive(EffectiveAccess access) =>
        SensitiveCapabilities.Any(s => PermissionCatalog.Each(s.Caps).Any(c => access.Has(s.Feature, c)));
}
