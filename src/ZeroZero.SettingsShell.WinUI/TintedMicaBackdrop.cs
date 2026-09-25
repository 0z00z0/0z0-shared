using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// Mica with the tint the application chose rather than the one Windows takes from the wallpaper.
/// The markup's <c>MicaBackdrop</c> carries no colour of its own, so a window that wants one
/// replaces its backdrop with this.
/// </summary>
/// <remarks>The colour is settable, because the window changes it when its theme changes rather
/// than building a second backdrop and swapping it in.</remarks>
internal sealed class TintedMicaBackdrop : SystemBackdrop
{
    private MicaController? _controller;
    private Color _colour;

    public TintedMicaBackdrop(Color colour) => _colour = colour;

    /// <summary>The tint, and the flat colour the window falls back to where the backdrop cannot
    /// be drawn — transparency off, or the window inactive.</summary>
    public Color Colour
    {
        get => _colour;
        set
        {
            _colour = value;
            if (_controller is null) return;
            _controller.TintColor = value;
            _controller.FallbackColor = value;
        }
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);

        // BaseAlt, matching the markup this replaces: the window's ground is a page's, not a
        // card's.
        _controller = new MicaController { Kind = MicaKind.BaseAlt, TintColor = _colour, FallbackColor = _colour };
        _controller.SetSystemBackdropConfiguration(GetDefaultSystemBackdropConfiguration(target, root));
        _controller.AddSystemBackdropTarget(target);
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        base.OnTargetDisconnected(target);

        _controller?.RemoveSystemBackdropTarget(target);
        _controller?.Dispose();
        _controller = null;
    }
}
