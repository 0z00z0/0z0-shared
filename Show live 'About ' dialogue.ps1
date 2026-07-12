# Builds (if needed) and launches the BrandAboutWindow test harness so you can eyeball the
# shared About dialog on screen without building ChargeKeeper or HyperVManagerTray.

$ErrorActionPreference = "Stop"

$harnessDir = Join-Path $PSScriptRoot "src\ZeroZero.Brand.WinUI.TestHarness"
$exePath    = Join-Path $harnessDir "bin\Debug\net10.0-windows10.0.26100.0\win-x64\ZeroZero.Brand.WinUI.TestHarness.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "Building test harness..."
    dotnet build $harnessDir
}

Start-Process -FilePath $exePath
