using Xunit;

namespace ZeroZero.Update.Tests;

public class ReleaseNotesTextTests
{
    [Fact]
    public void Strip_TakesTheMarkdownOffAndLeavesTheHashLineOut()
    {
        string body = "## Product v1.35.0\r\n\r\n### New\r\n- keep-awake: choose the **screen hold** from the [dashboard](https://example.invalid/docs)\r\n\r\n\r\nDownload `Product-Setup-1.35.0.exe` below.\r\n\r\n**SHA256 (installer):** `AD26D1A44E4D772CEDB730988E645FD127F7C0300678F9BD1C09C411443FE084`\r\n\r\n";

        string text = ReleaseNotesText.Strip(body);

        string[] lines = text.Split(Environment.NewLine);
        Assert.Equal(["Product v1.35.0", "", "New", "• keep-awake: choose the screen hold from the dashboard", "", "Download Product-Setup-1.35.0.exe below."], lines);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\n  ")]
    public void Strip_OfNothingIsEmpty(string? body)
    {
        Assert.Equal("", ReleaseNotesText.Strip(body));
    }

    [Fact]
    public void Strip_LeavesPlainTextAlone()
    {
        Assert.Equal("A sentence with 3 * 4 = 12 and a_snake_case_name.", ReleaseNotesText.Strip("A sentence with 3 * 4 = 12 and a_snake_case_name."));
    }

    [Fact]
    public void Strip_TakesEmphasisOffAWord()
    {
        Assert.Equal("This is important, and so is that.", ReleaseNotesText.Strip("This is *important*, and so is _that_."));
    }
}
