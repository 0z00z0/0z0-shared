# Builds (if needed) and launches the BrandAboutWindow test harness, so the shared About dialogue
# can be eyeballed on screen without building ChargeKeeper or HyperVManagerTray.

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "harness-capture.ps1")
$exePath = Get-HarnessExe

Start-Process -FilePath $exePath
