using System.ComponentModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Tier1.Api.Startup;

/// <summary>
/// One-off operations commands (<c>dotnet Tier1.Api.dll --backup ...</c>), replacing the old PowerShell scripts:
/// a full backup (PostgreSQL dump, MongoDB logs, evidence files, data-protection keys, log checkpoints, SHA-256 sums)
/// and a restore test that proves a backup can actually be restored.
/// Connection details come from the application's own configuration; <c>pg_dump</c>, <c>pg_restore</c> and <c>mongodump</c> must be installed.
/// </summary>
public static class Maintenance
{
    private static readonly string[] CountedTables =
        ["org.units", "kpi.indicators", "kpi.indicator_versions", "kpi.targets", "kpi.conflicts", "dc.cycles", "dc.submissions", "dc.values", "dc.documents", "iam.users"];

    private static readonly (string Name, string Key)[] DataFolders =
        [("files", "Files:Root"), ("keys", "DataProtection:KeysPath"), ("checkpoints", "MongoLogs:CheckpointDirectory")];

    /// <summary>Value that follows <paramref name="name"/> in the arguments, or null.</summary>
    public static string? Option(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
    }

    /// <summary>--backup &lt;destination&gt; [--keep-days 30] [--pg-bin dir] [--mongo-uri uri]</summary>
    public static async Task<int> BackupAsync(string[] args, IConfiguration config)
    {
        var destination = Option(args, "--backup") ?? throw new ArgumentException("Usage: --backup <destination folder> [--keep-days 30] [--pg-bin <dir>] [--mongo-uri <uri>]");
        var keepDays = int.Parse(Option(args, "--keep-days") ?? "30", CultureInfo.InvariantCulture);
        var pgBin = Option(args, "--pg-bin");
        var mongoUri = Option(args, "--mongo-uri") ?? config.GetConnectionString("Mongo");
        var pg = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Tier1"));

        var target = Path.Combine(destination, $"tier1-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(target);

        Console.WriteLine("1/4 PostgreSQL -> tier1.dump");
        await RunAsync(Tool(pgBin, "pg_dump"), ["-h", pg.Host!, "-p", pg.Port.ToString(CultureInfo.InvariantCulture), "-U", pg.Username!, "-d", pg.Database!, "-Fc", "-Z", "6", "-f", Path.Combine(target, "tier1.dump")], pg.Password);

        Console.WriteLine("2/4 MongoDB logs");
        if (string.IsNullOrWhiteSpace(mongoUri)) Console.WriteLine("   WARNING: no Mongo connection string; logs not included.");
        else
        {
            try { await RunAsync("mongodump", ["--uri", mongoUri, "--gzip", $"--archive={Path.Combine(target, "tier1_logs.archive.gz")}"], null); }
            catch (Win32Exception) { Console.WriteLine("   WARNING: mongodump is not installed (MongoDB Database Tools); logs not included."); }
        }

        Console.WriteLine("3/4 Files, keys and checkpoints");
        foreach (var (name, key) in DataFolders)
        {
            var folder = config[key];
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(Path.GetFullPath(folder))) continue;
            ZipFile.CreateFromDirectory(Path.GetFullPath(folder), Path.Combine(target, $"{name}.zip"), CompressionLevel.Optimal, includeBaseDirectory: false);
        }

        Console.WriteLine("4/4 Checksums");
        var sums = new StringBuilder();
        foreach (var file in new DirectoryInfo(target).GetFiles().Where(f => f.Name != "SHA256SUMS.txt").OrderBy(f => f.Name, StringComparer.Ordinal))
            sums.Append(CultureInfo.InvariantCulture, $"{Hash(file.FullName)}  {file.Name}\n");
        await File.WriteAllTextAsync(Path.Combine(target, "SHA256SUMS.txt"), sums.ToString(), Encoding.ASCII);

        foreach (var old in new DirectoryInfo(destination).GetDirectories("tier1-*").Where(d => d.LastWriteTime < DateTime.Now.AddDays(-keepDays))) old.Delete(recursive: true);
        var mb = new DirectoryInfo(target).GetFiles().Sum(f => f.Length) / 1024.0 / 1024.0;
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Backup ready: {target} ({mb:N1} MB)"));
        return 0;
    }

    /// <summary>--restore-test &lt;backup folder&gt; [--pg-bin dir]: verifies the checksums, restores into a temporary database, compares row counts, drops it.</summary>
    public static async Task<int> RestoreTestAsync(string[] args, IConfiguration config)
    {
        var backup = Option(args, "--restore-test") ?? throw new ArgumentException("Usage: --restore-test <backup folder> [--pg-bin <dir>]");
        var pgBin = Option(args, "--pg-bin");
        var pg = new NpgsqlConnectionStringBuilder(config.GetConnectionString("Tier1"));

        Console.WriteLine("1/4 Checksums");
        foreach (var line in await File.ReadAllLinesAsync(Path.Combine(backup, "SHA256SUMS.txt")))
        {
            var parts = line.Split("  ", 2);
            if (parts.Length != 2) continue;
            if (!string.Equals(Hash(Path.Combine(backup, parts[1])), parts[0], StringComparison.Ordinal)) throw new InvalidOperationException($"Checksum mismatch: {parts[1]}");
        }
        Console.WriteLine("   all files match");

        var temp = $"tier1_restore_test_{DateTime.Now:yyyyMMddHHmmss}";
        Console.WriteLine($"2/4 Restore into {temp}");
        await ExecuteAsync(pg.ConnectionString, $"CREATE DATABASE {temp}");
        try
        {
            var tempConnection = new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = temp, Pooling = false }.ConnectionString;
            await RunAsync(Tool(pgBin, "pg_restore"), ["-h", pg.Host!, "-p", pg.Port.ToString(CultureInfo.InvariantCulture), "-U", pg.Username!, "-d", temp, "--no-owner", "--exit-on-error", Path.Combine(backup, "tier1.dump")], pg.Password);

            Console.WriteLine("3/4 Compare row counts");
            var failed = 0;
            foreach (var table in CountedTables)
            {
                var restored = await CountAsync(tempConnection, table);
                if (restored is null) { Console.WriteLine($"   {table,-26} (not in this version)"); continue; }
                var live = await CountAsync(pg.ConnectionString, table) ?? 0;
                var mark = restored == live ? "ok" : restored < live ? "ok (newer data since backup)" : "MISMATCH";
                if (mark == "MISMATCH") failed++;
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"   {table,-26} backup {restored,8}  live {live,8}  {mark}"));
            }
            if (failed > 0) throw new InvalidOperationException($"{failed} tables restored with more rows than the live database.");

            Console.WriteLine("4/4 Files");
            foreach (var zip in new DirectoryInfo(backup).GetFiles("*.zip"))
            {
                using var archive = ZipFile.OpenRead(zip.FullName);
                Console.WriteLine($"   {zip.Name}: {archive.Entries.Count} entries readable");
            }
            Console.WriteLine("Restore test passed.");
            return 0;
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(pg.ConnectionString, $"DROP DATABASE IF EXISTS {temp} WITH (FORCE)");
        }
    }

    private static string Tool(string? bin, string name) => bin is null ? name : Path.Combine(bin, name);

    private static async Task RunAsync(string tool, IReadOnlyList<string> arguments, string? pgPassword)
    {
        var start = new ProcessStartInfo(tool) { UseShellExecute = false };
        foreach (var a in arguments) start.ArgumentList.Add(a);
        if (pgPassword is not null) start.Environment["PGPASSWORD"] = pgPassword;
        Process process;
        try { process = Process.Start(start) ?? throw new InvalidOperationException($"Could not start {tool}."); }
        catch (Win32Exception) when (tool is not "mongodump")
        {
            throw new InvalidOperationException($"{tool} was not found. Install the PostgreSQL client tools, or pass --pg-bin <folder containing it>.");
        }
        using var _ = process;
        await process.WaitForExitAsync();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(tool)} failed ({process.ExitCode}).");
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var conn = new NpgsqlConnection(connection);
        await conn.OpenAsync();
#pragma warning disable CA2100 // The statements are built from a generated database name only.
        await using var cmd = new NpgsqlCommand(sql, conn);
#pragma warning restore CA2100
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>Row count of a table, or null when the table does not exist in that database.</summary>
    private static async Task<long?> CountAsync(string connection, string table)
    {
        try
        {
            await using var conn = new NpgsqlConnection(connection);
            await conn.OpenAsync();
#pragma warning disable CA2100 // The table names come from the constant list above.
            await using var cmd = new NpgsqlCommand($"SELECT count(*) FROM {table}", conn);
#pragma warning restore CA2100
            return (long)(await cmd.ExecuteScalarAsync())!;
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return null;
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
