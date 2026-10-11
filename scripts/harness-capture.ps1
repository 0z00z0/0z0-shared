# Shared by the capture scripts beside it; dot-source it, never run it. It defines the harness
# lookup, the window calls, the white-anchor test and the window-only save, and loads one type.

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

function Get-HarnessExe {
    $exePath = Resolve-HarnessExe
    if (-not $exePath) {
        Write-Host "Building test harness..."
        dotnet build $harnessDir | Out-Host
        $exePath = Resolve-HarnessExe
        if (-not $exePath) { throw "Test harness exe not found under $harnessDir\bin\Debug." }
    }
    return $exePath
}

if (-not ('HarnessCapture' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public class HarnessCapture {
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int count);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern IntPtr SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern uint GetPixel(IntPtr dc, int x, int y);
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

    // A popup menu carries no title, so the tray capture asks for untitled windows as well.
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

    public static string TitleOf(IntPtr hWnd) {
        var sb = new StringBuilder(256);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassOf(IntPtr hWnd) {
        var sb = new StringBuilder(256);
        GetClassName(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // Moved without being activated: a popup being captured closes the instant it loses focus.
    public static void Park(IntPtr h, int x, int y) {
        SetWindowPos(h, new IntPtr(-1), x, y, 0, 0, 0x0001 | 0x0010);
    }
}
'@
}
Add-Type -AssemblyName System.Drawing

# Per-monitor-v2 so GetWindowRect returns physical pixels. Without it every rectangle comes back in
# system-DPI coordinates and each capture is the top-left corner of the real window.
[HarnessCapture]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null

function Find-HarnessWindow([uint32]$processId, [string]$title, [int]$timeoutMs) {
    $deadline = (Get-Date).AddMilliseconds($timeoutMs)
    while ((Get-Date) -lt $deadline) {
        foreach ($handle in [HarnessCapture]::ProcessWindows($processId, $true)) {
            if ([HarnessCapture]::TitleOf($handle) -eq $title) { return $handle }
        }
        Start-Sleep -Milliseconds 150
    }
    return [IntPtr]::Zero
}

# Whether the harness's --anchor window reads pure white off the screen device context; anything
# else means a dimmed, faded or locked screen. A bitmap copy of the screen is black on some displays.
function Test-WhiteAnchor([uint32]$processId, $beside = $null) {
    $handle = Find-HarnessWindow $processId "White Anchor" 8000
    if ($handle -eq [IntPtr]::Zero) { return $false }

    if ($null -ne $beside) {
        # Beside the captured window rather than in a corner another always-on-top window can hold.
        $x = $beside.Left - 260
        if ($x -lt 0) { $x = $beside.Right + 20 }
        [HarnessCapture]::Park($handle, $x, $beside.Top)
        Start-Sleep -Milliseconds 400
    }

    $rect = New-Object HarnessCapture+RECT
    [HarnessCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $dc = [HarnessCapture]::GetDC([IntPtr]::Zero)
    $raw = [HarnessCapture]::GetPixel($dc, [int](($rect.Left + $rect.Right) / 2), [int](($rect.Top + $rect.Bottom) / 2))
    [HarnessCapture]::ReleaseDC([IntPtr]::Zero, $dc) | Out-Null

    return (($raw -band 0xFF) -eq 255) -and ((($raw -shr 8) -band 0xFF) -eq 255) -and ((($raw -shr 16) -band 0xFF) -eq 255)
}

# PrintWindow with PW_RENDERFULLCONTENT takes the window's own composited bitmap from DWM, so
# nothing behind the window bleeds into the image.
function Save-Window([IntPtr]$handle, [string]$path) {
    $rect = New-Object HarnessCapture+RECT
    [HarnessCapture]::GetWindowRect($handle, [ref]$rect) | Out-Null
    $width  = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top

    $bmp = New-Object System.Drawing.Bitmap $width, $height
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $gfx.GetHdc()
    $ok  = [HarnessCapture]::PrintWindow($handle, $hdc, 2)   # 2 = PW_RENDERFULLCONTENT
    $gfx.ReleaseHdc($hdc)
    if (-not $ok) { $gfx.Dispose(); $bmp.Dispose(); throw "PrintWindow failed." }

    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $gfx.Dispose(); $bmp.Dispose()
    return "$width x $height"
}
