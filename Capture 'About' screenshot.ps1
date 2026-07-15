# Launches the BrandAboutWindow test harness and captures WINDOW-ONLY screenshots of both hosting
# scenarios it now opens — the tray-app popup (BrandAboutWindow) and the hosted-control demo
# (BrandAboutControl embedded in a plain window, simulating M365Migrator's in-nav About page) — to
# docs\screenshots\. The popup capture keeps the original "about-window.png" filename the README
# embeds; the hosted-control capture is new.
#
# Window-aware capture (PrintWindow + PW_RENDERFULLCONTENT) pulls each window's own composited
# bitmap straight from DWM — so the translucent Mica backdrop resolves cleanly and no desktop
# content bleeds through behind/around the dialog. A plain screen-region grab would capture
# whatever sits behind the window instead.
#
# The two windows are told apart by their AppWindow title (set in App.xaml.cs even though
# BrandAboutWindow hides its own title bar) rather than by process/creation order, which isn't a
# reliable indicator of on-screen (Z-order) enumeration order.

$ErrorActionPreference = "Stop"

$harnessDir = Join-Path $PSScriptRoot "src\ZeroZero.Brand.WinUI.TestHarness"
$exePath    = Join-Path $harnessDir "bin\Debug\net10.0-windows10.0.26100.0\win-x64\ZeroZero.Brand.WinUI.TestHarness.exe"
$outDir     = Join-Path $PSScriptRoot "docs\screenshots"

if (-not (Test-Path $exePath)) {
    Write-Host "Building test harness..."
    dotnet build $harnessDir
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
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> GetProcessWindows(uint pid) {
        var handles = new List<IntPtr>();
        EnumWindows((hWnd, lParam) => {
            GetWindowThreadProcessId(hWnd, out uint windowPid);
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

# Per-monitor-v2 so GetWindowRect returns physical pixels and the capture is full-resolution/sharp.
[AboutCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

# Maps a window's AppWindow title (set in App.xaml.cs) to the screenshot file it should produce.
# "about-window.png" is the original filename the README embeds — kept stable so that reference
# doesn't break as this script grows a second scenario.
$titleToFile = @{
    "Window Mode"        = "about-window.png"
    "Hosted Control Demo" = "about-hosted-control.png"
}

$p = Start-Process -FilePath $exePath -PassThru
try {
    $handles = @()
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 200
        $p.Refresh()
        $handles = [AboutCapture]::GetProcessWindows([uint32]$p.Id)
        if ($handles.Count -ge 2) { break }
    }
    if ($handles.Count -eq 0) { throw "Harness windows never appeared." }

    Start-Sleep -Milliseconds 800   # let the windows finish rendering before capturing

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    foreach ($hwnd in $handles) {
        $title = [AboutCapture]::GetTitle($hwnd)
        $fileName = $titleToFile[$title]
        if (-not $fileName) {
            $slug = ($title -replace '[^A-Za-z0-9]+', '-').Trim('-').ToLowerInvariant()
            if ([string]::IsNullOrEmpty($slug)) { $slug = "window" }
            $fileName = "about-$slug.png"
        }
        $outPath = Join-Path $outDir $fileName

        $rect = New-Object AboutCapture+RECT
        [AboutCapture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $w = $rect.Right - $rect.Left
        $h = $rect.Bottom - $rect.Top

        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $gfx = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $gfx.GetHdc()
        $ok  = [AboutCapture]::PrintWindow($hwnd, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
        $gfx.ReleaseHdc($hdc)
        if (-not $ok) { throw "PrintWindow failed for '$title'." }

        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $gfx.Dispose(); $bmp.Dispose()

        Write-Host "Saved '$title' screenshot ($w x $h) to $outPath"
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
}
