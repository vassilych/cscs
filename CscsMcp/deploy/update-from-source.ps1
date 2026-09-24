# Builds the CSCS Playground from this repository and installs or updates the "CscsMcp" service,
# on the server itself: the same package publish-windows.sh makes, without copying a zip around.
# Needs a .NET SDK 9 or newer (dotnet --list-sdks): a newer SDK builds these net9.0 projects and
# fetches the .NET 9 runtime packs from NuGet. After a git pull, in an elevated PowerShell:
#   powershell -ExecutionPolicy Bypass -File C:\Vassili\cscs\CscsMcp\deploy\update-from-source.ps1

$ErrorActionPreference = "Stop"
$Repo  = (Resolve-Path "$PSScriptRoot\..\..").Path
$Stage = Join-Path $env:TEMP "cscs-package"

$sdkMajors = @()
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $sdkMajors = dotnet --list-sdks | ForEach-Object { [int]($_ -split '\.')[0] }
}
if (-not ($sdkMajors | Where-Object { $_ -ge 9 })) {
    throw "A .NET SDK 9 or newer is needed to build (https://dotnet.microsoft.com/download), or build the zip elsewhere with publish-windows.sh."
}

Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue

# ReadyToRun for the worker: every script starts a new worker process, so its startup time is
# paid on every run. The server starts once and doesn't need it.
Write-Host "Publishing CscsSandbox..."
dotnet publish "$Repo\CscsSandbox\CscsSandbox.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishReadyToRun=true -o "$Stage\CscsMcp\sandbox" -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "CscsSandbox failed to build." }

Write-Host "Publishing CscsMcp..."
dotnet publish "$Repo\CscsMcp\CscsMcp.csproj" -c Release -r win-x64 --self-contained true `
    -o "$Stage\CscsMcp" -v quiet -nologo
if ($LASTEXITCODE -ne 0) { throw "CscsMcp failed to build." }

Get-ChildItem $Stage -Recurse -Filter *.pdb | Remove-Item
Copy-Item "$PSScriptRoot\install-cscs.ps1" $Stage

& "$Stage\install-cscs.ps1"
Remove-Item $Stage -Recurse -Force -ErrorAction SilentlyContinue
