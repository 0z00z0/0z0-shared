using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using ZeroZero.Brand.Core;
// This project's own namespace (ZeroZero.Brand.WinUI) nests inside ZeroZero.Brand, so an
// unqualified "Brand" would resolve to that enclosing namespace segment rather than the
// ZeroZero.Brand.Core.Brand class — alias it to sidestep the collision.
using CoreBrand = ZeroZero.Brand.Core.Brand;

namespace ZeroZero.Brand.WinUI;

/// <summary>
/// The shared About *content* for ZeroZero Software apps — brand header, description, three
/// co-equal link buttons (repository / website / donate) and an external-libraries credit list.
/// Deliberately owns no window chrome, sizing, or update/exit flow: those are tray-app-only
/// concerns that <see cref="BrandAboutWindow"/> layers on top when hosting this control in a
/// popup. A full windowed app with its own in-navigation About page (no popup, no update button)
/// hosts this control directly instead.
/// </summary>
public sealed partial class BrandAboutControl : UserControl
{
    private AboutInfo? _info;

    /// <summary>
    /// Raised after the external-libraries expander toggles, since that changes this control's
    /// desired height. A hosting <see cref="BrandAboutWindow"/> uses this to re-measure and resize
    /// itself to fit; a page host inside a scrollable layout can ignore it.
    /// </summary>
    public event EventHandler? ContentResized;

    public BrandAboutControl() => InitializeComponent();

    /// <summary>
    /// Supplies the per-app data to render. A method rather than a settable CLR property — the
    /// WinUI XAML compiler generates metadata requiring a parameterless constructor for the type of
    /// any public property on a XAML class, which <see cref="AboutInfo"/>'s <see langword="required"/>
    /// members deliberately don't have (CS9035). A XAML-hosted consumer (e.g. an in-nav About page)
    /// calls this from code-behind after construction; <see cref="BrandAboutWindow"/>'s constructor
    /// does it for you.
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

        RepoBtn.Click       += (_, _) => Open(info.RepoUrl);
        SiteBtn.Click       += (_, _) => Open(CoreBrand.WebsiteUrl);
        DonateBtn.Click     += (_, _) => Open(CoreBrand.BuyMeACoffeeUrl);

        PopulateExternalLibraries(info.ExternalLibraries);
    }

    private void PopulateExternalLibraries(IReadOnlyList<ExternalLibrary> libraries)
    {
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
