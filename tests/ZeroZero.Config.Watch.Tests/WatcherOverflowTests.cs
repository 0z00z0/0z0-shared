using Xunit;

namespace ZeroZero.Config.Watch.Tests;

/// <summary>What happens when Windows gives up rather than falls behind.</summary>
/// <remarks>
/// <para>The notification buffer for a folder holds a fixed number of bytes. When more changes
/// arrive than fit, Windows discards <b>everything</b> it was holding and reports that it did — it
/// does not deliver them late. So a watcher that answers a dropped notification by waiting longer
/// waits for something that is never coming, and the only correct answer is to read the file
/// again.</para>
/// <para>Provoking it takes two things at once, and each was measured on its own first. Volume
/// alone is not enough: twenty-four thousand files created in the watched folder, across eight
/// threads, dropped nothing, because the reader kept up. Stopping the reader alone drops nothing
/// either, because nothing is queued behind it. Together — the reader held inside a notification it
/// has just delivered while two thousand more changes land in the folder — the buffer overflows.
/// </para>
/// <para>Holding the reader is the test's own doing and nothing else's: it blocks in a handler the
/// watcher raises on the thread that would otherwise go back for more. Nothing outside this class
/// is stopped, which an earlier attempt at this — starving the thread pool — could not say.</para>
/// </remarks>
public sealed class WatcherOverflowTests : WatcherTestBase
{
    /// <summary>Changes made to the watched folder while the reader is held. The buffer holds 8 kB
    /// and one of these names costs a little over 300 bytes of it, so this is some seventy times
    /// what fits.</summary>
    private const int Flood = 2000;

    /// <summary>Long enough that few of them fit in the buffer, short enough that the whole path
    /// stays inside the classic limit.</summary>
    private const int NameLength = 144;

    [Fact]
    public void A_dropped_notification_is_reported()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        DropTheNotifications(harness);

        Assert.NotEmpty(harness.Failures);
        Assert.All(harness.Failures, failure => Assert.IsType<InternalBufferOverflowException>(failure));
    }

    /// <summary>The reason the recovery exists. Nothing is coming for the edit that was made while
    /// the buffer was overflowing, so unless the watcher goes and looks of its own accord the store
    /// holds a stale file for as long as nobody touches it again.</summary>
    [Fact]
    public void An_edit_made_while_notifications_were_dropped_is_examined_anyway()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        DropTheNotifications(harness, () => Given(new AppSettings { Retries = 11 }));

        // A notification counted after the loss was reported was raised by the watcher itself.
        // Without it nothing is due, the quiet window never closes, and the edit above is never
        // read: this is the assertion that the examination was forced rather than delivered.
        Assert.True(
            harness.Signals > harness.SignalsWhenFirstReported,
            $"The watcher reported the loss and then raised nothing of its own, so nothing was due and the edit was never read. The count still stands at {harness.Signals}.");

        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(3, reported.Before.Retries);
        Assert.Equal(11, reported.After.Retries);
        Assert.Equal(11, store.Read().Retries);
    }

    /// <summary>The watcher is thrown away and rebuilt inside the failure, so this is the test that
    /// the rebuilt one is armed. Without it the watcher goes deaf at the first dropped notification
    /// and never says so again.</summary>
    [Fact]
    public void An_edit_after_a_dropped_notification_is_still_reported()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        DropTheNotifications(harness);
        CrossTheWindow(harness);

        var before = harness.Signals;
        Given(new AppSettings { Retries = 9 });
        harness.AwaitSignals(before + 1);
        CrossTheWindow(harness);

        var reported = Assert.Single(harness.Changed);
        Assert.Equal(9, reported.After.Retries);
    }

    /// <summary>Why the flood above fills the buffer without waking anything, and why the temporary
    /// sibling an atomic write leaves behind wakes nothing either: a change to any other name in the
    /// folder is not a signal. A few short names, costing a few hundred bytes of an eight-kilobyte
    /// buffer, so this cannot overflow however busy the machine is and the silence means the filter
    /// rather than a loss.</summary>
    [Fact]
    public void A_change_to_another_name_in_the_folder_is_not_a_notification()
    {
        Given(new AppSettings());
        var store = Store();
        using var harness = Watch(store);

        Sibling(20, nameLength: 4);
        harness.AwaitQuiet();

        Assert.Equal(0, harness.Signals);
        Assert.Empty(harness.Examined);
        Assert.Empty(harness.Failures);
    }

    /// <summary>Overflows the operating system's notification buffer for the watched folder and
    /// returns once the watcher has reported the loss. Anything <paramref name="whileDropping"/>
    /// does happens with the buffer already overflowing, so no notification for it survives.
    /// </summary>
    private void DropTheNotifications(Harness harness, Action? whileDropping = null)
    {
        using var reading = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var held = 0;

        void Hold()
        {
            if (Interlocked.Exchange(ref held, 1) != 0) return;

            reading.Set();
            release.Wait(Delivery);
        }

        harness.Watcher.Signalled += Hold;

        try
        {
            // The watched name is the only one that reaches the handler, so writing what the store
            // already holds is how the reader is stopped without changing anything.
            Given(new AppSettings());

            Assert.True(
                reading.Wait(Delivery),
                $"The operating system delivered nothing within {Delivery.TotalSeconds:0} s, so the reader was never holding and nothing after this point was tested.");

            Sibling(Flood, NameLength);
            whileDropping?.Invoke();
        }
        finally
        {
            release.Set();
            harness.Watcher.Signalled -= Hold;
        }

        harness.AwaitFailure<InternalBufferOverflowException>();
    }

    /// <summary>Creates <paramref name="count"/> files in the watched folder under names the watcher
    /// is not watching. <paramref name="nameLength"/> decides how much of the buffer each one costs.
    /// </summary>
    private void Sibling(int count, int nameLength)
    {
        var padding = new string('n', nameLength);

        for (var file = 0; file < count; file++)
        {
            File.WriteAllText(Path.Combine(Root, $"{padding}{file:00000}.x"), "x");
        }
    }
}
