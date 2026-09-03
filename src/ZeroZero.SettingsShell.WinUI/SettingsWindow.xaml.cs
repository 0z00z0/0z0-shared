using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;
using ZeroZero.Controls.WinUI;
using ZeroZero.Win32;

namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// The settings window both applications converged on, with every page left to the
/// application. Mica chrome with the title bar painted for the theme, a navigation pane with a
/// product footer, one scroll viewer over the pages, placement against the application's saved
/// rectangle, and Escape to close. Each section is a <see cref="SettingsSection"/>: the shell
/// builds its page, shows and hides it, and calls the enter and leave hooks around every change.
/// </summary>
/// <remarks>
/// <para>Every page is built as the window opens and stays in the window hidden while another is
/// current, so a page that leaves the screen keeps its state — a staged edit, an open group, a
/// scroll position — and comes back as it was. <see cref="Rebuild()"/> is the one thing that
/// discards a page, and it leaves a build-once section alone.</para>
/// <para>The window is a singleton the application manages: it opens one, hands out
/// <see cref="Navigate"/> to whatever wants a section, and lets go of it on <see cref="Window.Closed"/>.
/// A page's teardown that must run whichever section is current — cancelling a probe on a panel
/// that is not on screen — goes on that same event, which the application subscribes to itself.</para>
/// </remarks>
public sealed partial class SettingsWindow : Window, ISectionHost<UIElement>
{
    private readonly SettingsWindowSetup _setup;
    private readonly SectionLifecycle<UIElement> _lifecycle;
    private readonly Dictionary<string, NavigationViewItem> _items = new(StringComparer.Ordinal);
    private readonly OverlappedPresenter _presenter;
    private readonly AppWindow _appWindow;

    // Selection changes arrive while the pane is being filled, before any page exists; nothing
    // is dispatched until the constructor says so.
    private bool _ready;
    private bool _fitPending;

    // The rectangle to remember: the last one seen while the window was neither maximised nor
    // minimised, so closing from either state saves the geometry the user last chose.
    private WindowRect? _restoredRect;

    public SettingsWindow(SettingsWindowSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(setup.Sections);
        _setup = setup;
        InitializeComponent();

        Title = setup.Title;
        Root.RequestedTheme = setup.Theme;
        ApplyLayout(setup);
        ApplyFooter(setup);

        _lifecycle = new SectionLifecycle<UIElement>(
            setup.Sections.Select(s => new SectionPlan<UIElement>(s.Tag, s.Build, s.Enter, s.Leave, s.BuildOnce)).ToArray(),
            this);
        foreach (var section in setup.Sections)
        {
            var item = new NavigationViewItem { Content = section.Label, Tag = section.Tag };
            if (section.Icon is { } icon) item.Icon = IconFor(icon);
            _items[section.Tag] = item;
            Navigation.MenuItems.Add(item);
        }
        _lifecycle.BuildAll();

        _appWindow = AppWindow;
        _presenter = _appWindow.Presenter as OverlappedPresenter ?? OverlappedPresenter.Create();
        if (!ReferenceEquals(_appWindow.Presenter, _presenter)) _appWindow.SetPresenter(_presenter);
        Place(setup);
        _appWindow.Changed += OnAppWindowChanged;

        // One call serves both policies: a root pinned to a theme resolves to it and never
        // changes, a root left at Default follows the application and is repainted on every
        // live change — including the one after load, when the actual theme first becomes real.
        TitleBarTheming.Follow(this);

        Root.KeyDown += OnRootKeyDown;
        Root.Loaded += OnRootLoaded;
        Closed += OnClosed;

        _ready = true;
        Navigate(setup.InitialTag ?? setup.Sections[0].Tag);
    }

    /// <summary>The sections, as declared.</summary>
    public IReadOnlyList<SettingsSection> Sections => _setup.Sections;

    /// <summary>The section on screen, or null once the window has closed.</summary>
    public string? CurrentTag => _lifecycle.Current;

    /// <summary>Shows a section: the current one's leave hook, then the change, then the new
    /// one's enter hook. Selecting the current section again does nothing.</summary>
    /// <exception cref="ArgumentException">No section carries the tag.</exception>
    public void Navigate(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        if (!_items.TryGetValue(tag, out var item))
            throw new ArgumentException($"No section is tagged '{tag}'.", nameof(tag));

        Navigation.SelectedItem = item;
        // The pane raises no selection change for an item already selected, and the lifecycle
        // does nothing for a section already current, so calling it here as well costs nothing
        // and covers the one without the other.
        Show(tag);
    }

    /// <summary>Discards and builds again every page whose section is not build-once. The
    /// current section, if rebuilt, leaves before its page goes and enters again on the new
    /// one.</summary>
    public void Rebuild() => _lifecycle.Rebuild();

    /// <summary>Discards and builds again one section's page.</summary>
    /// <exception cref="ArgumentException">No section carries the tag.</exception>
    /// <exception cref="InvalidOperationException">The section is build-once.</exception>
    public void Rebuild(string tag) => _lifecycle.Rebuild(tag);

    /// <summary>
    /// Sizes the window so the tallest page fits without scrolling: every page is measured at the
    /// width the pages have now, each made visible for its own measure and put back, and the
    /// window grows to the tallest — widening only for a page that cannot fit the width it has —
    /// within the work area of the monitor it is on. Before the content has loaded the measure
    /// would use fallback metrics, so a call then waits for load.
    /// </summary>
    public void FitToPages()
    {
        if (!Root.IsLoaded)
        {
            _fitPending = true;
            return;
        }

        double width = Pages.ActualWidth;
        double tallest = 0, widest = 0;
        foreach (var page in Pages.Children)
        {
            var was = page.Visibility;
            page.Visibility = Visibility.Visible;
            page.Measure(new Size(width, double.PositiveInfinity));
            tallest = Math.Max(tallest, page.DesiredSize.Height);
            widest = Math.Max(widest, page.DesiredSize.Width);
            page.Visibility = was;
        }

        var padding = _setup.PagePadding;
        double clientWidth = _setup.PaneWidth + padding.Left + Math.Max(width, widest) + padding.Right;
        double clientHeight = padding.Top + tallest + padding.Bottom;

        IntPtr hwnd = Win32Interop.GetWindowFromWindowId(_appWindow.Id);
        double scale = MonitorMetrics.ScaleForWindow(hwnd);
        var (frameWidth, frameHeight) = MonitorMetrics.NonClientSize(hwnd);
        var bounds = WorkAreaOf(DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest));
        var position = _appWindow.Position;
        var size = _appWindow.Size;

        int outerWidth = Math.Min(Math.Max((int)Math.Ceiling(clientWidth * scale) + frameWidth, size.Width), bounds.Width);
        int outerHeight = Math.Min((int)Math.Ceiling(clientHeight * scale) + frameHeight, bounds.Height);
        var fitted = new NativeRect(position.X, position.Y, position.X + outerWidth, position.Y + outerHeight).ClampInto(bounds);
        _appWindow.MoveAndResize(new RectInt32(fitted.Left, fitted.Top, fitted.Width, fitted.Height));
    }

    void ISectionHost<UIElement>.Add(UIElement page)
    {
        page.Visibility = Visibility.Collapsed;
        Pages.Children.Add(page);
    }

    void ISectionHost<UIElement>.Remove(UIElement page) => Pages.Children.Remove(page);

    void ISectionHost<UIElement>.Show(UIElement page) => page.Visibility = Visibility.Visible;

    void ISectionHost<UIElement>.Hide(UIElement page) => page.Visibility = Visibility.Collapsed;

    private void OnSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_ready) return;
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }) Show(tag);
    }

    private void Show(string tag)
    {
        _lifecycle.Select(tag);
        // One scroll viewer serves every page, so a new page would otherwise open wherever the
        // last one was scrolled to.
        Scroller.ChangeView(null, 0, null, disableAnimation: true);
    }

    private void OnRootKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Bubbling, so a control that takes Escape for itself has already had it; a flyout or a
        // drop-down lives in its own root and never routes here at all.
        if (e.Key != VirtualKey.Escape || e.Handled) return;
        e.Handled = true;
        Close();
    }

    private void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        if (!_fitPending) return;
        _fitPending = false;
        FitToPages();
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!args.DidPositionChange && !args.DidSizeChange) return;
        var rect = new WindowRect(sender.Position.X, sender.Position.Y, sender.Size.Width, sender.Size.Height);
        if (WindowPlacement.Remember(_presenter.State == OverlappedPresenterState.Restored, rect) is { } kept)
            _restoredRect = kept;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _appWindow.Changed -= OnAppWindowChanged;
        _lifecycle.Close();
        if (_setup.RectStore is { } store && _restoredRect is { } rect) store.Save(rect);
    }

    private void ApplyLayout(SettingsWindowSetup setup)
    {
        Navigation.OpenPaneLength = setup.PaneWidth;
        Scroller.Padding = setup.PagePadding;
        PageColumn.MaxWidth = setup.PageMaxWidth;
    }

    private void ApplyFooter(SettingsWindowSetup setup)
    {
        FooterMark.Source = setup.ProductMark;
        FooterMark.Visibility = setup.ProductMark is null ? Visibility.Collapsed : Visibility.Visible;
        FooterName.Text = setup.ProductName;
        FooterName.Visibility = setup.ProductName.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        FooterVersion.Text = setup.ProductVersion;
        FooterVersion.Visibility = setup.ProductVersion.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        bool any = setup.ProductMark is not null || setup.ProductName.Length > 0 || setup.ProductVersion.Length > 0;
        Footer.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Place(SettingsWindowSetup setup)
    {
        IntPtr hwnd = Win32Interop.GetWindowFromWindowId(_appWindow.Id);
        var (cursorWorkArea, cursorScale) = MonitorMetrics.ForCursor();
        var opening = WindowPlacement.Opening(
            setup.RectStore?.Load(),
            cursorWorkArea,
            cursorScale,
            setup.DefaultClientWidth,
            setup.DefaultClientHeight,
            MonitorMetrics.NonClientSize(hwnd),
            rect => WorkAreaOf(DisplayArea.GetFromRect(
                new RectInt32(rect.Left, rect.Top, rect.Width, rect.Height), DisplayAreaFallback.Nearest)));

        _appWindow.MoveAndResize(new RectInt32(opening.Left, opening.Top, opening.Width, opening.Height));
        // Seeded here: a window closed without ever being moved or resized raises no change to
        // record, and still has a rectangle worth keeping.
        _restoredRect = new WindowRect(opening.Left, opening.Top, opening.Width, opening.Height);
    }

    // An image source through IconSourceElement never loads: the icon stays blank and the
    // source's Opened and OpenFailed both stay silent (measured in the harness with an SVG). An
    // ImageIcon built here loads and draws it at the icon box's size.
    private static IconElement IconFor(IconSource icon) => icon switch
    {
        ImageIconSource image => new ImageIcon { Source = image.ImageSource },
        _ => new IconSourceElement { IconSource = icon },
    };

    private static NativeRect WorkAreaOf(DisplayArea area)
    {
        var work = area.WorkArea;
        return new NativeRect(work.X, work.Y, work.X + work.Width, work.Y + work.Height);
    }
}
