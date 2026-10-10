using Xunit;
using ZeroZero.Tray.WinUI;

namespace ZeroZero.Tray.Tests;

public class TrayStartupSequenceTests
{
    // Real time a sequence may take before the test counts it as hanging; the simulated clock never waits.
    private static readonly TimeSpan HangLimit = TimeSpan.FromSeconds(10);

    private sealed class Rig
    {
        public TimeSpan Now;
        public int Prepared, Attempts, Abandoned;
        public Func<bool> ShellPresent = () => true;
        public Func<int, bool> Refuses = _ => false;

        public Task<TrayStartupResult> RunAsync(TrayStartupOptions options) => Task.Run(() => TrayStartupSequence.RunAsync(
            options,
            () => ShellPresent(),
            () => Prepared++,
            () =>
            {
                Attempts++;
                if (Refuses(Attempts)) throw new InvalidOperationException("TryCreate failed.");
            },
            () => Abandoned++,
            wait => { Now += wait; return Task.CompletedTask; },
            () => Now)).WaitAsync(HangLimit);
    }

    [Fact]
    [Trait(Guard.Category, Guard.Value)]
    public async Task AShellThatNeverAppearsIsGivenUpOnAtTheWaitWithNothingCreated()
    {
        var rig = new Rig { ShellPresent = () => false };

        var result = await rig.RunAsync(new TrayStartupOptions { ShellWait = TimeSpan.FromSeconds(15) });

        Assert.Equal(TrayStartupOutcome.ShellNeverAppeared, result.Outcome);
        Assert.Equal(TimeSpan.FromSeconds(15), result.Elapsed);
        Assert.Equal((0, 0), (rig.Prepared, rig.Attempts));
    }

    [Fact]
    [Trait(Guard.Category, Guard.Value)]
    public async Task AnIconRefusedEveryTimeIsGivenUpOnAfterTheAttemptsAndAbandoned()
    {
        var rig = new Rig { Refuses = _ => true };

        var result = await rig.RunAsync(new TrayStartupOptions { CreateAttempts = 5, RetryDelay = TimeSpan.FromSeconds(1) });

        Assert.Equal(TrayStartupOutcome.CreationFailed, result.Outcome);
        Assert.Equal((5, 5, 1), (result.Attempts, rig.Attempts, rig.Abandoned));
        Assert.Equal(TimeSpan.FromSeconds(4), result.Elapsed);
        Assert.Equal("TryCreate failed.", result.LastError?.Message);
    }

    [Fact]
    public async Task ALateShellAndTwoRefusalsStillEndCreatedOnTheThirdAttempt()
    {
        var rig = new Rig { Refuses = attempt => attempt < 3 };
        rig.ShellPresent = () => rig.Now >= TimeSpan.FromSeconds(2);

        var result = await rig.RunAsync(new TrayStartupOptions());

        Assert.Equal(TrayStartupOutcome.Created, result.Outcome);
        Assert.Equal((1, 3, 0), (rig.Prepared, result.Attempts, rig.Abandoned));
    }

    // The callback-based TrayStartup.StartAsync(create, abandon, options) overload wires in a
    // genuine no-op for prepare, not a counted stub; this exercises that exact shape.
    [Fact]
    [Trait(Guard.Category, Guard.Value)]
    public async Task ANoOpPrepareStillLetsCreationSucceedAndSkipsAbandon()
    {
        int attempts = 0, abandoned = 0;

        var result = await TrayStartupSequence.RunAsync(
            new TrayStartupOptions(),
            () => true,
            static () => { },
            () => attempts++,
            () => abandoned++,
            _ => Task.CompletedTask,
            () => TimeSpan.Zero).WaitAsync(HangLimit);

        Assert.Equal(TrayStartupOutcome.Created, result.Outcome);
        Assert.Equal((1, 0), (attempts, abandoned));
    }
}
