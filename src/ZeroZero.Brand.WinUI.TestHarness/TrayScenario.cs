using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using ZeroZero.Tray;
using ZeroZero.Tray.WinUI;
using ZeroZero.Win32;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// The tray host on screen, from an icon, a tooltip and a menu of the rig's own: a ring in the
/// stroke tone the taskbar needs with a filled sector that grows with every click, a tooltip
/// whose third line is long enough for the discipline to cut before its suffix and whose last
/// line repeats the first, and a menu with a command, a toggle, a disabled entry and Exit. With a
/// probe path the rig records what the host created and stays until a stop file appears beside
/// the probe, so a test can measure the process from outside while the icon is up.
/// </summary>
internal sealed class TrayScenario : IDisposable
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);

    /// <summary>The icon's identity, fixed so the rig can find its own entry in the shell's
    /// per-icon settings.</summary>
    private static readonly Guid Id = new("5D1F3B62-7A4C-4E0B-9C2D-A1B2C3D4E5F6");

    private readonly TrayHost _host;
    private readonly string? _probePath;
    private readonly bool _ownFile;
    private readonly bool _openMenu;
    private readonly bool _promote;
    private readonly Action _exit;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "ZeroZero.Tray.Harness");
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private DispatcherQueueTimer? _stopTimer;
    private DispatcherQueueTimer? _menuTimer;
    private string? _promotedKey;
    private int _clicks;
    private bool _paused;
    private bool _disposed;

    public TrayScenario(string? probePath, bool ownFile, bool openMenu, bool promote, Action exit)
    {
        _probePath = probePath;
        _ownFile = ownFile;
        _openMenu = openMenu;
        _promote = promote;
        _exit = exit;
        _host = new TrayHost(new TrayHostOptions
        {
            Name = "ZeroZero Tray Harness",
            Id = Id,
            Icon = Render,
            Tooltip = TooltipLines,
            Menu = MenuItems,
            LeftClick = () => Note("left"),
            DoubleClick = () => Note("double"),
            CacheDirectory = _directory,
        });
        _host.Failed += (_, ex) =>
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "tray-harness-failed.txt"), ex + Environment.NewLine);
    }

    public void Start()
    {
        _host.Start();
        if (_promote) Promote();

        if (_openMenu)
        {
            // The menu where a right click on the icon would put it, opened by the host itself so
            // a capture needs no synthetic input on the taskbar.
            _menuTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _menuTimer.Interval = TimeSpan.FromSeconds(2);
            _menuTimer.IsRepeating = false;
            _menuTimer.Tick += (_, _) =>
            {
                var area = MonitorMetrics.PrimaryWorkArea();
                _host.ShowMenu(area.Right - 320, area.Bottom - 8);
            };
            _menuTimer.Start();
        }

        if (_probePath is null) return;

        WriteProbe();

        // Held in a field: a local timer is unrooted and can be collected before it ever ticks.
        _stopTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _stopTimer.Interval = TimeSpan.FromMilliseconds(250);
        _stopTimer.IsRepeating = true;
        _stopTimer.Tick += (_, _) =>
        {
            if (!File.Exists(_probePath + ".stop") && DateTimeOffset.UtcNow - _startedAt < Lifetime) return;
            _stopTimer.Stop();
            Dispose();
            _exit();
        };
        _stopTimer.Start();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Dispose();
        Unpromote();
    }

    private const string NotifyIconSettings = @"Control Panel\NotifyIconSettings";

    /// <summary>Puts the rig's icon in the taskbar proper rather than the overflow, through the
    /// shell's own per-icon setting: the entry for this executable gets IsPromoted, the value the
    /// taskbar settings page writes. The shell keeps one entry per executable, written when it
    /// first sees the executable and carrying the identity it saw then, so the match is on the
    /// path. Undone on exit.</summary>
    private void Promote()
    {
        using var settings = Registry.CurrentUser.OpenSubKey(NotifyIconSettings, writable: true);
        if (settings is null) return;

        foreach (string name in settings.GetSubKeyNames())
        {
            using var entry = settings.OpenSubKey(name, writable: true);
            if (entry is null) continue;
            if (!string.Equals(entry.GetValue("ExecutablePath") as string, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) continue;

            entry.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
            _promotedKey = name;
            return;
        }
    }

    private void Unpromote()
    {
        if (_promotedKey is null) return;
        using var entry = Registry.CurrentUser.OpenSubKey(NotifyIconSettings + "\\" + _promotedKey, writable: true);
        entry?.DeleteValue("IsPromoted", throwOnMissingValue: false);
        _promotedKey = null;
    }

    /// <summary>Where the shell put the icon, asked of the shell by the icon's identity: its
    /// rectangle in physical pixels, and whether that lies within the taskbar window, which is
    /// what tells an icon in the taskbar proper from one in the overflow.</summary>
    private string Placement()
    {
        var identifier = new NOTIFYICONIDENTIFIER { cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(), guidItem = _host.Id };
        int result = Shell_NotifyIconGetRect(ref identifier, out RECT rect);
        if (result != 0) return $"unknown (0x{result:X8})";

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        bool inTaskbar = taskbar != IntPtr.Zero && GetWindowRect(taskbar, out RECT bar)
            && rect.Left >= bar.Left && rect.Top >= bar.Top && rect.Right <= bar.Right && rect.Bottom <= bar.Bottom;
        return $"{rect.Left},{rect.Top},{rect.Right},{rect.Bottom} {(inTaskbar ? "taskbar" : "overflow")}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("shell32.dll")]
    private static extern int Shell_NotifyIconGetRect(ref NOTIFYICONIDENTIFIER identifier, out RECT rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out RECT rect);

    /// <summary>What the host created, as tab-separated pairs, written whole and then moved into
    /// place so a reader never sees half a file.</summary>
    private void WriteProbe()
    {
        var request = _host.CurrentRequest!;
        string[] lines =
        [
            $"created\t{_host.IsCreated}",
            $"id\t{_host.Id.ToString("B").ToUpperInvariant()}",
            $"pid\t{Environment.ProcessId.ToString(CultureInfo.InvariantCulture)}",
            $"slot\t{request.SlotPixels.ToString(CultureInfo.InvariantCulture)}",
            $"theme\t{request.Theme}",
            $"icon\t{(_ownFile ? OwnFilePath : _host.CachePath)}",
            $"placement\t{Placement()}",
        ];
        string staging = _probePath + ".tmp";
        File.WriteAllLines(staging, lines);
        File.Move(staging, _probePath!, overwrite: true);
    }

    private void Note(string kind)
    {
        _clicks++;
        if (_probePath is not null)
            File.AppendAllText(_probePath + ".clicks", $"{kind}\t{_clicks.ToString(CultureInfo.InvariantCulture)}{Environment.NewLine}");
        _host.Refresh();
    }

    private string OwnFilePath => Path.Combine(_directory, "harness-own.ico");

    /// <summary>The rig's own drawing: with <c>--file</c>, one file written once and handed over
    /// by path, the way an application with static per-state icons works; otherwise the frames
    /// of a render for this request, the way an application that draws its state works.</summary>
    private TrayIconImage Render(TrayIconRequest request)
    {
        byte[][] frames = request.SlotPixels == 32
            ? [Frame(request.SlotPixels, request.StrokeTone)]
            : [Frame(request.SlotPixels, request.StrokeTone), Frame(32, request.StrokeTone)];

        if (!_ownFile) return TrayIconImage.FromFrames(frames);

        Directory.CreateDirectory(_directory);
        if (!File.Exists(OwnFilePath))
        {
            using var file = File.Create(OwnFilePath);
            IcoFile.Write(file, frames);
        }
        return TrayIconImage.FromFile(OwnFilePath);
    }

    private byte[] Frame(int size, StrokeTone tone)
    {
        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        float stroke = Math.Max(1.5f, size / 8f);
        var ring = new RectangleF(stroke / 2, stroke / 2, size - stroke, size - stroke);
        var inner = RectangleF.Inflate(ring, -stroke * 1.5f, -stroke * 1.5f);

        // A sector of the inner disc grows with the click count and turns red when paused, so a
        // state change is a visible change of the icon.
        using var fill = new SolidBrush(_paused ? Color.FromArgb(0xE0, 0x40, 0x30) : Color.FromArgb(0x2E, 0x8B, 0x57));
        graphics.FillPie(fill, inner.X, inner.Y, inner.Width, inner.Height, -90f, 45f * (_clicks % 8 + 1));

        using var pen = new Pen(tone == StrokeTone.Light ? Color.White : Color.Black, stroke);
        graphics.DrawEllipse(pen, ring);

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private IEnumerable<TrayTooltipLine> TooltipLines() =>
    [
        new("ZeroZero Tray Harness"),
        new($"Clicks: {_clicks.ToString(CultureInfo.InvariantCulture)}"),
        new("A device name long enough to be cut by the discipline, so the suffix after the ellipsis is what to look for", " · 84 %"),
        new(_paused ? "Paused" : "Running"),
        new("ZeroZero Tray Harness"),
    ];

    private IEnumerable<TrayMenuItem> MenuItems() =>
    [
        TrayMenuItem.Command("Refresh icon", () => Note("menu")),
        TrayMenuItem.Toggle("Paused", _paused, () =>
        {
            _paused = !_paused;
            _host.Refresh();
        }),
        TrayMenuItem.Command("A disabled entry", null, isEnabled: false),
        TrayMenuItem.Separator(),
        TrayMenuItem.Command("Exit", () =>
        {
            Dispose();
            _exit();
        }),
    ];
}
