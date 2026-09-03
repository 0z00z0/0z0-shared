using Xunit;

namespace ZeroZero.Config.Watch.Tests;

/// <summary>Every test here changes a real file on a real disk and reads what the watcher reports.
/// Nothing is faked but the clock, and the clock is faked so that the quiet window is crossed by
/// the code rather than by waiting.</summary>
public sealed class WatcherOnDiskTests : WatcherTestBase
{
    [Fact]
    public void An_edit_made_by_hand_is_reported()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Given(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(3, reported.Before.Retries);
        Assert.Equal(9, reported.After.Retries);
        Assert.Equal(9, store.Read().Retries);
        Assert.Empty(harness.Failures);
    }

    [Fact]
    public void One_save_arrives_as_more_than_one_notification()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        // One ordinary write, in place, of the kind a text editor makes.
        Given(new AppSettings { Retries = 9 });

        // The premise the debounce exists for. If a future Windows ever coalesces these, this fails
        // and says so rather than leaving a debounce nobody can justify.
        harness.AwaitSignals(2);
        Assert.True(harness.Signals >= 2, $"A single save delivered {harness.Signals} notifications.");
    }

    [Fact]
    public void A_burst_of_notifications_is_examined_once()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Given(new AppSettings { Retries = 4 });
        Given(new AppSettings { Retries = 5 });
        Given(new AppSettings { Retries = 6 });
        harness.AwaitSignals(3);

        CrossTheWindow(harness);

        Assert.True(harness.Signals >= 3, $"Only {harness.Signals} notifications arrived, so nothing was collapsed.");
        var reported = Assert.Single(harness.Examined);
        Assert.Equal(6, reported.After.Retries);
        Assert.Single(harness.Changed);
    }

    [Fact]
    public void A_burst_still_pending_is_not_examined_before_the_window_closes()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Given(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        harness.AwaitQuiet();

        // Almost the whole window, and then the rest of it. The first move must find nothing due.
        Clock.Advance(Quiet - TimeSpan.FromMilliseconds(1));
        Assert.Empty(harness.Examined);

        Clock.Advance(TimeSpan.FromMilliseconds(2));
        Assert.Single(harness.Examined);
    }

    [Fact]
    public void A_notification_arriving_inside_the_window_restarts_it()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Given(new AppSettings { Retries = 4 });
        harness.AwaitQuiet();
        Clock.Advance(Quiet - TimeSpan.FromMilliseconds(50));

        var before = harness.Signals;
        Given(new AppSettings { Retries = 5 });
        harness.AwaitSignals(before + 1);
        harness.AwaitQuiet();

        // The remainder of the first window has passed, but the second save restarted it.
        Clock.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Empty(harness.Examined);

        Clock.Advance(Quiet);
        Assert.Single(harness.Examined);
    }

    [Fact]
    public void A_change_to_a_cosmetic_value_is_examined_and_dismissed()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Given(new AppSettings { WindowWidth = 1280, WindowHeight = 1024 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        // The file really was re-read — the store holds the new size — and the watcher still reported
        // nothing substantive. Without this line the test would pass on a watcher that never looked.
        Assert.Equal(1280, store.Read().WindowWidth);

        var reported = Assert.Single(harness.Examined);
        Assert.False(reported.IsSubstantive);
        Assert.Equal(800, reported.Before.WindowWidth);
        Assert.Equal(1280, reported.After.WindowWidth);
        Assert.Empty(harness.Changed);
    }

    [Fact]
    public void A_change_to_a_value_nobody_classified_is_reported()
    {
        Given(new AppSettings());
        var store = Store();

        // The list names every value that existed when it was written. Nickname came later.
        using var harness = Watch(store, Classifier("WindowWidth", "WindowHeight"));

        Given(new AppSettings { Nickname = "cellar" });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.True(reported.IsSubstantive);
        Assert.Equal("cellar", reported.After.Nickname);
    }

    [Fact]
    public void A_cosmetic_change_alongside_a_substantive_one_is_reported()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Given(new AppSettings { WindowWidth = 1280, Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        Assert.Single(harness.Changed);
    }

    [Fact]
    public void The_stores_own_write_is_examined_and_reported_as_no_change()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        // The store writes through a temporary sibling and a rename, so this is also the rename case.
        var result = store.Update(settings => settings.Retries = 9);
        Assert.True(result.Saved);

        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        // It woke, it looked, and it found the file agreeing with what it already held. Asserting
        // only that nothing was reported would pass on a watcher that never armed.
        var reported = Assert.Single(harness.Examined);
        Assert.False(reported.IsSubstantive);
        Assert.Equal(9, reported.Before.Retries);
        Assert.Equal(9, reported.After.Retries);
        Assert.Empty(harness.Changed);
        Assert.Empty(harness.Failures);
    }

    [Fact]
    public void A_file_replaced_by_rename_is_reported()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        // No write ever touches the watched file: another file is built and renamed over it, which
        // is what every write this library makes looks like from outside.
        GivenByRename(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(9, reported.After.Retries);
    }

    [Fact]
    public void A_first_file_arriving_by_rename_is_reported()
    {
        // Nothing on disk yet, so the rename has no file to replace. Measured: the operating system
        // reports that as a rename and nothing else — no create, no delete, no change — so this is
        // the case a watcher without the rename notification sleeps through, and it is the very
        // first save an application ever makes.
        var store = Store();
        using var harness = Watch(store);

        GivenByRename(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(9, reported.After.Retries);
    }

    [Fact]
    public void An_edit_after_the_stores_own_write_is_still_reported()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        store.Update(settings => settings.Retries = 9);
        harness.AwaitSignals(1);
        CrossTheWindow(harness);
        Assert.Empty(harness.Changed);

        var before = harness.Signals;
        Given(new AppSettings { Retries = 11 });
        harness.AwaitSignals(before + 1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(9, reported.Before.Retries);
        Assert.Equal(11, reported.After.Retries);
    }

    [Fact]
    public void A_deleted_file_is_reported_as_a_return_to_defaults()
    {
        Given(new AppSettings { Retries = 9 });
        var store = Store();
        using var harness = Watch(store);
        Assert.Equal(9, store.Read().Retries);

        File.Delete(FilePath);
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(9, reported.Before.Retries);
        Assert.Equal(3, reported.After.Retries);
    }

    [Fact]
    public void A_file_created_after_the_watcher_armed_is_reported()
    {
        // The folder is there and the file is not, which is what a first run looks like once the
        // application has created its own data directory.
        var store = Store();
        using var harness = Watch(store);

        Given(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(9, reported.After.Retries);
    }

    [Fact]
    public void A_disposed_watcher_reports_nothing_further()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        harness.Watcher.Dispose();

        Given(new AppSettings { Retries = 9 });
        CrossTheWindow(harness);

        Assert.Empty(harness.Examined);
        Assert.Empty(harness.Changed);
    }

    [Fact]
    public void Notifications_go_to_the_context_the_consumer_gave()
    {
        Given(new AppSettings());
        var store = Store();

        var context = new RecordingContext();
        using var harness = new Harness(
            SettingsWatcher<AppSettings>.For(store, Classifier(), Quiet, Clock, context));

        Given(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        Assert.Equal(2, context.Posts);
        Assert.Single(harness.Changed);
    }

    /// <summary>
    /// A failure goes to the context as well. Without this, one event arrives on the interface
    /// thread when it carries an obstruction and on a timer thread when it carries a store that
    /// threw, and a consumer following the guide would touch its interface off that thread.
    /// </summary>
    [Fact]
    public void A_failure_goes_to_the_context_as_every_other_notification_does()
    {
        Given(new AppSettings());
        var context = new RecordingContext();

        using var watcher = new SettingsWatcher<AppSettings>(
            new SettingsWatcherOptions<AppSettings>(
                FilePath,
                () => throw new InvalidOperationException("the store is in no state to be read"),
                () => { },
                Classifier())
            {
                Quiet = Quiet,
                Time = Clock,
                NotificationContext = context,
            });

        using var harness = new Harness(watcher);

        Given(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        // The examination threw, so nothing was examined and the failure is the only notification.
        Assert.Single(harness.Failures);
        Assert.Equal(1, context.Posts);
    }

    [Fact]
    public void A_folder_that_does_not_exist_yet_is_created_so_it_can_be_watched()
    {
        var folder = Path.Combine(Root, "not-yet");
        var settings = new AppSettings();

        using var watcher = new SettingsWatcher<AppSettings>(
            new SettingsWatcherOptions<AppSettings>(
                Path.Combine(folder, FileName), () => settings, () => { }, Classifier())
            {
                Quiet = Quiet,
                Time = Clock,
            });

        Assert.True(Directory.Exists(folder), "The folder was not created, so nothing could be watched in it.");
    }

    [Fact]
    public void A_store_that_throws_is_reported_rather_than_taking_the_process_with_it()
    {
        Given(new AppSettings());
        var failures = new List<Exception>();

        using var watcher = new SettingsWatcher<AppSettings>(
            new SettingsWatcherOptions<AppSettings>(
                FilePath,
                () => throw new InvalidOperationException("the store is in no state to be read"),
                () => { },
                Classifier())
            {
                Quiet = Quiet,
                Time = Clock,
            });

        watcher.Failed += (_, e) => { lock (failures) failures.Add(e.Error); };
        using var harness = new Harness(watcher);

        Given(new AppSettings { Retries = 9 });
        harness.AwaitSignals(1);
        CrossTheWindow(harness);

        var reported = Assert.Single(failures);
        Assert.IsType<InvalidOperationException>(reported);
        Assert.Empty(harness.Examined);
    }

    [Fact]
    public void The_question_the_classifier_answers_is_carried_by_the_watcher()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Assert.Equal("must the connection be rebuilt?", harness.Watcher.Question);
    }

    private sealed class RecordingContext : SynchronizationContext
    {
        private int _posts;

        public int Posts => Volatile.Read(ref _posts);

        public override void Post(SendOrPostCallback callback, object? state)
        {
            Interlocked.Increment(ref _posts);
            callback(state);
        }
    }
}
