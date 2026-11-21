#!/usr/bin/env bash
# Builds the Windows installer installer/output/WaterBillingSetup.exe on Linux (or macOS / WSL).
# Run from the repository root:   bash installer/build-installer.sh
# Needs: .NET 8 SDK and NSIS 3 (Ubuntu: sudo apt install nsis). On Windows, build-installer.ps1 does the same with Inno Setup.
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

echo "4/4 Compiling the installer..."
(cd installer && makensis -V2 WaterBilling.nsi)

echo "Done: installer/output/WaterBillingSetup.exe  (give this one file to the client)"
