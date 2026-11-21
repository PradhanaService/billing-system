#!/usr/bin/env bash
# Builds the Windows installer installer/output/WaterBillingSetup.exe on Linux (or macOS / WSL).
# Run from the repository root:   bash installer/build-installer.sh
# Needs: .NET 8 SDK and NSIS 3 (Ubuntu: sudo apt install nsis). On Windows, build-installer.ps1 does the same with Inno Setup.
# Signing (removes "Unknown publisher"): set SIGN_PFX and SIGN_PFX_PASSWORD (see installer/sign.sh and
# docs/code-signing.md) and install osslsigncode (sudo apt install osslsigncode). Without them the setup is unsigned.
set -euo pipefail
cd "$(dirname "$0")/.."

echo "1/4 Running tests..."
dotnet test tests

echo "2/4 Publishing the service (self-contained, no .NET needed on the shop PC)..."
rm -rf installer/output/app installer/output/desktop
dotnet publish src -c Release -r win-x64 --self-contained true -o installer/output/app
rm -f installer/output/app/*.pdb

echo "3/4 Publishing the Water Billing window (one .exe)..."
dotnet publish desktop/WaterBilling.Desktop -c Release -r win-x64 --self-contained true \
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o installer/output/desktop

echo "4/4 Signing the programs and compiling the installer..."
sign_define=()
if [ -n "${SIGN_PFX:-}" ]; then
  installer/sign.sh installer/output/app/WaterBilling.exe
  installer/sign.sh installer/output/desktop/WaterBillingApp.exe
  sign_define=(-DSIGN)
fi
(cd installer && makensis -V2 "${sign_define[@]}" WaterBilling.nsi)

# The publisher certificate and a one-click "trust" script go next to the setup (for a self-signed certificate).
if [ -n "${SIGN_PFX:-}" ]; then
  openssl pkcs12 -in "$SIGN_PFX" -passin env:SIGN_PFX_PASSWORD -nokeys -clcerts | openssl x509 -outform der -out installer/output/WaterBilling-Publisher.cer
  cp "installer/trust/Trust Water Billing publisher.cmd" installer/output/
fi

echo "Done: installer/output/WaterBillingSetup.exe  (give this one file to the client)"
