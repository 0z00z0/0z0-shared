using System.Windows.Input;

namespace ZeroZero.Tray.WinUI;

/// <summary>The command behind a menu entry. The library renders the flyout as a native popup
/// menu, from which only an item's Command fires, never its Click, so every entry carries one.</summary>
internal sealed class TrayMenuCommand : ICommand
{
    private readonly Action? _invoke;
    private readonly bool _isEnabled;

    public TrayMenuCommand(Action? invoke, bool isEnabled)
    {
        _invoke = invoke;
        _isEnabled = isEnabled;
    }

    // The flyout is rebuilt for every opening, so an entry's enabled state never changes in place.
    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => _isEnabled && _invoke is not null;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) _invoke!();
    }
}
