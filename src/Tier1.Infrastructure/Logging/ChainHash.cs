using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MongoDB.Bson;

namespace Tier1.Infrastructure.Logging;

/// <summary>
/// سلسلة البصمات: hash = HMAC-SHA256(key, prevHash ‖ seq ‖ canonicalJson(event)).
/// المفتاح خارج القاعدتين (D7)، فمن يملك PostgreSQL وMongoDB معًا لا يعيد بناء السلسلة.
/// الأحداث الأقدم من تفعيل المفتاح بصمتها SHA-256 بلا مفتاح، ويميزها غياب alg.
/// الصيغة القانونية تحسب من وثيقة BSON نفسها (بعد التحويل وقبل الحفظ، وبعد القراءة عند الفحص)،
/// بمفاتيح مرتبة وتمثيل ثابت لكل نوع، فتنتج النص نفسه في الحالتين. حقل integrity لا يدخل في البصمة.
/// </summary>
public static class ChainHash
{
    public const string Genesis = "0000000000000000000000000000000000000000000000000000000000000000";
    public const string IntegrityField = "integrity";
    public const string HmacAlg = "hmac-sha256";

    /// <summary>بصمة SHA-256 بلا مفتاح (الأحداث السابقة لتفعيل المفتاح).</summary>
    public static string Compute(string prevHash, long seq, BsonDocument document) => Compute(prevHash, seq, document, null);

    public static string Compute(string prevHash, long seq, BsonDocument document, ChainKey? key)
    {
        var bytes = Encoding.UTF8.GetBytes(prevHash + "\n" + seq.ToString(CultureInfo.InvariantCulture) + "\n" + Canonical(document));
        var hash = key is null ? SHA256.HashData(bytes) : HMACSHA256.HashData(key.Secret, bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>توقيع نقطة تثبيت منشورة خارج المنظومة.</summary>
    public static string SignCheckpoint(ChainKey key, string chain, long seq, string hash, DateTimeOffset at) =>
        Convert.ToHexString(HMACSHA256.HashData(key.Secret, Encoding.UTF8.GetBytes(
            $"LC-CHECKPOINT-v1|{chain}|{seq.ToString(CultureInfo.InvariantCulture)}|{hash}|{at.UtcDateTime.ToString("O", CultureInfo.InvariantCulture)}"))).ToLowerInvariant();

    /// <summary>JSON قانوني للوثيقة دون حقل السلامة.</summary>
    public static string Canonical(BsonDocument document)
    {
        var sb = new StringBuilder();
        WriteDocument(sb, document, skipIntegrity: true);
        return sb.ToString();
    }

    /// <summary>يحول حمولة الصندوق الصادر إلى وثيقة BSON: ts تاريخ BSON (للفهارس وTTL)، والباقي كما هو.</summary>
    public static BsonDocument FromPayload(string payload)
    {
        var doc = BsonDocument.Parse(payload);
        if (doc.TryGetValue("ts", out var ts) && ts.IsString)
            doc["ts"] = new BsonDateTime(DateTime.Parse(ts.AsString, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
        return doc;
    }

    /// <summary>alg بصيغة «hmac-sha256:keyId»، أو null للأحداث القديمة بلا مفتاح.</summary>
    public static BsonDocument WithIntegrity(BsonDocument document, string chain, long seq, string prevHash, string hash, string? alg = null)
    {
        var copy = document.DeepClone().AsBsonDocument;
        var integrity = new BsonDocument
        {
            { "chain", chain },
            { "seq", seq },
            { "prevHash", prevHash },
            { "hash", hash },
        };
        if (alg is not null) integrity["alg"] = alg;
        copy[IntegrityField] = integrity;
        return copy;
    }

    private static void WriteDocument(StringBuilder sb, BsonDocument doc, bool skipIntegrity)
    {
        sb.Append('{');
        var first = true;
        foreach (var element in doc.Elements.Where(e => !(skipIntegrity && e.Name == IntegrityField)).OrderBy(e => e.Name, StringComparer.Ordinal))
        {
            if (!first) sb.Append(',');
            first = false;
            sb.Append(JsonSerializer.Serialize(element.Name)).Append(':');
            WriteValue(sb, element.Value);
        }
        sb.Append('}');
    }

    private static void WriteValue(StringBuilder sb, BsonValue value)
    {
        switch (value.BsonType)
        {
            case BsonType.Document:
                WriteDocument(sb, value.AsBsonDocument, skipIntegrity: false);
                break;
            case BsonType.Array:
                sb.Append('[');
                var arr = value.AsBsonArray;
                for (var i = 0; i < arr.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    WriteValue(sb, arr[i]);
                }
                sb.Append(']');
                break;
            case BsonType.String:
                sb.Append(JsonSerializer.Serialize(value.AsString));
                break;
            case BsonType.Null:
            case BsonType.Undefined:
                sb.Append("null");
                break;
            case BsonType.Boolean:
                sb.Append(value.AsBoolean ? "true" : "false");
                break;
            case BsonType.Int32:
                sb.Append(value.AsInt32.ToString(CultureInfo.InvariantCulture));
                break;
            case BsonType.Int64:
                sb.Append(value.AsInt64.ToString(CultureInfo.InvariantCulture));
                break;
            case BsonType.Double:
                sb.Append(value.AsDouble.ToString("R", CultureInfo.InvariantCulture));
                break;
            case BsonType.Decimal128:
                sb.Append(value.AsDecimal128.ToString());
                break;
            case BsonType.DateTime:
                sb.Append("\"$date:").Append(value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append('"');
                break;
            case BsonType.ObjectId:
                sb.Append("\"$oid:").Append(value.AsObjectId.ToString()).Append('"');
                break;
            default:
                sb.Append(JsonSerializer.Serialize(value.ToString()));
                break;
        }
    }
}
