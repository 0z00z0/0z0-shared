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

$repoRoot   = Split-Path $PSScriptRoot -Parent   # scripts\ sits one level below the repository root
$harnessDir = Join-Path $repoRoot "src\ZeroZero.Brand.WinUI.TestHarness"
$outDir     = Join-Path $repoRoot "docs\screenshots"

# The harness csproj derives its RuntimeIdentifier from the running process architecture, so the
# output folder is win-x64 on x64 and win-arm64 on arm64. Locate the exe instead of assuming one.
function Resolve-HarnessExe {
    Get-ChildItem -Path (Join-Path $harnessDir "bin\Debug") -Filter "ZeroZero.Brand.WinUI.TestHarness.exe" `
                  -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}

$exePath = Resolve-HarnessExe
if (-not $exePath) {
    Write-Host "Building test harness..."
    dotnet build $harnessDir
    $exePath = Resolve-HarnessExe
    if (-not $exePath) { throw "Test harness exe not found under $harnessDir\bin\Debug." }
}

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class UpdateCapture {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> GetProcessWindows(uint pid) {
        var handles = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            uint windowPid;
            GetWindowThreadProcessId(hWnd, out windowPid);
            if (windowPid == pid && IsWindowVisible(hWnd) && GetWindowTextLength(hWnd) > 0) {
                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    public static string GetTitle(IntPtr hWnd) {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
'@
Add-Type -AssemblyName System.Drawing

# Per-monitor-v2 so GetWindowRect returns physical pixels and the capture is full-resolution and
# sharp. Without it every rectangle comes back in system-DPI coordinates and each capture is the
# top-left corner of the real window.
[UpdateCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Find-HarnessWindow([uint32]$processId, [string]$title, [int]$timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        foreach ($handle in [UpdateCapture]::GetProcessWindows($processId)) {
            if ([UpdateCapture]::GetTitle($handle) -eq $title) { return $handle }
        }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

function Test-Anchor([uint32]$processId) {
    $handle = Find-HarnessWindow $processId "White Anchor" 8000
    if ($handle -eq [IntPtr]::Zero) { return $false }

    $rect = New-Object UpdateCapture+RECT
    [UpdateCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $dc = [UpdateCapture]::GetDC([IntPtr]::Zero)
    $raw = [UpdateCapture]::GetPixel($dc, [int](($rect.Left + $rect.Right) / 2), [int](($rect.Top + $rect.Bottom) / 2))
    [UpdateCapture]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null

    return (($raw -band 0xFF) -eq 255) -and ((($raw -shr 8) -band 0xFF) -eq 255) -and ((($raw -shr 16) -band 0xFF) -eq 255)
}

function Save-Window([IntPtr]$handle, [string]$path) {
    $rect = New-Object UpdateCapture+RECT
    [UpdateCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width  = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    $ok  = [UpdateCapture]::PrintWindow($handle, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
    $gfx.ReleaseHdc($hdc)
    if (-not $ok) { $gfx.Dispose(); $bmp.Dispose(); throw "PrintWindow failed." }

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()
    return "$width x $height"
}

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
            if (-not (Test-Anchor ([uint32]$p.Id))) {
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
