# Launches the update-window test harness once per stage and captures WINDOW-ONLY screenshots of
# both themes to docs\screenshots\, under the filenames the update guide embeds.
#
# One stage per run. The window shows one stage at a time by design — the question, the download,
# the bar full while the hash and the signature are checked, a refusal, a failure, the notice that
# nothing newer exists, and a check that did not complete — so a picture of each costs a run. Both
# themes open side by side within a run, and the harness parks them after the last stage change,
# because every stage change recentres the window on the monitor it opened on.
#
# Window-aware capture (PrintWindow + PW_RENDERFULLCONTENT) pulls each window's own composited
# bitmap straight from DWM, so the translucent Mica backdrop resolves cleanly and no desktop
# content bleeds through behind or around it. A plain screen-region grab would capture whatever
# sits behind the window instead.
#
# Each capture is anchored. The harness opens a small pure-white window under --anchor and the
# pixel at its centre is read off the screen device context: anything but white means the screen
# was dimmed, faded or locked, and the capture is refused rather than filed. The reading goes
# through the device context because a bitmap copy of the screen returns black on some displays.

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "harness-capture.ps1")
$exePath = Get-HarnessExe

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# The stage the harness is asked for, and the name the pictures are filed under.
$stages = @("question", "download", "verifying", "refusal", "failure", "uptodate", "check-failed")

foreach ($stage in $stages) {
    $saved = $false
    foreach ($attempt in 1..3) {
        $p = Start-Process -FilePath $exePath -ArgumentList @("--update", "--stage", $stage, "--anchor") -PassThru
        try {
            $handles = @{}
            foreach ($theme in @("Light", "Dark")) {
                $handles[$theme] = Find-HarnessWindow ([uint32]$p.Id) "Update $theme $stage" 12000
            }
            if ($handles.Values -contains [IntPtr]::Zero) { continue }

            Start-Sleep -Milliseconds 1200   # let both windows finish rendering before capturing
            if (-not (Test-WhiteAnchor ([uint32]$p.Id))) {
                Write-Host "The white anchor did not read white: the screen is dimmed or locked. Retrying."
                continue
            }

            foreach ($theme in @("Light", "Dark")) {
                $outPath = Join-Path $outDir "update-$stage-$($theme.ToLowerInvariant()).png"
                $size = Save-Window $handles[$theme] $outPath
                Write-Host "Saved '$stage' $theme ($size) to $outPath"
            }
            $saved = $true
        }
        finally {
            if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
        }
        if ($saved) { break }
    }
    if (-not $saved) { throw "Could not capture the '$stage' stage in three attempts." }
}
