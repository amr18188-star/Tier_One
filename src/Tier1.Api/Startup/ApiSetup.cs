using Tier1.Application.Abstractions;
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

namespace Tier1.Api.Startup;

public static class ApiSetup
{
    public const string LoginLimit = "login";
    public const string SensitiveLimit = "sensitive";

    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<CookieNames>(config.GetSection("Cookies"));
        // BE-22: the API contract as an OpenAPI document; served in Development only, and pinned by a test (docs/api/openapi.json).
        services.AddOpenApi();
        // Demo data after the server starts (Development only): Seed:DemoData=true.
        if (config.GetValue<bool>("Seed:DemoData") && !config.GetValue<bool>("Seed:Flow"))
            services.AddHostedService<Tier1.Api.Startup.DemoData.DemoDataSeeder>();
        services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)); // BE-7: names only
            o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        });
        // BE-6: malformed JSON, an unparsable id or a missing body reach the exception handler in every environment, so they get
        // the JSON error with a code and trace id, not a bodiless 400 (the default outside Development).
        services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
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
                await ApiErrors.WriteAsync(ctx.HttpContext, 429, "rate_limited", Lang.Pick("طلبات كثيرة. انتظر قليلًا ثم أعد المحاولة.", "Too many requests. Wait a moment and retry."), ct);
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
        app.UseTraceIdHeader();
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
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> log, ITechnicalLog tech, IDatabaseErrors databaseErrors) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception exception, CancellationToken ct)
    {
        var db = databaseErrors.Classify(exception);
        var (status, code, title) = exception switch
        {
            DomainException d => (d.Status, d.Code, d.Message),
            DbUpdateConcurrencyException => (409, "conflict", Lang.Pick("عدّل مستخدم آخر هذا العنصر. حدّث الصفحة ثم أعد المحاولة.", "Someone else changed this item. Refresh and try again.")),
            // DB-3: two official cycles for one quarter, created at the same moment.
            _ when db is { Kind: DatabaseErrorKind.Duplicate, Constraint: "ux_cycles_official_quarter" } =>
                (409, "quarter_taken", Lang.Pick("توجد دورة رسمية لهذا الربع بالفعل.", "A cycle for this quarter already exists.")),
            // سباق على قيد تفرد (طلبان متزامنان لنفس الشيء): تعارض لا خطأ خادم.
            _ when db.Kind == DatabaseErrorKind.Duplicate =>
                (409, "conflict", Lang.Pick("سبقك طلب آخر للعنصر نفسه. حدّث الصفحة ثم أعد المحاولة.", "Another request for the same item came first. Refresh and try again.")),
            // نص أطول من عمود القاعدة، أو قيمة يرفضها قيد التحقق، أو مرجع غير موجود: خطأ في المدخلات لا خطأ خادم.
            _ when db.Kind == DatabaseErrorKind.TooLong => (400, "text_too_long", Lang.Pick("أحد النصوص أطول من المسموح. اختصره ثم أعد المحاولة.", "A text is longer than allowed. Shorten it and retry.")),
            _ when db.Kind == DatabaseErrorKind.InvalidValue => (400, "invalid_value", Lang.Pick("قيمة غير مقبولة في الطلب.", "A value in the request is not accepted.")),
            _ when db.Kind == DatabaseErrorKind.InUse => (409, "in_use", Lang.Pick("العنصر مرتبط ببيانات أخرى أو لم يعد موجودًا. حدّث الصفحة ثم أعد المحاولة.", "The item is linked to other data or no longer exists. Refresh and try again.")),
            BadHttpRequestException b => (b.StatusCode, "bad_request", Lang.Pick("الطلب غير صالح.", "Invalid request.")),
            System.Text.Json.JsonException => (400, "bad_request", Lang.Pick("الطلب غير صالح.", "Invalid request.")),
            _ => (500, "server_error", Lang.Pick("حدث خطأ غير متوقع. أعد المحاولة، وإن تكرر أبلغ الدعم الفني.", "An unexpected error occurred. Retry; if it persists, contact support.")),
        };
        var traceId = ApiErrors.TraceId(ctx);
        if (status >= 500)
        {
            log.LogError(exception, "Unhandled error on {Path}", ctx.Request.Path);
            tech.Error(new AppErrorRecord(exception.GetType().FullName ?? "", exception.Message,
                exception.StackTrace is { } s ? (s.Length > 8000 ? s[..8000] : s) : "",
                (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? ctx.Request.Path.Value ?? "", ctx.Request.Method,
                ctx.User.FindFirst(ClaimNames.UserId)?.Value, traceId));
        }
        await ApiErrors.WriteAsync(ctx, status, code, title, ct);
        return true;
    }
}
