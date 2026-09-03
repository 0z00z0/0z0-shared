using Xunit;

namespace ZeroZero.Config.Watch.Tests;

/// <summary>The classifier on its own: what it compares, what it skips, and which way round the
/// default falls.</summary>
public sealed class ClassifierTests
{
    private static SettingsChangeClassifier<AppSettings> Windows() =>
        new("must the connection be rebuilt?", ["WindowWidth", "WindowHeight"]);

    [Fact]
    public void Two_identical_states_are_not_substantive()
    {
        var classifier = Windows();

        Assert.False(classifier.IsSubstantive(new AppSettings(), new AppSettings()));
    }

    [Fact]
    public void A_named_value_is_skipped()
    {
        var classifier = Windows();

        Assert.False(classifier.IsSubstantive(
            new AppSettings { WindowWidth = 800 },
            new AppSettings { WindowWidth = 1280 }));
    }

    [Fact]
    public void Every_value_the_list_does_not_name_counts()
    {
        var classifier = Windows();

        Assert.True(classifier.IsSubstantive(new AppSettings(), new AppSettings { Retries = 9 }));
        Assert.True(classifier.IsSubstantive(new AppSettings(), new AppSettings { Broker = "attic" }));
        Assert.True(classifier.IsSubstantive(new AppSettings(), new AppSettings { StartMinimised = true }));
    }

    [Fact]
    public void A_value_added_after_the_list_was_written_counts()
    {
        // The list names everything that existed when it was written; Nickname came later and nobody
        // has classified it. It counts, because the list says what to skip rather than what to weigh.
        var classifier = new SettingsChangeClassifier<AppSettings>(
            "must the connection be rebuilt?",
            ["StartMinimised", "Broker", "Retries", "WindowWidth", "WindowHeight", "Window"]);

        Assert.True(classifier.IsSubstantive(new AppSettings(), new AppSettings { Nickname = "cellar" }));
    }

    [Fact]
    public void A_named_value_inside_a_nested_object_is_skipped_and_its_sibling_is_not()
    {
        var classifier = new SettingsChangeClassifier<AppSettings>("does the layout matter?", ["Window/Left"]);

        Assert.False(classifier.IsSubstantive(
            new AppSettings(),
            new AppSettings { Window = new Placement { Left = 40 } }));

        Assert.True(classifier.IsSubstantive(
            new AppSettings(),
            new AppSettings { Window = new Placement { Top = 40 } }));
    }

    [Fact]
    public void Naming_a_nested_object_skips_all_of_it()
    {
        var classifier = new SettingsChangeClassifier<AppSettings>("does the layout matter?", ["Window"]);

        Assert.False(classifier.IsSubstantive(
            new AppSettings(),
            new AppSettings { Window = new Placement { Left = 40, Top = 90 } }));
    }

    [Fact]
    public void An_empty_list_makes_every_difference_count()
    {
        var classifier = new SettingsChangeClassifier<AppSettings>("did anything move?", []);

        Assert.True(classifier.IsSubstantive(new AppSettings(), new AppSettings { WindowWidth = 1280 }));
    }

    [Fact]
    public void A_name_is_matched_however_it_is_cased()
    {
        var classifier = new SettingsChangeClassifier<AppSettings>("does the size matter?", ["windowwidth"]);

        Assert.False(classifier.IsSubstantive(new AppSettings(), new AppSettings { WindowWidth = 1280 }));
    }

    [Fact]
    public void A_name_matching_nothing_is_refused()
    {
        var error = Assert.Throws<ArgumentException>(() =>
            new SettingsChangeClassifier<AppSettings>("does the size matter?", ["WidnowWidth"]));

        Assert.Contains("WidnowWidth", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_deeper_name_is_taken_on_trust_when_its_first_segment_exists()
    {
        // A nested object may be null in a default instance, so only the first segment can be
        // checked without refusing lists that are correct.
        var classifier = new SettingsChangeClassifier<AppSettings>("does the layout matter?", ["Window/Nowhere"]);

        Assert.True(classifier.IsSubstantive(
            new AppSettings(),
            new AppSettings { Window = new Placement { Left = 40 } }));
    }

    [Fact]
    public void The_fingerprint_carries_everything_not_named()
    {
        var classifier = Windows();
        var fingerprint = classifier.Fingerprint(new AppSettings { Broker = "attic", WindowWidth = 1280 });

        Assert.Contains("attic", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("1280", fingerprint, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowWidth", fingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public void The_question_is_carried_because_the_answer_means_nothing_without_it()
    {
        var classifier = new SettingsChangeClassifier<AppSettings>("must the connection be rebuilt?", []);

        Assert.Equal("must the connection be rebuilt?", classifier.Question);
    }

    [Fact]
    public void A_question_with_no_name_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new SettingsChangeClassifier<AppSettings>(" ", []));
    }

    [Fact]
    public void A_shape_that_is_not_an_object_can_have_nothing_named_inside_it()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new SettingsChangeClassifier<NotAnObject>("does it matter?", ["Anything"]));
    }
}
