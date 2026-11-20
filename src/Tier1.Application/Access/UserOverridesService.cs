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

/// <summary>ARCH-2: individual allow and deny overrides on a staff account.</summary>
public sealed class UserOverridesService(IIdentityStore db, IClock clock, ICurrentUser current, IAccessService access, IAuditLog audit, IamPolicy policy, UsersAdminService users)
{
    public async Task<OverrideDto> AddOverrideAsync(Guid userId, AddOverrideRequest req, CancellationToken ct)
    {
        IamPolicy.RequireReason(req.Reason);
        var user = await policy.LoadManagedAsync(userId, "iam.override_add", Features.Grants, ct);
        if (string.IsNullOrWhiteSpace(req.Feature)) throw new DomainException("unknown_feature", "اختر خاصية.", "Choose a feature.", 400);
        var def = PermissionCatalog.Find(req.Feature) ?? throw new DomainException("unknown_feature", "الخاصية غير موجودة.", "Unknown feature.", 400);
        Cap caps;
        AccessScope scope;
        try
        {
            caps = PermissionCatalog.ParseCaps(req.Caps ?? "");
            scope = PermissionCatalog.ParseScope(string.IsNullOrEmpty(req.Scope) ? "A" : req.Scope);
        }
        catch (FormatException)
        {
            throw new DomainException("invalid_grant", "الإمكانيات أو النطاق غير صالحة.", "Invalid capabilities or scope.", 400);
        }
        // «نطاقه» يحتاج قيم وحدات لا يحملها الاستثناء الفردي؛ فيقتصر على الكل والمسند إليه وسجلاته.
        if (scope == AccessScope.Unit)
            throw new DomainException("invalid_scope", "الاستثناء الفردي لا يأخذ نطاق «نطاقه». اختر الكل أو المسند إليه أو سجلاته.", "An individual override cannot use the unit scope. Choose all, assigned or own.", 400);
        var specials = (req.Specials ?? []).Distinct(StringComparer.Ordinal).ToList();
        if ((caps & ~def.AllowedCaps) != Cap.None || specials.Any(s => !def.HasSpecial(s)))
            throw new DomainException("invalid_cap", $"إمكانية أو إجراء لا معنى له في «{def.Name.Ar}».", $"A capability or action that does not exist in '{def.Key}'.", 400);
        if (caps == Cap.None && specials.Count == 0)
            throw new DomainException("empty_override", "اختر إمكانية أو إجراءً واحدًا على الأقل.", "Choose at least one capability or action.", 400);
        if (!Enum.IsDefined(req.Effect)) throw new DomainException("invalid_effect", "نوع الاستثناء غير صالح.", "Invalid override type.", 400);
        if (req.Effect == OverrideEffect.Allow)
        {
            if (specials.Any(s => PermissionCatalog.IsSuperAdminOnly(def.Key, s)))
                await policy.DenyAsync(user, "iam.override_add", Features.Grants, "SOD-10", new { feature = def.Key, specials },
                    "هذا الإجراء للمدير الأعلى وحده ولا يمنح فرديًا.", "This action belongs to super administrators only and cannot be granted individually.", ct);
            await policy.EnsureGranterCoversAsync(user, def, caps, scope, specials, "iam.override_add", ct);
            foreach (var constrained in await policy.ConstrainedCodesAsync(userId, ct))
                RoleRules.Validate(constrained, false, [new GrantInput(def.Key, caps, scope, specials)]);
        }
        if (req.ValidTo is { } to && to <= clock.UtcNow)
            throw new DomainException("invalid_dates", "تاريخ النهاية يجب أن يكون في المستقبل.", "The end date must be in the future.", 400);
        var o = new UserOverride
        {
            UserId = userId,
            Feature = def.Key,
            Caps = caps,
            Specials = specials,
            Effect = req.Effect,
            Scope = scope,
            ValidFrom = clock.UtcNow,
            ValidTo = req.ValidTo,
            GrantedBy = current.UserId,
            GrantedAt = clock.UtcNow,
            Reason = req.Reason.Trim(),
        };
        db.UserOverrides.Add(o);
        var verbAr = req.Effect == OverrideEffect.Allow ? "منح صلاحية فردية" : "منع صلاحية فردية";
        var verbEn = req.Effect == OverrideEffect.Allow ? "Granted an individual permission" : "Denied an individual permission";
        audit.Record(IamPolicy.UserEvent("iam.override_add", verbAr, verbEn, user, "A", Features.Grants) with
        {
            Changes = [new FieldChange(def.Key, null, $"{(req.Effect == OverrideEffect.Allow ? "+" : "-")}{PermissionCatalog.Letters(caps)}:{GrantView.ScopeCode(scope)}" + (specials.Count > 0 ? "+" + string.Join(',', specials) : ""))],
            Reason = o.Reason,
        });
        await db.SaveChangesAsync(ct);
        await access.BumpVersionAsync(ct);
        return (await users.GetAsync(userId, ct)).Overrides.First(x => x.Id == o.Id);
    }

    public async Task RevokeOverrideAsync(Guid overrideId, string reason, CancellationToken ct)
    {
        IamPolicy.RequireReason(reason);
        var o = await db.UserOverrides.FirstOrDefaultAsync(x => x.Id == overrideId, ct) ?? throw DomainException.NotFound();
        if (o.RevokedAt is not null) return;
        var user = await policy.LoadManagedAsync(o.UserId, "iam.override_revoke", Features.Grants, ct);
        // سحب منع فردي يعيد صلاحية، فهو منح في أثره: لا يتجاوز ما يملكه الساحب.
        if (o.Effect == OverrideEffect.Deny)
            await policy.EnsureGranterCoversAsync(user, PermissionCatalog.Get(o.Feature), o.Caps, o.Scope, o.Specials, "iam.override_revoke", ct);
        o.RevokedAt = clock.UtcNow;
        o.RevokedBy = current.UserId;
        audit.Record(IamPolicy.UserEvent("iam.override_revoke", "سحب استثناء فردي", "Revoked an individual override", user, "D", Features.Grants) with
        {
            Changes = [new FieldChange(o.Feature, $"{(o.Effect == OverrideEffect.Allow ? "+" : "-")}{PermissionCatalog.Letters(o.Caps)}", null)],
            Reason = reason.Trim(),
        });
        await db.SaveChangesAsync(ct);
        await access.BumpVersionAsync(ct);
    }
}
