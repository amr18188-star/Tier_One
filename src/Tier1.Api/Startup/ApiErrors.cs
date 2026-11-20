using System.Diagnostics;

namespace Tier1.Api.Startup;

/// <summary>
/// BE-6: one shape for every API error, whoever writes it (the exception handler, authentication, CSRF, the rate limiter, an
/// unknown route): {status, code, title, traceId}. The trace id is the one in the X-Trace-Id header, so the user can quote it.
/// </summary>
public static class ApiErrors
{
    public const string TraceHeader = "X-Trace-Id";

    public static string TraceId(HttpContext ctx) => Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

    public static Task WriteAsync(HttpContext ctx, int status, string code, string title, CancellationToken ct = default)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { status, code, title, traceId = TraceId(ctx) }, ct);
    }

    /// <summary>Every response carries its trace id, so a support request can name the exact request.</summary>
    public static IApplicationBuilder UseTraceIdHeader(this IApplicationBuilder app) => app.Use((ctx, next) =>
    {
        ctx.Response.OnStarting(() =>
        {
            ctx.Response.Headers[TraceHeader] = TraceId(ctx);
            return Task.CompletedTask;
        });
        return next(ctx);
    });
}
