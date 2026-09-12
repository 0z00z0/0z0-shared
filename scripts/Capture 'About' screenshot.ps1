# Launches the BrandAboutWindow test harness and captures WINDOW-ONLY screenshots of both hosting
# scenarios — the tray-app popup (BrandAboutWindow) and the hosted-control demo (BrandAboutControl
# embedded in a plain window, simulating an in-navigation About page) — to docs\screenshots\, under
# the filenames the brand guide embeds.
#
# One surface per run. The popup dismisses itself the moment it loses focus, so a second window
# opened beside it would take the focus and close it before anything could be captured; the harness
# opens the popup alone by default and the hosted demo under --hosted, and this script runs it
# twice.
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
public class AboutCapture {
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
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
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

    // Moved without being activated: focus has to stay on the window being captured, which closes
    // the instant it loses it.
    public static void Park(IntPtr h, int x, int y) {
        SetWindowPos(h, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
    }
}
'@
Add-Type -AssemblyName System.Drawing

# Per-monitor-v2 so GetWindowRect returns physical pixels and the capture is full-resolution and
# sharp. Without it every rectangle comes back in system-DPI coordinates and each capture is the
# top-left corner of the real window.
[AboutCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Find-HarnessWindow([uint32]$processId, [string]$title, [int]$timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        foreach ($handle in [AboutCapture]::GetProcessWindows($processId)) {
            if ([AboutCapture]::GetTitle($handle) -eq $title) { return $handle }
        }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

function Test-Anchor([uint32]$processId, $beside) {
    $handle = Find-HarnessWindow $processId "White Anchor" 8000
    if ($handle -eq [IntPtr]::Zero) { return $false }

    # Parked beside the captured window rather than in a screen corner, which another always-on-top
    # window can hold: a patch nothing can see says nothing about the screen.
    $x = $beside.Left - 260
    if ($x -lt 0) { $x = $beside.Right + 20 }
    [AboutCapture]::Park($handle, $x, $beside.Top)
    Start-Sleep -Milliseconds 400

    $rect = New-Object AboutCapture+RECT
    [AboutCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $dc = [AboutCapture]::GetDC([IntPtr]::Zero)
    $raw = [AboutCapture]::GetPixel($dc, [int](($rect.Left + $rect.Right) / 2), [int](($rect.Top + $rect.Bottom) / 2))
    [AboutCapture]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null

    return (($raw -band 0xFF) -eq 255) -and ((($raw -shr 8) -band 0xFF) -eq 255) -and ((($raw -shr 16) -band 0xFF) -eq 255)
}

function Save-Window([IntPtr]$handle, [string]$path) {
    $rect = New-Object AboutCapture+RECT
    [AboutCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width  = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    $ok  = [AboutCapture]::PrintWindow($handle, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
    $gfx.ReleaseHdc($hdc)
    if (-not $ok) { $gfx.Dispose(); $bmp.Dispose(); throw "PrintWindow failed." }

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()
    return "$width x $height"
}

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# The title the harness gives each surface, the switches that open it alone, and the file the brand
# guide embeds.
$surfaces = @(
    @{ Title = "Window Mode";         Args = @("--anchor");             File = "about-window.png" }
    @{ Title = "Hosted Control Demo"; Args = @("--anchor", "--hosted"); File = "about-hosted-control.png" }
)

foreach ($surface in $surfaces) {
    $saved = $false
    foreach ($attempt in 1..4) {
        $p = Start-Process -FilePath $exePath -ArgumentList $surface.Args -PassThru
        try {
            $handle = Find-HarnessWindow ([uint32]$p.Id) $surface.Title 12000
            if ($handle -eq [IntPtr]::Zero) { continue }

            Start-Sleep -Milliseconds 1200   # let the window finish rendering before capturing
            if (-not [AboutCapture]::IsWindow($handle)) {
                Write-Host "'$($surface.Title)' dismissed itself before the capture; retrying."
                continue
            }

            $rect = New-Object AboutCapture+RECT
            [AboutCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
            if (-not (Test-Anchor ([uint32]$p.Id) $rect)) {
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
