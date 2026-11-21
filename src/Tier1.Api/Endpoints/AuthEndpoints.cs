using Tier1.Api.Security;
using Tier1.Api.Startup;
using Tier1.Application.Accounts;
using Tier1.Infrastructure.Security;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace Tier1.Api.Endpoints;

public sealed record LoginRequest(string Email, string Password);

public sealed record CodeRequest(string Code);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public sealed record LanguageRequest(string Language);

public static class AuthEndpoints
{
    public static void MapAuth(this RouteGroupBuilder api)
    {
        // Sign-in, sign-out and their cookies are never replayed from a stored answer (ADR-0018).
        var auth = api.MapGroup("/auth").WithoutIdempotency();
        auth.MapGet("/csrf", (HttpContext ctx, IAntiforgery af) =>
        {
            var tokens = af.GetAndStoreTokens(ctx);
            return TypedResults.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous();

        auth.MapPost("/login", async (LoginRequest req, SignInService signIn, HttpContext ctx, IOptions<CookieNames> cookies, CancellationToken ct) =>
        {
            var result = await signIn.LoginAsync(new LoginCommand(req.Email, req.Password), ct);
            SessionCookies.Issue(ctx.Response, cookies.Value, result.Token);
            return TypedResults.Ok(new { next = result.Next });
        }).AllowAnonymous().RequireRateLimiting(ApiSetup.LoginLimit);

        // بعد التحقق بخطوتين أو تغيير الكلمة يصدر رمز جلسة جديد؛ الرمز السابق لا يعود صالحًا.
        auth.MapPost("/mfa/verify", async (CodeRequest req, SignInService signIn, HttpContext ctx, IOptions<CookieNames> cookies, CancellationToken ct) =>
        {
            SessionCookies.Issue(ctx.Response, cookies.Value, await signIn.VerifyMfaAsync(req.Code, ct));
            return TypedResults.Ok(new { next = "done" });
        }).RequireAuthorization(Policies.Partial).RequireRateLimiting(ApiSetup.LoginLimit);

        auth.MapPost("/mfa/setup", async (SignInService signIn, CancellationToken ct) => TypedResults.Ok(await signIn.BeginMfaSetupAsync(ct)))
            .RequireAuthorization(Policies.Partial);

        auth.MapPost("/mfa/enable", async (CodeRequest req, SignInService signIn, HttpContext ctx, IOptions<CookieNames> cookies, CancellationToken ct) =>
        {
            SessionCookies.Issue(ctx.Response, cookies.Value, await signIn.EnableMfaAsync(req.Code, ct));
            return TypedResults.Ok(new { next = "done" });
        }).RequireAuthorization(Policies.Partial).RequireRateLimiting(ApiSetup.LoginLimit);

        auth.MapPost("/change-password", async (ChangePasswordRequest req, SignInService signIn, HttpContext ctx, IOptions<CookieNames> cookies, CancellationToken ct) =>
        {
            SessionCookies.Issue(ctx.Response, cookies.Value, await signIn.ChangePasswordAsync(req.CurrentPassword, req.NewPassword, ct));
            return Results.NoContent();
        }).RequireAuthorization(Policies.Partial).RequireRateLimiting(ApiSetup.LoginLimit);

        auth.MapPost("/logout", async (SignInService signIn, HttpContext ctx, IOptions<CookieNames> cookies, CancellationToken ct) =>
        {
            await signIn.LogoutAsync(ct);
            SessionCookies.Clear(ctx.Response, cookies.Value);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Partial);

        api.MapPost("/sessions/logout-all", async (SignInService signIn, HttpContext ctx, IOptions<CookieNames> cookies, CancellationToken ct) =>
        {
            var count = await signIn.LogoutAllAsync(ct);
            SessionCookies.Clear(ctx.Response, cookies.Value);
            return TypedResults.Ok(new { revoked = count });
        }).RequireAuthorization(Policies.Staff).WithoutIdempotency();

        api.MapGet("/me", async (MeService me, HttpContext ctx, CancellationToken ct) =>
            TypedResults.Ok(await me.GetAsync(ctx.User.HasClaim(ClaimNames.MfaPending, "true"), ct))).RequireAuthorization(Policies.Partial);
        api.MapPut("/me/language", async (LanguageRequest req, MeService me, CancellationToken ct) =>
        {
            await me.SetLanguageAsync(req.Language, ct);
            return Results.NoContent();
        }).RequireAuthorization(Policies.Staff);
        api.MapGet("/me/sessions", async (MeService me, CancellationToken ct) => TypedResults.Ok(await me.SessionsAsync(ct))).RequireAuthorization(Policies.Staff);
    }
}
