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
using Npgsql;

namespace Tier1.Infrastructure.Logging;

/// <summary>
/// عامل إرسال الصندوق الصادر إلى MongoDB على مرحلتين:
/// <list type="number">
/// <item>الحجز: يأخذ الأحداث غير المحجوزة بترتيبها، ويعطي كلًا منها رقمه وبصمته في سلسلته، ويحدّث حالة السلسلة
/// ومرساة اليوم، ويحفظ ذلك كله في PostgreSQL في معاملة واحدة.</item>
/// <item>الإرسال: يرسل الأحداث المحجوزة غير المرسلة بالقيم المحفوظة نفسها، ثم يعلّمها مرسلة.</item>
/// </list>
/// العطل بين المرحلتين أو بعد الإرسال يعيد الوثيقة نفسها حرفيًا، فيتجاهلها قيد التفرد على _id: لا فقد ولا تكرار
/// ولا كسر للسلسلة. عامل واحد يعمل في كل لحظة عبر قفل استشاري في PostgreSQL، فيبقى ترتيب السلسلة ثابتًا.
/// </summary>
public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes,
    NpgsqlDataSource dataSource,
    MongoLogContext mongo,
    ChainKeyHolder keys,
    OutboxSignal signal,
    IOptions<MongoLogOptions> options,
    ILogger<OutboxDispatcher> log) : BackgroundService
{
    /// <summary>مفتاح القفل الاستشاري لقائد الإرسال.</summary>
    private const long LeaderLockKey = 0x4C43_4C4F_4753; // "LCLOGS"

    /// <summary>قفل معاملة الحجز؛ مختلف عن قفل القائد الذي يحمله اتصال آخر للنسخة نفسها.</summary>
    private const long ReserveLockKey = 0x4C43_5253_5256; // "LCRSRV"

    private NpgsqlConnection? _leader;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var failures = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await EnsureLeaderAsync(stoppingToken))
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                    continue;
                }
                var reserved = await ReserveAsync(stoppingToken);
                var sent = await SendAsync(stoppingToken);
                failures = 0;
                if (reserved == 0 && sent == 0) await signal.WaitAsync(TimeSpan.FromSeconds(2), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            // أي خطأ غير متوقع يسجل ويعاد بعد مهلة؛ لا يوقف الخدمة ولا التطبيق.
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                failures++;
                var delay = TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, Math.Min(failures, 5))));
                log.LogWarning(e, "Outbox dispatch failed (attempt {Attempt}); retrying in {Delay}.", failures, delay);
                await RecordFailureAsync(e, stoppingToken);
                await ReleaseLeaderIfBrokenAsync();
                await Task.Delay(delay, stoppingToken);
            }
        }
        await ReleaseLeaderIfBrokenAsync(force: true);
    }

    /// <summary>المرحلة الأولى: حجز مواضع السلسلة وحفظها في PostgreSQL.</summary>
    internal async Task<int> ReserveAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // الحجز مسلسل بقفل المعاملة حتى لو ظنت نسختان أنهما القائد (انقطاع اتصال القائد مثلًا)،
        // ورقم السلسلة رمز تزامن فلا يعطى الرقم نفسه مرتين.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({ReserveLockKey})", ct);
        var rows = await db.Outbox.Where(o => o.SentAt == null && o.ChainSeq == null).OrderBy(o => o.Id).Take(options.Value.DispatchBatch).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        var chains = rows.Select(r => r.Collection).Distinct().ToList();
        var states = await db.ChainStates.Where(s => chains.Contains(s.Chain)).ToDictionaryAsync(s => s.Chain, ct);
        var anchors = new Dictionary<(string, DateOnly), ChainAnchor>();
        var now = DateTimeOffset.UtcNow;
        foreach (var row in rows)
        {
            if (!states.TryGetValue(row.Collection, out var state))
            {
                state = new ChainState { Chain = row.Collection, Seq = 0, Hash = ChainHash.Genesis };
                db.ChainStates.Add(state);
                states[row.Collection] = state;
            }
            var doc = ChainHash.FromPayload(row.Payload);
            var seq = state.Seq + 1;
            var key = keys.Key;
            var hash = ChainHash.Compute(state.Hash, seq, doc, key);
            row.ChainSeq = seq;
            row.PrevHash = state.Hash;
            row.Hash = hash;
            row.HashAlg = key?.Alg;
            if (key is not null) state.HmacFromSeq ??= seq;
            state.Seq = seq;
            state.Hash = hash;

            // يوم المرساة لا يرجع إلى الخلف، فتبقى أرقام كل يوم متصلة.
            var eventDay = CairoTime.DateOf(new DateTimeOffset(doc["ts"].ToUniversalTime(), TimeSpan.Zero));
            var day = state.LastDay is { } last && last > eventDay ? last : eventDay;
            state.LastDay = day;
            if (!anchors.TryGetValue((row.Collection, day), out var anchor))
            {
                anchor = await db.ChainAnchors.FirstOrDefaultAsync(a => a.Chain == row.Collection && a.Day == day, ct);
                if (anchor is null)
                {
                    anchor = new ChainAnchor { Chain = row.Collection, Day = day, FirstSeq = seq };
                    db.ChainAnchors.Add(anchor);
                }
                anchors[(row.Collection, day)] = anchor;
            }
            anchor.LastSeq = seq;
            anchor.LastHash = hash;
            anchor.EventCount++;
            anchor.UpdatedAt = now;
            // يوم أضيف إليه حدث بعد فحصه يعاد فحصه.
            anchor.Intact = null;
            anchor.VerifiedAt = null;
            anchor.BrokenAtSeq = null;
            anchor.VerifyNote = null;
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return rows.Count;
    }

    /// <summary>المرحلة الثانية: إرسال المحجوز بالقيم المحفوظة، ثم تعليمه مرسلًا.</summary>
    internal async Task<int> SendAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Tier1DbContext>();
        var rows = await db.Outbox.Where(o => o.SentAt == null && o.ChainSeq != null).OrderBy(o => o.Id).Take(options.Value.DispatchBatch).ToListAsync(ct);
        if (rows.Count == 0) return 0;
        foreach (var group in rows.GroupBy(r => r.Collection))
        {
            var docs = group.OrderBy(r => r.ChainSeq).Select(r => ChainHash.WithIntegrity(ChainHash.FromPayload(r.Payload), r.Collection, r.ChainSeq!.Value, r.PrevHash!, r.Hash!, r.HashAlg)).ToList();
            try
            {
                await mongo.Chained(group.Key).InsertManyAsync(docs, new InsertManyOptions { IsOrdered = false }, ct);
            }
            catch (MongoBulkWriteException<BsonDocument> e) when (e.WriteErrors.All(w => w.Category == ServerErrorCategory.DuplicateKey) && e.WriteConcernError is null)
            {
                // تكرار مقبول فقط إن كانت الوثيقة المحفوظة هي نفسها (وصلت في محاولة سابقة قبل تعليمها مرسلة).
                // تصادم رقم السلسلة مع حدث آخر خطأ جسيم: لا يعلم مرسلًا ويبقى في الصندوق ظاهرًا للمراقبة.
                var duplicates = e.WriteErrors.Select(w => docs[w.Index]).ToList();
                var ids = duplicates.Select(d => d["_id"]).ToList();
                var stored = await mongo.Collection(group.Key).Find(Builders<BsonDocument>.Filter.In("_id", ids)).ToListAsync(ct);
                var storedHash = stored.ToDictionary(d => d["_id"].ToString()!, d => d.GetValue(ChainHash.IntegrityField, new BsonDocument()).AsBsonDocument.GetValue("hash", "").ToString());
                var collisions = duplicates.Where(d => storedHash.GetValueOrDefault(d["_id"].ToString()!) != d[ChainHash.IntegrityField]["hash"].AsString).ToList();
                if (collisions.Count > 0)
                {
                    log.LogError("Log chain collision in {Collection}: {Count} event(s) clash with stored events (first seq {Seq}). They stay unsent.",
                        group.Key, collisions.Count, collisions[0][ChainHash.IntegrityField]["seq"]);
                    throw new InvalidOperationException($"Chain collision in {group.Key}.");
                }
                log.LogInformation("{Count} log events were already in MongoDB ({Collection}).", e.WriteErrors.Count, group.Key);
            }
        }
        var now = DateTimeOffset.UtcNow;
        foreach (var r in rows)
        {
            r.SentAt = now;
            r.LastError = null;
        }
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }

    private async Task<bool> EnsureLeaderAsync(CancellationToken ct)
    {
        if (_leader is { State: System.Data.ConnectionState.Open })
        {
            // الاتصال قد يبدو مفتوحًا بعد أن أسقطه الخادم (وسقط القفل معه)؛ يفحص بطلب فعلي.
            try
            {
                await using var probe = new NpgsqlCommand("SELECT 1", _leader);
                await probe.ExecuteScalarAsync(ct);
                return true;
            }
            catch (NpgsqlException)
            {
                log.LogWarning("The log dispatcher lost its leader connection; acquiring again.");
            }
        }
        await ReleaseLeaderIfBrokenAsync(force: true);
        var conn = await dataSource.OpenConnectionAsync(ct);
        await using var cmd = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", conn);
        cmd.Parameters.AddWithValue(LeaderLockKey);
        if (await cmd.ExecuteScalarAsync(ct) is true)
        {
            _leader = conn;
            log.LogInformation("This instance is now the log dispatcher.");
            return true;
        }
        await conn.DisposeAsync();
        return false;
    }

    private async Task ReleaseLeaderIfBrokenAsync(bool force = false)
    {
        if (_leader is null) return;
        if (!force && _leader.State == System.Data.ConnectionState.Open) return;
        await _leader.DisposeAsync();
        _leader = null;
    }

    private async Task RecordFailureAsync(Exception e, CancellationToken ct)
    {
        try
        {
            await using var cmd = dataSource.CreateCommand(
                "UPDATE logs.outbox SET attempts = attempts + 1, last_error = $1 WHERE id IN (SELECT id FROM logs.outbox WHERE sent_at IS NULL ORDER BY id LIMIT $2)");
            cmd.Parameters.AddWithValue(e.GetType().Name + ": " + (e.Message.Length > 900 ? e.Message[..900] : e.Message));
            cmd.Parameters.AddWithValue(options.Value.DispatchBatch);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (NpgsqlException)
        {
            // القاعدة نفسها غير متاحة؛ تعاد المحاولة في الدورة التالية.
        }
    }

    public override void Dispose()
    {
        _leader?.Dispose();
        base.Dispose();
    }
}
