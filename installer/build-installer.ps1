# Builds the Windows installer. Run on a Windows PC from the repository root:
#     powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1
# Needs: .NET 8 SDK, and Inno Setup 6 (https://jrsoftware.org/isinfo.php).
$ErrorActionPreference = "Stop"

Write-Host "1/3 Running tests..."
dotnet test
if ($LASTEXITCODE -ne 0) { throw "Tests failed. No installer built." }

Write-Host "2/3 Publishing self-contained Windows build..."
Remove-Item -Recurse -Force installer\output\app -ErrorAction SilentlyContinue
dotnet publish src -c Release -r win-x64 --self-contained true -o installer\output\app
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
Remove-Item installer\output\app\*.pdb -ErrorAction SilentlyContinue

Write-Host "3/3 Compiling installer..."
$iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { throw "Inno Setup 6 not found at $iscc" }
& $iscc installer\WaterBilling.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

Write-Host "Done: installer\output\WaterBillingSetup.exe"
