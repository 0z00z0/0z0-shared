namespace ZeroZero.Brand.Core;

/// <summary>
/// A button of the application's own in the About surface's row, placed after the studio's Website
/// and Donate buttons. The surface draws it in the row's style; the application supplies only the
/// label and what a press does.
/// </summary>
/// <param name="Label">The button's text, which is also its accessible name.</param>
/// <param name="OnClick">Run on the UI thread each time the button is pressed.</param>
public sealed record AboutButton(string Label, Action OnClick);
