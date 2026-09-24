#!/usr/bin/env bash
# Builds CscsMcp-win-x64.zip: the CSCS Playground server and its sandbox worker, self-contained for
# Windows x64 (the server needs no .NET install), plus install-cscs.ps1 to install or update it.
#
#   CscsMcp/deploy/publish-windows.sh            -> CscsMcp/bin/CscsMcp-win-x64.zip
#   CscsMcp/deploy/publish-windows.sh OUT_DIR    -> OUT_DIR/CscsMcp-win-x64.zip
#
# Runs on macOS, Linux or Windows (Git Bash). On the server: unzip, then in an elevated PowerShell
#   powershell -ExecutionPolicy Bypass -File .\install-cscs.ps1
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$DEPLOY_DIR/../.." && pwd)"
OUT_DIR="${1:-$REPO/CscsMcp/bin}"
mkdir -p "$OUT_DIR"
OUT_DIR="$(cd "$OUT_DIR" && pwd)"
STAGE="$OUT_DIR/win-x64-package"
ZIP="$OUT_DIR/CscsMcp-win-x64.zip"

rm -rf "$STAGE" "$ZIP"
mkdir -p "$STAGE/CscsMcp"

# ReadyToRun for the worker: every script starts a new worker process, so its startup time is
# paid on every run. The server starts once and doesn't need it.
echo "Publishing CscsSandbox..."
dotnet publish "$REPO/CscsSandbox/CscsSandbox.csproj" -c Release -r win-x64 --self-contained true \
    -p:PublishReadyToRun=true -o "$STAGE/CscsMcp/sandbox" -v quiet -nologo
echo "Publishing CscsMcp..."
dotnet publish "$REPO/CscsMcp/CscsMcp.csproj" -c Release -r win-x64 --self-contained true \
    -o "$STAGE/CscsMcp" -v quiet -nologo

# Symbols only add size; crash reports from the worker are discarded anyway.
find "$STAGE" -name '*.pdb' -delete
cp "$DEPLOY_DIR/install-cscs.ps1" "$STAGE/"

for f in CscsMcp/CscsMcp.exe CscsMcp/appsettings.json CscsMcp/sandbox/CscsSandbox.exe install-cscs.ps1; do
    [ -f "$STAGE/$f" ] || { echo "error: $f missing from the package" >&2; exit 1; }
done

if command -v zip >/dev/null 2>&1; then
    (cd "$STAGE" && zip -qr -9 "$ZIP" .)
else
    (cd "$STAGE" && python3 -m zipfile -c "$ZIP" CscsMcp install-cscs.ps1)
fi
rm -rf "$STAGE"

echo
echo "Package: $ZIP ($(du -h "$ZIP" | cut -f1))"
echo "Listens on: $(grep -o '"Urls": *"[^"]*"' "$REPO/CscsMcp/appsettings.json")"
echo "On the server: unzip it, then in an elevated PowerShell in that folder:"
echo "  powershell -ExecutionPolicy Bypass -File .\\install-cscs.ps1"
