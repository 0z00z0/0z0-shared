namespace ZeroZero.Brand.Core;

/// <summary>
/// Studio-wide constants shared by every ZeroZero Software app: identity, links, and the brand
/// palette. Kept as plain string/hex constants (rather than platform color types) so this class
/// is usable from any .NET target, WinUI or not.
/// </summary>
public static class Brand
{
    /// <summary>The studio name, exactly as it should be displayed: "ZeroZero" (no space between
    /// the Zeros) + " Software".</summary>
    public const string StudioName = "ZeroZero Software";

    public const string Tagline = "Small tools. Zero bloat.";

    public const string WebsiteUrl = "https://0z0.xyz";

    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/ezpl";

    public const string OrgUrl = "https://github.com/0z00z0";

    // ── Palette ──────────────────────────────────────────────────────────────
    // Matches the public site's [Ø] mark and its canvas background.

    public const string ColorBg = "#0a0f17";
    public const string ColorBg2 = "#0e1620";
    public const string ColorTeal = "#27e0c8";
    public const string ColorBlue = "#11a9d6";
    public const string ColorPurple = "#7b8cff";
    public const string ColorIndigo = "#3f5be0";
    public const string ColorAmber = "#d8a657";
}
