# Sets up the Tier1 system on this machine from the transfer archive.
# Run it from the extracted "transfer" folder, next to the "Tier1" folder:
#   powershell -ExecutionPolicy Bypass -File restore-here.ps1
#   powershell -ExecutionPolicy Bypass -File restore-here.ps1 -FromDumps   # rebuild the databases from the dumps
#
# Steps: fix machine paths in the settings, start the project's own PostgreSQL (port 5440) on the copied data
# (or rebuild it from the dumps when the PostgreSQL version differs), and import the MongoDB logs.
param([switch]$FromDumps)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Join-Path (Split-Path -Parent $here) 'Tier1'
if (-not (Test-Path (Join-Path $root 'Tier1.slnx')) -and -not (Test-Path (Join-Path $root 'src'))) { throw "Tier1 folder not found next to $here" }

# 1. Paths in the development settings point at the old machine folder.
$cfg = Join-Path $root 'src\Tier1.Api\appsettings.Development.json'
$old = 'D:\\AMR_Private\\Tier1'
$new = $root.TrimEnd('\').Replace('\', '\\')
if ($new -ne $old) {
    $text = [IO.File]::ReadAllText($cfg)
    [IO.File]::WriteAllText($cfg, $text.Replace($old, $new), (New-Object Text.UTF8Encoding($false)))
    Write-Host "Settings now point at $root" -ForegroundColor Green
}

# 2. PostgreSQL: the copied cluster needs the same major version; otherwise rebuild from the dumps.
$pgctl = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\pg_ctl.exe' -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $pgctl) { throw "Install PostgreSQL 18 (the server binaries are enough), then run this script again." }
$bin = $pgctl.DirectoryName
$installed = (& "$bin\pg_ctl.exe" --version) -replace '^\D*(\d+).*$', '$1'
$pgdata = Join-Path $root 'data\pgdata'
$cluster = if (Test-Path (Join-Path $pgdata 'PG_VERSION')) { (Get-Content (Join-Path $pgdata 'PG_VERSION') -TotalCount 1).Trim() } else { '' }
$restore = $FromDumps -or ($cluster -ne $installed)
if ($restore -and (Test-Path $pgdata)) {
    $aside = "$pgdata.old-$(Get-Date -Format yyyyMMddHHmmss)"
    Rename-Item $pgdata $aside
    Write-Host "Copied cluster (PostgreSQL $cluster) moved aside to $aside; rebuilding from the dumps on PostgreSQL $installed." -ForegroundColor Yellow
}
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\dev-db.ps1')
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL did not start." }
if ($restore) {
    $vars = @{}
    Get-Content (Join-Path $root '.env') | ForEach-Object { if ($_ -match '^([^=]+)=(.*)$') { $vars[$Matches[1]] = $Matches[2] } }
    $env:PGPASSWORD = $vars.POSTGRES_PASSWORD
    try {
        & "$bin\createdb.exe" -h 127.0.0.1 -p 5440 -U tier1_dev tier1_demo
        foreach ($db in 'tier1', 'tier1_demo') {
            & "$bin\pg_restore.exe" -h 127.0.0.1 -p 5440 -U tier1_dev -d $db --no-owner --no-privileges (Join-Path $here "backups\$db.dump")
            if ($LASTEXITCODE -ne 0) { throw "pg_restore $db failed." }
            Write-Host "Restored $db" -ForegroundColor Green
        }
    } finally {
        Remove-Item Env:PGPASSWORD
    }
}

# 3. MongoDB logs (MongoDB service on 127.0.0.1:27017, no authentication).
$mongo = Test-NetConnection 127.0.0.1 -Port 27017 -InformationLevel Quiet -WarningAction SilentlyContinue
if ($mongo) {
    Push-Location (Join-Path $here 'tools')
    try { dotnet run mongo-transfer.cs -- import "mongodb://127.0.0.1:27017" (Join-Path $here 'backups\mongo') } finally { Pop-Location }
} else {
    Write-Warning "MongoDB is not running on 127.0.0.1:27017. Install MongoDB 8 Community as a service, then run:"
    Write-Warning "  cd `"$(Join-Path $here 'tools')`"; dotnet run mongo-transfer.cs -- import mongodb://127.0.0.1:27017 `"$(Join-Path $here 'backups\mongo')`""
}

Write-Host ""
Write-Host "Done. Start the demo with:  powershell -ExecutionPolicy Bypass -File `"$(Join-Path $root 'scripts\demo.ps1')`"" -ForegroundColor Green
Write-Host "Or the development server:  cd `"$root`"; dotnet run --project src\Tier1.Api" -ForegroundColor Green
