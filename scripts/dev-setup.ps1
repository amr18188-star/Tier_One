# يهيئ بيئة التطوير على PostgreSQL وMongoDB المثبتين على الجهاز (بلا Docker).
# يولد الأسرار في .env وappsettings.Development.json (لا يدخلان المستودع)، ويطبع أمر إنشاء حساب القاعدة مرة واحدة.
# الاستخدام: powershell -ExecutionPolicy Bypass -File scripts\dev-setup.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function New-Secret([int]$bytes = 24) {
    $b = New-Object byte[] $bytes
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    return ([Convert]::ToBase64String($b) -replace '[+/=]', 'x')
}

function New-Key {
    $k = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($k)
    return [Convert]::ToBase64String($k)
}

$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) {
    @(
        "POSTGRES_PASSWORD=$(New-Secret)",
        "T1_DEMO_PASSWORD=Tier1-Demo-$(New-Secret 6)",
        "T1_CHAIN_KEY=$(New-Key)"
    ) | Set-Content -Path $envFile -Encoding ascii
    Write-Host "Created .env"
}
$vars = @{}
Get-Content $envFile | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { $vars[$Matches[1]] = $Matches[2] } }

$dev = Join-Path $root 'src/Tier1.Api/appsettings.Development.json'
$settings = [ordered]@{
    ConnectionStrings = [ordered]@{
        Tier1 = "Host=127.0.0.1;Port=5440;Database=tier1;Username=tier1_dev;Password=$($vars.POSTGRES_PASSWORD)"
        # MongoDB المحلي بلا مصادقة في التطوير؛ الإنتاج بحسابين منفصلين للإضافة والإدارة.
        Mongo = "mongodb://127.0.0.1:27017/tier1_logs"
        MongoAdmin = "mongodb://127.0.0.1:27017/"
    }
    MongoLogs = [ordered]@{ SetupOnStartup = $true; ChainKey = $vars.T1_CHAIN_KEY; CheckpointDirectory = (Join-Path $root 'data/checkpoints') }
    Seed = @{ Demo = $true; DemoPassword = $vars.T1_DEMO_PASSWORD }
    DataProtection = @{ KeysPath = (Join-Path $root 'data/keys') }
    Files = @{ Root = (Join-Path $root 'data/files') }
    Cookies = @{ Secure = $false }
    # التحقق بخطوتين موقوف في التطوير؛ غير التطوير يرفض البدء به موقوفًا.
    Auth = @{ RequireMfa = $false }
    RateLimiting = @{ LoginPerMinute = 600; SensitivePerMinute = 2000 }
}
$settings | ConvertTo-Json -Depth 5 | Set-Content -Path $dev -Encoding utf8
Write-Host "Wrote $dev"

Write-Host ""
Write-Host "Next: powershell -ExecutionPolicy Bypass -File scripts\dev-db.ps1   (private PostgreSQL on 127.0.0.1:5440)"
Write-Host "Demo accounts: *@tier1.local with the password T1_DEMO_PASSWORD from .env"
