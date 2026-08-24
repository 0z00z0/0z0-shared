using Xunit;

namespace ZeroZero.Config.Tests;

/// <summary>When change notification fires, and on which thread it arrives.</summary>
public class SettingsFileNotificationTests : SettingsFileTestBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    /// <summary>Captures posted callbacks instead of running them, the way a UI thread's context
    /// queues work rather than executing it inline.</summary>
    private sealed class RecordingContext : SynchronizationContext
    {
        private readonly List<Action> _queued = [];

        public int Queued
        {
            get { lock (_queued) return _queued.Count; }
        }

        public override void Post(SendOrPostCallback callback, object? state)
        {
            lock (_queued) _queued.Add(() => callback(state));
        }

        public void Drain()
        {
            Action[] pending;
            lock (_queued)
            {
                pending = [.. _queued];
                _queued.Clear();
            }

            foreach (var callback in pending) callback();
        }
    }

    [Fact]
    public void Update_RaisesChangedOnce()
    {
        var file = Create();
        var changes = 0;
        file.Changed += (_, _) => changes++;

        file.Update(s => s.Label = "desk");

        Assert.Equal(1, changes);
    }

    [Fact]
    public void Update_ThatChangesNothing_RaisesNothing()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");

        var changes = 0;
        file.Changed += (_, _) => changes++;
        file.Update(s => s.Label = "desk");

        Assert.Equal(0, changes);
    }

    [Fact]
    public void Changed_CarriesTheFileAsItsSender()
    {
        var file = Create();
        object? sender = null;
        file.Changed += (s, _) => sender = s;

        file.Update(s => s.Label = "desk");

        Assert.Same(file, sender);
    }

    [Fact]
    public void Changed_IsRaisedOutsideTheLock()
    {
        var file = Create();
        var readCompleted = false;

        // A subscriber does real work, and another thread must be able to read while it does. If
        // the notification were raised while the lock was held, this read would block until the
        // handler returned — which it cannot, because it is waiting for the read.
        file.Changed += (_, _) => readCompleted = Task.Run(() => file.Read()).Wait(Patience);

        file.Update(s => s.Label = "desk");

        Assert.True(readCompleted);
    }

    [Fact]
    public void NotificationContext_ReceivesChangedInsteadOfTheMutatingThread()
    {
        var context = new RecordingContext();
        var file = Create(notificationContext: context);
        var changes = 0;
        file.Changed += (_, _) => changes++;

        file.Update(s => s.Label = "desk");

        Assert.Equal(0, changes);
        Assert.Equal(1, context.Queued);

        context.Drain();
        Assert.Equal(1, changes);
    }

    [Fact]
    public void NotificationContext_ReceivesSaveFailedToo()
    {
        var context = new RecordingContext();
        var file = Create(notificationContext: context);
        file.Update(s => s.Label = "desk");
        context.Drain();

        var failures = 0;
        file.SaveFailed += (_, _) => failures++;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            file.Update(s => s.Label = "cabinet");
        }

        Assert.Equal(0, failures);
        context.Drain();
        Assert.Equal(1, failures);
    }

    [Fact]
    public void Reload_RaisesChangedOnlyWhenTheFileDiffers()
    {
        var file = Create();
        file.Update(s => s.Label = "desk");

        var changes = 0;
        file.Changed += (_, _) => changes++;

        Assert.False(file.Reload());
        Assert.Equal(0, changes);

        File.WriteAllText(FilePath, "{ \"Label\": \"cabinet\" }");

        Assert.True(file.Reload());
        Assert.Equal(1, changes);
        Assert.Equal("cabinet", file.Read().Label);
    }
}
