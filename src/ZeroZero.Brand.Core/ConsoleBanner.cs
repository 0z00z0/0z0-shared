namespace ZeroZero.Brand.Core;

/// <summary>
/// Prints a plain-ASCII "about" banner for non-UI (CLI/console) tools. Deliberately uses no
/// box-drawing Unicode and no brand glyphs (e.g. no "Ø") — just letters, digits, and punctuation
/// — so it renders correctly in any terminal, including legacy code pages and redirected output.
/// </summary>
public static class ConsoleBanner
{
    private const string Rule = "==========================================";

    public static void Print(AboutInfo info)
    {
        var w = Console.Out;

        w.WriteLine(Rule);
        w.WriteLine($" {Brand.StudioName}");
        w.WriteLine($" {Brand.Tagline}");
        w.WriteLine(Rule);
        w.WriteLine($" {info.AppName} v{info.Version}");
        w.WriteLine($" {info.Description}");
        w.WriteLine();
        w.WriteLine($" {info.RepoUrl}");
        w.WriteLine($" {Brand.WebsiteUrl}");
        w.WriteLine($" {Brand.BuyMeACoffeeUrl}");

        if (info.ExternalLibraries.Count > 0)
        {
            w.WriteLine();
            w.WriteLine(" External libraries:");
            foreach (var lib in info.ExternalLibraries)
                w.WriteLine($"   {lib.Name} ({lib.Author}) - {lib.Purpose} [{lib.License}]");
        }

        w.WriteLine(Rule);
    }
}
