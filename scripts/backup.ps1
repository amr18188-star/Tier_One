# نسخة احتياطية كاملة: قاعدة PostgreSQL (pg_dump بصيغة مخصصة)، وسجلات MongoDB (mongodump إن وجد)، ومجلد الأدلة والمستندات،
# ومفاتيح حماية البيانات ونقاط تثبيت السجلات. كل نسخة في مجلد بتاريخها مع ملف SHA-256 للتحقق، وتحذف النسخ الأقدم من مدة الاحتفاظ.
#
# الاستخدام (جدولة يومية من Task Scheduler مثلًا):
#   $env:PGPASSWORD = '...'   # أو ملف pgpass.conf للحساب الذي يشغل المهمة
#   powershell -ExecutionPolicy Bypass -File scripts\backup.ps1 -Destination E:\Backups\Tier1 -DataRoot D:\Tier1Data
param(
    [Parameter(Mandatory)] [string] $Destination,
    [string] $DataRoot = '',
    [string] $PgHost = '127.0.0.1',
    [int] $PgPort = 5432,
    [string] $PgDatabase = 'tier1',
    [string] $PgUser = 'tier1',
    [string] $MongoUri = '',
    [int] $KeepDays = 30,
    [string] $PgBin = 'C:\Program Files\PostgreSQL\18\bin'
)
$ErrorActionPreference = 'Stop'
if (-not $DataRoot) { $DataRoot = Join-Path (Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)) 'data' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$target = Join-Path $Destination "tier1-$stamp"
New-Item -ItemType Directory -Force $target | Out-Null

Write-Host "1/4 PostgreSQL -> tier1.dump"
& (Join-Path $PgBin 'pg_dump.exe') -h $PgHost -p $PgPort -U $PgUser -d $PgDatabase -Fc -Z 6 -f (Join-Path $target 'tier1.dump')
if ($LASTEXITCODE -ne 0) { throw "pg_dump failed ($LASTEXITCODE)" }

Write-Host "2/4 MongoDB logs"
$mongodump = Get-Command mongodump -ErrorAction SilentlyContinue
if ($MongoUri -and $mongodump) {
    & $mongodump.Source --uri $MongoUri --gzip --archive=(Join-Path $target 'tier1_logs.archive.gz')
    if ($LASTEXITCODE -ne 0) { throw "mongodump failed ($LASTEXITCODE)" }
} else {
    Write-Warning "mongodump or -MongoUri missing: logs not included. Install MongoDB Database Tools and pass -MongoUri."
}

Write-Host "3/4 Files, keys and checkpoints"
foreach ($name in 'files', 'keys', 'checkpoints') {
    $src = Join-Path $DataRoot $name
    if (Test-Path $src) { Compress-Archive -Path (Join-Path $src '*') -DestinationPath (Join-Path $target "$name.zip") -CompressionLevel Optimal -Force }
}

Write-Host "4/4 Checksums"
Get-ChildItem $target -File | Where-Object Name -ne 'SHA256SUMS.txt' | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
} | Set-Content (Join-Path $target 'SHA256SUMS.txt') -Encoding ascii

Get-ChildItem $Destination -Directory -Filter 'tier1-*' | Where-Object LastWriteTime -lt (Get-Date).AddDays(-$KeepDays) | Remove-Item -Recurse -Force
$size = (Get-ChildItem $target -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Backup ready: {0} ({1:N1} MB)" -f $target, $size)
