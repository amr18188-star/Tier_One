using System.Threading.Channels;
using Tier1.Application.Auditing;
using Tier1.Domain.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;

namespace Tier1.Infrastructure.Logging;

public sealed class MongoLogOptions
{
    public string Database { get; set; } = "tier1_logs";

    /// <summary>ينشئ المجموعات والفهارس عند البدء بحساب الإدارة (التطوير). الإنتاج يشغل --mongo-setup مرة واحدة.</summary>
    public bool SetupOnStartup { get; set; }

    public int DispatchBatch { get; set; } = 200;

    public int HttpRequestsTtlDays { get; set; } = 90;

    public int AppErrorsTtlDays { get; set; } = 180;

    public int JobRunsTtlDays { get; set; } = 365;

    /// <summary>ساعة الفحص الليلي بتوقيت القاهرة.</summary>
    public int NightlyVerifyHour { get; set; } = 2;

    /// <summary>مفتاح بصمة السلسلة (Base64، 32 بايت على الأقل)، خارج القاعدتين (D7). إلزامي في الإنتاج.</summary>
    public string? ChainKey { get; set; }

    /// <summary>مجلد ملف نقاط التثبيت؛ في الإنتاج مجلد مشترك على خادم آخر بصلاحية إلحاق فقط.</summary>
    public string? CheckpointDirectory { get; set; }

    /// <summary>بريد المراجع الرقابي لنقاط التثبيت.</summary>
    public List<string> CheckpointEmails { get; set; } = [];
}

/// <summary>اتصال التطبيق بقاعدة السجلات بحساب لا يملك إلا الإضافة والقراءة (SOD-12).</summary>
public sealed class MongoLogContext
{
    public MongoLogContext(IMongoClient client, IOptions<MongoLogOptions> options)
    {
        Database = client.GetDatabase(options.Value.Database);
    }

    public IMongoDatabase Database { get; }

    /// <summary>الأحداث المتسلسلة تكتب بإقرار الأغلبية مع التدوين، فلا يعد الحدث مسلمًا قبل حفظه فعلًا.</summary>
    public IMongoCollection<BsonDocument> Chained(string name) =>
        Database.GetCollection<BsonDocument>(name).WithWriteConcern(WriteConcern.WMajority.With(journal: true));

    public IMongoCollection<BsonDocument> Collection(string name) => Database.GetCollection<BsonDocument>(name);
}

/// <summary>إنشاء المجموعات والفهارس وفهارس TTL بحساب الإدارة. آمن للتكرار.</summary>
public static class MongoSetup
{
    public static async Task RunAsync(string adminConnectionString, MongoLogOptions o, ILogger log, CancellationToken ct)
    {
        var db = new MongoClient(adminConnectionString).GetDatabase(o.Database);
        var existing = (await (await db.ListCollectionNamesAsync(cancellationToken: ct)).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
        foreach (var name in LogCollections.Chained.Concat([LogCollections.JobRuns, LogCollections.HttpRequests, LogCollections.AppErrors]))
            if (!existing.Contains(name)) await db.CreateCollectionAsync(name, cancellationToken: ct);

        var keys = Builders<BsonDocument>.IndexKeys;
        foreach (var name in LogCollections.Chained)
        {
            var c = db.GetCollection<BsonDocument>(name);
            await c.Indexes.CreateManyAsync(
            [
                new CreateIndexModel<BsonDocument>(keys.Descending("ts"), new CreateIndexOptions { Name = "ts" }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("integrity.seq"), new CreateIndexOptions { Name = "chain_seq", Unique = true }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("actor.userId").Descending("ts"), new CreateIndexOptions { Name = "actor_ts" }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("target.type").Ascending("target.id").Descending("ts"), new CreateIndexOptions { Name = "target_ts" }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("target.ref").Descending("ts"), new CreateIndexOptions { Name = "target_ref_ts" }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("feature").Ascending("action").Descending("ts"), new CreateIndexOptions { Name = "feature_action_ts" }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("module").Descending("ts"), new CreateIndexOptions { Name = "module_ts" }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("context.correlationId"), new CreateIndexOptions { Name = "correlation" }),
                new CreateIndexModel<BsonDocument>(keys.Ascending("context.ip").Descending("ts"), new CreateIndexOptions { Name = "ip_ts" }),
            ], ct);
        }
        await Ttl(db, LogCollections.HttpRequests, o.HttpRequestsTtlDays, ct);
        await Ttl(db, LogCollections.AppErrors, o.AppErrorsTtlDays, ct);
        await Ttl(db, LogCollections.JobRuns, o.JobRunsTtlDays, ct);
        await db.GetCollection<BsonDocument>(LogCollections.HttpRequests).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(keys.Ascending("correlationId"), new CreateIndexOptions { Name = "correlation" }), cancellationToken: ct);
        log.LogInformation("MongoDB log collections and indexes are ready in {Database}.", o.Database);
    }

    private static async Task Ttl(IMongoDatabase db, string name, int days, CancellationToken ct)
    {
        var c = db.GetCollection<BsonDocument>(name);
        var spec = new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("ts"),
            new CreateIndexOptions { Name = "ttl_ts", ExpireAfter = TimeSpan.FromDays(days) });
        try
        {
            await c.Indexes.CreateOneAsync(spec, cancellationToken: ct);
        }
        catch (MongoCommandException e) when (e.Code is 85 or 86)
        {
            // تغيرت مدة الاحتفاظ: يعاد إنشاء الفهرس بالمدة الجديدة.
            await c.Indexes.DropOneAsync("ttl_ts", ct);
            await c.Indexes.CreateOneAsync(spec, cancellationToken: ct);
        }
    }
}

/// <summary>إشارة واحدة معلقة على الأكثر توقظ عامل الإرسال.</summary>
public sealed class OutboxSignal : IOutboxSignal, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(0, 1);

    public void Notify()
    {
        try
        {
            _semaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // إشارة معلقة بالفعل.
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _semaphore.WaitAsync(timeout, ct);

    public void Dispose() => _semaphore.Dispose();
}

/// <summary>كتابة صف الصندوق الصادر باتصال مستقل، خارج أي معاملة جارية.</summary>
public sealed class OutboxDirectWriter(NpgsqlDataSource dataSource) : IOutboxDirectWriter
{
    public async Task InsertAsync(OutboxEvent e, CancellationToken ct)
    {
        await using var cmd = dataSource.CreateCommand(
            "INSERT INTO logs.outbox (event_id, collection, payload, created_at, attempts) VALUES ($1, $2, $3, $4, 0)");
        cmd.Parameters.AddWithValue(e.EventId);
        cmd.Parameters.AddWithValue(e.Collection);
        cmd.Parameters.AddWithValue(e.Payload);
        cmd.Parameters.AddWithValue(e.CreatedAt);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

/// <summary>
/// السجلات التقنية (الطلبات والأخطاء وتشغيل المهام) تكتب مباشرة بلا سلسلة ولا صندوق صادر.
/// الطابور محدود؛ عند الامتلاء يسقط الأقدم، وفقد بعضها عند العطل مقبول.
/// </summary>
public sealed class TechLogWriter : Tier1.Application.Abstractions.ITechnicalLog
{
    public void Request(Tier1.Application.Abstractions.HttpRequestRecord r) => Write(LogCollections.HttpRequests, new BsonDocument
    {
        { "_id", Guid.CreateVersion7().ToString() },
        { "ts", DateTime.UtcNow },
        { "method", r.Method },
        { "path", r.Path },
        { "status", r.Status },
        { "durationMs", r.DurationMs },
        { "userId", (BsonValue?)r.UserId ?? BsonNull.Value },
        { "ip", (BsonValue?)r.Ip ?? BsonNull.Value },
        { "correlationId", r.CorrelationId },
    });

    public void Error(Tier1.Application.Abstractions.AppErrorRecord e) => Write(LogCollections.AppErrors, new BsonDocument
    {
        { "_id", Guid.CreateVersion7().ToString() },
        { "ts", DateTime.UtcNow },
        { "type", e.Type },
        { "message", e.Message },
        { "stack", e.Stack },
        { "path", e.Path },
        { "method", e.Method },
        { "userId", (BsonValue?)e.UserId ?? BsonNull.Value },
        { "correlationId", e.CorrelationId },
    });

    private readonly Channel<(string Collection, BsonDocument Doc)> _channel =
        Channel.CreateBounded<(string, BsonDocument)>(new BoundedChannelOptions(10_000) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });

    public void Write(string collection, BsonDocument doc) => _channel.Writer.TryWrite((collection, doc));

    internal ChannelReader<(string Collection, BsonDocument Doc)> Reader => _channel.Reader;
}

public sealed class TechLogPump(TechLogWriter writer, MongoLogContext mongo, ILogger<TechLogPump> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var batch = new List<(string Collection, BsonDocument Doc)>(500);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await writer.Reader.WaitToReadAsync(stoppingToken)) return;
                while (batch.Count < 500 && writer.Reader.TryRead(out var item)) batch.Add(item);
                foreach (var group in batch.GroupBy(b => b.Collection))
                    await mongo.Collection(group.Key).InsertManyAsync(group.Select(g => g.Doc), new InsertManyOptions { IsOrdered = false }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Dropping {Count} technical log entries; MongoDB is unavailable.", batch.Count);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            finally
            {
                batch.Clear();
            }
        }
    }
}
