using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ZeroZero.Config.Watch.Tests;

/// <summary>A throwaway folder per test, a real settings file inside it, and a clock the test moves.
/// xUnit builds one instance per test, so no two tests share a directory.</summary>
/// <remarks>
/// <para>Two different waits are used here, and confusing them is how a watcher test becomes a
/// timing test. Waiting for the operating system to deliver a notification is a liveness wait: the
/// deadline is generous and reaching it means the notification never came, which is a real failure
/// rather than a slow machine. Crossing the quiet window is not a wait at all — the clock is moved,
/// so the debounce is decided by the code under test and by nothing else.</para>
/// </remarks>
public abstract class WatcherTestBase : IDisposable
{
    protected const string FileName = "settings.json";

    /// <summary>The quiet window every test arms its watcher with.</summary>
    protected static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a test will wait for Windows to deliver a notification for a change the
    /// test has already made. Deliberately far longer than delivery has ever taken (measured in
    /// milliseconds), because this deadline is only ever reached when nothing is coming.</summary>
    protected static readonly TimeSpan Delivery = TimeSpan.FromSeconds(30);

    /// <summary>How long the file must go without a new notification before a test accepts that the
    /// operating system has finished reporting the change the test made. Measured: the notifications
    /// for one save arrive within a millisecond or two of each other.</summary>
    protected static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(300);

    protected WatcherTestBase()
    {
        Root = Path.Combine(Path.GetTempPath(), "ZeroZero.Config.Watch.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    /// <summary>The throwaway folder.</summary>
    protected string Root { get; }

    /// <summary>The settings file under test.</summary>
    protected string FilePath => Path.Combine(Root, FileName);

    /// <summary>The clock the quiet window is measured on.</summary>
    protected FakeTimeProvider Clock { get; } = new();

    /// <summary>A classifier naming the two window sizes as cosmetic, and nothing else. Written, in
    /// the story every one of these tests tells, before <see cref="AppSettings.Nickname"/> existed.</summary>
    protected static SettingsChangeClassifier<AppSettings> Classifier(params string[] cosmetic) =>
        new("must the connection be rebuilt?", cosmetic.Length > 0 ? cosmetic : ["WindowWidth", "WindowHeight"]);

    /// <summary>Writes the file exactly as the shape serialises, which is what a hand edit that keeps
    /// the file valid leaves behind.</summary>
    protected void Given(AppSettings settings) =>
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, SettingsFileOptions.DefaultSerialiser));

    /// <summary>Replaces the file by renaming another one over it, which is the shape every write
    /// this library makes reaches the disk as — and the shape a watcher taking only change
    /// notifications sleeps through.</summary>
    protected void GivenByRename(AppSettings settings)
    {
        var temp = Path.Combine(Root, "incoming.json");
        var text = JsonSerializer.Serialize(settings, SettingsFileOptions.DefaultSerialiser);

        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(Encoding.UTF8.GetBytes(text));
            stream.Flush(flushToDisk: true);
        }

        File.Move(temp, FilePath, overwrite: true);
    }

    /// <summary>Waits for the operating system to finish reporting, then moves the clock past the
    /// quiet window, which is what makes the debounce fire. The examination runs on the thread that
    /// moves the clock, so everything the watcher reports has been reported by the time this
    /// returns.</summary>
    protected void CrossTheWindow(Harness harness)
    {
        harness.AwaitQuiet();
        Clock.Advance(Quiet + TimeSpan.FromMilliseconds(1));
    }

    /// <summary>A store over the file, as an application would hold one.</summary>
    protected SettingsFile<AppSettings> Store() => new(new SettingsFileOptions(Root, FileName));

    /// <summary>A watcher over a store, with everything it reports recorded.</summary>
    protected Harness Watch(SettingsFile<AppSettings> store, SettingsChangeClassifier<AppSettings>? classifier = null) =>
        new(SettingsWatcher<AppSettings>.For(store, classifier ?? Classifier(), Quiet, Clock));

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary folder is the operating system's problem, not the test's.
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>A watcher and everything it has reported.</summary>
    protected sealed class Harness : IDisposable
    {
        private const int PollMilliseconds = 5;

        public Harness(SettingsWatcher<AppSettings> watcher)
        {
            Watcher = watcher;

            watcher.Examined += (_, e) => { lock (Examined) Examined.Add(e); };
            watcher.Changed += (_, e) => { lock (Changed) Changed.Add(e); };

            watcher.Failed += (_, e) =>
            {
                lock (Failures)
                {
                    // Read before the failure is recorded, so it counts only what the operating
                    // system had delivered by the time the watcher reported this.
                    if (Failures.Count == 0) SignalsWhenFirstReported = Signals;
                    Failures.Add(e.Error);
                }
            };
        }

        public SettingsWatcher<AppSettings> Watcher { get; }

        public List<SettingsChangeEventArgs<AppSettings>> Examined { get; } = [];

        public List<SettingsChangeEventArgs<AppSettings>> Changed { get; } = [];

        public List<Exception> Failures { get; } = [];

        /// <summary>Every notification the operating system delivered, before the quiet window.</summary>
        public int Signals => Watcher.Signals;

        /// <summary>What <see cref="Signals"/> stood at when the first failure was reported. A
        /// signal counted after this one came from the watcher itself rather than from the
        /// operating system, which is how a forced examination is told from a delivered one.</summary>
        public int SignalsWhenFirstReported { get; private set; }

        /// <summary>Blocks until the operating system has delivered at least <paramref name="total"/>
        /// notifications for changes the test has already made. Fails the test rather than carrying
        /// on if they never arrive, because every assertion after this point assumes they did.</summary>
        public void AwaitSignals(int total)
        {
            var deadline = Environment.TickCount64 + (long)Delivery.TotalMilliseconds;

            while (Signals < total)
            {
                Assert.True(
                    Environment.TickCount64 < deadline,
                    $"The operating system delivered {Signals} notifications of the {total} this test provoked, within {Delivery.TotalSeconds:0} s. Nothing after this point was tested.");

                Thread.Sleep(PollMilliseconds);
            }
        }

        /// <summary>Blocks until the operating system has stopped delivering notifications for
        /// changes the test has already made.</summary>
        /// <remarks>A single save arrives as several notifications a millisecond or two apart, and
        /// one of them landing after the clock has been moved would restart the quiet window and make
        /// the examination the test is about to assert on never happen. This is not a timing
        /// assertion — nothing here decides whether the code is right — it is the test waiting for
        /// the change it made to have finished being reported.</remarks>
        public void AwaitQuiet()
        {
            var deadline = Environment.TickCount64 + (long)Delivery.TotalMilliseconds;

            while (true)
            {
                var seen = Signals;
                Thread.Sleep(Settle);

                if (Signals == seen) return;

                Assert.True(
                    Environment.TickCount64 < deadline,
                    $"Notifications were still arriving after {Delivery.TotalSeconds:0} s, so the file never settled.");
            }
        }

        /// <summary>Blocks until the watcher has reported a failure of the given kind. A liveness
        /// wait like <see cref="AwaitSignals"/>: reaching the deadline means the report never came,
        /// which is the failure, not a slow machine.</summary>
        public TError AwaitFailure<TError>() where TError : Exception
        {
            var deadline = Environment.TickCount64 + (long)Delivery.TotalMilliseconds;

            while (true)
            {
                lock (Failures)
                {
                    if (Failures.OfType<TError>().FirstOrDefault() is { } found) return found;
                }

                Assert.True(
                    Environment.TickCount64 < deadline,
                    $"No {typeof(TError).Name} was reported within {Delivery.TotalSeconds:0} s. Nothing after this point was tested.");

                Thread.Sleep(PollMilliseconds);
            }
        }

        public void Dispose() => Watcher.Dispose();
    }
}
