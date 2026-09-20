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

    /// <summary>
    /// How much of the notes is read. The panel is a few lines in a small window, not a document
    /// viewer, and a bounded read also caps what an address under someone else's control can make
    /// this control download.
    /// </summary>
    private const int MaxNotesCharacters = 4000;

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
    /// ignored rather than starting a parallel request.</summary>
    private CancellationTokenSource? _fetch;

    /// <summary>Set once the host is going away, so a reply landing after that touches nothing.</summary>
    private bool _dismissed;

    /// <summary>The notes as fetched, so reopening the panel costs no second request.</summary>
    private string? _notes;

    /// <summary>The row. Its first <see cref="_builtInButtons"/> children are the control's own;
    /// everything after them came from the host.</summary>
    private readonly ButtonRowPanel _buttonRow = new() { Spacing = RowSpacing };

    private readonly int _builtInButtons;

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

        // Named as the markup named them, so a probe that finds a row button by name still does.
        var siteButton   = CreateRowButton("Website", "SiteBtn");
        var donateButton = CreateRowButton(DonateContent(), "DonateBtn");
        // The content is a panel now, not a string, so the name is stated rather than inferred.
        AutomationProperties.SetName(donateButton, "Donate");
        _newsButton      = CreateRowButton("What's new", "NewsBtn");
        _buttonRow.Children.Add(siteButton);
        _buttonRow.Children.Add(donateButton);
        _buttonRow.Children.Add(_newsButton);
        _builtInButtons = _buttonRow.Children.Count;
        ButtonRowSlot.Child = _buttonRow;

        // Wired once at construction, never from SetInfo: a consumer with a cached in-navigation
        // About page calls SetInfo on every navigation, and wiring there would stack one more
        // handler per call — the third visit would open each link three times. The notes handler
        // therefore reads the current _info rather than capturing a SetInfo argument.
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
        _dismissed = true;
        try { _fetch?.Cancel(); }
        catch (ObjectDisposedException) { /* the fetch already finished and disposed its source */ }
    }

    /// <summary>
    /// Supplies the per-app data to render. A method rather than a settable CLR property — the
    /// WinUI XAML compiler generates metadata requiring a parameterless constructor for the type of
    /// any public property on a XAML class, which <see cref="AboutInfo"/>'s <see langword="required"/>
    /// members deliberately don't have (CS9035). A XAML-hosted consumer (e.g. an in-nav About page)
    /// calls this from code-behind after construction; <see cref="BrandAboutWindow"/>'s constructor
    /// calls it itself.
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
        // "Licence" (noun) per the studio's British-English house style (design-language.md).
        // Year is computed, not a literal, so this doesn't go stale like a hard-coded one would.
        FooterText.Text      = $"Copyright © {DateTime.UtcNow.Year} {CoreBrand.StudioName} · MIT Licence";

        // A repopulate is a different application's data, so notes fetched for the previous one are
        // not shown against it. An address that changed also drops whatever is on screen.
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

    /// <summary>
    /// Replaces the host's buttons at the end of the row. Rebuilt on every call for the same reason
    /// the credit list is cleared: a cached page calls SetInfo on every visit, and a new button per
    /// call carries exactly one handler.
    /// </summary>
    private void PopulateHostButtons(IReadOnlyList<AboutButton> buttons)
    {
        while (_buttonRow.Children.Count > _builtInButtons)
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
    /// and the button is named by the label alone, as it was when the label carried a character in
    /// front of it.
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
            ContentResized?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_info?.ReleaseNotesUrl is not { Length: > 0 } url) return;

        NewsPanel.Visibility = Visibility.Visible;

        if (_notes is { } already)
        {
            NewsText.Text = already;
            ContentResized?.Invoke(this, EventArgs.Empty);
            return;
        }

        // One request at a time. A second press while the first is in flight reopens the panel and
        // waits with it rather than queueing another.
        if (_fetch is not null)
        {
            ContentResized?.Invoke(this, EventArgs.Empty);
            return;
        }

        NewsText.Text = Fetching;
        ContentResized?.Invoke(this, EventArgs.Empty);

        var fetch = new CancellationTokenSource(FetchTimeout);
        _fetch = fetch;
        try
        {
            string text = await FetchNotesAsync(url, fetch.Token);
            if (_dismissed) return;
            _notes = text;
            NewsText.Text = text;
        }
        catch (Exception ex)
        {
            // Every failure reads the same to the reader — unreachable, refused, timed out, not
            // found. Nothing is retried: the sentence stays until the panel is closed and reopened,
            // which is the reader's decision rather than this control's.
            if (_dismissed) return;
            Debug.WriteLine($"BrandAboutControl: release notes fetch failed: {ex}");
            NewsText.Text = FetchFailed;
        }
        finally
        {
            _fetch = null;
            fetch.Dispose();
            // The panel's height settled either way, so the host resizes to what is now in it.
            if (!_dismissed)
            {
                try { ContentResized?.Invoke(this, EventArgs.Empty); }
                catch (Exception ex) { Debug.WriteLine($"BrandAboutControl: resize after fetch: {ex}"); }
            }
        }
    }

    /// <summary>
    /// The notes as text, bounded in both time and size by the caller's token and
    /// <see cref="MaxNotesCharacters"/>. Reads the body as it arrives rather than whole, so a large
    /// document costs the first few thousand characters and no more.
    /// </summary>
    private static async Task<string> FetchNotesAsync(string url, CancellationToken token)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();

        await using var body = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(body);

        var buffer = new char[MaxNotesCharacters];
        int read = await reader.ReadBlockAsync(buffer, token);
        string text = new string(buffer, 0, read).TrimEnd();

        if (text.Length == 0) return "There is nothing to show for this release.";

        // A full buffer means the body had more; say so rather than ending mid-sentence in silence.
        return read == buffer.Length ? text + "…" : text;
    }

    private void PopulateExternalLibraries(IReadOnlyList<ExternalLibrary> libraries)
    {
        // Cleared before every repopulate, for the same reason the link handlers attach once: a
        // consumer with a cached in-navigation About page calls SetInfo on every navigation, and
        // appending to a panel that still holds the previous lines renders the whole credit list
        // once per visit. Cleared ahead of the empty-list exit too, so a later info without
        // libraries leaves no stale lines behind.
        LibrariesPanel.Children.Clear();

        if (libraries.Count == 0)
        {
            LibrariesGroup.Visibility = Visibility.Collapsed;
            return;
        }

        foreach (var lib in libraries)
        {
            var line = new TextBlock
            {
                FontSize      = 11,
                TextWrapping  = TextWrapping.Wrap,
                Opacity       = 0.85,
            };

            // Credits the Author alongside Name/Purpose/License — a consumer with a bespoke
            // Library/Author/Purpose/License table (e.g. M365Migrator) can render ExternalLibrary
            // itself instead, but this shared flowing-line rendering shouldn't silently drop the
            // author it already has on hand.
            if (lib.Url is { } url)
            {
                var link = new Hyperlink();
                link.Inlines.Add(new Run { Text = lib.Name });
                link.Click += (_, _) => Open(url);
                line.Inlines.Add(link);
                line.Inlines.Add(new Run { Text = $" — {lib.Author} — {lib.Purpose} ({lib.License})" });
            }
            else
            {
                line.Text = $"{lib.Name} — {lib.Author} — {lib.Purpose} ({lib.License})";
            }

            LibrariesPanel.Children.Add(line);
        }
    }

    private void OnLibrariesToggle(object sender, RoutedEventArgs e)
    {
        bool expanded = LibrariesPanel.Visibility == Visibility.Visible;
        LibrariesPanel.Visibility  = expanded ? Visibility.Collapsed : Visibility.Visible;
        LibrariesToggleBtn.Content = expanded ? "External libraries ▾" : "External libraries ▴";
        ContentResized?.Invoke(this, EventArgs.Empty);
    }

    private static void Open(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
