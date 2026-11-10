using System.Globalization;
using System.Text;
using System.Text.Json;
using Tier1.Application.Auditing;
using Tier1.Domain.Logging;
using Tier1.Infrastructure.Email;
using Tier1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Tier1.Infrastructure.Logging;

/// <summary>المفتاح المحمّل من الإعدادات مرة واحدة؛ null في بيئة لم يضبط فيها (التطوير القديم والاختبارات).</summary>
public sealed class ChainKeyHolder(IOptions<MongoLogOptions> options)
{
    private readonly Lazy<ChainKey?> _key = new(() => ChainKey.FromConfig(options.Value.ChainKey));

    public ChainKey? Key => _key.Value;
}

public sealed record CheckpointLine(string Chain, long Seq, string Hash, DateTimeOffset At, string KeyId, string Signature);

/// <summary>
/// نشر نقاط التثبيت خارج المنظومة (D7): سطر JSON لكل سلسلة في ملف إلحاق على خادم آخر،
/// ورسالة يومية لبريد المراجع الرقابي. ينشر فقط بعد فحص سليم، فلا يُعتمد ما عُبث به.
/// </summary>
public sealed class CheckpointPublisher(
    Tier1DbContext db,
    MongoLogContext mongo,
    ChainKeyHolder keys,
    IEmailSender email,
    IAuditLog audit,
    IOptions<MongoLogOptions> options,
    ILogger<CheckpointPublisher> log)
{
    public const string FileName = "checkpoints.jsonl";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CheckpointLine>> PublishAsync(CancellationToken ct)
    {
        var key = keys.Key;
        if (key is null) return [];
        var o = options.Value;
        // بالثانية الكاملة، فيطابق التوقيع بعد المرور بالملف والقاعدة (دقتها جزء من مليون).
        var now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var lines = new List<CheckpointLine>();
        foreach (var chain in LogCollections.Chained)
        {
            var last = await mongo.Collection(chain).Find(Builders<BsonDocument>.Filter.Exists("integrity.seq"))
                .Sort(Builders<BsonDocument>.Sort.Descending("integrity.seq")).Limit(1).FirstOrDefaultAsync(ct);
            if (last is null) continue;
            var seq = last["integrity"]["seq"].ToInt64();
            var hash = last["integrity"]["hash"].AsString;
            if (await db.ChainCheckpoints.AnyAsync(c => c.Chain == chain && c.Seq == seq, ct)) continue;
            lines.Add(new CheckpointLine(chain, seq, hash, now, key.Id, ChainHash.SignCheckpoint(key, chain, seq, hash, now)));
        }
        if (lines.Count == 0) return [];

        var targets = new List<string>();
        if (!string.IsNullOrWhiteSpace(o.CheckpointDirectory))
        {
            Directory.CreateDirectory(o.CheckpointDirectory);
            // إلحاق فقط: الملف يفتح للإضافة، وعلى الخادم الآخر تمنح صلاحية الإلحاق دون التعديل.
            await using var stream = new FileStream(Path.Combine(o.CheckpointDirectory, FileName), FileMode.Append, FileAccess.Write, FileShare.Read);
            await using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            foreach (var line in lines) await writer.WriteLineAsync(JsonSerializer.Serialize(line, Json).AsMemory(), ct);
            targets.Add("file");
        }
        if (o.CheckpointEmails.Count > 0)
        {
            try
            {
                var body = new StringBuilder();
                body.AppendLine("نقاط تثبيت سجلات منظومة Tier 1 بجامعة العاصمة. احتفظ بهذه الرسالة؛ أي اختلاف لاحق عنها يعني تلاعبًا بالسجلات.");
                body.AppendLine();
                foreach (var l in lines) body.AppendLine(JsonSerializer.Serialize(l, Json));
                await email.SendAsync(o.CheckpointEmails, $"LC log checkpoints {now.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} UTC", body.ToString(), ct);
                targets.Add("email");
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or MailKit.Net.Smtp.SmtpCommandException or MailKit.ServiceNotConnectedException or System.Net.Sockets.SocketException)
            {
                log.LogWarning(e, "Could not email the log checkpoints.");
                await audit.RecordNowAsync(new AuditEntry(LogCollections.Integration, "log", "log.audit", "log.checkpoint_email", "فشل إرسال نقاط التثبيت بالبريد", "Failed to email log checkpoints") { Outcome = "failure" }, ct);
            }
        }
        var to = string.Join(',', targets);
        foreach (var l in lines)
            db.ChainCheckpoints.Add(new ChainCheckpoint { Chain = l.Chain, Seq = l.Seq, Hash = l.Hash, At = l.At, Signature = l.Signature, PublishedTo = to });
        audit.Record(new AuditEntry(LogCollections.Integration, "log", "log.audit", "log.checkpoint_publish", "نشر نقاط تثبيت السجلات", "Published log checkpoints")
        {
            Outcome = targets.Count > 0 ? "success" : "failure",
            Details = new { targets, checkpoints = lines.Select(l => new { l.Chain, l.Seq, l.Hash }) },
        });
        await db.SaveChangesAsync(ct);
        return lines;
    }

    /// <summary>النقاط المنشورة: من الملف الخارجي إن ضبط (المصدر الموثوق)، وإلا من نسخة القاعدة.</summary>
    public async Task<IReadOnlyList<CheckpointLine>> PublishedAsync(CancellationToken ct)
    {
        var dir = options.Value.CheckpointDirectory;
        if (!string.IsNullOrWhiteSpace(dir))
        {
            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return [];
            var result = new List<CheckpointLine>();
            foreach (var line in await File.ReadAllLinesAsync(path, ct))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    if (JsonSerializer.Deserialize<CheckpointLine>(line, Json) is { } cp) result.Add(cp);
                }
                catch (JsonException)
                {
                    result.Add(new CheckpointLine("?", -1, "", default, "", "unreadable"));
                }
            }
            return result;
        }
        return await db.ChainCheckpoints.AsNoTracking()
            .Select(c => new CheckpointLine(c.Chain, c.Seq, c.Hash, c.At, keys.Key == null ? "" : keys.Key.Id, c.Signature)).ToListAsync(ct);
    }
}
