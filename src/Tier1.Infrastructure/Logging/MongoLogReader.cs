using System.Text.Json;
using System.Text.RegularExpressions;
using Tier1.Application.Logs;
using Tier1.Domain.Common;
using Tier1.Domain.Logging;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

namespace Tier1.Infrastructure.Logging;

/// <summary>قراءة السجلات من MongoDB بفلاتر مفهرسة، الأحدث أولًا.</summary>
public sealed class MongoLogReader(MongoLogContext mongo) : ILogReader
{
    private static readonly JsonWriterSettings RelaxedJson = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };

    public async Task<LogPageDto> SearchAsync(LogFilter filter, CancellationToken ct)
    {
        var r = filter.Request;
        var c = mongo.Collection(r.Collection);
        var f = Build(filter);
        var total = await c.CountDocumentsAsync(f, new CountOptions { Limit = 100_000 }, ct);
        var docs = await c.Find(f).Sort(Builders<BsonDocument>.Sort.Descending("ts")).Skip((r.Page - 1) * r.PageSize).Limit(r.PageSize).ToListAsync(ct);
        return new LogPageDto(docs.Select(d => Map(r.Collection, d)).ToList(), total, r.Page, r.PageSize);
    }

    public async Task<IReadOnlyList<LogItemDto>> ExportAsync(LogFilter filter, int max, CancellationToken ct)
    {
        var c = mongo.Collection(filter.Request.Collection);
        var docs = await c.Find(Build(filter)).Sort(Builders<BsonDocument>.Sort.Descending("ts")).Limit(max).ToListAsync(ct);
        return docs.Select(d => Map(filter.Request.Collection, d)).ToList();
    }

    private static FilterDefinition<BsonDocument> Build(LogFilter filter)
    {
        var r = filter.Request;
        var b = Builders<BsonDocument>.Filter;
        var tech = r.Collection is LogCollections.HttpRequests or LogCollections.AppErrors or LogCollections.JobRuns;
        var parts = new List<FilterDefinition<BsonDocument>>();
        if (r.From is { } from) parts.Add(b.Gte("ts", from.UtcDateTime));
        if (r.To is { } to) parts.Add(b.Lt("ts", to.UtcDateTime));
        var actorField = tech ? "userId" : "actor.userId";
        if (r.ActorId is { } actor) parts.Add(b.Eq(actorField, actor.ToString()));
        if (filter.RestrictActor is { } me) parts.Add(b.Eq(actorField, me.ToString()));
        if (!string.IsNullOrWhiteSpace(r.CorrelationId)) parts.Add(b.Eq(tech ? "correlationId" : "context.correlationId", r.CorrelationId.Trim()));
        if (!tech)
        {
            if (!string.IsNullOrWhiteSpace(r.Module)) parts.Add(b.Eq("module", r.Module.Trim()));
            if (!string.IsNullOrWhiteSpace(r.Feature)) parts.Add(b.Eq("feature", r.Feature.Trim()));
            if (!string.IsNullOrWhiteSpace(r.Action)) parts.Add(b.Eq("action", r.Action.Trim()));
            if (!string.IsNullOrWhiteSpace(r.Outcome)) parts.Add(b.Eq("outcome", r.Outcome.Trim()));
            if (!string.IsNullOrWhiteSpace(r.Ip)) parts.Add(b.Eq("context.ip", r.Ip.Trim()));
            if (!string.IsNullOrWhiteSpace(r.TargetId)) parts.Add(b.Or(b.Eq("target.id", r.TargetId.Trim()), b.Eq("related.id", r.TargetId.Trim())));
            if (!string.IsNullOrWhiteSpace(r.Ref))
            {
                // بحث بادئة غير حساس لحالة الأحرف، مع تهريب النص فلا يصبح تعبيرًا منتظمًا من المستخدم.
                var pattern = new BsonRegularExpression("^" + Regex.Escape(r.Ref.Trim()), "i");
                parts.Add(b.Or(b.Regex("target.ref", pattern), b.Regex("related.ref", pattern), b.Eq("target.id", r.Ref.Trim())));
            }
            if (filter.ModulesIn is { } modules) parts.Add(b.In("module", modules));
        }
        else if (!string.IsNullOrWhiteSpace(r.Action))
        {
            parts.Add(b.Regex(r.Collection == LogCollections.JobRuns ? "job" : "path", new BsonRegularExpression(Regex.Escape(r.Action.Trim()), "i")));
        }
        return parts.Count == 0 ? b.Empty : b.And(parts);
    }

    private static LogItemDto Map(string collection, BsonDocument d)
    {
        var ts = new DateTimeOffset(d.GetValue("ts", BsonNull.Value) is { IsBsonDateTime: true } t ? t.ToUniversalTime() : DateTime.UnixEpoch, TimeSpan.Zero);
        var id = d.GetValue("_id", BsonNull.Value).ToString() ?? "";
        if (collection is LogCollections.HttpRequests or LogCollections.AppErrors or LogCollections.JobRuns)
            return MapTech(collection, id, ts, d);
        var ctx = d.GetValue("context", new BsonDocument()) is { IsBsonDocument: true } c
            ? c.AsBsonDocument.Elements.ToDictionary(e => e.Name, e => Str(e.Value))
            : new Dictionary<string, string?>();
        var integrity = d.GetValue("integrity", BsonNull.Value);
        return new LogItemDto(
            id,
            ts,
            collection,
            Str(d, "module"),
            Str(d, "feature"),
            Str(d, "action"),
            Str(d, "outcome"),
            Lang.Name(Str(d, "messageAr") ?? "", Str(d, "messageEn")),
            d.GetValue("actor", BsonNull.Value) is { IsBsonDocument: true } a
                ? new LogActorDto(Str(a.AsBsonDocument, "userId"), Str(a.AsBsonDocument, "name"),
                    a.AsBsonDocument.GetValue("roles", new BsonArray()) is { IsBsonArray: true } roles ? roles.AsBsonArray.Select(x => x.ToString() ?? "").ToList() : [],
                    Str(a.AsBsonDocument, "type"))
                : null,
            Target(d.GetValue("target", BsonNull.Value)),
            d.GetValue("related", new BsonArray()) is { IsBsonArray: true } rel ? rel.AsBsonArray.Select(Target).OfType<LogTargetDto>().ToList() : [],
            d.GetValue("changes", new BsonArray()) is { IsBsonArray: true } ch
                ? ch.AsBsonArray.Where(x => x.IsBsonDocument).Select(x => new LogChangeDto(Str(x.AsBsonDocument, "field") ?? "", Str(x.AsBsonDocument, "old"), Str(x.AsBsonDocument, "new"))).ToList()
                : [],
            Str(d, "reason"),
            d.GetValue("details", BsonNull.Value) is { IsBsonNull: false } details ? Json(details) : null,
            ctx,
            integrity.IsBsonDocument && integrity.AsBsonDocument.TryGetValue("seq", out var seq) ? seq.ToInt64() : null,
            integrity.IsBsonDocument ? Str(integrity.AsBsonDocument, "hash") : null);
    }

    private static LogItemDto MapTech(string collection, string id, DateTimeOffset ts, BsonDocument d)
    {
        var (action, outcome, message) = collection switch
        {
            LogCollections.HttpRequests => ($"{Str(d, "method")} {Str(d, "path")}", Str(d, "status"), $"{Str(d, "status")} · {Str(d, "durationMs")} ms"),
            LogCollections.AppErrors => (Str(d, "path"), "error", Str(d, "message")),
            _ => (Str(d, "job"), Str(d, "outcome"), Str(d, "summary")),
        };
        var copy = d.DeepClone().AsBsonDocument;
        copy.Remove("_id");
        copy.Remove("ts");
        return new LogItemDto(id, ts, collection, "log", "log.tech", action, outcome, message,
            Str(d, "userId") is { } uid ? new LogActorDto(uid, null, [], "user") : null, null, [], [], null, Json(copy),
            new Dictionary<string, string?> { ["ip"] = Str(d, "ip"), ["correlationId"] = Str(d, "correlationId") }, null, null);
    }

    private static LogTargetDto? Target(BsonValue v) =>
        v is { IsBsonDocument: true } ? new LogTargetDto(Str(v.AsBsonDocument, "type"), Str(v.AsBsonDocument, "id"), Str(v.AsBsonDocument, "ref"), Str(v.AsBsonDocument, "ownerId")) : null;

    private static string? Str(BsonDocument d, string name) => d.TryGetValue(name, out var v) ? Str(v) : null;

    private static string? Str(BsonValue v) => v.IsBsonNull ? null : v.IsString ? v.AsString : v.IsBsonDocument || v.IsBsonArray ? v.ToJson(RelaxedJson) : v.ToString();

    private static JsonElement Json(BsonValue v)
    {
        using var doc = JsonDocument.Parse(v.ToJson(RelaxedJson));
        return doc.RootElement.Clone();
    }
}
