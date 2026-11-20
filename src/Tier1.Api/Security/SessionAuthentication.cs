using System.Security.Claims;
using System.Text.Encodings.Web;
using Tier1.Application.Accounts;
using Tier1.Domain.Common;
using Tier1.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Tier1.Api.Security;

public sealed class CookieNames
{
    /// <summary>في الإنتاج «__Host-t1_session» (HTTPS ومسار الجذر وبلا نطاق).</summary>
    public string Session { get; set; } = "t1_session";

    public string Antiforgery { get; set; } = "t1_af";

    public bool Secure { get; set; } = true;
}

/// <summary>
/// جلسة معتمة في كوكي HttpOnly/Secure/SameSite=Lax؛ لا رموز في localStorage. الجلسة التي تنتظر التحقق بخطوتين
/// أو تغيير كلمة المرور لا تصلح إلا لمسارات ذلك نفسه.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionService sessions,
    IOptions<CookieNames> cookies) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(cookies.Value.Session, out var token) || string.IsNullOrEmpty(token))
            return AuthenticateResult.NoResult();
        // FE-2: background polls say so (X-Passive: 1), so an unattended open tab still reaches the idle timeout.
        var passive = Request.Headers["X-Passive"] == "1";
        var principal = await sessions.ValidateAsync(token, Context.RequestAborted, passive);
        if (principal is null) return AuthenticateResult.Fail("invalid_session");
        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, principal.UserId.ToString()),
            // معرف ثابت لربط رمز CSRF بالمستخدم نفسه عبر مراحل الدخول (لا يتغير بإكمال التحقق بخطوتين).
            new(ClaimTypes.NameIdentifier, principal.UserId.ToString()),
            new(ClaimNames.SessionId, principal.SessionId.ToString()),
            new(ClaimNames.Name, principal.DisplayName),
        };
        if (principal.MfaPending) claims.Add(new Claim(ClaimNames.MfaPending, "true"));
        if (principal.MfaVerified) claims.Add(new Claim(ClaimNames.MfaVerified, "true"));
        if (principal.MustChangePassword) claims.Add(new Claim(ClaimNames.PasswordChangePending, "true"));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName, ClaimNames.Name, null)), SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        await Response.WriteAsJsonAsync(new { status = 401, code = "unauthenticated", title = Lang.Pick("انتهت الجلسة. سجّل الدخول مرة أخرى.", "Your session has ended. Sign in again.") });
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        var mfa = Context.User.HasClaim(ClaimNames.MfaPending, "true");
        var pwd = Context.User.HasClaim(ClaimNames.PasswordChangePending, "true");
        await Response.WriteAsJsonAsync(new
        {
            status = 403,
            code = mfa ? "mfa_required" : pwd ? "password_change_required" : "forbidden",
            title = mfa ? Lang.Pick("أكمل التحقق بخطوتين أولًا.", "Complete two-step verification first.")
                : pwd ? Lang.Pick("غيّر كلمة المرور المؤقتة أولًا.", "Change your temporary password first.")
                : Lang.Pick("ليست لديك صلاحية لهذا الإجراء.", "You are not allowed to do this."),
        });
    }
}

public static class Policies
{
    /// <summary>جلسة مكتملة: كلمة مرور + تحقق بخطوتين إن كان مطلوبًا، ولا كلمة مرور مؤقتة معلقة.</summary>
    public const string Staff = "staff";

    /// <summary>جلسة موثقة بكلمة المرور تنتظر التحقق بخطوتين أو تغيير كلمة المرور.</summary>
    public const string Partial = "partial";
}

public static class SessionCookies
{
    /// <summary>كوكي جلسة بلا تاريخ انتهاء: يمحى بإغلاق المتصفح (أجهزة مشتركة)، والخادم يفرض المهلة المطلقة والخمول بنفسه.</summary>
    public static void Issue(HttpResponse response, CookieNames o, string token) =>
        response.Cookies.Append(o.Session, token, new CookieOptions { HttpOnly = true, Secure = o.Secure, SameSite = SameSiteMode.Lax, Path = "/", IsEssential = true });

    public static void Clear(HttpResponse response, CookieNames o) =>
        response.Cookies.Delete(o.Session, new CookieOptions { HttpOnly = true, Secure = o.Secure, SameSite = SameSiteMode.Lax, Path = "/" });
}
