# يمسح بيانات المؤشرات كلها ويحمل الإطار الشامل لتقييم أداء الجامعات (KPI.xlsx) في قاعدة التطوير أو النسخة التجريبية.
# يمسح: المؤشرات وتعريفاتها، والمستهدفات، والتعارضات، والمبادرات، والدورات، والنماذج، والقيم وأدلتها.
# يبقي: الحسابات والأدوار والصلاحيات والجهات والإعدادات وهياكل الكليات ومستنداتها والسجلات.
# الاستخدام:
#   powershell -ExecutionPolicy Bypass -File scripts\reset-kpi.ps1            قاعدة التطوير (tier1)
#   powershell -ExecutionPolicy Bypass -File scripts\reset-kpi.ps1 -Demo      النسخة التجريبية (tier1_demo)
# أوقف الخادم المعني قبل التشغيل.
param([switch]$Demo)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)

& powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\dev-db.ps1')
if ($LASTEXITCODE -ne 0) { throw "Could not start the development PostgreSQL." }

$vars = @{}
Get-Content (Join-Path $root '.env') | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { $vars[$Matches[1]] = $Matches[2] } }

# نسخة احتياطية كاملة للقاعدة قبل المسح، في data\backups.
$database = if ($Demo) { 'tier1_demo' } else { 'tier1' }
$bin = (Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\pg_dump.exe' | Sort-Object FullName -Descending | Select-Object -First 1).DirectoryName
$backups = Join-Path $root 'data\backups'
New-Item -ItemType Directory -Force $backups | Out-Null
$dump = Join-Path $backups "pre-reset-$database-$(Get-Date -Format 'yyyyMMdd-HHmmss').dump"
$env:PGPASSWORD = $vars.POSTGRES_PASSWORD
try {
    & "$bin\pg_dump.exe" -h 127.0.0.1 -p 5440 -U tier1_dev -d $database -Fc -Z 6 -f $dump
    if ($LASTEXITCODE -ne 0) { throw "pg_dump failed; nothing was deleted." }
} finally {
    Remove-Item Env:PGPASSWORD
}
Write-Host "Backup saved: $dump" -ForegroundColor Green

$env:ASPNETCORE_ENVIRONMENT = 'Development'
if ($Demo) {
    # الإعدادات نفسها التي يشغل بها scripts\demo.ps1 النسخة التجريبية.
    $demoData = Join-Path $root 'data\demo'
    $mongoDb = (Get-Content (Join-Path $demoData 'mongo-database.txt') -TotalCount 1).Trim()
    $env:ConnectionStrings__Tier1 = "Host=127.0.0.1;Port=5440;Database=tier1_demo;Username=tier1_dev;Password=$($vars.POSTGRES_PASSWORD)"
    $env:ConnectionStrings__Mongo = "mongodb://127.0.0.1:27017/$mongoDb"
    $env:MongoLogs__Database = $mongoDb
    $env:MongoLogs__CheckpointDirectory = Join-Path $demoData 'checkpoints'
    $env:DataProtection__KeysPath = Join-Path $demoData 'keys'
    $env:Files__Root = Join-Path $demoData 'files'
    $env:Email__PickupDirectory = Join-Path $demoData 'mail'
    Write-Host "Resetting the demo database (tier1_demo)..." -ForegroundColor Yellow
} else {
    Write-Host "Resetting the development database (tier1)..." -ForegroundColor Yellow
}

Set-Location $root
dotnet run --project src\Tier1.Api --no-launch-profile -- --reset-kpi --confirm
if ($LASTEXITCODE -ne 0) { throw "The reset failed." }
