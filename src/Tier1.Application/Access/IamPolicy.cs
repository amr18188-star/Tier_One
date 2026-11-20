using System.Net.Mail;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Tier1.Application.Abstractions;
using Tier1.Application.Accounts;
using Tier1.Application.Auditing;
using Tier1.Application.Security;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Identity;

namespace Tier1.Application.Access;

/// <summary>
/// ARCH-2: the account and role rules every IAM change goes through, in one place:
/// <list type="bullet">
/// <item>SOD-9: nobody manages their own account; an account holding a reserved role (SA, SYS, AUD, DIR) is managed by a super
/// administrator only; an individual grant (or lifting an individual deny) never exceeds what the granter holds.</item>
/// <item>SOD-10: a super administrator assignment waits for an independent super administrator, and at least two permanent ones remain.</item>
/// <item>SOD-11: the system administrator role stays within its constraint, and the super administrator does not combine with it or the auditor.</item>
/// </list>
/// A refusal is audited before it is thrown.
/// </summary>
public sealed class IamPolicy(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, ISessionService sessions, IAuditLog audit, IOptions<AuthOptions> authOptions)
{
    public const int MinimumSuperAdmins = 2;

    /// <summary>
    /// SOD-9: لا أحد يدير حسابه. وحامل دور محجوز (ولو بإسناد ينتظر الاعتماد) لا يديره إلا المدير الأعلى.
    /// </summary>
    internal async Task<AppUser> LoadManagedAsync(Guid id, string action, string feature, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct) ?? throw DomainException.NotFound();
        if (current.UserId == id)
            await DenyAsync(user, action, feature, "SOD-9", null,
                "لا تعدل حسابك أو أدوارك أو صلاحياتك بنفسك. يتولاها مسؤول آخر.", "You cannot manage your own account, roles or permissions. Another administrator must do it.", ct);
        string[] reservedCodes = [.. PermissionCatalog.ReservedRoles];
        var reserved = await db.UserRoles.Where(a => a.UserId == id && a.RevokedAt == null)
            .Join(db.Roles.Where(r => reservedCodes.Contains(r.Code)), a => a.RoleId, r => r.Id, (a, r) => r.Code)
            .FirstOrDefaultAsync(ct);
        if (reserved is not null && !(await access.CurrentAsync(ct)).IsSuperAdmin)
            await DenyAsync(user, action, feature, "SOD-9", new { holds = reserved },
                "هذا الحساب يحمل دورًا محجوزًا، فلا يديره إلا المدير الأعلى.", "This account holds a reserved role, so only a super administrator can manage it.", ct);
        return user;
    }

    internal async Task EnsureMayManageRoleAsync(Role role, AppUser user, CancellationToken ct)
    {
        if (!PermissionCatalog.ReservedRoles.Contains(role.Code)) return;
        if ((await access.CurrentAsync(ct)).IsSuperAdmin) return;
        await DenyAsync(user, "iam.role_manage", Features.Grants, "SOD-9", new { role = role.Code },
            "هذا الدور يديره المدير الأعلى وحده.", "Only a super administrator can manage this role.", ct);
    }

    /// <summary>SOD-9: المنح الفردي (أو ما في أثره) لا يتجاوز ما يملكه المانح، ولا يمنح غير المدير الأعلى إجراء قرار.</summary>
    internal async Task EnsureGranterCoversAsync(AppUser user, FeatureDefinition def, Cap caps, AccessScope scope, IReadOnlyCollection<string> specials, string action, CancellationToken ct)
    {
        var mine = await access.CurrentAsync(ct);
        if (mine.IsSuperAdmin) return;
        if (!mine.Covers(def.Key, caps, scope, specials) || specials.Any(PermissionCatalog.DecisionSpecials.Contains))
            await DenyAsync(user, action, Features.Grants, "SOD-9", new { feature = def.Key, caps = PermissionCatalog.Letters(caps), specials },
                "لا تمنح غيرك ما لا تملكه أنت.", "You cannot grant what you do not have yourself.", ct);
    }

    /// <summary>
    /// SOD-11 على الصلاحية المجمعة: من يحمل دور مسؤول النظام أو المراجع الرقابي لا يضاف له دور يتجاوز قيده،
    /// ولا يسند أحدهما لمن يحمل أدوارًا تتجاوزه. المدير الأعلى لا يجتمع مع أي منهما.
    /// </summary>
    internal async Task EnsureCompatibleRoleAsync(AppUser user, Role role, CancellationToken ct)
    {
        var heldIds = await db.UserRoles.Where(a => a.UserId == user.Id && a.RevokedAt == null && a.RoleId != role.Id).Select(a => a.RoleId).Distinct().ToListAsync(ct);
        var held = await db.Roles.AsNoTracking().Include(r => r.Grants).Where(r => heldIds.Contains(r.Id)).ToListAsync(ct);
        // قيد المحتوى على مسؤول النظام وحده منذ D6؛ واستقلال الرقابة يبقى: المدير الأعلى لا يجتمع مع مسؤول النظام ولا المراجع الرقابي.
        static bool Constrained(string code) => code is RoleRules.SystemAdminRole;
        static bool ApartFromSuperAdmin(string code) => code is RoleRules.SystemAdminRole or RoleRules.AuditorRole;
        static IEnumerable<GrantInput> Inputs(Role r) => r.Grants.Select(g => new GrantInput(g.Feature, g.Caps, g.Scope, g.Specials));
        var sa = PermissionCatalog.SuperAdminRole;
        if ((role.Code == sa && held.Any(h => ApartFromSuperAdmin(h.Code))) || (ApartFromSuperAdmin(role.Code) && held.Any(h => h.Code == sa)))
            await DenyAsync(user, "iam.role_assign", Features.Grants, "SOD-11", new { role = role.Code },
                "دور المدير الأعلى لا يجتمع مع مسؤول النظام أو المراجع الرقابي.", "The super administrator role cannot be combined with the system administrator or the oversight auditor.", ct);
        try
        {
            foreach (var constrained in held.Where(h => Constrained(h.Code)))
                RoleRules.Validate(constrained.Code, false, Inputs(role));
            if (Constrained(role.Code))
            {
                foreach (var other in held) RoleRules.Validate(role.Code, false, Inputs(other));
                var allows = await db.UserOverrides.Where(o => o.UserId == user.Id && o.RevokedAt == null && o.Effect == OverrideEffect.Allow).ToListAsync(ct);
                RoleRules.Validate(role.Code, false, allows.Select(o => new GrantInput(o.Feature, o.Caps, o.Scope, o.Specials)).GroupBy(g => g.Feature).Select(g => g.First()));
            }
        }
        catch (DomainException e) when (e.Code == "sod_sod_11")
        {
            await DenyAsync(user, "iam.role_assign", Features.Grants, "SOD-11", new { role = role.Code }, e.Message, e.Message, ct);
        }
    }

    internal async Task<IReadOnlyList<string>> ConstrainedCodesAsync(Guid userId, CancellationToken ct) =>
        await db.UserRoles.Where(a => a.UserId == userId && a.RevokedAt == null)
            .Join(db.Roles.Where(r => r.Code == RoleRules.SystemAdminRole), a => a.RoleId, r => r.Id, (a, r) => r.Code)
            .Distinct().ToListAsync(ct);

    /// <summary>هل أنشأ «other» حساب «user» أو أسند له دور المدير الأعلى أو اعتمده؟</summary>
    internal async Task<bool> DependsOnAsync(Guid user, Guid other, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(u => u.Id == user && u.CreatedBy == other, ct)) return true;
        return await db.UserRoles.Where(a => a.UserId == user && a.RevokedAt == null && (a.GrantedBy == other || a.ApprovedBy == other))
            .Join(db.Roles.Where(r => r.Code == PermissionCatalog.SuperAdminRole), a => a.RoleId, r => r.Id, (a, r) => a.Id)
            .AnyAsync(ct);
    }

    /// <summary>
    /// SOD-10: لا يقل المديرون الأعلون الدائمون (معتمدون، بلا تاريخ نهاية، بحسابات نشطة) عن اثنين.
    /// الإسناد المؤقت لا يحسب، فلا يمر جعل كل مدير مؤقتًا واحدًا بعد الآخر.
    /// </summary>
    internal async Task EnsureSuperAdminsRemainAsync(Guid? excludeUser, Guid? excludeAssignment, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var saRole = db.Roles.Where(r => r.Code == PermissionCatalog.SuperAdminRole).Select(r => r.Id);
        var affected = excludeUser is { } u
            ? await db.UserRoles.AnyAsync(a => a.UserId == u && a.RevokedAt == null && a.ApprovedAt != null && saRole.Contains(a.RoleId), ct)
            : true;
        if (!affected) return;
        var remaining = await db.UserRoles
            .Where(a => a.RevokedAt == null && a.ApprovedAt != null && a.ValidFrom <= now && a.ValidTo == null && saRole.Contains(a.RoleId) && a.Id != excludeAssignment && a.UserId != excludeUser)
            .Join(db.Users.Where(x => x.Active), a => a.UserId, x => x.Id, (a, x) => a.UserId)
            .Distinct().CountAsync(ct);
        if (remaining < MinimumSuperAdmins)
            throw DomainException.SeparationOfDuties("SOD-10", "لا يقل عدد حسابات المدير الأعلى الدائمة النشطة عن اثنين. أضف مديرًا أعلى آخر أولًا.", "There must be at least two permanent active super administrators. Add another one first.");
    }

    /// <summary>دور يتطلب التحقق بخطوتين: تنتهي جلسات صاحبه ليدخل من جديد بالتحقق.</summary>
    internal async Task AfterRoleChangeAsync(Guid userId, string roleCode, CancellationToken ct)
    {
        if (authOptions.Value.MfaEnforcedFor(roleCode)) await sessions.RevokeAllAsync(userId, ct);
    }

    internal async Task DenyAsync(AppUser user, string action, string feature, string rule, object? details, string messageAr, string messageEn, CancellationToken ct)
    {
        await audit.RecordNowAsync(UserEvent(action, "رفض بقاعدة فصل", "Blocked by a separation rule", user, "-", feature) with
        {
            Outcome = "denied",
            Details = new { rule, details },
        }, ct);
        throw DomainException.SeparationOfDuties(rule, messageAr, messageEn);
    }

    internal static AuditEntry UserEvent(string action, string messageAr, string messageEn, AppUser user, string cap, string feature = Features.Users) =>
        AuditEvents.Security(action, feature, messageAr, messageEn) with
        {
            Permission = $"{feature}:{cap}",
            Target = new AuditTarget("User", user.Id.ToString(), user.Email),
        };

    internal static List<string> CleanScope(IReadOnlyList<string>? values) =>
        (values ?? []).Where(v => v is not null).Select(v => v.Trim()).Where(v => v.Length is > 0 and <= 64).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(50).ToList();

    internal static string NormalizeEmail(string? email)
    {
        var e = (email ?? "").Trim().ToLowerInvariant();
        if (e.Length is < 5 or > 254 || !MailAddress.TryCreate(e, out var parsed) || parsed.Address != e)
            throw new DomainException("invalid_email", "البريد الإلكتروني غير صالح.", "Invalid email address.", 400);
        return e;
    }

    internal static void RequireReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new DomainException("reason_required", "اكتب سببًا واضحًا (5 أحرف على الأقل).", "Enter a clear reason (at least 5 characters).", 400);
    }
}
