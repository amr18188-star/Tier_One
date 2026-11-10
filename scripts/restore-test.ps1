# اختبار الاسترجاع: يتحقق من بصمات النسخة، ويسترجع قاعدة PostgreSQL في قاعدة مؤقتة، ويقارن عدد السجلات في الجداول الأساسية
# بالقاعدة الأصلية، ثم يحذف القاعدة المؤقتة. يشغل شهريًا على الأقل؛ النسخة التي لم يختبر استرجاعها لا يعتمد عليها.
#
#   $env:PGPASSWORD = '...'
#   powershell -ExecutionPolicy Bypass -File scripts\restore-test.ps1 -Backup E:\Backups\Tier1\tier1-20261111-020000
param(
    [Parameter(Mandatory)] [string] $Backup,
    [string] $PgHost = '127.0.0.1',
    [int] $PgPort = 5432,
    [string] $PgDatabase = 'tier1',
    [string] $PgUser = 'tier1',
    [string] $PgBin = 'C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference = 'Stop'
$psql = Join-Path $PgBin 'psql.exe'

Write-Host "1/4 Checksums"
foreach ($line in Get-Content (Join-Path $Backup 'SHA256SUMS.txt')) {
    $hash, $name = $line -split '  ', 2
    $actual = (Get-FileHash (Join-Path $Backup $name) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $hash) { throw "Checksum mismatch: $name" }
}
Write-Host "   all files match"

$temp = "tier1_restore_test_$(Get-Date -Format 'yyyyMMddHHmmss')"
Write-Host "2/4 Restore into $temp"
& $psql -h $PgHost -p $PgPort -U $PgUser -d $PgDatabase -v ON_ERROR_STOP=1 -q -c "CREATE DATABASE $temp" | Out-Null
try {
    & (Join-Path $PgBin 'pg_restore.exe') -h $PgHost -p $PgPort -U $PgUser -d $temp --no-owner --exit-on-error (Join-Path $Backup 'tier1.dump')
    if ($LASTEXITCODE -ne 0) { throw "pg_restore failed ($LASTEXITCODE)" }

    Write-Host "3/4 Compare row counts"
    $tables = 'org.units', 'kpi.indicators', 'kpi.indicator_versions', 'kpi.targets', 'kpi.conflicts', 'dc.cycles', 'dc.submissions', 'dc.values', 'dc.documents', 'iam.users'
    $failed = 0
    foreach ($t in $tables) {
        $q = "SELECT count(*) FROM $t"
        $a = (& $psql -h $PgHost -p $PgPort -U $PgUser -d $temp -tA -c $q 2>$null)
        if ($LASTEXITCODE -ne 0) { Write-Host ("   {0,-26} (not in this version)" -f $t); continue }
        $b = (& $psql -h $PgHost -p $PgPort -U $PgUser -d $PgDatabase -tA -c $q)
        $mark = if ($a -eq $b) { 'ok' } elseif ([int]$a -le [int]$b) { 'ok (newer data since backup)' } else { $failed++; 'MISMATCH' }
        Write-Host ("   {0,-26} backup {1,8}  live {2,8}  {3}" -f $t, $a, $b, $mark)
    }
    if ($failed -gt 0) { throw "$failed tables restored with more rows than the live database." }

    Write-Host "4/4 Files"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach ($zip in Get-ChildItem $Backup -Filter '*.zip') {
        $archive = [IO.Compression.ZipFile]::OpenRead($zip.FullName)
        try { Write-Host ("   {0}: {1} entries readable" -f $zip.Name, $archive.Entries.Count) } finally { $archive.Dispose() }
    }
    Write-Host "Restore test passed."
}
finally {
    & $psql -h $PgHost -p $PgPort -U $PgUser -d $PgDatabase -q -c "DROP DATABASE IF EXISTS $temp" | Out-Null
}
