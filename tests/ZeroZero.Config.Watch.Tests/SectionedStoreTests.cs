using System.Text.Json;
using Xunit;
using ZeroZero.Config.Sections;

namespace ZeroZero.Config.Watch.Tests;

/// <summary>The watcher over one section of a shared document, which is the wiring
/// <see cref="SettingsWatcherOptions{T}"/> exists for and the one the convenience overload does not
/// cover.</summary>
/// <remarks>
/// <para>A section store can be blind in a way a whole-file store cannot: the document spells the
/// section's name in a case the addressing type does not, so the section reads as its type's
/// defaults for ever and every write to it is refused. Nothing about that is a change, so an
/// examination reports none — and without something saying so, a person editing the file watches
/// nothing happen with no reason given.</para>
/// </remarks>
public sealed class SectionedStoreTests : WatcherTestBase
{
    private const string SectionName = "general";

    /// <summary>The document as a hand written file has it, with the section spelled however
    /// <paramref name="section"/> says and one member carrying <paramref name="broker"/>.</summary>
    private void GivenDocument(string section, string broker) =>
        File.WriteAllText(
            FilePath,
            $$"""
            {
              "version": 1,
              "{{section}}": {
                "Broker": "{{broker}}",
                "Retries": 3
              }
            }
            """);

    private SectionedSettingsFile Document() =>
        new(new SectionedSettingsOptions(Root, FileName) { SectionOrder = [SectionName] });

    /// <summary>Rewrites the document and crosses the quiet window, waiting for the operating system
    /// to deliver first. Without that wait the clock can be moved before any notification has
    /// arrived, which arms no window and examines nothing — green on a fast machine and red on a
    /// runner, which is exactly what happened.</summary>
    private void Edit(Harness harness, string section, string broker)
    {
        var delivered = harness.Signals;
        GivenDocument(section, broker);
        harness.AwaitSignals(delivered + 1);
        CrossTheWindow(harness);
    }
    private Harness Watch(SectionedSettingsFile document, SettingsSection<AppSettings> section) =>
        new(new SettingsWatcher<AppSettings>(
            new SettingsWatcherOptions<AppSettings>(FilePath, section.Read, () => document.Reload(), Classifier())
            {
                Quiet = Quiet,
                Time = Clock,
                Obstruction = () => section.ConflictingKey is { } spelling
                    ? $"The document spells this section '{spelling}', not '{section.Name}'."
                    : null,
            }));

    [Fact]
    public void A_hand_edit_to_a_section_the_type_addresses_is_reported()
    {
        GivenDocument(SectionName, "localhost");

        var document = Document();
        var section = document.Section<AppSettings>(SectionName);
        using var harness = Watch(document, section);

        Assert.Null(section.ConflictingKey);
        Assert.Equal("localhost", section.Read().Broker);

        Edit(harness, SectionName, "elsewhere.invalid");

        var change = Assert.Single(harness.Changed);
        Assert.Equal("localhost", change.Before.Broker);
        Assert.Equal("elsewhere.invalid", change.After.Broker);
        Assert.Empty(harness.Failures);
    }

    [Fact]
    public void A_section_the_document_spells_in_another_case_reads_as_defaults_and_refuses_its_writes()
    {
        GivenDocument("General", "localhost");

        var document = Document();
        var section = document.Section<AppSettings>(SectionName);

        // The reading side does not throw and does not report: it hands back the type's own
        // defaults, and only ConflictingKey says why.
        Assert.Equal("localhost", new AppSettings().Broker);
        Assert.Equal(3, section.Read().Retries);
        Assert.Equal("General", section.ConflictingKey);

        var before = File.ReadAllBytes(FilePath);
        var result = section.Update(settings => settings.Retries = 9);

        Assert.False(result.Saved);
        var conflict = Assert.IsType<SettingsKeyCaseConflictException>(result.Error);
        Assert.Equal(SectionName, conflict.Wanted);
        Assert.Equal("General", conflict.Found);
        Assert.Equal(before, File.ReadAllBytes(FilePath));
    }

    /// <summary>The re-read does not throw on a case conflict — it succeeds and moves nothing — so
    /// the refusal reaches the watcher as neither a change nor an exception. It is reported once,
    /// as an obstruction, and never as a change.</summary>
    [Fact]
    public void A_hand_edit_to_a_section_spelled_in_another_case_is_reported_as_an_obstruction_and_not_as_a_change()
    {
        GivenDocument("General", "localhost");

        var document = Document();
        var section = document.Section<AppSettings>(SectionName);
        using var harness = Watch(document, section);

        Edit(harness, "General", "elsewhere.invalid");

        // Examined, because the file did move. Not Changed, because nothing the store can see moved.
        var examined = Assert.Single(harness.Examined);
        Assert.False(examined.IsSubstantive);
        Assert.Empty(harness.Changed);

        // And not silent: the reason nothing reloaded is reported once.
        var failure = Assert.Single(harness.Failures);
        var obstruction = Assert.IsType<SettingsWatchObstructedException>(failure);
        Assert.Contains("'General'", obstruction.Message, StringComparison.Ordinal);
        Assert.Contains($"'{SectionName}'", obstruction.Message, StringComparison.Ordinal);
        Assert.Equal(examined.Obstruction, obstruction.Reason);
    }

    [Fact]
    public void The_obstruction_is_reported_once_however_many_times_the_file_moves()
    {
        GivenDocument("General", "localhost");

        var document = Document();
        var section = document.Section<AppSettings>(SectionName);
        using var harness = Watch(document, section);

        Edit(harness, "General", "one.invalid");

        Edit(harness, "General", "two.invalid");

        Assert.Equal(2, harness.Examined.Count);
        Assert.All(harness.Examined, e => Assert.NotNull(e.Obstruction));
        Assert.Single(harness.Failures);
    }

    /// <summary>Repairing the spelling clears it: the next edit reports a change and no obstruction,
    /// and a later relapse is reported again rather than being remembered as already said.</summary>
    [Fact]
    public void Repairing_the_spelling_clears_the_obstruction_and_a_relapse_is_reported_again()
    {
        GivenDocument("General", "localhost");

        var document = Document();
        var section = document.Section<AppSettings>(SectionName);
        using var harness = Watch(document, section);

        Edit(harness, "General", "one.invalid");
        Assert.Single(harness.Failures);

        Edit(harness, SectionName, "two.invalid");

        Assert.Null(harness.Examined[^1].Obstruction);
        Assert.Equal("two.invalid", Assert.Single(harness.Changed).After.Broker);
        Assert.Single(harness.Failures);

        Edit(harness, "General", "three.invalid");

        Assert.NotNull(harness.Examined[^1].Obstruction);
        Assert.Equal(2, harness.Failures.Count);
    }

    /// <summary>A store with nothing in its way names none, and the watcher raises nothing: the
    /// whole-file wiring is untouched by any of this.</summary>
    [Fact]
    public void A_store_that_names_no_obstruction_is_examined_exactly_as_before()
    {
        Given(new AppSettings { Broker = "localhost" });

        var store = Store();
        using var harness = Watch(store);

        var delivered = harness.Signals;
        Given(new AppSettings { Broker = "elsewhere.invalid" });
        harness.AwaitSignals(delivered + 1);
        CrossTheWindow(harness);

        Assert.Null(Assert.Single(harness.Examined).Obstruction);
        Assert.Single(harness.Changed);
        Assert.Empty(harness.Failures);
    }

    /// <summary>The document the fixture writes is the shape the section store reads, so a test
    /// above that finds defaults is finding them for the spelling and for nothing else.</summary>
    [Fact]
    public void The_fixture_document_is_one_the_serialiser_agrees_with()
    {
        GivenDocument(SectionName, "localhost");

        using var parsed = JsonDocument.Parse(File.ReadAllText(FilePath));
        Assert.Equal(
            "localhost",
            parsed.RootElement.GetProperty(SectionName).GetProperty("Broker").GetString());
    }
}
