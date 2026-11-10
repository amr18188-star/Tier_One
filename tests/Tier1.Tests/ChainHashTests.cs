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
}
