using Xunit;

namespace ZeroZero.ReleaseVerification.Tests;

/// <summary>signing-gate.ps1: a tagged run with no signing secret stops; a branch run says so and goes on.</summary>
public sealed class SigningGateTests
{
    private static ScriptResult Gate(string refType, string? secret) =>
        Scripts.Run("signing-gate.ps1", new Dictionary<string, string?> { ["RELEASE_SIGNING_SECRET"] = secret }, "-RefType", refType);

    [Fact]
    public void Fails_a_tag_without_the_secret()
    {
        var result = Gate("tag", null);

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("would ship unsigned", result.Output);
    }

    [Fact]
    public void Treats_a_blank_secret_as_absent()
    {
        var result = Gate("tag", "   ");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("would ship unsigned", result.Output);
    }

    [Fact]
    public void Passes_a_tag_with_the_secret_and_never_prints_it()
    {
        var result = Gate("tag", "s3cret-value");

        Assert.True(result.Passed, result.ToString());
        Assert.Contains("the signing secret is present", result.Output);
        Assert.DoesNotContain("s3cret-value", result.Output);
    }

    [Fact]
    public void Allows_a_branch_without_the_secret_and_says_nothing_from_it_is_a_release()
    {
        var result = Gate("branch", null);

        Assert.True(result.Passed, result.ToString());
        Assert.Contains("Nothing from this run is a release", result.Output);
    }

    [Fact]
    public void Fails_a_ref_type_it_cannot_place()
    {
        var result = Gate("other", "s3cret-value");

        Assert.False(result.Passed, result.ToString());
        Assert.Contains("neither tag nor branch", result.Output);
    }
}
