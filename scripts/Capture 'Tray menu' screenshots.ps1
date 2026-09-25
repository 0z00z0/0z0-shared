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
public class MenuCapture {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static string ClassOf(IntPtr hWnd) {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string TitleOf(IntPtr hWnd) {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // The popup menu carries no title, so the title filter the other capture scripts use would
    // drop it.
    public static List<IntPtr> ProcessWindows(uint pid, bool titled) {
        var handles = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            uint windowPid;
            GetWindowThreadProcessId(hWnd, out windowPid);
            if (windowPid == pid && IsWindowVisible(hWnd) && (!titled || GetWindowTextLength(hWnd) > 0)) {
                handles.Add(hWnd);
            }
            return true;
        }, IntPtr.Zero);
        return handles;
    }
}
'@
Add-Type -AssemblyName System.Drawing

# Per-monitor-v2 so GetWindowRect returns physical pixels and the capture is full-resolution.
[MenuCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Find-Menu([uint32]$processId, [int]$timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        foreach ($handle in [MenuCapture]::ProcessWindows($processId, $false)) {
            if ([MenuCapture]::ClassOf($handle) -ne "#32768") { continue }
            # A menu window exists before it is laid out, and its rectangle is empty until then.
            $rect = New-Object MenuCapture+RECT
            [MenuCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
            if ($rect.Right - $rect.Left -gt 0 -and $rect.Bottom - $rect.Top -gt 0) { return $handle }
        }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

function Test-Anchor([uint32]$processId) {
    $handle = [IntPtr]::Zero
    foreach ($candidate in [MenuCapture]::ProcessWindows($processId, $true)) {
        if ([MenuCapture]::TitleOf($candidate) -eq "White Anchor") { $handle = $candidate }
    }
    if ($handle -eq [IntPtr]::Zero) { return $false }

    $rect = New-Object MenuCapture+RECT
    [MenuCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $dc = [MenuCapture]::GetDC([IntPtr]::Zero)
    $raw = [MenuCapture]::GetPixel($dc, [int](($rect.Left + $rect.Right) / 2), [int](($rect.Top + $rect.Bottom) / 2))
    [MenuCapture]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null

    return (($raw -band 0xFF) -eq 255) -and ((($raw -shr 8) -band 0xFF) -eq 255) -and ((($raw -shr 16) -band 0xFF) -eq 255)
}

function Save-Window([IntPtr]$handle, [string]$path) {
    $rect = New-Object MenuCapture+RECT
    [MenuCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width  = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    $ok  = [MenuCapture]::PrintWindow($handle, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
    $gfx.ReleaseHdc($hdc)
    if (-not $ok) { $gfx.Dispose(); $bmp.Dispose(); throw "PrintWindow failed." }

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()
    return "$width x $height"
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
            if (-not (Test-Anchor ([uint32]$p.Id))) {
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
