using System.Globalization;
using System.Text;
using System.Text.Json;
using Tier1.Application.Abstractions;
using Tier1.Application.Access;
using Tier1.Application.Auditing;
using Tier1.Domain.Access;
using Tier1.Domain.Common;
using Tier1.Domain.Logging;
using Microsoft.EntityFrameworkCore;

namespace Tier1.Application.Logs;

public sealed record LogSearchRequest(
    string Collection,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    Guid? ActorId = null,
    string? Module = null,
    string? Feature = null,
    string? Action = null,
    string? Outcome = null,
    string? Ref = null,
    string? TargetId = null,
    string? Ip = null,
    string? CorrelationId = null,
    int Page = 1,
    int PageSize = 50);

/// <summary>البحث بعد تطبيق النطاق: وحدات مسموحة أو أحداث المستخدم نفسه فقط.</summary>
public sealed record LogFilter(LogSearchRequest Request, IReadOnlyCollection<string>? ModulesIn, Guid? RestrictActor);

public sealed record LogActorDto(string? UserId, string? Name, IReadOnlyList<string> Roles, string? Type);

public sealed record LogTargetDto(string? Type, string? Id, string? Ref, string? OwnerId);

public sealed record LogChangeDto(string Field, string? Old, string? New);

public sealed record LogItemDto(
    string Id,
    DateTimeOffset Ts,
    string Collection,
    string? Module,
    string? Feature,
    string? Action,
    string? Outcome,
    string? Message,
    LogActorDto? Actor,
    LogTargetDto? Target,
    IReadOnlyList<LogTargetDto> Related,
    IReadOnlyList<LogChangeDto> Changes,
    string? Reason,
    JsonElement? Details,
    IReadOnlyDictionary<string, string?> Context,
    long? Seq,
    string? Hash);

public sealed record LogPageDto(IReadOnlyList<LogItemDto> Items, long Total, int Page, int PageSize);

public sealed record ChainDayDto(string Chain, DateOnly Day, long FirstSeq, long LastSeq, int EventCount, string LastHash, bool? Intact, long? BrokenAtSeq, string? Note, DateTimeOffset? VerifiedAt);

public sealed record LogsStatusDto(int PendingOutbox, DateTimeOffset? OldestPendingAt, IReadOnlyList<ChainDayDto> Days);

public interface ILogReader
{
    Task<LogPageDto> SearchAsync(LogFilter filter, CancellationToken ct);

    Task<IReadOnlyList<LogItemDto>> ExportAsync(LogFilter filter, int max, CancellationToken ct);
}

public interface IChainVerifier
{
    /// <summary>يعيد حساب السلاسل للأيام المحددة ويحدث نتيجة المرساة؛ يرجع الأيام المفحوصة.</summary>
    Task<IReadOnlyList<ChainDayDto>> VerifyAsync(DateOnly from, DateOnly to, CancellationToken ct);
}

public sealed record ExportFile(string FileName, byte[] Content);

/// <summary>
/// قراءة السجلات بصلاحيات المصفوفة: لا إمكانية تعديل أو إضافة أو حذف لأي دور (SOD-12).
/// النطاق «الكل» بلا قيد، و«نطاقه» وحدات الإسناد، و«المسند إليه» و«سجلاته» أحداث المستخدم نفسه.
/// </summary>
public sealed class LogsService(IAuditStore db, IClock clock, ICurrentUser current, IAccessService access, ILogReader reader, IChainVerifier verifier, IAuditLog audit)
{
    public const int MaxExportRows = 10_000;

    public static string FeatureFor(string collection) => collection switch
    {
        LogCollections.Audit => Features.AuditLog,
        LogCollections.Security => Features.SecurityLog,
        LogCollections.Access => Features.AccessLog,
        LogCollections.Integration or LogCollections.JobRuns or LogCollections.HttpRequests or LogCollections.AppErrors => Features.TechLog,
        _ => throw new DomainException("unknown_collection", "مجموعة سجلات غير معروفة.", "Unknown log collection.", 400),
    };

    public async Task<LogPageDto> SearchAsync(LogSearchRequest req, CancellationToken ct)
    {
        var filter = await FilterAsync(req, Cap.View, ct);
        return await reader.SearchAsync(filter, ct);
    }

    /// <summary>تصدير CSV بإجراء خاص؛ والتصدير نفسه حدث في سجل الاطلاع ببصمة الملف وعدد الصفوف.</summary>
    public async Task<ExportFile> ExportAsync(LogSearchRequest req, CancellationToken ct)
    {
        var feature = FeatureFor(req.Collection);
        var mine = await access.CurrentAsync(ct);
        if (!mine.HasSpecial(feature, Specials.Export)) throw DomainException.Forbidden();
        var filter = await FilterAsync(req, Cap.View, ct);
        var items = await reader.ExportAsync(filter, MaxExportRows, ct);
        var bytes = Csv(items);
        var name = $"{req.Collection}-{CairoTime.DateOf(clock.UtcNow):yyyyMMdd}.csv";
        await audit.RecordNowAsync(AuditEvents.Access("log", feature, "log.export", "تصدير سجل", "Exported a log") with
        {
            Permission = $"{feature}:export",
            Target = new AuditTarget("LogCollection", req.Collection),
            Details = new
            {
                rows = items.Count,
                sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                filters = new { req.From, req.To, req.ActorId, req.Module, req.Feature, req.Action, req.Outcome, req.Ref, req.TargetId, req.Ip },
            },
        }, ct);
        return new ExportFile(name, bytes);
    }

    public async Task<LogsStatusDto> StatusAsync(int days, CancellationToken ct)
    {
        await RequireChainAccessAsync(ct);
        var since = CairoTime.DateOf(clock.UtcNow).AddDays(-Math.Clamp(days, 1, 366));
        var anchors = await db.ChainAnchors.AsNoTracking().Where(a => a.Day >= since)
            .OrderByDescending(a => a.Day).ThenBy(a => a.Chain).ToListAsync(ct);
        var pending = await db.Outbox.AsNoTracking().Where(o => o.SentAt == null).CountAsync(ct);
        var oldest = await db.Outbox.AsNoTracking().Where(o => o.SentAt == null).OrderBy(o => o.Id).Select(o => (DateTimeOffset?)o.CreatedAt).FirstOrDefaultAsync(ct);
        return new LogsStatusDto(pending, oldest, anchors.Select(ToDto).ToList());
    }

    public async Task<IReadOnlyList<ChainDayDto>> VerifyAsync(DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        await RequireChainAccessAsync(ct);
        var today = CairoTime.DateOf(clock.UtcNow);
        var end = to ?? today;
        var start = from ?? end.AddDays(-6);
        if (start > end || end.DayNumber - start.DayNumber > 31)
            throw new DomainException("invalid_range", "الفترة غير صالحة (31 يومًا على الأكثر في الطلب الواحد).", "Invalid range (31 days at most per request).", 400);
        var result = await verifier.VerifyAsync(start, end, ct);
        audit.Record(AuditEvents.Security("log.verify", Features.AuditLog, "فحص سلامة السجلات", "Verified log integrity") with
        {
            Outcome = result.All(r => r.Intact != false) ? "success" : "failure",
            Details = new { from = start, to = end, days = result.Count, broken = result.Where(r => r.Intact == false).Select(r => new { r.Chain, r.Day, r.BrokenAtSeq }) },
        });
        await db.SaveChangesAsync(ct);
        return result;
    }

    public static ChainDayDto ToDto(ChainAnchor a) => new(a.Chain, a.Day, a.FirstSeq, a.LastSeq, a.EventCount, a.LastHash, a.Intact, a.BrokenAtSeq, a.VerifyNote, a.VerifiedAt);

    private async Task RequireChainAccessAsync(CancellationToken ct)
    {
        // حالة السلاسل وفحصها لمن يرى سجل العمليات كاملًا: المراجع الرقابي ومدير المركز والمدير الأعلى افتراضيًا.
        var mine = await access.CurrentAsync(ct);
        if (mine.ScopeFor(Features.AuditLog, Cap.View) != AccessScope.All)
            throw DomainException.Forbidden();
    }

    private async Task<LogFilter> FilterAsync(LogSearchRequest req, Cap cap, CancellationToken ct)
    {
        var feature = FeatureFor(req.Collection);
        var mine = await access.CurrentAsync(ct);
        var scope = mine.ScopeFor(feature, cap) ?? throw DomainException.Forbidden();
        var page = Math.Clamp(req.Page, 1, 10_000);
        var size = Math.Clamp(req.PageSize, 1, 200);
        var normalized = req with { Page = page, PageSize = size };
        return scope switch
        {
            AccessScope.All => new LogFilter(normalized, null, null),
            // السجلات لا تقسم على الجهات؛ «نطاقه» فيها لا معنى له، فلا يرى شيئًا بدل أن يرى كل شيء.
            AccessScope.Unit => throw DomainException.Forbidden(),
            _ => new LogFilter(normalized, null, current.UserId ?? throw DomainException.Forbidden()),
        };
    }

    private static byte[] Csv(IReadOnlyList<LogItemDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("time_cairo,actor,roles,action,message,outcome,target_type,target_ref,changes,reason,ip,correlation_id,seq");
        foreach (var i in items)
        {
            var changes = string.Join(" | ", i.Changes.Select(c => $"{c.Field}: {c.Old} -> {c.New}"));
            sb.AppendJoin(',', new[]
            {
                CairoTime.ToCairo(i.Ts).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                i.Actor?.Name, string.Join(' ', i.Actor?.Roles ?? []), i.Action, i.Message, i.Outcome,
                i.Target?.Type, i.Target?.Ref ?? i.Target?.Id, changes, i.Reason,
                i.Context.GetValueOrDefault("ip"), i.Context.GetValueOrDefault("correlationId"), i.Seq?.ToString(CultureInfo.InvariantCulture),
            }.Select(Cell));
            sb.AppendLine();
        }
        // BOM ليفتح Excel النص العربي صحيحًا.
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(sb.ToString())];
    }

    /// <summary>خلية CSV آمنة: اقتباس، ومنع حقن الصيغ في Excel.</summary>
    private static string Cell(string? value)
    {
        var v = value ?? "";
        if (v.Length > 0 && "=+-@\t\r".Contains(v[0], StringComparison.Ordinal)) v = "'" + v;
        return "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
