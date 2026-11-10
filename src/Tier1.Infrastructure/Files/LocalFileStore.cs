using System.Text.RegularExpressions;
using Tier1.Application.Abstractions;
using Microsoft.Extensions.Configuration;

namespace Tier1.Infrastructure.Files;

/// <summary>
/// تخزين الملفات على قرص الخادم تحت مجلد واحد (`Files:Root`). المفتاح معرّف تولده المنظومة، فلا يتحكم المستخدم في المسار،
/// والكتابة إلى ملف مؤقت ثم نقله، فلا يبقى ملف ناقص. النسخ الاحتياطي للمجلد مع نسخ القاعدة.
/// </summary>
public sealed partial class LocalFileStore(IConfiguration config) : IFileStore
{
    private readonly string root = Path.GetFullPath(config["Files:Root"] is { Length: > 0 } r ? r : Path.Combine(AppContext.BaseDirectory, "data", "files"));

    public long MaxBytes { get; } = Math.Clamp(config.GetValue("Files:MaxMb", 20), 1, 100) * 1024L * 1024L;

    public async Task SaveAsync(string key, Stream content, CancellationToken ct)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".part";
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await content.CopyToAsync(file, ct);
        File.Move(temp, path);
    }

    public Stream OpenRead(string key) => new FileStream(PathFor(key), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    private string PathFor(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ArgumentException("Invalid storage key.", nameof(key));
        var path = Path.GetFullPath(Path.Combine(root, key));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new ArgumentException("Invalid storage key.", nameof(key));
        return path;
    }

    [GeneratedRegex("^[a-z]{2,10}/[0-9]{4}/[0-9a-f]{32}$")]
    private static partial Regex KeyPattern();
}
