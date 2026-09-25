using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Win32;

namespace ZeroZero.Tray.WinUI;

/// <summary>
/// The tray icon as a service: created once on the UI thread with efficiency mode refused, kept
/// current through the taskbar's theme and display changes and the shell's restarts, its tooltip
/// held to the shell's limit, its clicks classified, and its menu rebuilt from the application's
/// descriptor each time it is about to open. The application supplies what the icon shows, what
/// the tooltip says, what the menu holds and what a click does, and nothing else.
/// </summary>
public sealed class TrayHost : IDisposable
{
    private readonly TrayHostOptions _options;
    private readonly TrayIconFileCache _cache;
    private TaskbarIcon? _icon;
    private System.Drawing.Icon? _loaded;
    private DispatcherQueue? _dispatcher;
    private TrayClickPolicy? _clicks;
    private TrayIconRequest? _request;
    private TrayIconPlacement? _placementReplaced;
    private bool _disposed;

    public TrayHost(TrayHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Name);
        ArgumentNullException.ThrowIfNull(options.Icon);
        _options = options;
        _cache = new TrayIconFileCache(options.CacheDirectory ?? Path.GetTempPath(), options.Name);
        Id = options.Id ?? TrayIcon.CreateUniqueGuidFromString(options.Name);
    }

    /// <summary>The identity handed to the notify-icon library: the one given, or the one derived
    /// from the name. Not necessarily the identity the shell ends up holding —
    /// <see cref="ShellId"/> is that one.</summary>
    public Guid Id { get; }

    /// <summary>
    /// The identity the shell holds the icon by, read from the library's own icon rather than
    /// worked out again; null before <see cref="Start"/>. It is <see cref="Id"/>, and the shell's
    /// per-icon settings are keyed on it.
    /// </summary>
    public Guid? ShellId => _icon?.TrayIcon.Id;

    /// <summary>Where the shell draws the icon now, as its own setting says; null before
    /// <see cref="Start"/>, and null while the shell keeps no entry for the icon, which is the
    /// case until it has drawn it at least once.</summary>
    public TrayIconPlacement? Placement => ShellId is { } id ? NotifyIconSettings.Read(id) : null;

    /// <summary>
    /// Asks the shell to draw the icon in the notification area rather than behind the overflow
    /// chevron, or the other way round: the setting is written, and the icon is then registered
    /// again, because the shell reads the setting when an icon is registered and not while one is
    /// up. Returns whether anything was written — false means the setting already reads as wanted,
    /// or the shell keeps no entry this icon alone picks out, and in neither case is the icon
    /// disturbed.
    /// </summary>
    /// <remarks>The setting is the person's own. Only the entry carrying this icon's identity is
    /// ever written, nothing is written when it already says what is wanted, and what the first
    /// write replaced is kept so <see cref="RestorePlacement"/> can put it back. On the UI thread,
    /// after <see cref="Start"/>.</remarks>
    public bool AskForPlacement(TrayIconPlacement wanted)
    {
        ThrowIfNotStarted();
        if (ShellId is not { } id) return false;

        var before = NotifyIconSettings.Read(id);
        if (!NotifyIconSettings.Write(id, wanted)) return false;

        _placementReplaced ??= before;
        Reregister();
        return true;
    }

    /// <summary>
    /// Puts back the placement this host first replaced, and registers the icon again. Returns
    /// whether anything was written; nothing was ever replaced, or the setting already reads that
    /// way, and nothing happens.
    /// </summary>
    public bool RestorePlacement()
    {
        ThrowIfNotStarted();
        if (ShellId is not { } id || _placementReplaced is not { } before) return false;
        if (!NotifyIconSettings.Write(id, before)) return false;

        _placementReplaced = null;
        Reregister();
        return true;
    }

    /// <summary>Raised on the UI thread when a refresh the host started on its own — after a
    /// theme, display or shell change — throws, which is the application's icon delegate or its
    /// tooltip composer failing. A refresh the application asked for throws to the caller.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Whether <see cref="Start"/> has run.</summary>
    public bool IsStarted => _icon is not null;

    /// <summary>Whether the shell currently holds the icon.</summary>
    public bool IsCreated => _icon?.IsCreated ?? false;

    /// <summary>The slot and theme the icon was last rendered for; null before <see cref="Start"/>.</summary>
    public TrayIconRequest? CurrentRequest => _request;

    /// <summary>Where a render is written.</summary>
    public string CachePath => _cache.Path;

    /// <summary>
    /// Creates the icon. On the UI thread, once, after the XAML runtime is up: the listeners are
    /// marshalled to the dispatcher queue of the thread that calls this.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon is not null) throw new InvalidOperationException("The host has already started.");
        _dispatcher = DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException("Start the host on the UI thread: its listeners are marshalled to that thread's dispatcher queue.");

        var doubleClick = TimeSpan.FromMilliseconds(Math.Max(1, NativeMethods.GetDoubleClickTime()));
        _clicks = new TrayClickPolicy(doubleClick, _options.ReopenGuard);

        var icon = new TaskbarIcon
        {
            // The name first and the identity after it, deliberately: the library derives an
            // identity of its own from the name and assigns it, so a name applied last overwrites
            // whatever identity was set before it and the shell never sees the one asked for.
            CustomName = _options.Name,
            Id = Id,
            // A left click is reported at once; whether it was the first half of a double click
            // is the policy's to say.
            NoLeftClickDelay = true,
            ContextMenuMode = ContextMenuMode.PopupMenu,
            MenuActivation = PopupActivationMode.RightClick,
        };
        _icon = icon;

        Render();
        ApplyTooltip();
        RebuildMenu();

        // The library's default arms efficiency mode for the whole process: idle priority class
        // and power throttling, applied here and never restored. Refused, and a test measures the
        // process afterwards rather than this argument.
        icon.ForceCreate(enablesEfficiencyMode: false);

        var window = icon.TrayIcon.MessageWindow;
        window.MouseEventReceived += OnMouseEvent;
        window.TaskbarCreated += OnTaskbarCreated;
        window.DpiChanged += OnDisplayChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    /// <summary>The state changed: the icon is asked for again and the tooltip recomposed. On the
    /// UI thread.</summary>
    public void Refresh()
    {
        ThrowIfNotStarted();
        Render();
        ApplyTooltip();
    }

    /// <summary>The tooltip recomposed for the current state. On the UI thread.</summary>
    public void RefreshTooltip()
    {
        ThrowIfNotStarted();
        ApplyTooltip();
    }

    /// <summary>The menu rebuilt from the descriptor now, ahead of the rebuild that precedes every
    /// opening. On the UI thread.</summary>
    public void RefreshMenu()
    {
        ThrowIfNotStarted();
        RebuildMenu();
    }

    /// <summary>The application's pop-out was just dismissed; a left click that arrives within the
    /// guard is the click that dismissed it and is dropped.</summary>
    public void NotePopOutDismissed() => _clicks?.NoteDismissed(Now);

    /// <summary>Opens the menu at a point on screen, in physical pixels, rebuilt from the
    /// descriptor first as it is before a right click. Returns once the menu has closed. On the
    /// UI thread.</summary>
    public void ShowMenu(int x, int y)
    {
        ThrowIfNotStarted();
        RebuildMenu();
        _icon!.ShowContextMenu(new System.Drawing.Point(x, y));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;

        if (_icon is { } icon)
        {
            if (icon.IsCreated)
            {
                var window = icon.TrayIcon.MessageWindow;
                window.MouseEventReceived -= OnMouseEvent;
                window.TaskbarCreated -= OnTaskbarCreated;
                window.DpiChanged -= OnDisplayChanged;
            }
            icon.Dispose();
            _icon = null;
        }

        _loaded?.Dispose();
        _loaded = null;
    }

    /// <summary>Removes the icon and adds it again, so the shell reads the per-icon settings
    /// afresh. The library keeps the icon's state, so what comes back is the same icon with the
    /// same tooltip and the same menu.</summary>
    private void Reregister()
    {
        if (_icon is not { IsCreated: true } icon) return;

        try
        {
            icon.TrayIcon.TryRemove();
            icon.TrayIcon.Create();
        }
        catch (InvalidOperationException ex)
        {
            Failed?.Invoke(this, ex);
        }
    }

    private static TimeSpan Now => TimeSpan.FromMilliseconds(Environment.TickCount64);

    private void ThrowIfNotStarted()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon is null) throw new InvalidOperationException("The host has not started.");
    }

    /// <summary>
    /// Draws the menu in the taskbar's own theme, or in the one the application pinned. The menu
    /// is a native popup rather than a XAML flyout, so its colours come from the process's menu
    /// mode and not from any property on the flyout or the icon (measured: a theme set on either
    /// leaves the menu light). The mode is process-wide and covers every Win32 menu the
    /// application shows.
    /// </summary>
    private void ApplyMenuTheme()
    {
        var mode = (_options.MenuTheme ?? ThemeForTaskbar()) switch
        {
            ElementTheme.Light => NativeMethods.PreferredAppMode.ForceLight,
            ElementTheme.Dark => NativeMethods.PreferredAppMode.ForceDark,
            // Default hands the menu back to the application, which is what Windows decides from
            // the apps theme on its own.
            _ => NativeMethods.PreferredAppMode.Default,
        };

        try
        {
            NativeMethods.SetPreferredAppMode(mode);
            // Without this the first menu's theme is cached for the process and every later one
            // opens in it, whatever the mode now says.
            NativeMethods.FlushMenuThemes();
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or DllNotFoundException)
        {
            // Before Windows 10 1903 there is no such export, and the menu then stays as Windows
            // draws it — which is what a host that never asked would have had.
        }
    }

    /// <summary>The element theme the taskbar is drawn in now. Its own registry value, not the
    /// apps one the application follows.</summary>
    private static ElementTheme ThemeForTaskbar() =>
        TaskbarThemes.Read() == TaskbarTheme.Light ? ElementTheme.Light : ElementTheme.Dark;

    private static TrayIconRequest ReadRequest()
    {
        var theme = TaskbarThemes.Read();
        return new TrayIconRequest(TrayIconSlot.PixelsForTaskbar(), theme, TaskbarThemes.StrokeToneFor(theme));
    }

    /// <summary>Asks the application for the icon at the current slot and theme and hands the
    /// shell the frame of the slot's own size, so nothing is resampled.</summary>
    private void Render()
    {
        var request = ReadRequest();
        _request = request;

        TrayIconImage image = _options.Icon(request);
        var (path, changed) = _cache.Resolve(image);
        if (!changed && _loaded is not null)
        {
            Push();
            return;
        }

        var previous = _loaded;
        // The file's bytes are read into the handle here; a later overwrite of the cache file does
        // not reach an icon already loaded.
        _loaded = new System.Drawing.Icon(path, request.SlotPixels, request.SlotPixels);
        // The library keeps the object: it re-adds this handle after a shell restart.
        _icon!.Icon = _loaded;
        previous?.Dispose();
    }

    /// <summary>The request re-read after a theme, display or shell change: a change is a new
    /// render, and no change is a push of the current icon, which repairs an icon the shell
    /// dropped without saying so.</summary>
    private void Reconcile()
    {
        if (ReadRequest() != _request) Render();
        else Push();
    }

    /// <summary>Puts the current icon to the shell again. A refusal means the shell no longer
    /// holds the icon and sent no TaskbarCreated, so it is added afresh from the state the
    /// library still holds.</summary>
    private void Push()
    {
        if (_icon is null || _loaded is null || !_icon.IsCreated) return;
        if (_icon.TrayIcon.UpdateIcon(_loaded.Handle)) return;

        try
        {
            _icon.TrayIcon.TryRemove();
            _icon.TrayIcon.Create();
        }
        catch (InvalidOperationException ex)
        {
            Failed?.Invoke(this, ex);
        }
    }

    private void ApplyTooltip()
    {
        if (_icon is null) return;
        string text = _options.Tooltip is { } lines ? TrayTooltip.Compose(lines()) : "";
        if (!string.Equals(_icon.ToolTipText, text, StringComparison.Ordinal)) _icon.ToolTipText = text;
    }

    private void RebuildMenu()
    {
        if (_icon is null || _options.Menu is null) return;

        ApplyMenuTheme();

        var flyout = new MenuFlyout();
        foreach (var item in _options.Menu())
        {
            if (item.IsSeparator)
            {
                flyout.Items.Add(new MenuFlyoutSeparator());
                continue;
            }

            var command = new TrayMenuCommand(item.Invoke, item.IsEnabled);
            if (item.IsChecked is { } isChecked)
                flyout.Items.Add(new ToggleMenuFlyoutItem { Text = item.Text, IsChecked = isChecked, IsEnabled = item.IsEnabled, Command = command });
            else
                flyout.Items.Add(new MenuFlyoutItem { Text = item.Text, IsEnabled = item.IsEnabled, Command = command });
        }
        _icon.ContextFlyout = flyout;
    }

    private void OnMouseEvent(object? sender, MessageWindow.MouseEventReceivedEventArgs e)
    {
        if (_disposed || _clicks is null) return;

        switch (e.MouseEvent)
        {
            case MouseEvent.IconRightMouseDown:
                // Ahead of the mouse-up the library opens the menu on, so what opens is current.
                Guarded(RebuildMenu);
                break;
            case MouseEvent.IconLeftMouseUp:
                if (_clicks.OnLeftUp(Now) == TrayClick.Left) _options.LeftClick?.Invoke();
                break;
            case MouseEvent.IconLeftDoubleClick:
            case MouseEvent.IconDoubleClick:
                if (_clicks.OnDoubleClick(Now) == TrayClick.Double) _options.DoubleClick?.Invoke();
                break;
        }
    }

    // The library re-adds the icon in its own handler first; this one runs after it and brings
    // the render and the tooltip up to date with whatever the new taskbar is.
    private void OnTaskbarCreated(object? sender, EventArgs e) => Enqueue(() =>
    {
        Reconcile();
        ApplyTooltip();
    });

    private void OnDisplayChanged(object? sender, EventArgs e) => Enqueue(Reconcile);

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // The taskbar's theme arrives as a general or colour preference change; the rest is noise.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color) Enqueue(Reconcile);
    }

    private void Enqueue(Action action)
    {
        if (_disposed || _dispatcher is null) return;
        _dispatcher.TryEnqueue(() =>
        {
            if (!_disposed) Guarded(action);
        });
    }

    private void Guarded(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex);
        }
    }
}
