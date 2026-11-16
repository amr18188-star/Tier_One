using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Tier1.Infrastructure.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tier1.Tests;

public class ChainHashTests
{
    private const string Payload = """
        {"_id":"0192f5c4-7e1a-7b3c-9a51-3f0c2d8e4b10","ts":"2026-10-27T08:15:42.120Z","module":"adm","feature":"adm.roles","action":"iam.change_approve",
         "messageAr":"اعتماد تعديل صلاحيات دور","actor":{"userId":"u1","roles":["SA"]},"changes":[{"field":"req.files","old":"VPEA:U","new":"VPEAD:U"}],
         "details":{"version":3,"ratio":0.15,"ok":true,"note":null}}
        """;

    [Fact]
    public void Hash_survives_the_round_trip_through_bson()
    {
        var doc = ChainHash.FromPayload(Payload);
        var hash = ChainHash.Compute(ChainHash.Genesis, 1, doc);
        // ما يحفظ في MongoDB ويقرأ منه عند الفحص.
        var stored = BsonSerializer.Deserialize<BsonDocument>(ChainHash.WithIntegrity(doc, "security_events", 1, ChainHash.Genesis, hash).ToBson());
        Assert.Equal(hash, ChainHash.Compute(ChainHash.Genesis, 1, stored));
        Assert.Equal(hash, stored["integrity"]["hash"].AsString);
    }

    [Fact]
    public void Ts_is_a_bson_date_for_indexes_and_ttl() =>
        Assert.Equal(BsonType.DateTime, ChainHash.FromPayload(Payload)["ts"].BsonType);

    [Fact]
    public void Key_order_does_not_change_the_hash()
    {
        var a = BsonDocument.Parse("""{"b":1,"a":{"y":2,"x":"س"}}""");
        var b = BsonDocument.Parse("""{"a":{"x":"س","y":2},"b":1}""");
        Assert.Equal(ChainHash.Compute(ChainHash.Genesis, 5, a), ChainHash.Compute(ChainHash.Genesis, 5, b));
    }

    [Fact]
    public void Any_change_to_content_sequence_or_previous_hash_changes_the_hash()
    {
        var doc = ChainHash.FromPayload(Payload);
        var hash = ChainHash.Compute(ChainHash.Genesis, 7, doc);
        var tampered = doc.DeepClone().AsBsonDocument;
        tampered["messageAr"] = "تعديل خبيث";
        Assert.NotEqual(hash, ChainHash.Compute(ChainHash.Genesis, 7, tampered));
        Assert.NotEqual(hash, ChainHash.Compute(ChainHash.Genesis, 8, doc));
        Assert.NotEqual(hash, ChainHash.Compute(new string('1', 64), 7, doc));
    }

    [Fact]
    public void Keyed_hash_cannot_be_rebuilt_without_the_key()
    {
        var doc = ChainHash.FromPayload(Payload);
        var key = ChainKey.FromBytes(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var other = ChainKey.FromBytes(Enumerable.Range(2, 32).Select(i => (byte)i).ToArray());
        var keyed = ChainHash.Compute(ChainHash.Genesis, 1, doc, key);
        Assert.NotEqual(ChainHash.Compute(ChainHash.Genesis, 1, doc), keyed);
        Assert.NotEqual(ChainHash.Compute(ChainHash.Genesis, 1, doc, other), keyed);
        Assert.Equal(keyed, ChainHash.Compute(ChainHash.Genesis, 1, doc, key));
        Assert.StartsWith("hmac-sha256:", key.Alg, StringComparison.Ordinal);
        Assert.NotEqual(key.Id, other.Id);
    }

    [Fact]
    public void Checkpoint_signature_covers_every_field()
    {
        var key = ChainKey.FromBytes(new byte[32]);
        var at = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);
        var sig = ChainHash.SignCheckpoint(key, "security_events", 10, "abc", at);
        Assert.NotEqual(sig, ChainHash.SignCheckpoint(key, "security_events", 11, "abc", at));
        Assert.NotEqual(sig, ChainHash.SignCheckpoint(key, "security_events", 10, "abd", at));
        Assert.NotEqual(sig, ChainHash.SignCheckpoint(key, "audit_events", 10, "abc", at));
        Assert.NotEqual(sig, ChainHash.SignCheckpoint(key, "security_events", 10, "abc", at.AddSeconds(1)));
    }

    [Fact]
    public void Short_chain_key_is_rejected() =>
        Assert.Throws<InvalidOperationException>(() => ChainKey.FromConfig(Convert.ToBase64String(new byte[16])));

    [Fact]
    public void Integrity_field_is_outside_the_hash()
    {
        var doc = ChainHash.FromPayload(Payload);
        var withIntegrity = ChainHash.WithIntegrity(doc, "audit_events", 3, ChainHash.Genesis, "x");
        Assert.Equal(ChainHash.Canonical(doc), ChainHash.Canonical(withIntegrity));
    }

    // ───────── قيم معروفة مسبقًا (known-answer) ─────────
    // القيم أدناه حُسبت خارج الكود (Python: hashlib وhmac) من الصيغة الموثقة في ChainHash:
    // hash = H(prevHash \n seq \n canonicalJson). أي تغيير في الصيغة القانونية يكسر كل السلاسل المخزنة،
    // فيجب أن يفشل هذا الاختبار قبل أن يصل التغيير إلى الإنتاج.

    private const string GoldenPayload = """{"b":1,"a":{"y":2,"x":"s"},"c":[true,null,"t"],"d":0.15,"ts":"2026-10-27T08:15:42.120Z"}""";
    private const string GoldenCanonical = """{"a":{"x":"s","y":2},"b":1,"c":[true,null,"t"],"d":0.15,"ts":"$date:2026-10-27T08:15:42.120Z"}""";
    private const string GoldenUnkeyedSeq1 = "8e0ad854f9ce2fae3bf77d8b85ec483eedfcb8f550163e62a4fe5b999250ff51";
    private const string GoldenKeyedSeq2 = "101d152429783a01881b4d90ef87bfd937042610404f10b6d543eebb9e0790f5";
    private const string GoldenKeyId = "ae216c2e";
    private const string GoldenCheckpointSignature = "5c75bed868e56dcad303bb48aaabad8e0b4be6272b623746df0296af5dd5a725";

    private static ChainKey SequentialKey() => ChainKey.FromBytes(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    [Fact]
    public void Known_answer_vectors_pin_the_canonical_format()
    {
        var doc = ChainHash.FromPayload(GoldenPayload);
        Assert.Equal(GoldenCanonical, ChainHash.Canonical(doc));

        var unkeyed = ChainHash.Compute(ChainHash.Genesis, 1, doc);
        Assert.Equal(GoldenUnkeyedSeq1, unkeyed);

        var key = SequentialKey();
        Assert.Equal(GoldenKeyId, key.Id);
        Assert.Equal("hmac-sha256:" + GoldenKeyId, key.Alg);
        Assert.Equal(GoldenKeyedSeq2, ChainHash.Compute(unkeyed, 2, doc, key));

        var at = new DateTimeOffset(2027, 1, 15, 8, 0, 0, TimeSpan.Zero);
        Assert.Equal(GoldenCheckpointSignature, ChainHash.SignCheckpoint(key, "security_events", 10, "abc", at));
    }

    // ───────── الصيغة كما هي موثقة، محسوبة في الاختبار نفسه ─────────

    private static string ExpectedHash(string prevHash, long seq, string canonical, byte[]? secret)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{prevHash}\n{seq}\n{canonical}"));
        var hash = secret is null ? SHA256.HashData(bytes) : HMACSHA256.HashData(secret, bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(2L)]
    [InlineData(12_345_678_901L)]
    [InlineData(long.MaxValue)]
    public void Hash_equals_the_documented_formula_recomputed_independently(long seq)
    {
        var doc = ChainHash.FromPayload(Payload); // يحوي نصًا عربيًا وتاريخًا وأرقامًا وnull
        var prev = new string('a', 64);
        var secret = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
        var canonical = ChainHash.Canonical(doc);

        Assert.Equal(ExpectedHash(prev, seq, canonical, null), ChainHash.Compute(prev, seq, doc));
        Assert.Equal(ExpectedHash(prev, seq, canonical, secret), ChainHash.Compute(prev, seq, doc, ChainKey.FromBytes(secret)));
    }

    // ───────── ترتيب المصفوفات ─────────

    [Fact]
    public void Array_order_changes_the_hash_but_object_key_order_does_not()
    {
        var original = BsonDocument.Parse("""{"roles":["SA","VPEA"],"changes":[{"field":"a","old":"1","new":"2"},{"field":"b","old":"3","new":"4"}]}""");
        var arrayReordered = BsonDocument.Parse("""{"roles":["VPEA","SA"],"changes":[{"field":"a","old":"1","new":"2"},{"field":"b","old":"3","new":"4"}]}""");
        var elementsReordered = BsonDocument.Parse("""{"roles":["SA","VPEA"],"changes":[{"field":"b","old":"3","new":"4"},{"field":"a","old":"1","new":"2"}]}""");
        var keysReordered = BsonDocument.Parse("""{"changes":[{"new":"2","old":"1","field":"a"},{"new":"4","old":"3","field":"b"}],"roles":["SA","VPEA"]}""");

        var hash = ChainHash.Compute(ChainHash.Genesis, 1, original);
        Assert.NotEqual(hash, ChainHash.Compute(ChainHash.Genesis, 1, arrayReordered));
        Assert.NotEqual(hash, ChainHash.Compute(ChainHash.Genesis, 1, elementsReordered));
        Assert.Equal(hash, ChainHash.Compute(ChainHash.Genesis, 1, keysReordered));
    }

    // ───────── كل أنواع BSON تنجو من الحفظ والقراءة ─────────

    [Fact]
    public void Every_supported_bson_type_keeps_its_hash_through_bson_bytes()
    {
        var doc = new BsonDocument
        {
            { "_id", "0192f5c4-7e1a-7b3c-9a51-3f0c2d8e4b10" },
            { "i32", 7 },
            { "i64", 9_007_199_254_740_993L }, // أكبر من 2^53: لا تتحمل Double
            { "dbl", 0.1 + 0.2 },
            { "dblBig", -1234.5678e10 },
            { "dec", new Decimal128(12.50m) },
            { "oid", ObjectId.Parse("507f1f77bcf86cd799439011") },
            { "ts", new BsonDateTime(new DateTime(2026, 10, 27, 8, 15, 42, 120, DateTimeKind.Utc)) },
            { "yes", true },
            { "no", false },
            { "nul", BsonNull.Value },
            { "bin", new BsonBinaryData(new byte[] { 1, 2, 3 }) },
            { "stamp", new BsonTimestamp((1_800_000_000L << 32) | 5) },
            { "ar", "اعتماد تعديل صلاحيات دور" },
            { "arr", new BsonArray { 1, 2L, "x", new BsonDocument("k", true), new BsonArray { 3.5, BsonNull.Value } } },
            { "obj", new BsonDocument { { "z", 1 }, { "a", new BsonDocument("deep", "ع") } } },
        };
        var key = SequentialKey();

        foreach (var k in new ChainKey?[] { null, key })
        {
            var hash = ChainHash.Compute(ChainHash.Genesis, 42, doc, k);
            var stored = BsonSerializer.Deserialize<BsonDocument>(ChainHash.WithIntegrity(doc, "audit_events", 42, ChainHash.Genesis, hash, k?.Alg).ToBson());
            Assert.Equal(hash, ChainHash.Compute(ChainHash.Genesis, 42, stored, k));
            Assert.Equal(ChainHash.Canonical(doc), ChainHash.Canonical(stored));
        }
    }

    // ───────── استقلال البصمة عن ثقافة النظام ─────────

    [Theory]
    [InlineData("ar-SA")] // تقويم أم القرى وأرقام عربية
    [InlineData("ar-EG")]
    [InlineData("fa-IR")] // تقويم فارسي وأرقام فارسية
    [InlineData("tr-TR")] // الفاصلة العشرية والحرف I
    [InlineData("de-DE")]
    public void Hash_and_signature_do_not_depend_on_the_current_culture(string culture)
    {
        const long seq = 1_234_567_890_123L;
        var at = new DateTimeOffset(2027, 1, 15, 8, 0, 0, TimeSpan.Zero);
        var key = SequentialKey();

        string Run()
        {
            var doc = ChainHash.FromPayload(Payload);
            doc["big"] = 1_234_567_890_123L;
            doc["ratio"] = 1234.5678;
            doc["dec"] = new Decimal128(1234.50m);
            doc["date"] = new BsonDateTime(new DateTime(2026, 3, 9, 4, 5, 6, 7, DateTimeKind.Utc));
            return ChainHash.Canonical(doc) + "|" + ChainHash.Compute(ChainHash.Genesis, seq, doc) + "|"
                + ChainHash.Compute(ChainHash.Genesis, seq, doc, key) + "|" + ChainHash.SignCheckpoint(key, "audit_events", seq, "abc", at);
        }

        var expected = Run();
        var oldCulture = CultureInfo.CurrentCulture;
        var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            Assert.Equal(expected, Run());
        }
        finally
        {
            CultureInfo.CurrentCulture = oldCulture;
            CultureInfo.CurrentUICulture = oldUi;
        }
    }

    // ───────── حدود طول المفتاح وصيغ Base64 ─────────

    private static byte[] Bytes(int count) => Enumerable.Range(1, count).Select(i => (byte)i).ToArray();

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(31)]
    public void Chain_key_shorter_than_32_bytes_is_rejected(int length) =>
        Assert.Throws<InvalidOperationException>(() => ChainKey.FromConfig(Convert.ToBase64String(Bytes(length))));

    [Theory]
    [InlineData(32)]
    [InlineData(33)]
    [InlineData(64)]
    public void Chain_key_of_32_bytes_or_more_is_accepted(int length)
    {
        var key = ChainKey.FromConfig(Convert.ToBase64String(Bytes(length)));
        Assert.NotNull(key);
        Assert.Equal(Bytes(length), key.Secret);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_chain_key_gives_null_so_the_startup_guard_can_refuse_it(string? value) =>
        Assert.Null(ChainKey.FromConfig(value));

    [Fact]
    public void Chain_key_accepts_url_safe_unpadded_and_padded_whitespace_forms()
    {
        var secret = Enumerable.Range(0, 32).Select(i => i % 2 == 0 ? (byte)0xFB : (byte)0xFF).ToArray(); // ينتج + و/ في Base64
        var standard = Convert.ToBase64String(secret);
        Assert.Contains('+', standard);
        Assert.Contains('/', standard);

        var urlSafe = standard.Replace('+', '-').Replace('/', '_');
        var urlSafeUnpadded = urlSafe.TrimEnd('=');
        var standardUnpadded = standard.TrimEnd('=');

        var expected = ChainKey.FromConfig(standard)!;
        Assert.Equal(secret, expected.Secret);
        foreach (var form in new[] { urlSafe, urlSafeUnpadded, standardUnpadded, "  " + standard + "\n" })
        {
            var key = ChainKey.FromConfig(form);
            Assert.NotNull(key);
            Assert.Equal(expected.Secret, key.Secret);
            Assert.Equal(expected.Id, key.Id);
            Assert.Equal(expected.Alg, key.Alg);
        }
    }

    // ───────── وقت نقطة التثبيت ─────────

    [Fact]
    public void Checkpoint_signature_depends_on_the_instant_not_on_the_offset_it_was_written_in()
    {
        var key = SequentialKey();
        var utc = new DateTimeOffset(2027, 1, 15, 8, 0, 0, TimeSpan.Zero);
        var cairo = new DateTimeOffset(2027, 1, 15, 10, 0, 0, TimeSpan.FromHours(2));
        var newYork = new DateTimeOffset(2027, 1, 15, 3, 0, 0, TimeSpan.FromHours(-5));
        Assert.Equal(utc, cairo);

        var sig = ChainHash.SignCheckpoint(key, "security_events", 10, "abc", utc);
        Assert.Equal(sig, ChainHash.SignCheckpoint(key, "security_events", 10, "abc", cairo));
        Assert.Equal(sig, ChainHash.SignCheckpoint(key, "security_events", 10, "abc", newYork));
        Assert.NotEqual(sig, ChainHash.SignCheckpoint(key, "security_events", 10, "abc", utc.AddHours(2)));
    }

    [Fact]
    public void Checkpoint_signature_is_keyed_and_deterministic()
    {
        var at = new DateTimeOffset(2027, 1, 15, 8, 0, 0, TimeSpan.Zero);
        var sig = ChainHash.SignCheckpoint(SequentialKey(), "security_events", 10, "abc", at);
        Assert.Equal(sig, ChainHash.SignCheckpoint(SequentialKey(), "security_events", 10, "abc", at));
        Assert.NotEqual(sig, ChainHash.SignCheckpoint(ChainKey.FromBytes(Bytes(33)), "security_events", 10, "abc", at));
        Assert.Matches("^[0-9a-f]{64}$", sig);
    }
}
