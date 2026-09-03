namespace ZeroZero.Tray.WinUI;

/// <summary>
/// One entry of the menu descriptor the application returns: a command, a checked command, or a
/// separator. The host builds the flyout from these each time the menu is about to open, which is
/// what keeps a menu current without the application touching a control.
/// </summary>
public sealed class TrayMenuItem
{
    private TrayMenuItem(string? text, Action? invoke, bool isEnabled, bool? isChecked, bool isSeparator)
    {
        Text = text;
        Invoke = invoke;
        IsEnabled = isEnabled;
        IsChecked = isChecked;
        IsSeparator = isSeparator;
    }

    /// <summary>The label; null for a separator.</summary>
    public string? Text { get; }

    /// <summary>What choosing the entry does; null for a separator or a label with no action.</summary>
    public Action? Invoke { get; }

    /// <summary>Whether the entry can be chosen.</summary>
    public bool IsEnabled { get; }

    /// <summary>The check mark: true or false for a toggle, null for a plain command.</summary>
    public bool? IsChecked { get; }

    /// <summary>A rule between groups.</summary>
    public bool IsSeparator { get; }

    /// <summary>A command.</summary>
    public static TrayMenuItem Command(string text, Action? invoke, bool isEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TrayMenuItem(text, invoke, isEnabled, null, false);
    }

    /// <summary>A command drawn with a check mark that reflects a state.</summary>
    public static TrayMenuItem Toggle(string text, bool isChecked, Action? invoke, bool isEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new TrayMenuItem(text, invoke, isEnabled, isChecked, false);
    }

    /// <summary>A rule between groups.</summary>
    public static TrayMenuItem Separator() => new(null, null, false, null, true);
}
