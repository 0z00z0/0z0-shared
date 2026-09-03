namespace ZeroZero.Tray.WinUI;

/// <summary>What a click on the icon is taken as, once the policy has looked at it.</summary>
public enum TrayClick
{
    /// <summary>Not acted on: the click that dismissed a pop-out, or a double click whose first
    /// half was refused.</summary>
    Ignored,
    /// <summary>A single left click.</summary>
    Left,
    /// <summary>The second click of a double click; the first has already been reported as
    /// <see cref="Left"/>.</summary>
    Double,
}
