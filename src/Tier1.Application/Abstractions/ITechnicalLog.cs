namespace Tier1.Application.Abstractions;

/// <summary>One API request in the technical log: no bodies and no query strings.</summary>
public sealed record HttpRequestRecord(string Method, string Path, int Status, long DurationMs, string? UserId, string? Ip, string CorrelationId);

/// <summary>One unexpected error in the technical log, under the correlation id the user was shown.</summary>
public sealed record AppErrorRecord(string Type, string Message, string Stack, string Path, string Method, string? UserId, string CorrelationId);

/// <summary>
/// BE-14: the technical logs (requests and unexpected errors). Written to a background queue so a request is never slowed;
/// the log store and its document format live in Infrastructure, not in the web layer.
/// </summary>
public interface ITechnicalLog
{
    void Request(HttpRequestRecord request);

    void Error(AppErrorRecord error);
}
