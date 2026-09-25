namespace ZeroZero.Tray;

/// <summary>
/// The single write that puts one icon where it is wanted: the entry to write it into and the
/// value to write. Nothing else of the settings is touched.
/// </summary>
/// <param name="Entry">The entry's name under the settings key.</param>
/// <param name="IsPromoted">The value to write, as the shell's own setting spells it.</param>
public readonly record struct TrayIconPlacementPlan(string Entry, int IsPromoted);
