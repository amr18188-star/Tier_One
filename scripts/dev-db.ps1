# قاعدة تطوير خاصة بالمشروع، بلا Docker وبلا كلمة مرور postgres وبلا تغيير في خدمة PostgreSQL المثبتة:
# نسخة مستقلة من برامج PostgreSQL المثبتة نفسها، بياناتها في data\pgdata، على المنفذ 5440 وعنوان الجهاز فقط.
# الاستخدام:
#   powershell -ExecutionPolicy Bypass -File scripts\dev-db.ps1          تهيئة أول مرة ثم تشغيل
#   powershell -ExecutionPolicy Bypass -File scripts\dev-db.ps1 stop     إيقاف
param([ValidateSet('start', 'stop', 'status')][string]$Action = 'start')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$port = 5440
$data = Join-Path $root 'data\pgdata'
$log = Join-Path $root 'data\pgdata.log'
$bin = (Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\pg_ctl.exe' | Sort-Object FullName -Descending | Select-Object -First 1).DirectoryName
if (-not $bin) { throw "PostgreSQL binaries were not found under C:\Program Files\PostgreSQL." }

$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) { throw "Run scripts\dev-setup.ps1 first." }
$vars = @{}
Get-Content $envFile | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { $vars[$Matches[1]] = $Matches[2] } }

if ($Action -eq 'stop') { & "$bin\pg_ctl.exe" -D $data stop -m fast; exit $LASTEXITCODE }
if ($Action -eq 'status') { & "$bin\pg_ctl.exe" -D $data status; exit $LASTEXITCODE }

if (-not (Test-Path (Join-Path $data 'PG_VERSION'))) {
    New-Item -ItemType Directory -Force (Split-Path $data) | Out-Null
    $pw = Join-Path $env:TEMP "tier1-pw-$PID.txt"
    try {
        # الحساب المالك tier1_dev بكلمة المرور المولدة في .env؛ الملف المؤقت يحذف فورًا.
        Set-Content -Path $pw -Value $vars.POSTGRES_PASSWORD -Encoding ascii -NoNewline
        & "$bin\initdb.exe" -D $data -U tier1_dev --pwfile=$pw --auth=scram-sha-256 --encoding=UTF8 --locale=C | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "initdb failed." }
    } finally {
        Remove-Item $pw -ErrorAction SilentlyContinue
    }
    Add-Content -Path (Join-Path $data 'postgresql.conf') -Value "`nport = $port`nlisten_addresses = '127.0.0.1'`ntimezone = 'Africa/Cairo'`n"
    Write-Host "Initialized $data"
}

& "$bin\pg_ctl.exe" -D $data status | Out-Null
if ($LASTEXITCODE -ne 0) {
    # الخادم يرث مخرجات من يشغله فيبقي الطرفية معلقة؛ يشغل منفصلًا ثم ينتظر جاهزيته.
    Start-Process -FilePath "$bin\pg_ctl.exe" -ArgumentList @('-D', "`"$data`"", '-l', "`"$log`"", 'start') -WindowStyle Hidden
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        Start-Sleep -Seconds 1
        & "$bin\pg_isready.exe" -h 127.0.0.1 -p $port | Out-Null
        $ready = $LASTEXITCODE -eq 0
    }
    if (-not $ready) { throw "PostgreSQL did not start; see $log" }
}

$env:PGPASSWORD = $vars.POSTGRES_PASSWORD
try {
    $exists = & "$bin\psql.exe" -U tier1_dev -h 127.0.0.1 -p $port -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname = 'tier1'"
    if ($exists -ne '1') { & "$bin\createdb.exe" -U tier1_dev -h 127.0.0.1 -p $port tier1; Write-Host "Created database tier1" }
} finally {
    Remove-Item Env:PGPASSWORD
}
Write-Host "PostgreSQL for Tier1 is running on 127.0.0.1:$port" -ForegroundColor Green
