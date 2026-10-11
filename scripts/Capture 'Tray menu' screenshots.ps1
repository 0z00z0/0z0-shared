# Launches the tray test harness once per theme and captures WINDOW-ONLY screenshots of the tray
# menu to docs\screenshots\, under the filenames the tray guide embeds.
#
# The menu is a native popup, not a XAML flyout: the notify-icon library renders the descriptor as
# one, and the host gives it a theme through the icon element the library draws it from. So the
# window captured is the popup-menu class, found by class name rather than by title, and the theme
# is pinned per run with --menu-theme rather than by moving the machine's own taskbar setting.
#
# The harness opens the menu itself two seconds after the icon appears, so the capture needs no
# synthetic click on the taskbar. Opening it blocks the harness's message loop until the menu
# closes, which is why the process is stopped rather than asked to exit.
#
# Window-aware capture (PrintWindow + PW_RENDERFULLCONTENT) pulls the window's own composited
# bitmap straight from DWM. A screen-region grab of the taskbar and what sits over it returns
# stale or empty content on this shell, measured.
#
# Each capture is anchored. The harness opens a small pure-white window under --anchor and the
# pixel at its centre is read off the screen device context: anything but white means the screen
# was dimmed, faded or locked, and the capture is refused rather than filed.

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "harness-capture.ps1")
$exePath = Get-HarnessExe

function Find-Menu([uint32]$processId, [int]$timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        foreach ($handle in [HarnessCapture]::ProcessWindows($processId, $false)) {
            if ([HarnessCapture]::ClassOf($handle) -ne "#32768") { continue }
            # A menu window exists before it is laid out, and its rectangle is empty until then.
            $rect = New-Object HarnessCapture+RECT
            [HarnessCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
            if ($rect.Right - $rect.Left -gt 0 -and $rect.Bottom - $rect.Top -gt 0) { return $handle }
        }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

foreach ($theme in @("Light", "Dark")) {
    $saved = $false
    foreach ($attempt in 1..4) {
        $p = Start-Process -FilePath $exePath -PassThru -ArgumentList @(
            "--tray", "--menu", "--anchor", "--menu-theme", $theme)
        try {
            $handle = Find-Menu ([uint32]$p.Id) 20000
            if ($handle -eq [IntPtr]::Zero) { continue }

            Start-Sleep -Milliseconds 600   # let the menu finish its open animation
            if (-not (Test-WhiteAnchor ([uint32]$p.Id))) {
                Write-Host "The white anchor did not read white: the screen is dimmed or locked. Retrying."
                continue
            }

            $outPath = Join-Path $outDir "tray-menu-$($theme.ToLowerInvariant()).png"
            $size = Save-Window $handle $outPath
            Write-Host "Saved the $theme tray menu ($size) to $outPath"
            $saved = $true
        }
        finally {
            # Stopped rather than asked to exit: the harness's message loop is inside the menu.
            if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        }
        if ($saved) { break }
    }
    if (-not $saved) { throw "Could not capture the $theme tray menu in four attempts." }
}
