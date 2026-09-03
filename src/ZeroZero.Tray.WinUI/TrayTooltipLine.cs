namespace ZeroZero.Tray.WinUI;

/// <summary>One line of the tooltip: its text, and a suffix that survives truncation, the part
/// that carries the number after a name the user chose and may have made long.</summary>
/// <param name="Text">The line, or its truncatable part when a suffix is given.</param>
/// <param name="Suffix">Kept whole when the line is cut; includes its own separator.</param>
public readonly record struct TrayTooltipLine(string? Text, string? Suffix = null);
