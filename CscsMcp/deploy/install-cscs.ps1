# Installs (or updates) the CSCS Playground MCP server as the Windows service "CscsMcp".
# publish-windows.sh packs this script next to a CscsMcp\ folder in CscsMcp-win-x64.zip.
# Run from an elevated PowerShell, in the folder you unzipped:
#   powershell -ExecutionPolicy Bypass -File .\install-cscs.ps1
# Safe to run again for an update: it stops the service, replaces the binaries, keeps your appsettings.json.

$ErrorActionPreference = "Stop"
$Target  = "C:\Services\CscsMcp"
$Port    = 17578
$Account = "NT SERVICE\CscsMcp"
$Source  = Join-Path $PSScriptRoot "CscsMcp"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) { throw "Run this from an elevated (Administrator) PowerShell." }
if (-not (Test-Path (Join-Path $Source "CscsMcp.exe"))) { throw "CscsMcp\CscsMcp.exe not found next to this script." }

Write-Host "1. Stopping the old service, if any"
$svc = Get-Service CscsMcp -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne "Stopped") { Stop-Service CscsMcp -Force; Start-Sleep 2 }

Write-Host "2. Copying files to $Target"
Get-ChildItem $Source -Recurse | Unblock-File
New-Item -ItemType Directory -Force $Target | Out-Null
$keepSettings = (Test-Path "$Target\appsettings.json") -and $svc
if ($keepSettings) { Copy-Item "$Target\appsettings.json" "$env:TEMP\cscs-appsettings.bak" -Force }
Copy-Item "$Source\*" $Target -Recurse -Force
if ($keepSettings) { Copy-Item "$env:TEMP\cscs-appsettings.bak" "$Target\appsettings.json" -Force; Write-Host "   kept your existing appsettings.json" }
New-Item -ItemType Directory -Force "$Target\runs" | Out-Null

Write-Host "3. Creating the service under its own virtual account"
if (-not $svc) {
    sc.exe create CscsMcp binPath= "`"$Target\CscsMcp.exe`"" start= auto DisplayName= "CSCS Playground MCP" | Out-Null
    sc.exe description CscsMcp "Public sandboxed CSCS scripting playground (MCP), port $Port" | Out-Null
    sc.exe failure CscsMcp reset= 86400 actions= restart/5000/restart/5000/restart/30000 | Out-Null
}
sc.exe config CscsMcp obj= "$Account" | Out-Null

Write-Host "4. File permissions"
icacls $Target /grant "${Account}:(OI)(CI)(RX)" | Out-Null
icacls "$Target\runs" /grant "${Account}:(OI)(CI)(M)" | Out-Null
# The playground account must never read the other services' secrets (MySQL, AI keys, MCP keys).
foreach ($secret in "C:\Services\BrainPingPong", "C:\Services\ChatCompareMcp") {
    if (Test-Path $secret) { icacls $secret /deny "${Account}:(OI)(CI)(F)" | Out-Null; Write-Host "   denied $secret" }
}

Write-Host "5. Firewall: port $Port from Cloudflare only"
$cloudflare = "173.245.48.0/20","103.21.244.0/22","103.22.200.0/22","103.31.4.0/22","141.101.64.0/18",
              "108.162.192.0/18","190.93.240.0/20","188.114.96.0/20","197.234.240.0/22","198.41.128.0/17",
              "162.158.0.0/15","104.16.0.0/13","104.24.0.0/14","172.64.0.0/13","131.0.72.0/22"
Get-NetFirewallRule -DisplayName "CSCS Playground MCP $Port" -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName "CSCS Playground MCP $Port" -Direction Inbound -Protocol TCP -LocalPort $Port `
    -RemoteAddress $cloudflare -Action Allow | Out-Null

Write-Host "6. Starting"
Start-Service CscsMcp
Start-Sleep 4
try {
    $health = Invoke-RestMethod "http://localhost:$Port/health" -TimeoutSec 15
    $health | Format-List
    Write-Host "Local check OK. Now open https://cscs.brainpingpong.com/health from anywhere." -ForegroundColor Green
} catch {
    Write-Host "The service did not answer on port $Port. Last errors from the event log:" -ForegroundColor Red
    Get-WinEvent -LogName Application -MaxEvents 20 |
        Where-Object { $_.ProviderName -match "CscsMcp|\.NET Runtime|Application Error" } |
        Select-Object -First 5 TimeCreated, ProviderName, Message | Format-List
}
