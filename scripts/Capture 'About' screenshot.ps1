# Launches the BrandAboutWindow test harness and captures WINDOW-ONLY screenshots of both hosting
# scenarios — the tray-app popup (BrandAboutWindow) and the hosted-control demo (BrandAboutControl
# embedded in a plain window, simulating an in-navigation About page) — in both themes, to
# docs\screenshots\, under the filenames the brand guide embeds.
#
# One surface and one theme per run. The popup dismisses itself the moment it loses focus, so a
# second window opened beside it would take the focus and close it before anything could be
# captured; the harness opens the popup alone by default and the hosted demo under --hosted, and
# this script runs it four times.
#
# For the same reason a capture is retried: anything on the machine that takes focus while the
# window settles closes it, and that is the window behaving correctly rather than a failure worth
# reporting.
#
# Window-aware capture (PrintWindow + PW_RENDERFULLCONTENT) pulls each window's own composited
# bitmap straight from DWM — so the translucent Mica backdrop resolves cleanly and no desktop
# content bleeds through behind or around the dialogue. A plain screen-region grab would capture
# whatever sits behind the window instead.
#
# Each capture is anchored. The harness opens a small pure-white window under --anchor, this script
# parks it beside the window being captured, and the pixel at its centre is read off the screen
# device context: anything but white means the screen was dimmed, faded or locked, and the capture
# is refused rather than filed. The reading goes through the device context because a bitmap copy of
# the screen returns black on some displays.

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "harness-capture.ps1")
$exePath = Get-HarnessExe

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# The title the harness gives each surface, the switches that open it alone, and the file the brand
# guide embeds. One theme per run: the popup dismisses itself the moment it loses focus, so two
# windows in one run would leave one of them closing before it could be captured.
$surfaces = foreach ($theme in @("Light", "Dark")) {
    @{ Title = "Window Mode";         Args = @("--anchor", "--theme", $theme);             File = "about-window-$($theme.ToLowerInvariant()).png" }
    @{ Title = "Hosted Control Demo"; Args = @("--anchor", "--hosted", "--theme", $theme); File = "about-hosted-control-$($theme.ToLowerInvariant()).png" }
}

foreach ($surface in $surfaces) {
    $saved = $false
    foreach ($attempt in 1..4) {
        $p = Start-Process -FilePath $exePath -ArgumentList $surface.Args -PassThru
        try {
            $handle = Find-HarnessWindow ([uint32]$p.Id) $surface.Title 12000
            if ($handle -eq [IntPtr]::Zero) { continue }

            Start-Sleep -Milliseconds 1200   # let the window finish rendering before capturing
            if (-not [HarnessCapture]::IsWindow($handle)) {
                Write-Host "'$($surface.Title)' dismissed itself before the capture; retrying."
                continue
            }

            $rect = New-Object HarnessCapture+RECT
            [HarnessCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
            if (-not (Test-WhiteAnchor ([uint32]$p.Id) $rect)) {
                Write-Host "The white anchor did not read white: the screen is dimmed or locked. Retrying."
                continue
            }

            $outPath = Join-Path $outDir $surface.File
            $size = Save-Window $handle $outPath
            Write-Host "Saved '$($surface.Title)' screenshot ($size) to $outPath"
            $saved = $true
        }
        finally {
            if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        }
        if ($saved) { break }
    }
    if (-not $saved) { throw "Could not capture '$($surface.Title)' in four attempts." }
}
