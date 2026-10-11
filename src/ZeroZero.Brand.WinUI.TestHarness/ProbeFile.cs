using Microsoft.UI.Dispatching;

namespace ZeroZero.Brand.WinUI.TestHarness;

/// <summary>
/// How a run tells the outside it has finished measuring. The numbers are written to a staging file
/// and moved into place, so a reader never meets half a file, and an empty <c>.done</c> marker beside
/// the probe says the move is over: the probe path is bound while the move is in flight, and an open
/// in that window fails with a sharing violation. An exception while measuring is written to
/// <c>.error.txt</c> beside the probe, and the marker is still written so a reader does not wait.
/// </summary>
internal static class ProbeFile
{
    /// <summary>Hands <paramref name="dump"/> a staging path to write to, then publishes it at
    /// <paramref name="path"/> and marks it complete.</summary>
    public static void Write(string path, Action<string> dump)
    {
        string staging = path + ".tmp";
        File.Delete(path + ".done");
        File.WriteAllText(staging, "");
        try
        {
            dump(staging);
        }
        catch (Exception ex)
        {
            File.WriteAllText(path + ".error.txt", ex.ToString());
        }
        File.Move(staging, path, overwrite: true);
        File.WriteAllText(path + ".done", "");
    }

    /// <summary>Timers waiting to tick, held here so a collection cannot take one before it fires.</summary>
    private static readonly List<DispatcherQueueTimer> Pending = [];

    /// <summary>Runs <see cref="Write"/> once, <paramref name="delay"/> from now, on the calling
    /// thread's dispatcher: layout has had that long to settle.</summary>
    public static void WriteAfter(TimeSpan delay, string path, Action<string> dump)
    {
        var timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        timer.Interval = delay;
        timer.IsRepeating = false;
        timer.Tick += (_, _) =>
        {
            Pending.Remove(timer);
            Write(path, dump);
        };
        Pending.Add(timer);
        timer.Start();
    }
}
