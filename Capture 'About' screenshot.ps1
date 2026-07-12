# Launches the BrandAboutWindow test harness and captures a WINDOW-ONLY screenshot of the dialog
# to docs\screenshots\about-window.png (the image the README embeds).
#
# Window-aware capture (PrintWindow + PW_RENDERFULLCONTENT) pulls the window's own composited
# bitmap straight from DWM — so the translucent Mica backdrop resolves cleanly and no desktop
# content bleeds through behind/around the dialog. A plain screen-region grab would capture
# whatever sits behind the window instead.

$ErrorActionPreference = "Stop"

$harnessDir = Join-Path $PSScriptRoot "src\ZeroZero.Brand.WinUI.TestHarness"
$exePath    = Join-Path $harnessDir "bin\Debug\net10.0-windows10.0.26100.0\win-x64\ZeroZero.Brand.WinUI.TestHarness.exe"
$outPath    = Join-Path $PSScriptRoot "docs\screenshots\about-window.png"

if (-not (Test-Path $exePath)) {
    Write-Host "Building test harness..."
    dotnet build $harnessDir
}

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class AboutCapture {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
Add-Type -AssemblyName System.Drawing

# Per-monitor-v2 so GetWindowRect returns physical pixels and the capture is full-resolution/sharp.
[AboutCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

$p = Start-Process -FilePath $exePath -PassThru
try {
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 30; $i++) {
        Start-Sleep -Milliseconds 200
        $p.Refresh()
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { $hwnd = $p.MainWindowHandle; break }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw "Harness window never appeared." }

    Start-Sleep -Milliseconds 800   # let the window finish rendering before capturing

    $rect = New-Object AboutCapture+RECT
    [AboutCapture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
    $w = $rect.Right - $rect.Left
    $h = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    $ok  = [AboutCapture]::PrintWindow($hwnd, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
    $gfx.ReleaseHdc($hdc)
    if (-not $ok) { throw "PrintWindow failed." }

    New-Item -ItemType Directory -Force -Path (Split-Path $outPath) | Out-Null
    $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()

    Write-Host "Saved window-only screenshot ($w x $h) to $outPath"
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
}
