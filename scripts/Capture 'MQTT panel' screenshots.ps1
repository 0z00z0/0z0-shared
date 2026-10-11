# Launches the test harness in its MQTT scenario and captures WINDOW-ONLY screenshots of the eight it
# opens — light and dark, each as the panel opens, each with the Broker group open, each with the
# publish list open, and each holding a staged edit behind a closed group — to docs\screenshots\.
#
# -Branded captures the other two instead: the same panel with a deliberately extreme studio palette
# declared in the application's own resources, which is what shows how far the module's theme keys
# reach. The colours are unlike anything the stock theme can produce, so every surface in the image
# is either obviously overridden or obviously not.
#
# Window-aware capture (PrintWindow + PW_RENDERFULLCONTENT) pulls each window's own composited bitmap
# straight from DWM, so nothing behind the window bleeds into the image. Each window is brought
# forward first: an occluded window's surface can be stale, and PW_RENDERFULLCONTENT re-renders more
# reliably for a window that is actually on top.
#
# The display is held awake and the desktop is checked for composition before anything is captured.
# DWM composes nothing while the display is off, so a capture taken then is uniformly black — and
# black is not obviously wrong until someone opens the file. The run is also anchored: the harness
# opens a pure-white window under --anchor, and anything but white read off the screen refuses it.

param([switch]$Branded)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "harness-capture.ps1")
$exePath = Get-HarnessExe

function Test-DesktopComposing {
    $bmp = New-Object System.Drawing.Bitmap 240, 240
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen(0, 0, 0, 0, (New-Object System.Drawing.Size 240, 240))
    $gfx.Dispose()
    $lit = $false
    for ($x = 0; $x -lt 240 -and -not $lit; $x += 8) {
        for ($y = 0; $y -lt 240 -and -not $lit; $y += 8) {
            $p = $bmp.GetPixel($x, $y)
            if ($p.R -gt 8 -or $p.G -gt 8 -or $p.B -gt 8) { $lit = $true }
        }
    }
    $bmp.Dispose()
    return $lit
}

[HarnessCapture]::MonitorOn()
[HarnessCapture]::SetThreadExecutionState(
    [HarnessCapture]::ES_CONTINUOUS -bor [HarnessCapture]::ES_DISPLAY_REQUIRED -bor [HarnessCapture]::ES_SYSTEM_REQUIRED) | Out-Null

$composing = $false
for ($i = 0; $i -lt 15; $i++) {
    if (Test-DesktopComposing) { $composing = $true; break }
    [HarnessCapture]::MonitorOn()
    Start-Sleep -Milliseconds 700
}
if (-not $composing) { throw "Desktop is not composing - a capture now would be black." }
Write-Host "Desktop is composing; display held awake."

# Maps a window's AppWindow title (set in App.xaml.cs) to the screenshot file it should produce.
# Anything not in the map is not part of this scenario and is skipped.
$titleToFile = if ($Branded) {
    @{
        "MQTT Panel Light Branded" = "mqtt-panel-light-branded.png"
        "MQTT Panel Dark Branded"  = "mqtt-panel-dark-branded.png"
    }
} else {
    @{
        "MQTT Panel Light"        = "mqtt-panel-light.png"
        "MQTT Panel Dark"         = "mqtt-panel-dark.png"
        "MQTT Panel Light Broker" = "mqtt-panel-light-broker.png"
        "MQTT Panel Dark Broker"  = "mqtt-panel-dark-broker.png"
        "MQTT Panel Light Groups" = "mqtt-panel-light-groups.png"
        "MQTT Panel Dark Groups"  = "mqtt-panel-dark-groups.png"
        "MQTT Panel Light Edited" = "mqtt-panel-light-edited.png"
        "MQTT Panel Dark Edited"  = "mqtt-panel-dark-edited.png"
    }
}

$harnessArgs = if ($Branded) { @("--mqtt", "--brand", "--anchor") } else { @("--mqtt", "--anchor") }
$p = Start-Process -FilePath $exePath -ArgumentList $harnessArgs -PassThru
try {
    $handles = @()
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        $handles = [HarnessCapture]::ProcessWindows([uint32]$p.Id, $true)
        $panels = @($handles | Where-Object { $titleToFile.ContainsKey([HarnessCapture]::TitleOf($_)) })
        if ($panels.Count -ge $titleToFile.Count) { break }
    }
    if ($handles.Count -eq 0) { throw "Harness windows never appeared." }

    Start-Sleep -Milliseconds 1200   # let the windows finish rendering before capturing
    if (-not (Test-WhiteAnchor ([uint32]$p.Id))) {
        throw "The white anchor did not read white, or the harness opened none: the screen is dimmed or locked."
    }

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    foreach ($hwnd in $handles) {
        $title = [HarnessCapture]::TitleOf($hwnd)
        $fileName = $titleToFile[$title]
        if (-not $fileName) { continue }
        $outPath = Join-Path $outDir $fileName

        [HarnessCapture]::SetForegroundWindow($hwnd) | Out-Null
        Start-Sleep -Milliseconds 500

        $size = Save-Window $hwnd $outPath
        Write-Host "Saved '$title' screenshot ($size) to $outPath"
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    [HarnessCapture]::SetThreadExecutionState([HarnessCapture]::ES_CONTINUOUS) | Out-Null
}
