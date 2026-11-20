using System.Diagnostics;
using Tier1.Application.Logs;
using Tier1.Domain.Common;
using Tier1.Domain.Logging;
using Tier1.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Tier1.Infrastructure.Logging;

/// <summary>
/// فحص سلامة السلاسل يومًا يومًا: كل حدث يعاد حساب بصمته (بالمفتاح منذ تفعيله) من محتواه وبصمة سابقه، والأرقام متصلة بلا فجوة،
/// وآخر بصمة في اليوم تطابق مرساته المحفوظة في PostgreSQL. ثم تقارن السلسلة بنقاط التثبيت المنشورة خارج المنظومة،
/// فيكتشف حتى من عدل القاعدتين معًا. إن سلم كل شيء تنشر نقطة تثبيت جديدة.
/// </summary>
public sealed class ChainVerifier(Tier1DbContext db, MongoLogContext mongo, TechLogWriter tech, ChainKeyHolder keys, CheckpointPublisher checkpoints) : IChainVerifier
{
    public async Task<IReadOnlyList<ChainDayDto>> VerifyAsync(DateOnly from, DateOnly to, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        var states = await db.ChainStates.AsNoTracking().ToDictionaryAsync(s => s.Chain, ct);
        var anchors = await db.ChainAnchors.AsNoTracking().Where(a => a.Day >= from && a.Day <= to).OrderBy(a => a.Chain).ThenBy(a => a.Day).ToListAsync(ct);
        // أقل رقم محجوز لم يرسل بعد في كل سلسلة: الأيام التي تحويه تنتظر الإرسال، فلا تعد مكسورة أثناء تعطل MongoDB.
        var unsent = await db.Outbox.AsNoTracking().Where(o => o.SentAt == null && o.ChainSeq != null)
            .GroupBy(o => o.Collection).Select(g => new { Chain = g.Key, Min = g.Min(o => o.ChainSeq) }).ToDictionaryAsync(x => x.Chain, x => x.Min, ct);
        foreach (var anchor in anchors)
        {
            var now = DateTimeOffset.UtcNow;
            (bool? intact, long? brokenAt, string? note) = unsent.TryGetValue(anchor.Chain, out var min) && min <= anchor.LastSeq
                ? (null, null, "بانتظار الإرسال إلى MongoDB")
                : await VerifyDayAsync(anchor, states.GetValueOrDefault(anchor.Chain)?.HmacFromSeq, ct);
            // النتيجة تحفظ فقط إن لم يضف العامل أحداثًا لليوم أثناء الفحص؛ وإلا يبقى اليوم غير مفحوص.
            var updated = await db.ChainAnchors
                .Where(a => a.Chain == anchor.Chain && a.Day == anchor.Day && a.LastSeq == anchor.LastSeq)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Intact, intact).SetProperty(a => a.BrokenAtSeq, brokenAt)
                    .SetProperty(a => a.VerifyNote, note).SetProperty(a => a.VerifiedAt, intact is null ? null : now), ct);
            anchor.Intact = updated == 1 ? intact : null;
            anchor.BrokenAtSeq = updated == 1 ? brokenAt : null;
            anchor.VerifyNote = updated == 1 ? note : "أضيفت أحداث أثناء الفحص";
            anchor.VerifiedAt = updated == 1 && intact is not null ? now : null;
        }

        // نقاط التثبيت المنشورة: أي اختلاف يكسر يوم الحدث المعني ولو كان خارج الفترة المطلوبة.
        var checkpointProblems = 0;
        foreach (var cp in await checkpoints.PublishedAsync(ct))
        {
            var problem = await CheckCheckpointAsync(cp, ct);
            if (problem is null) continue;
            checkpointProblems++;
            await db.ChainAnchors.Where(a => a.Chain == cp.Chain && a.FirstSeq <= cp.Seq && a.LastSeq >= cp.Seq)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Intact, false).SetProperty(a => a.BrokenAtSeq, cp.Seq)
                    .SetProperty(a => a.VerifyNote, problem).SetProperty(a => a.VerifiedAt, DateTimeOffset.UtcNow), ct);
            foreach (var a in anchors.Where(a => a.Chain == cp.Chain && a.FirstSeq <= cp.Seq && a.LastSeq >= cp.Seq))
            {
                a.Intact = false;
                a.BrokenAtSeq = cp.Seq;
                a.VerifyNote = problem;
            }
        }

        // ينشر ما سلم فقط؛ نشر حالة عُبث بها يجعلها مرجعًا.
        var published = anchors.Any(a => a.Intact == false) || checkpointProblems > 0 ? [] : await checkpoints.PublishAsync(ct);
        tech.Write(LogCollections.JobRuns, new BsonDocument
        {
            { "_id", Guid.CreateVersion7().ToString() },
            { "ts", DateTime.UtcNow },
            { "job", "logs.verify_chains" },
            { "outcome", anchors.Any(a => a.Intact == false) ? "failure" : "success" },
            { "durationMs", watch.ElapsedMilliseconds },
            { "summary", $"{anchors.Count} days, {anchors.Count(a => a.Intact == false)} broken, {checkpointProblems} checkpoint problems, {published.Count} checkpoints published" },
            { "from", from.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) },
            { "to", to.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) },
        });
        return anchors.Select(LogsService.ToDto).ToList();
    }

    private async Task<string?> CheckCheckpointAsync(CheckpointLine cp, CancellationToken ct)
    {
        if (cp.Signature == "unreadable") return "سطر غير مقروء في ملف نقاط التثبيت";
        var key = keys.Key;
        if (key is null) return null;
        if (cp.KeyId != key.Id) return "نقطة تثبيت بمفتاح غير معروف";
        if (!Application.Security.Tokens.FixedEquals(ChainHash.SignCheckpoint(key, cp.Chain, cp.Seq, cp.Hash, cp.At), cp.Signature))
            return "توقيع نقطة التثبيت غير صحيح";
        var doc = await mongo.Collection(cp.Chain).Find(Builders<BsonDocument>.Filter.Eq("integrity.seq", cp.Seq)).FirstOrDefaultAsync(ct);
        if (doc is null) return $"حدث نقطة التثبيت مفقود: الرقم {cp.Seq}";
        return doc["integrity"]["hash"].AsString == cp.Hash ? null : "السلسلة تخالف نقطة التثبيت المنشورة خارج المنظومة";
    }

    private async Task<(bool? Intact, long? BrokenAt, string? Note)> VerifyDayAsync(ChainAnchor anchor, long? hmacFromSeq, CancellationToken ct)
    {
        var c = mongo.Collection(anchor.Chain);
        var b = Builders<BsonDocument>.Filter;
        var expectedPrev = ChainHash.Genesis;
        if (anchor.FirstSeq > 1)
        {
            var prev = await c.Find(b.Eq("integrity.seq", anchor.FirstSeq - 1)).FirstOrDefaultAsync(ct);
            // سابق أول حدث في اليوم يخص يومًا آخر؛ إن حذف يكتشفه فحص ذلك اليوم، وهنا يكفي ما يقوله الحدث الأول.
            expectedPrev = prev?["integrity"]["hash"].AsString ?? "";
        }
        var docs = await c.Find(b.Gte("integrity.seq", anchor.FirstSeq) & b.Lte("integrity.seq", anchor.LastSeq))
            .Sort(Builders<BsonDocument>.Sort.Ascending("integrity.seq")).ToListAsync(ct);
        var expectedSeq = anchor.FirstSeq;
        string? last = null;
        foreach (var doc in docs)
        {
            if (!doc.TryGetValue(ChainHash.IntegrityField, out var integrityValue) || !integrityValue.IsBsonDocument)
                return (false, expectedSeq, "حدث بلا بيانات سلامة");
            var integrity = integrityValue.AsBsonDocument;
            var seq = integrity["seq"].ToInt64();
            if (seq != expectedSeq) return (false, expectedSeq, $"حدث مفقود: الرقم {expectedSeq}");
            var storedPrev = integrity["prevHash"].AsString;
            if (expectedPrev.Length > 0 && storedPrev != expectedPrev) return (false, seq, "البصمة السابقة لا تطابق");
            ChainKey? key = null;
            var alg = integrity.GetValue("alg", BsonNull.Value);
            if (alg.IsBsonNull)
            {
                // بعد تفعيل المفتاح لا يقبل حدث بلا مفتاح: هكذا يعاد بناء السلسلة لو قُبل.
                if (hmacFromSeq is { } since && seq >= since) return (false, seq, "حدث بلا بصمة مفتاح بعد تفعيله");
            }
            else
            {
                key = keys.Key;
                if (key is null || alg.AsString != key.Alg) return (false, seq, "حدث ببصمة مفتاح غير معروف");
            }
            var recomputed = ChainHash.Compute(storedPrev, seq, doc, key);
            if (recomputed != integrity["hash"].AsString) return (false, seq, "محتوى الحدث تغير بعد حفظه");
            expectedPrev = recomputed;
            last = recomputed;
            expectedSeq++;
        }
        if (expectedSeq <= anchor.LastSeq) return (false, expectedSeq, $"حدث مفقود: الرقم {expectedSeq}");
        if (last != anchor.LastHash) return (false, anchor.LastSeq, "آخر بصمة لا تطابق المرساة");
        if (docs.Count != anchor.EventCount) return (false, anchor.LastSeq, "عدد الأحداث لا يطابق المرساة");
        return (true, null, null);
    }
}

/// <summary>فحص ليلي لأيام الأسبوع الأخير في الساعة المحددة بتوقيت القاهرة.</summary>
public sealed class NightlyChainVerification(IServiceScopeFactory scopes, IOptions<MongoLogOptions> options, Persistence.JobLock jobs, ILogger<NightlyChainVerification> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var today = CairoTime.DateOf(now);
            var next = CairoTime.StartOfDay(today).AddHours(options.Value.NightlyVerifyHour);
            if (next <= now) next = CairoTime.StartOfDay(today.AddDays(1)).AddHours(options.Value.NightlyVerifyHour);
            await Task.Delay(next - now, stoppingToken);
            try
            {
                await jobs.RunExclusiveAsync(Persistence.JobLock.NightlyChain, async ct =>
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var verifier = scope.ServiceProvider.GetRequiredService<IChainVerifier>();
                    var day = CairoTime.DateOf(DateTimeOffset.UtcNow);
                    var result = await verifier.VerifyAsync(day.AddDays(-7), day, ct);
                    var broken = result.Where(r => r.Intact == false).ToList();
                    if (broken.Count > 0)
                        log.LogError("Log integrity check found {Count} broken day(s): {Days}", broken.Count, string.Join(", ", broken.Select(b => $"{b.Chain}/{b.Day}@{b.BrokenAtSeq}")));
                }, stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogWarning(e, "Nightly log integrity check could not run.");
            }
        }
    }
}
