using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using ZeroZero.Brand.Core;
// This project's own namespace (ZeroZero.Brand.WinUI) nests inside ZeroZero.Brand, so an
// unqualified "Brand" would resolve to that enclosing namespace segment rather than the
// ZeroZero.Brand.Core.Brand class — alias it to sidestep the collision.
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// The shared About *content* for ZeroZero Software apps — brand header, description, a row of
/// buttons (website / donate / what's new, then any the host supplies through
/// <see cref="AboutInfo.Buttons"/>), the release notes themselves, and an external-libraries credit
/// list. Deliberately owns no window chrome, sizing, or update/exit flow: those are tray-app-only
/// concerns that <see cref="BrandAboutWindow"/> layers on top when hosting this control in a popup. A
/// full windowed app with its own in-navigation About page (no popup, no update button) hosts this
/// control directly instead.
/// </summary>
public sealed partial class BrandAboutControl : UserControl
{
    /// <summary>
    /// How long the notes fetch is given before it is abandoned. Short on purpose: the reader
    /// pressed a button on a small window and is waiting for it, so a request that has not
    /// answered by now has failed as far as they are concerned.
    /// </summary>
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(3);

    /// <summary>What is shown while the fetch is in flight, and what replaces it when it fails —
    /// one plain sentence, no error code, no retry offered.</summary>
    private const string Fetching = "Fetching the notes…";
    private const string FetchFailed = "The release notes could not be fetched.";

    /// <summary>Gap between neighbouring row buttons, and between wrapped lines of them.</summary>
    private const double RowSpacing = 8;

    // Static so an application that opens About repeatedly reuses one set of connections. The
    // client's own timeout matches the per-request one, so neither route can outlive the other.
    private static readonly HttpClient Http = new() { Timeout = FetchTimeout };

    private AboutInfo? _info;

    /// <summary>The fetch in flight, if any. One at a time: a second press while it runs is
    /// ignored rather than starting a parallel request. A reply is applied only while its own fetch
    /// is still this one, so a fetch abandoned by <see cref="CancelPendingFetch"/> touches nothing
    /// and the next press starts a fresh one.</summary>
    private CancellationTokenSource? _fetch;

    /// <summary>The notes as fetched, so reopening the panel costs no second request.</summary>
    private string? _notes;

    /// <summary>The row. Its first <see cref="_builtInButtonCount"/> children are the control's own;
    /// everything after them came from the host.</summary>
    private readonly ButtonRowPanel _buttonRow = new() { Spacing = RowSpacing };

    private readonly int _builtInButtonCount;

    private readonly Button _newsButton;

    /// <summary>
    /// Raised after the external-libraries list or the release-notes panel toggles, since either
    /// changes this control's desired height. A hosting <see cref="BrandAboutWindow"/> uses this to
    /// re-measure and resize itself to fit; a page host inside a scrollable layout can ignore it.
    /// </summary>
    public event EventHandler? ContentResized;

    public BrandAboutControl()
    {
        InitializeComponent();

        StudioNameText.Text = CoreBrand.StudioName;
        TaglineText.Text    = CoreBrand.Tagline;

        // Named so a probe can find a row button by name.
        var siteButton   = CreateRowButton("Website", "SiteBtn");
        var donateButton = CreateRowButton(DonateContent(), "DonateBtn");
        // The content is a panel rather than a string, so the accessible name is stated explicitly.
        AutomationProperties.SetName(donateButton, "Donate");
        _newsButton      = CreateRowButton("What's new", "NewsBtn");
        _buttonRow.Children.Add(siteButton);
        _buttonRow.Children.Add(donateButton);
        _buttonRow.Children.Add(_newsButton);
        _builtInButtonCount = _buttonRow.Children.Count;
        ButtonRowSlot.Child = _buttonRow;

        // Wired once here, never from SetInfo, which a cached page calls on every visit: wiring there
        // stacks a handler per call. The notes handler reads the current _info for the same reason.
        _newsButton.Click  += (_, _) => _ = ToggleNotesAsync();
        siteButton.Click   += (_, _) => Open(CoreBrand.WebsiteUrl);
        donateButton.Click += (_, _) => Open(CoreBrand.BuyMeACoffeeUrl);

        // A page host navigating away is the same event as a window closing: whatever is in flight
        // must not land on a control that is no longer on screen.
        Unloaded += (_, _) => CancelPendingFetch();
    }

    /// <summary>
    /// Abandons any notes fetch in flight and stops its reply reaching this control. A host closing
    /// its window calls this before it closes; the control also calls it for itself when it is
    /// unloaded.
    /// </summary>
    public void CancelPendingFetch()
    {
        var fetch = _fetch;
        _fetch = null;
        fetch?.Cancel();
    }

    /// <summary>
    /// Supplies the per-app data to render; calling it again, as a cached page does on every visit,
    /// is safe. A method, because a public <see cref="AboutInfo"/> property fails the XAML build with
    /// CS9035 (docs/consume-brand-about-control.md). <see cref="BrandAboutWindow"/> calls it itself.
    /// </summary>
    public void SetInfo(AboutInfo info)
    {
        _info = info;
        Populate(info);
    }

    private void Populate(AboutInfo info)
    {
        AppNameText.Text     = info.AppName;
        VersionText.Text     = $"v{info.Version}";
        DescriptionText.Text = info.Description;
        FooterText.Text     = $"Copyright © {DateTime.UtcNow.Year} {CoreBrand.StudioName} · MIT Licence";

        // A repopulate is a different application's data, so notes fetched, or still being fetched,
        // for the previous one are not shown against it.
        CancelPendingFetch();
        _notes = null;
        NewsText.Text = "";
        NewsPanel.Visibility = Visibility.Collapsed;

        // No address to point at, no button: the same rule the update button follows, rather than a
        // dead row that answers with a failure sentence every time it is pressed. The row panel
        // gives a collapsed button neither width nor spacing.
        _newsButton.Visibility = info.ReleaseNotesUrl is { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

        PopulateHostButtons(info.Buttons);
        PopulateExternalLibraries(info.ExternalLibraries);
    }

    /// <summary>Replaces the host's buttons at the end of the row; rebuilt on every call, so each
    /// carries exactly one handler.</summary>
    private void PopulateHostButtons(IReadOnlyList<AboutButton> buttons)
    {
        while (_buttonRow.Children.Count > _builtInButtonCount)
        {
            _buttonRow.Children.RemoveAt(_buttonRow.Children.Count - 1);
        }

        foreach (var button in buttons)
        {
            var rowButton = CreateRowButton(button.Label, name: null);
            var onClick = button.OnClick;
            rowButton.Click += (_, _) => onClick();
            _buttonRow.Children.Add(rowButton);
        }
    }

    /// <summary>A button in the row's own style: the brand face at the row's size, natural width.
    /// The content is a label for every button but Donate, which carries a drawn mark beside
    /// its own.</summary>
    private Button CreateRowButton(object content, string? name)
    {
        var button = new Button
        {
            Content    = content,
            FontSize   = 11,
            FontFamily = (FontFamily)Resources["BrandFont"],
        };
        if (name is not null) button.Name = name;
        return button;
    }

    /// <summary>
    /// The drawn cup and the label beside it. The mark comes out of the template in the markup, so
    /// its colours stay <c>ThemeResource</c> lookups and follow a theme change; a shape built here
    /// would have taken one colour and kept it.
    /// </summary>
    /// <remarks>
    /// The button reads as one thing to a screen reader: the mark is out of the accessibility tree
    /// and the button is named by the label alone.
    /// </remarks>
    private StackPanel DonateContent()
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add((UIElement)((DataTemplate)Resources["DonateMark"]).LoadContent());
        content.Children.Add(new TextBlock { Text = "Donate", VerticalAlignment = VerticalAlignment.Center });
        return content;
    }

    /// <summary>
    /// Shows or hides the notes panel, fetching the notes the first time it is opened. Nothing is
    /// fetched until the reader asks for it, and nothing blocks: the button returns immediately and
    /// the panel fills in, or says it could not.
    /// </summary>
    private async Task ToggleNotesAsync()
    {
        if (NewsPanel.Visibility == Visibility.Visible)
        {
            NewsPanel.Visibility = Visibility.Collapsed;
            RaiseContentResized();
            return;
        }

        if (_info?.ReleaseNotesUrl is not { Length: > 0 } url) return;

        NewsPanel.Visibility = Visibility.Visible;

        if (_notes is { } already)
        {
            NewsText.Text = already;
            RaiseContentResized();
            return;
        }

        // One request at a time. A second press while the first is in flight reopens the panel and
        // waits with it rather than queueing another.
        if (_fetch is not null)
        {
            RaiseContentResized();
            return;
        }

        NewsText.Text = Fetching;
        RaiseContentResized();

        var fetch = new CancellationTokenSource(FetchTimeout);
        _fetch = fetch;
        try
        {
            string text = await ReleaseNotesReader.FetchAsync(Http, url, fetch.Token);
            if (_fetch != fetch) return;
            _notes = text;
            NewsText.Text = text;
        }
        catch (Exception ex)
        {
            // Every failure reads the same to the reader — unreachable, refused, timed out, not
            // found. Nothing is retried: the sentence stays until the panel is closed and reopened,
            // which is the reader's decision rather than this control's.
            if (_fetch != fetch) return;
            Debug.WriteLine($"BrandAboutControl: release notes fetch failed: {ex}");
            NewsText.Text = FetchFailed;
        }
        finally
        {
            bool isCurrent = _fetch == fetch;
            if (isCurrent) _fetch = null;
            fetch.Dispose();
            // The panel's height settled either way, so the host resizes to what is now in it.
            if (isCurrent) RaiseContentResized();
        }
    }

    private void PopulateExternalLibraries(IReadOnlyList<ExternalLibrary> libraries)
    {
        // Cleared on every call, or a cached page's repeated SetInfo lists every credit once per visit.
        LibrariesPanel.Children.Clear();
        LibrariesGroup.Visibility = libraries.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

        foreach (var library in libraries)
        {
            var line = new TextBlock
            {
                FontSize      = 11,
                TextWrapping  = TextWrapping.Wrap,
                Opacity       = 0.85,
            };

            if (library.Url is { } url)
            {
                var link = new Hyperlink();
                link.Inlines.Add(new Run { Text = library.Name });
                link.Click += (_, _) => Open(url);
                line.Inlines.Add(link);
                line.Inlines.Add(new Run { Text = $" — {library.Author} — {library.Purpose} ({library.License})" });
            }
            else
            {
                line.Text = $"{library.Name} — {library.Author} — {library.Purpose} ({library.License})";
            }

            LibrariesPanel.Children.Add(line);
        }
    }

    private void OnLibrariesToggle(object sender, RoutedEventArgs e)
    {
        bool expanded = LibrariesPanel.Visibility == Visibility.Visible;
        LibrariesPanel.Visibility  = expanded ? Visibility.Collapsed : Visibility.Visible;
        LibrariesToggleBtn.Content = expanded ? "External libraries ▾" : "External libraries ▴";
        RaiseContentResized();
    }

    /// <summary>Every raise goes through here: a host's throwing handler is logged, never let out of a
    /// click handler or lost in the fetch's unobserved task.</summary>
    private void RaiseContentResized()
    {
        try { ContentResized?.Invoke(this, EventArgs.Empty); }
        catch (Exception ex) { Debug.WriteLine($"BrandAboutControl: a ContentResized handler threw: {ex}"); }
    }

    /// <summary>Opens a link in the default browser. A machine with no handler for the address throws
    /// from the shell, which a click handler must not let escape.</summary>
    private static void Open(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine($"BrandAboutControl: opening {url}: {ex}");
        }
    }
}
