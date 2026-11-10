using System.Diagnostics;
using System.Globalization;
using Tier1.Domain.Common;
using Tier1.Domain.Logging;
using Tier1.Infrastructure.Logging;
using Tier1.Infrastructure.Security;
using Microsoft.AspNetCore.Antiforgery;
using MongoDB.Bson;

namespace Tier1.Api.Security;

/// <summary>
/// حماية CSRF: كل طلب يغير بيانات على /api يحمل رأس X-XSRF-TOKEN المطابق للكوكي (Double Submit موقع من الخادم).
/// الواجهة تحصل على الرمز من /api/auth/csrf.
/// </summary>
public sealed class CsrfMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var unsafeMethod = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);
        if (unsafeMethod && ctx.Request.Path.StartsWithSegments("/api") && !await antiforgery.IsRequestValidAsync(ctx))
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            await ctx.Response.WriteAsJsonAsync(new { status = 400, code = "csrf", title = Lang.Pick("انتهت صلاحية الصفحة. حدّث الصفحة وحاول مرة أخرى.", "This page has expired. Refresh and try again.") });
            return;
        }
        await next(ctx);
    }
}

/// <summary>لغة الطلب من Accept-Language (الواجهة ترسله دائمًا)، والعربية افتراضيًا.</summary>
public sealed class RequestLanguageMiddleware(RequestDelegate next)
{
    private static readonly CultureInfo Arabic = CultureInfo.GetCultureInfo("ar-EG");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public Task InvokeAsync(HttpContext ctx)
    {
        var header = ctx.Request.Headers.AcceptLanguage.ToString();
        CultureInfo.CurrentUICulture = header.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? English : Arabic;
        return next(ctx);
    }
}

/// <summary>
/// سجل الطلبات التقني في MongoDB (http_requests): الطريقة والمسار القالبي والحالة والمدة والمستخدم والعنوان،
/// بلا أجسام الطلبات ولا سلاسل الاستعلام. يكتب عبر طابور غير متزامن فلا يبطئ الطلب.
/// </summary>
public sealed class RequestLoggingMiddleware(RequestDelegate next, TechLogWriter tech)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!ctx.Request.Path.StartsWithSegments("/api"))
        {
            await next(ctx);
            return;
        }
        var watch = Stopwatch.StartNew();
        try
        {
            await next(ctx);
        }
        finally
        {
            var route = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;
            tech.Write(LogCollections.HttpRequests, new BsonDocument
            {
                { "_id", Guid.CreateVersion7().ToString() },
                { "ts", DateTime.UtcNow },
                { "method", ctx.Request.Method },
                { "path", route ?? ctx.Request.Path.Value ?? "" },
                { "status", ctx.Response.StatusCode },
                { "durationMs", watch.ElapsedMilliseconds },
                { "userId", (BsonValue?)ctx.User.FindFirst(ClaimNames.UserId)?.Value ?? BsonNull.Value },
                { "ip", (BsonValue?)ctx.Connection.RemoteIpAddress?.ToString() ?? BsonNull.Value },
                { "correlationId", Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier },
            });
        }
    }
}

public static class SecurityHeaders
{
    /// <summary>سياسة محتوى صارمة: لا سكربتات خارجية ولا إطارات.</summary>
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (ctx, next) =>
    {
        var h = ctx.Response.Headers;
        h["X-Content-Type-Options"] = "nosniff";
        h["Referrer-Policy"] = "no-referrer";
        h["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=()";
        h["Cross-Origin-Opener-Policy"] = "same-origin";
        h["X-Frame-Options"] = "DENY";
        if (!h.ContainsKey("Content-Security-Policy"))
            h["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; font-src 'self' data:; connect-src 'self'; object-src 'none'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
        // على HTTPS (مباشرة أو عبر الوسيط الموثوق): المتصفح لا يعود إلى HTTP سنة كاملة.
        if (ctx.Request.IsHttps) h["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";
        // ردود الواجهة البرمجية فيها بيانات شخصية ومستندات: لا تحفظ في ذاكرة المتصفح ولا الوسطاء (أجهزة المعامل المشتركة).
        if (ctx.Request.Path.StartsWithSegments("/api"))
            ctx.Response.OnStarting(() =>
            {
                h.CacheControl = "no-store";
                h.Pragma = "no-cache";
                return Task.CompletedTask;
            });
        await next();
    });
}
