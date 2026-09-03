namespace ZeroZero.Tray.WinUI;

/// <summary>What the application's icon delegate is asked to draw for: the slot the taskbar draws
/// the icon at, in physical pixels, and the taskbar's theme with the stroke tone that reads on it.
/// The host re-asks with a new request when either changes.</summary>
/// <param name="SlotPixels">The taskbar's slot at its own scale: 16 at 100 %, 24 at 150 %.</param>
/// <param name="Theme">Whether the taskbar the icon sits on is light or dark.</param>
/// <param name="StrokeTone">The tone strokes need to read on that taskbar.</param>
public sealed record TrayIconRequest(int SlotPixels, TaskbarTheme Theme, StrokeTone StrokeTone);
