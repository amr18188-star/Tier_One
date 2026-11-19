using Tier1.Application.Access;
using Tier1.Application.Accounts;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.Extensions.Options;

namespace Tier1.Api.Security;

/// <summary>بند صلاحية: إمكانية في خاصية، أو إجراء خاص فيها.</summary>
public sealed record PermissionItem(string Feature, Cap Cap, string? Special)
{
    public override string ToString() => Special is null ? $"{Feature}:{PermissionCatalog.Letters(Cap)}" : $"{Feature}:{Special}";
}

/// <summary>يكفي أي بند من البنود (للشاشات التي يصلها أكثر من دور بخصائص مختلفة).</summary>
public sealed class PermissionRequirement(IReadOnlyList<PermissionItem> items) : IAuthorizationRequirement
{
    public IReadOnlyList<PermissionItem> Items { get; } = items;
}

public sealed class PermissionHandler(IAccessService access, IOptions<AuthOptions> auth, Tier1.Application.Settings.SettingsService settings) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (context.User.HasClaim(ClaimNames.MfaPending, "true") || context.User.HasClaim(ClaimNames.PasswordChangePending, "true")) return;
        if (!Guid.TryParse(context.User.FindFirst(ClaimNames.UserId)?.Value, out var uid)) return;
        var mine = await access.GetAsync(uid, CancellationToken.None);
        // من يلزمه التحقق بخطوتين (دور أمان ثابت، أو صلاحية قرار أو حساسة والمفتاح مشغل، D11) لا يعمل في جلسة بلا تحقق؛
        // يلزم دخول جديد بالتحقق. ويشمل ذلك دورًا حساسًا أسند بعد الدخول، أو تشغيل المدير الأعلى للمفتاح أثناء الجلسة.
        if (!context.User.HasClaim(ClaimNames.MfaVerified, "true")
            && MfaPolicy.Requires(mine, auth.Value, await settings.MfaForDecisionsAsync(CancellationToken.None))) return;
        if (requirement.Items.Any(i => i.Special is null ? mine.Has(i.Feature, i.Cap) : mine.HasSpecial(i.Feature, i.Special)))
            context.Succeed(requirement);
    }
}

/// <summary>
/// سياسات الصلاحيات تبنى من اسمها: «perm:adm.roles:V» أو «perm:adm.roles:approve»، وعدة بنود تفصل بـ «|».
/// الخاصية والإمكانية والإجراء يتحقق منها في الكتالوج عند البناء، فلا تمر سياسة بخطأ إملائي.
/// </summary>
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public const string Prefix = "perm:";

    public static string Name(params PermissionItem[] items) => Prefix + string.Join('|', items.Select(i => i.ToString()));

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal)) return await base.GetPolicyAsync(policyName);
        var items = policyName[Prefix.Length..].Split('|').Select(Parse).ToList();
        return new AuthorizationPolicyBuilder().RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(items)).Build();
    }

    internal static PermissionItem Parse(string text)
    {
        var parts = text.Split(':');
        var def = PermissionCatalog.Find(parts[0]) ?? throw new InvalidOperationException($"Unknown feature in policy '{text}'.");
        if (parts[1].All(c => "VPEAD".Contains(c, StringComparison.Ordinal)))
        {
            var cap = PermissionCatalog.ParseCaps(parts[1]);
            if (PermissionCatalog.Each(cap).Count() != 1 || !def.AllowedCaps.HasFlag(cap)) throw new InvalidOperationException($"Invalid capability in policy '{text}'.");
            return new PermissionItem(def.Key, cap, null);
        }
        if (!def.HasSpecial(parts[1])) throw new InvalidOperationException($"Unknown special action in policy '{text}'.");
        return new PermissionItem(def.Key, Cap.None, parts[1]);
    }
}

/// <summary>كل رفض صلاحية على خاصية حدث أمني بالبند المطلوب والمسار.</summary>
public sealed class DenialLoggingResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden && policy.Requirements.OfType<PermissionRequirement>().FirstOrDefault() is { } req
            && !context.User.HasClaim(ClaimNames.MfaPending, "true") && !context.User.HasClaim(ClaimNames.PasswordChangePending, "true"))
        {
            var audit = context.RequestServices.GetRequiredService<IAuditLog>();
            await audit.RecordNowAsync(AuditEvents.Security("access.denied", req.Items[0].Feature, "رفض وصول", "Denied access") with
            {
                Outcome = "denied",
                Permission = string.Join('|', req.Items.Select(i => i.ToString())),
                Target = new AuditTarget("Endpoint", context.Request.Method + " " + (context.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText),
            }, context.RequestAborted);
        }
        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string feature, Cap cap)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(PermissionPolicyProvider.Name(new PermissionItem(feature, cap, null)));

    public static TBuilder RequireSpecial<TBuilder>(this TBuilder builder, string feature, string special)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(PermissionPolicyProvider.Name(new PermissionItem(feature, Cap.None, special)));

    /// <summary>يكفي أي بند، بصيغة «feature:V» أو «feature:special».</summary>
    public static TBuilder RequireAnyPermission<TBuilder>(this TBuilder builder, params string[] items)
        where TBuilder : IEndpointConventionBuilder => builder.RequireAuthorization(PermissionPolicyProvider.Name([.. items.Select(PermissionPolicyProvider.Parse)]));
}
