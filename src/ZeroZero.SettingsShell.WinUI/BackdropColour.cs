namespace ZeroZero.SettingsShell.WinUI;

/// <summary>
/// A colour the backdrop is tinted with, as three channels. Plain numbers rather than a platform
/// colour type, so the rule that decides which colour a window opens with can be pinned by a test
/// that never loads the XAML runtime.
/// </summary>
/// <param name="Red">0 to 255.</param>
/// <param name="Green">0 to 255.</param>
/// <param name="Blue">0 to 255.</param>
public readonly record struct BackdropColour(byte Red, byte Green, byte Blue)
{
    /// <summary>The colour written the way a style sheet writes it, <c>#RRGGBB</c>, six digits
    /// with no alpha.</summary>
    public static BackdropColour Parse(string hex)
    {
        ArgumentNullException.ThrowIfNull(hex);
        string digits = hex.StartsWith('#') ? hex[1..] : hex;
        if (digits.Length != 6 || !uint.TryParse(digits, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out uint value))
            throw new FormatException($"A backdrop colour is six hexadecimal digits, optionally behind a hash: '{hex}'.");

        return new BackdropColour((byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }
}
