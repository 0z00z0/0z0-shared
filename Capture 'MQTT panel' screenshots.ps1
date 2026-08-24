# Launches the test harness in its MQTT scenario and captures WINDOW-ONLY screenshots of the eight it
# opens — light and dark, each as the panel opens, each with the Broker group open, each with the
# publish list open, and each holding a staged edit behind a closed group — to docs\screenshots\.
#
# Window-aware capture (PrintWindow + PW_RENDERFULLCONTENT) pulls each window's own composited bitmap
# straight from DWM, so nothing behind the window bleeds into the image. Each window is brought
# forward first: an occluded window's surface can be stale, and PW_RENDERFULLCONTENT re-renders more
# reliably for a window that is actually on top.
#
# The display is held awake and the desktop is checked for composition before anything is captured.
# DWM composes nothing while the display is off, so a capture taken then is uniformly black — and
# black is not obviously wrong until someone opens the file.

$ErrorActionPreference = "Stop"

$harnessDir = Join-Path $PSScriptRoot "src\ZeroZero.Brand.WinUI.TestHarness"
$outDir     = Join-Path $PSScriptRoot "docs\screenshots"

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
public class PanelCapture {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, IntPtr e);
    [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint esFlags);
    public struct RECT { public int Left, Top, Right, Bottom; }

    public const uint ES_CONTINUOUS = 0x80000000, ES_DISPLAY_REQUIRED = 0x00000002, ES_SYSTEM_REQUIRED = 0x00000001;

    public static void MonitorOn() {
        IntPtr r;
        // WM_SYSCOMMAND / SC_MONITORPOWER / -1 = on, then a mouse nudge so an already-blanked
        // display powers back up rather than only being told not to blank again.
        SendMessageTimeout((IntPtr)0xffff, 0x0112, (IntPtr)0xF170, (IntPtr)(-1), 0x0002, 1000, out r);
        mouse_event(0x0001, 1, 0, 0, IntPtr.Zero);
        mouse_event(0x0001, -1, 0, 0, IntPtr.Zero);
    }

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

# Per-monitor-v2 so GetWindowRect returns physical pixels and the capture is full-resolution/sharp.
[PanelCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

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

[PanelCapture]::MonitorOn()
[PanelCapture]::SetThreadExecutionState(
    [PanelCapture]::ES_CONTINUOUS -bor [PanelCapture]::ES_DISPLAY_REQUIRED -bor [PanelCapture]::ES_SYSTEM_REQUIRED) | Out-Null

$composing = $false
for ($i = 0; $i -lt 15; $i++) {
    if (Test-DesktopComposing) { $composing = $true; break }
    [PanelCapture]::MonitorOn()
    Start-Sleep -Milliseconds 700
}
if (-not $composing) { throw "Desktop is not composing - a capture now would be black." }
Write-Host "Desktop is composing; display held awake."

# Maps a window's AppWindow title (set in App.xaml.cs) to the screenshot file it should produce.
# Anything not in the map is not part of this scenario and is skipped.
$titleToFile = @{
    "MQTT Panel Light"        = "mqtt-panel-light.png"
    "MQTT Panel Dark"         = "mqtt-panel-dark.png"
    "MQTT Panel Light Broker" = "mqtt-panel-light-broker.png"
    "MQTT Panel Dark Broker"  = "mqtt-panel-dark-broker.png"
    "MQTT Panel Light Groups" = "mqtt-panel-light-groups.png"
    "MQTT Panel Dark Groups"  = "mqtt-panel-dark-groups.png"
    "MQTT Panel Light Edited" = "mqtt-panel-light-edited.png"
    "MQTT Panel Dark Edited"  = "mqtt-panel-dark-edited.png"
}

$p = Start-Process -FilePath $exePath -ArgumentList "--mqtt" -PassThru
try {
    $handles = @()
    for ($i = 0; $i -lt 40; $i++) {
        Start-Sleep -Milliseconds 250
        $p.Refresh()
        $handles = [PanelCapture]::GetProcessWindows([uint32]$p.Id)
        if ($handles.Count -ge $titleToFile.Count) { break }
    }
    if ($handles.Count -eq 0) { throw "Harness windows never appeared." }

    Start-Sleep -Milliseconds 1200   # let the windows finish rendering before capturing

    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    foreach ($hwnd in $handles) {
        $title = [PanelCapture]::GetTitle($hwnd)
        $fileName = $titleToFile[$title]
        if (-not $fileName) { continue }
        $outPath = Join-Path $outDir $fileName

        [PanelCapture]::SetForegroundWindow($hwnd) | Out-Null
        Start-Sleep -Milliseconds 500

        $rect = New-Object PanelCapture+RECT
        [PanelCapture]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
        $w = $rect.Right - $rect.Left
        $h = $rect.Bottom - $rect.Top

        $bmp = New-Object System.Drawing.Bitmap $w, $h
        $gfx = [System.Drawing.Graphics]::FromImage($bmp)
        $hdc = $gfx.GetHdc()
        $ok  = [PanelCapture]::PrintWindow($hwnd, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
        $gfx.ReleaseHdc($hdc)
        if (-not $ok) { throw "PrintWindow failed for '$title'." }

        $bmp.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $gfx.Dispose(); $bmp.Dispose()

        Write-Host "Saved '$title' screenshot ($w x $h) to $outPath"
    }
}
finally {
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force }
    [PanelCapture]::SetThreadExecutionState([PanelCapture]::ES_CONTINUOUS) | Out-Null
}
