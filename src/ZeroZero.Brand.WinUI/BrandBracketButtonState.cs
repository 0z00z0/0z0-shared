namespace ZeroZero.Brand.WinUI;

/// <summary>
/// What a <see cref="BrandBracketButton"/> shows. The host sets it; only <see cref="Success"/>
/// changes on its own, back to <see cref="Rest"/>.
/// </summary>
public enum BrandBracketButtonState
{
    /// <summary>Waiting to be pressed: the orange chevron, the label and the brand gradient brackets.</summary>
    Rest,

    /// <summary>Work is running: a text spinner in place of the chevron, and the brackets breathe.</summary>
    Busy,

    /// <summary>The work finished well: the brand slashed zero in place of the chevron, teal brackets
    /// and label, and a return to <see cref="Rest"/> four seconds later.</summary>
    Success,

    /// <summary>Something needs the reader: amber brackets and label and a dimmed caret, held until the
    /// host sets another state. A press raises <see cref="BrandBracketButton.Click"/> as in every
    /// other state.</summary>
    Attention,
}
