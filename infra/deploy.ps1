<#
infra/deploy.ps1 — publish LicensingAdmin + LicensingApi and ship them to the LXC.
PowerShell equivalent of deploy.sh. Run from the repo root on Windows.

Prerequisites:
  - OpenSSH Client (Windows 10/11 ships it; Settings → Optional Features)
  - .NET SDK
  - SSH key loaded in ssh-agent, or password auth

Usage:
  .\infra\deploy.ps1 10.11.1.41

Never put the database password in this file — pass it via the environment or an
interactive prompt (see below).
#>

param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$LxcHost,

    # Optional: dump the remote DB before deploying. When set, the password is read
    # from the environment variable DB_PASSWORD so it never lands in this file or in
    # your shell history.
    [Parameter(Mandatory = $false)]
    [switch]$BackupDatabase
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$PublishDir = Join-Path $RepoRoot "publish"
$Timestamp = Get-Date -Format "yyyyMMdd_HHmmss"

Write-Host "==> publishing LicensingAdmin (framework-dependent, linux-x64)"
Remove-Item -Recurse -Force "$PublishDir\admin" -ErrorAction SilentlyContinue
dotnet publish "$RepoRoot\LicensingAdmin\LicensingAdmin.csproj" `
    -c Release -o "$PublishDir\admin" --self-contained false -r linux-x64

Write-Host "==> publishing LicensingApi (framework-dependent, linux-x64)"
Remove-Item -Recurse -Force "$PublishDir\api" -ErrorAction SilentlyContinue
dotnet publish "$RepoRoot\LicensingApi\LicensingApi.csproj" `
    -c Release -o "$PublishDir\api" --self-contained false -r linux-x64

Write-Host "==> packaging MiautrixLicensingClient"
Remove-Item -Recurse -Force "$PublishDir\client" -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path "$PublishDir\client" | Out-Null
dotnet publish "$RepoRoot\MiautrixLicensingClient\MiautrixLicensingClient.csproj" `
    -c Release -o "$PublishDir\client\files"
New-Item -ItemType Directory -Path "$PublishDir\client\files\docs" | Out-Null
Copy-Item "$RepoRoot\docs\client-integration\MiautrixLicensingClient-Integration.*" "$PublishDir\client\files\docs\" -Force
Compress-Archive -Path "$PublishDir\client\files\*" -DestinationPath "$PublishDir\client\MiautrixLicensingClient.zip"
Remove-Item -Recurse -Force "$PublishDir\client\files"

Write-Host "==> backup + stop services on $LxcHost"
ssh root@$LxcHost "mkdir -p /var/backups/licensing-v2-pre"

if ($BackupDatabase) {
    $dbPassword = $env:DB_PASSWORD
    if ([string]::IsNullOrWhiteSpace($dbPassword)) {
        throw "Set `$env:DB_PASSWORD before using -BackupDatabase."
    }
    Write-Host "==> dumping licensing_app from 172.16.101.12"
    # PGPASSWORD is passed as an env assignment on the remote side only; the value
    # never appears in this script, a committed file, or the local shell history.
    ssh root@$LxcHost "PGPASSWORD='$dbPassword' pg_dump -h 172.16.101.12 -U licensing licensing_app > /var/backups/licensing-v2-pre/db_$Timestamp.sql"
}

ssh root@$LxcHost "tar -czf /var/backups/licensing-v2-pre/files_backup_$Timestamp.tar.gz /var/www/licensing/ || true"
ssh root@$LxcHost "systemctl stop licensing-admin licensing-api || true"

Write-Host "==> syncing published output"
ssh root@$LxcHost "rm -rf /var/www/licensing/admin/* /var/www/licensing/api/* && mkdir -p /var/www/licensing/admin/wwwroot/downloads"
scp -rq "$PublishDir\admin\." "root@${LxcHost}:/var/www/licensing/admin/"
scp -rq "$PublishDir\api\." "root@${LxcHost}:/var/www/licensing/api/"
scp -rq "$PublishDir\client\MiautrixLicensingClient.zip" "root@${LxcHost}:/var/www/licensing/admin/wwwroot/downloads/"
# Fix ownership and permissions: ensure readable/executable by whichever user runs systemd.
ssh root@$LxcHost "chmod -R 755 /var/www/licensing/admin /var/www/licensing/api"

Write-Host "==> starting services on $LxcHost"
ssh root@$LxcHost "systemctl daemon-reload && systemctl start licensing-admin licensing-api && systemctl --no-pager status licensing-admin licensing-api"

Write-Host "==> done."
Write-Host "    Admin: https://$LxcHost/"
Write-Host "    API:   https://${LxcHost}:8080/"
