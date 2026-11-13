#:package MongoDB.Driver@3.12.0
// نقل سجلات المنظومة (MongoDB) بين جهازين دون أدوات MongoDB Database Tools.
// كل مستند سطر JSON بالصيغة الموسعة القانونية، فتبقى الأنواع (التواريخ والمعرفات) وتسلسل البصمات كما هي.
//
//   dotnet run mongo-transfer.cs -- export <mongodb-uri> <folder> <db1> [db2 ...]
//   dotnet run mongo-transfer.cs -- import <mongodb-uri> <folder>
//
// الاستيراد يرفض مجموعة فيها مستندات بالفعل، فلا تتكرر السجلات.
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;

if (args.Length < 3 || args[0] is not ("export" or "import"))
{
    Console.Error.WriteLine("usage: export <uri> <folder> <db...> | import <uri> <folder>");
    return 2;
}
var client = new MongoClient(args[1]);
var folder = Path.GetFullPath(args[2]);
var json = new JsonWriterSettings { OutputMode = JsonOutputMode.CanonicalExtendedJson };

if (args[0] == "export")
{
    foreach (var dbName in args[3..])
    {
        var db = client.GetDatabase(dbName);
        var dir = Directory.CreateDirectory(Path.Combine(folder, dbName));
        foreach (var name in await (await db.ListCollectionNamesAsync()).ToListAsync())
        {
            var count = 0;
            await using var w = new StreamWriter(Path.Combine(dir.FullName, name + ".jsonl"), false, new System.Text.UTF8Encoding(false));
            using var cursor = await db.GetCollection<BsonDocument>(name).FindAsync(FilterDefinition<BsonDocument>.Empty);
            while (await cursor.MoveNextAsync())
                foreach (var doc in cursor.Current)
                {
                    await w.WriteLineAsync(doc.ToJson(json));
                    count++;
                }
            Console.WriteLine($"{dbName}.{name}: {count}");
        }
    }
    return 0;
}

foreach (var dir in Directory.GetDirectories(folder))
{
    var db = client.GetDatabase(Path.GetFileName(dir));
    foreach (var file in Directory.GetFiles(dir, "*.jsonl"))
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var coll = db.GetCollection<BsonDocument>(name);
        if (await coll.EstimatedDocumentCountAsync() > 0)
        {
            Console.Error.WriteLine($"{db.DatabaseNamespace.DatabaseName}.{name} already has documents; skipped.");
            continue;
        }
        var batch = new List<BsonDocument>();
        var count = 0;
        foreach (var line in File.ReadLines(file))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            batch.Add(BsonDocument.Parse(line));
            if (batch.Count == 1000) { await coll.InsertManyAsync(batch); count += batch.Count; batch.Clear(); }
        }
        if (batch.Count > 0) { await coll.InsertManyAsync(batch); count += batch.Count; }
        Console.WriteLine($"{db.DatabaseNamespace.DatabaseName}.{name}: {count}");
    }
}
return 0;
