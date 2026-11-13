# النسخة التجريبية للعرض والتجربة: قاعدة منفصلة tier1_demo وسجلات ومجلد بيانات خاصة بها،
# فلا تختلط ببيانات اختبارات التطوير (قاعدة tier1 على المنفذ 5150).
# الاستخدام:
#   powershell -ExecutionPolicy Bypass -File scripts\demo.ps1            تشغيل النسخة التجريبية على http://localhost:5160
#   powershell -ExecutionPolicy Bypass -File scripts\demo.ps1 -Reset     حذف بياناتها والبدء من الصفر
# بعد أول تشغيل: python tools\demo_data.py   يملؤها ببيانات تجريبية واقعية عبر واجهة البرنامج نفسها.
param([switch]$Reset, [int]$Port = 5160)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$demoData = Join-Path $root 'data\demo'

$vars = @{}
Get-Content (Join-Path $root '.env') | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { $vars[$Matches[1]] = $Matches[2] } }
# المنفذ نفسه الذي يستخدمه dev-setup.ps1: T1_POSTGRES_PORT في .env، وإلا القاعدة الخاصة على 5440.
$pgPort = if ($vars.T1_POSTGRES_PORT -match '^\d+$') { [int]$vars.T1_POSTGRES_PORT } else { 5440 }
if ($pgPort -eq 5440) {
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\dev-db.ps1')
    if ($LASTEXITCODE -ne 0) { throw "Could not start the development PostgreSQL." }
}
$bin = (Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\psql.exe' | Sort-Object FullName -Descending | Select-Object -First 1).DirectoryName

$env:PGPASSWORD = $vars.POSTGRES_PASSWORD
try {
    $exists = & "$bin\psql.exe" -U tier1_dev -h 127.0.0.1 -p $pgPort -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'tier1_demo'"
    $canCreate = (& "$bin\psql.exe" -U tier1_dev -h 127.0.0.1 -p $pgPort -d postgres -tAc "SELECT rolcreatedb FROM pg_roles WHERE rolname = current_user") -eq 't'
    if (($Reset -or $exists -ne '1') -and -not $canCreate) {
        # لا يحذف القاعدة ما دام الحساب لا يستطيع إعادة إنشائها.
        throw "tier1_dev cannot create databases on 127.0.0.1:$pgPort. Create tier1_demo owned by tier1_dev from pgAdmin (or grant CREATEDB), then run this again."
    }
    if ($Reset) {
        & "$bin\dropdb.exe" -U tier1_dev -h 127.0.0.1 -p $pgPort --if-exists --force tier1_demo
        if (Test-Path $demoData) { Remove-Item -Recurse -Force $demoData }
        Write-Host "Removed the demo database and its files."
        $exists = ''
    }
    if ($exists -ne '1') { & "$bin\createdb.exe" -U tier1_dev -h 127.0.0.1 -p $pgPort tier1_demo; Write-Host "Created database tier1_demo" }
} finally {
    Remove-Item Env:PGPASSWORD
}

# سلسلة السجلات تبدأ من جديد مع كل قاعدة جديدة: قاعدة MongoDB باسم جديد بعد كل حذف (لا تتوفر أداة لحذف القديمة).
New-Item -ItemType Directory -Force $demoData | Out-Null
$mongoFile = Join-Path $demoData 'mongo-database.txt'
if (-not (Test-Path $mongoFile)) { "tier1_demo_logs_$(Get-Date -Format yyyyMMddHHmmss)" | Set-Content -Path $mongoFile -Encoding ascii }
$mongoDb = (Get-Content $mongoFile -TotalCount 1).Trim()

$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:ConnectionStrings__Tier1 = "Host=127.0.0.1;Port=$pgPort;Database=tier1_demo;Username=tier1_dev;Password=$($vars.POSTGRES_PASSWORD)"
$env:ConnectionStrings__Mongo = "mongodb://127.0.0.1:27017/$mongoDb"
$env:MongoLogs__Database = $mongoDb
$env:MongoLogs__CheckpointDirectory = Join-Path $demoData 'checkpoints'
$env:DataProtection__KeysPath = Join-Path $demoData 'keys'
$env:Files__Root = Join-Path $demoData 'files'
$env:Email__PickupDirectory = Join-Path $demoData 'mail'
$env:App__PublicUrl = "http://localhost:$Port"
# الكوكيز لا تفصل بالمنفذ؛ أسماء خاصة حتى لا تتداخل جلسة النسخة التجريبية مع خادم التطوير في المتصفح نفسه.
$env:Cookies__Session = 't1demo_session'
$env:Cookies__Antiforgery = 't1demo_af'

Write-Host "Tier1 demo: http://localhost:$Port  (accounts *@tier1.local, password T1_DEMO_PASSWORD in .env)" -ForegroundColor Green
Set-Location $root
dotnet run --project src\Tier1.Api --no-launch-profile
