# Builds the Windows installer WaterBillingSetup.exe. Run on a Windows PC from the repository root:
#     powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
# Needs: .NET 8 SDK, Inno Setup 6 (https://jrsoftware.org/isinfo.php) and internet once (WebView2 download).
$ErrorActionPreference = "Stop"

Write-Host "1/5 Running tests..."
dotnet test
if ($LASTEXITCODE -ne 0) { throw "Tests failed. No installer built." }

Write-Host "2/5 Publishing the service (self-contained, no .NET needed on the shop PC)..."
Remove-Item -Recurse -Force installer\output\app, installer\output\desktop -ErrorAction SilentlyContinue
dotnet publish src -c Release -r win-x64 --self-contained true -o installer\output\app
if ($LASTEXITCODE -ne 0) { throw "Publish of the service failed." }
Remove-Item installer\output\app\*.pdb -ErrorAction SilentlyContinue

Write-Host "3/5 Publishing the Water Billing window (one .exe)..."
dotnet publish desktop\WaterBilling.Desktop -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o installer\output\desktop
if ($LASTEXITCODE -ne 0) { throw "Publish of the window failed." }

Write-Host "4/5 Downloading the WebView2 Runtime installer (used only on PCs that do not have it)..."
$wv2 = "installer\output\MicrosoftEdgeWebview2Setup.exe"
try { Invoke-WebRequest "https://go.microsoft.com/fwlink/p/?LinkId=2124703" -OutFile $wv2 -UseBasicParsing }
catch { Write-Warning "Could not download WebView2 setup. The installer still works on Windows 11 and up-to-date Windows 10." }

Write-Host "5/5 Compiling installer..."
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { throw "Inno Setup 6 not found at $iscc" }
& $iscc installer\WaterBilling.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Write-Host "Done: installer\output\WaterBillingSetup.exe  (give this one file to the client)"
