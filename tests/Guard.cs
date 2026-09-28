namespace ZeroZero.Tests;

// The mark that puts a test in the default run: [Trait(Guard.Category, Guard.Value)]. Constants
// rather than literals, so a mistyped name fails the build instead of quietly dropping a guard.
// .github/scripts/run-tests.ps1 filters on the same two strings. docs/testing.md says what earns
// the mark; on a [Theory] it marks every row.
internal static class Guard
{
    public const string Category = "Category";

    public const string Value = "Guard";
}
