using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Tier1.Api.Security;
using Tier1.Domain.Common;
using Tier1.Domain.Logging;
using Tier1.Infrastructure.Logging;
using Tier1.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using MongoDB.Bson;

namespace Tier1.Api.Startup;

public static class ApiSetup
{
    public const string LoginLimit = "login";
    public const string SensitiveLimit = "sensitive";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CookieNames>(config.GetSection("Cookies"));
        // Demo data after the server starts (Development only): Seed:DemoData=true.
        if (config.GetValue<bool>("Seed:DemoData")) services.AddHostedService<Tier1.Api.Startup.DemoData.DemoDataSeeder>();
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });
        services.AddAuthentication(SessionAuthenticationHandler.SchemeName)
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationHandler.SchemeName, null);
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, DenialLoggingResultHandler>();
        var complete = new AuthorizationPolicyBuilder().RequireAuthenticatedUser()
            .RequireAssertion(c => !c.User.HasClaim(ClaimNames.MfaPending, "true") && !c.User.HasClaim(ClaimNames.PasswordChangePending, "true")).Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(Policies.Staff, complete)
            .AddPolicy(Policies.Partial, p => p.RequireAuthenticatedUser())
            // أي مسار بلا سياسة صريحة يتطلب جلسة مكتملة؛ والاختبار يتأكد أن كل مسار /api يعلن صلاحيته.
            .SetFallbackPolicy(complete);

        services.AddAntiforgery(o =>
        {
            o.HeaderName = "X-XSRF-TOKEN";
            o.Cookie.Name = config["Cookies:Antiforgery"] ?? "t1_af";
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.Cookie.SecurePolicy = config.GetValue("Cookies:Secure", true) ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
        });

        var limits = config.GetSection("RateLimiting");
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.StatusCode = 429;
                await ctx.HttpContext.Response.WriteAsJsonAsync(new { status = 429, code = "rate_limited", title = Lang.Pick("طلبات كثيرة. انتظر قليلًا ثم أعد المحاولة.", "Too many requests. Wait a moment and retry.") }, ct);
            };
            // الدخول والتحقق بخطوتين: حد لكل عنوان يتسع لشبكة الجامعة خلف عنوان واحد؛ الحماية لكل حساب بقفله بعد المحاولات الفاشلة.
            o.AddPolicy(LoginLimit, ctx => RateLimitPartition.GetFixedWindowLimiter("login:" + Ip(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.GetValue("LoginPerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
            // العمليات الحساسة (الصلاحيات، الحسابات، التصدير، الرفع): حد لكل مستخدم، وإلا لكل عنوان.
            o.AddPolicy(SensitiveLimit, ctx => RateLimitPartition.GetFixedWindowLimiter(Caller(ctx), _ => new FixedWindowRateLimiterOptions { PermitLimit = limits.GetValue("SensitivePerMinute", 30), Window = TimeSpan.FromMinutes(1) }));
        });

        services.Configure<ForwardedHeadersOptions>(o =>
        {
            // الثقة في رؤوس forwarded من الوسيط العكسي المعرّف فقط.
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
            foreach (var proxy in config.GetSection("Network:TrustedProxies").Get<string[]>() ?? [])
                if (System.Net.IPAddress.TryParse(proxy, out var ip)) o.KnownProxies.Add(ip);
        });
        services.AddExceptionHandler<ApiExceptionHandler>();
        services.AddProblemDetails();
        return services;
    }

    public static WebApplication UsePipeline(this WebApplication app)
    {
        app.UseForwardedHeaders();
        app.UseExceptionHandler();
        app.UseSecurityHeaders();
        app.UseMiddleware<RequestLanguageMiddleware>();
        app.UseDefaultFiles();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = c =>
            {
                if (c.Context.Request.Path.StartsWithSegments("/assets")) c.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            },
        });
        // المصادقة قبل حدود المعدل: الحدود «لكل مستخدم» تعرف هويته.
        app.UseAuthentication();
        app.UseRouting();
        app.UseMiddleware<RequestLoggingMiddleware>();
        app.UseRateLimiter();
        app.UseAuthorization();
        app.UseMiddleware<CsrfMiddleware>();
        return app;
    }

    private static string Ip(HttpContext ctx) => ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    /// <summary>صاحب الطلب لحدود المعدل: المستخدم المسجل، وإلا العنوان.</summary>
    private static string Caller(HttpContext ctx) =>
        ctx.User.FindFirst(ClaimNames.UserId)?.Value is { } uid ? "u:" + uid : "ip:" + Ip(ctx);
}

/// <summary>
/// أخطاء موحدة: رمز ثابت ورسالة بلغة الطلب، دون كشف تفاصيل داخلية. الأخطاء غير المتوقعة تسجل في app_errors
/// بمعرف الارتباط نفسه الذي يظهر للمستخدم.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log, TechLogWriter tech) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        var (status, code, title) = exception switch
        {
            DomainException d => (d.Status, d.Code, d.Message),
            DbUpdateConcurrencyException => (409, "conflict", Lang.Pick("عدّل مستخدم آخر هذا العنصر. حدّث الصفحة ثم أعد المحاولة.", "Someone else changed this item. Refresh and try again.")),
            // سباق على قيد تفرد (طلبان متزامنان لنفس الشيء): تعارض لا خطأ خادم.
            DbUpdateException { InnerException: Npgsql.PostgresException { SqlState: "23505" } } =>
                (409, "conflict", Lang.Pick("سبقك طلب آخر للعنصر نفسه. حدّث الصفحة ثم أعد المحاولة.", "Another request for the same item came first. Refresh and try again.")),
            // نص أطول من عمود القاعدة، أو قيمة يرفضها قيد التحقق، أو مرجع غير موجود: خطأ في المدخلات لا خطأ خادم.
            _ when PgState(exception) == "22001" => (400, "text_too_long", Lang.Pick("أحد النصوص أطول من المسموح. اختصره ثم أعد المحاولة.", "A text is longer than allowed. Shorten it and retry.")),
            _ when PgState(exception) is "23514" or "22P02" or "22003" => (400, "invalid_value", Lang.Pick("قيمة غير مقبولة في الطلب.", "A value in the request is not accepted.")),
            _ when PgState(exception) == "23503" => (409, "in_use", Lang.Pick("العنصر مرتبط ببيانات أخرى أو لم يعد موجودًا. حدّث الصفحة ثم أعد المحاولة.", "The item is linked to other data or no longer exists. Refresh and try again.")),
            BadHttpRequestException b => (b.StatusCode, "bad_request", Lang.Pick("الطلب غير صالح.", "Invalid request.")),
            System.Text.Json.JsonException => (400, "bad_request", Lang.Pick("الطلب غير صالح.", "Invalid request.")),
            _ => (500, "server_error", Lang.Pick("حدث خطأ غير متوقع. أعد المحاولة، وإن تكرر أبلغ الدعم الفني.", "An unexpected error occurred. Retry; if it persists, contact support.")),
        };
        var traceId = Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;
        if (status >= 500)
        {
            log.LogError(exception, "Unhandled error on {Path}", ctx.Request.Path);
            tech.Write(LogCollections.AppErrors, new BsonDocument
            {
                { "_id", Guid.CreateVersion7().ToString() },
                { "ts", DateTime.UtcNow },
                { "type", exception.GetType().FullName },
                { "message", exception.Message },
                { "stack", exception.StackTrace is { } s ? (s.Length > 8000 ? s[..8000] : s) : "" },
                { "path", (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? ctx.Request.Path.Value ?? "" },
                { "method", ctx.Request.Method },
                { "userId", (BsonValue?)ctx.User.FindFirst(ClaimNames.UserId)?.Value ?? BsonNull.Value },
                { "correlationId", traceId },
            });
        }
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(new { status, code, title, traceId }, ct);
        return true;
    }

    /// <summary>رمز خطأ PostgreSQL من الاستثناء نفسه (ExecuteUpdate) أو من داخل DbUpdateException (SaveChanges).</summary>
    private static string? PgState(Exception e) => (e as Npgsql.PostgresException ?? e.InnerException as Npgsql.PostgresException)?.SqlState;
}
