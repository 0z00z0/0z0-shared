using Microsoft.UI.Xaml;
using ZeroZero.Controls.WinUI;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// Title-bar theming on screen: a dark Mica page with its bar untreated, the same page with the
/// bar painted for its theme, a light page painted for its theme (which leaves the bar stock),
/// a light page whose bar is pinned dark, the fixed override an application pinned dark uses, and
/// a light page whose bar is painted dark and then light again, what a live switch back leaves.
/// The caption strip is the thing to look at; the page is only there to be dark or light.
/// </summary>
public sealed partial class TitleBarWindow : Window
{
    private const double ClientWidth = 420;

    /// <summary>How the bar is treated in this window.</summary>
    public enum Treatment
    {
        /// <summary>Nothing done to the bar: the defect, on a dark page.</summary>
        None,
        /// <summary>Painted for the content's actual theme, and re-painted on a live change.</summary>
        FollowTheme,
        /// <summary>Painted dark whatever the page's theme.</summary>
        FixedDark,
        /// <summary>Painted dark, then light again: what a live switch back leaves behind.</summary>
        DarkThenLight,
    }

    public TitleBarWindow(string title, ElementTheme theme, Treatment treatment, int offset)
    {
        InitializeComponent();
        Title = title;
        Root.RequestedTheme = theme;
        Heading.Text = title;
        Note.Text = treatment switch
        {
            Treatment.None => "The bar is untreated. On a dark page the caption area stays light: Mica paints nothing there.",
            Treatment.FollowTheme => "TitleBarTheming.Follow: the bar takes the page's theme now and on every live change.",
            Treatment.FixedDark => "TitleBarTheming.Apply(window, ElementTheme.Dark): a dark bar pinned over a light page.",
            _ => "Apply(Dark) then Apply(Light): the bar after a live switch back to light.",
        };

        switch (treatment)
        {
            case Treatment.FollowTheme:
                TitleBarTheming.Follow(this);
                break;
            case Treatment.FixedDark:
                TitleBarTheming.Apply(this, ElementTheme.Dark);
                break;
            case Treatment.DarkThenLight:
                TitleBarTheming.Apply(this, ElementTheme.Dark);
                TitleBarTheming.Apply(this, ElementTheme.Light);
                break;
        }

        Resize(offset);
    }

    private void Resize(int offset) =>
        WindowSizing.FitToContent(this, Body, ClientWidth, offset, minimumContentHeight: 120);
}
