using System.Diagnostics;

namespace Tier1.Api.Startup;

/// <summary>One invalid field of a request: its JSON name, the error code, and the message in the request's language.</summary>
public sealed record FieldError(string Field, string Code, string Message);

/// <summary>
/// BE-6: one shape for every API error, whoever writes it (the exception handler, authentication, CSRF, the rate limiter, an
/// unknown route): {status, code, title, traceId, errors}. The trace id is the one in the X-Trace-Id header, so the user can quote
/// it. <c>errors</c> lists the request fields to mark in the form (empty when the error is about the request as a whole).
/// The statuses and codes are in docs/api/errors.md.
/// </summary>
public static class ApiErrors
{
    public const string TraceHeader = "X-Trace-Id";

    public static string TraceId(HttpContext ctx) => Activity.Current?.TraceId.ToString() ?? ctx.TraceIdentifier;

    public static Task WriteAsync(HttpContext ctx, int status, string code, string title, CancellationToken ct = default) =>
        WriteAsync(ctx, status, code, title, [], ct);

    public static Task WriteAsync(HttpContext ctx, int status, string code, string title, IReadOnlyList<FieldError> errors, CancellationToken ct = default)
    {
        ctx.Response.StatusCode = status;
        return ctx.Response.WriteAsJsonAsync(new { status, code, title, traceId = TraceId(ctx), errors }, ct);
    }

    /// <summary>
    /// The field a JSON reading error is about, from its path: <c>$.endsOn</c> is "endsOn", <c>$.items[2].value</c> is
    /// "items[2].value". Null for the body as a whole.
    /// </summary>
    public static string? FieldFromJsonPath(string? path) =>
        path is { Length: > 2 } p && p.StartsWith("$.", StringComparison.Ordinal) ? p[2..] : null;

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
